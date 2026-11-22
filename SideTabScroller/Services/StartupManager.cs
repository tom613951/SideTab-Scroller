using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace SideTabScroller.Services;

internal sealed class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SideTabScroller";
    private const string TaskName = "SideTabScroller";
    private const int SchtasksTimeoutMs = 5000;

    private readonly SemaphoreSlim _taskSemaphore = new(1, 1);

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var (exitCode, _) = await RunSchtasksAsync($"/query /tn \"{TaskName}\"", captureOutput: false, cancellationToken)
                .ConfigureAwait(false);
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> EnsureTaskConfiguredAsync(CancellationToken cancellationToken = default)
    {
        await _taskSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (exitCode, xml) = await RunSchtasksAsync($"/query /tn \"{TaskName}\" /xml", captureOutput: true, cancellationToken)
                .ConfigureAwait(false);
            if (exitCode != 0 || string.IsNullOrWhiteSpace(xml))
            {
                return false;
            }

            var currentExe = GetExecutablePath();
            bool hasBatteryLimit =
                xml.Contains("<StopIfGoingOnBatteries>true</StopIfGoingOnBatteries>", StringComparison.OrdinalIgnoreCase) ||
                xml.Contains("<DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries>", StringComparison.OrdinalIgnoreCase);
            bool hasExecutionLimit =
                !xml.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", StringComparison.OrdinalIgnoreCase);
            bool pathDiffers =
                !xml.Contains(EscapeXml(currentExe), StringComparison.OrdinalIgnoreCase);

            if (hasBatteryLimit || hasExecutionLimit || pathDiffers)
            {
                await CreateOrUpdateTaskAsync(cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _taskSemaphore.Release();
        }
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await _taskSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CleanupLegacyRunKey();

            if (enabled)
            {
                await CreateOrUpdateTaskAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var (exitCode, _) = await RunSchtasksAsync($"/delete /tn \"{TaskName}\" /f", captureOutput: false, cancellationToken)
                    .ConfigureAwait(false);
                // ExitCode 1 means task not found, which is safe to ignore when disabling
                if (exitCode != 0 && exitCode != 1)
                {
                    throw new Win32Exception(exitCode, $"删除开机任务失败 (错误代码: {exitCode})。");
                }
            }
        }
        finally
        {
            _taskSemaphore.Release();
        }
    }

    private static void CleanupLegacyRunKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Ignore legacy cleanup errors
        }
    }

    private static async Task CreateOrUpdateTaskAsync(CancellationToken cancellationToken)
    {
        var exePath = GetExecutablePath();
        var tempXmlPath = Path.Combine(Path.GetTempPath(), $"SideTabScroller_Task_{Guid.NewGuid():N}.xml");

        try
        {
            // Windows Task Scheduler defaults to stopping on batteries and 72h limit; specify explicit XML overrides.
            var xmlContent = $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <RegistrationInfo>
                    <Author>SideTabScroller</Author>
                    <Description>SideTabScroller Startup Task</Description>
                  </RegistrationInfo>
                  <Principals>
                    <Principal id="Author">
                      <LogonType>InteractiveToken</LogonType>
                      <RunLevel>HighestAvailable</RunLevel>
                    </Principal>
                  </Principals>
                  <Settings>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <IdleSettings>
                      <StopOnIdleEnd>false</StopOnIdleEnd>
                      <RestartOnIdle>false</RestartOnIdle>
                    </IdleSettings>
                  </Settings>
                  <Triggers>
                    <LogonTrigger>
                      <Enabled>true</Enabled>
                    </LogonTrigger>
                  </Triggers>
                  <Actions Context="Author">
                    <Exec>
                      <Command>{EscapeXml(exePath)}</Command>
                      <Arguments>--minimized</Arguments>
                    </Exec>
                  </Actions>
                </Task>
                """;

            await File.WriteAllTextAsync(tempXmlPath, xmlContent, Encoding.Unicode, cancellationToken).ConfigureAwait(false);

            var (exitCode, _) = await RunSchtasksAsync(
                    $"/create /tn \"{TaskName}\" /xml \"{tempXmlPath}\" /f",
                    captureOutput: false,
                    cancellationToken)
                .ConfigureAwait(false);

            if (exitCode != 0)
            {
                throw new Win32Exception(exitCode, $"创建开机任务失败 (错误代码: {exitCode})。");
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempXmlPath))
                {
                    File.Delete(tempXmlPath);
                }
            }
            catch
            {
                // Ignore temp file cleanup errors
            }
        }
    }

    private static async Task<(int ExitCode, string? StdOut)> RunSchtasksAsync(
        string arguments,
        bool captureOutput,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = captureOutput,
            StandardOutputEncoding = captureOutput ? Encoding.UTF8 : null
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 schtasks.exe 进程。");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(SchtasksTimeoutMs);

        try
        {
            Task<string>? outputTask = captureOutput
                ? process.StandardOutput.ReadToEndAsync(timeoutCts.Token)
                : null;

            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            string? output = outputTask != null ? await outputTask.ConfigureAwait(false) : null;
            return (process.ExitCode, output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore kill errors on timeout
            }

            throw new TimeoutException("schtasks.exe 执行超时。");
        }
    }

    internal static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot resolve the application executable path.");
    }
}
