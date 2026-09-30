using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace SideTabScroller.Services;

internal sealed class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SideTabScroller";
    private const string TaskName = "SideTabScroller";

    private static readonly object TaskLock = new();

    public bool IsEnabled()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/query /tn \"{TaskName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var process = Process.Start(startInfo);
            if (process == null) return false;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public void EnsureTaskConfigured()
    {
        try
        {
            lock (TaskLock)
            {
                if (!IsEnabled())
                {
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/query /tn \"{TaskName}\" /xml",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                using var process = Process.Start(startInfo);
                if (process == null) return;
                var xml = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(xml))
                {
                    return;
                }

                var currentExe = GetExecutablePath();
                bool hasBatteryLimit = xml.Contains("<StopIfGoingOnBatteries>true</StopIfGoingOnBatteries>", StringComparison.OrdinalIgnoreCase)
                    || xml.Contains("<DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries>", StringComparison.OrdinalIgnoreCase);
                bool hasExecutionLimit = !xml.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", StringComparison.OrdinalIgnoreCase);
                bool pathDiffers = !xml.Contains(EscapeXml(currentExe), StringComparison.OrdinalIgnoreCase);

                if (hasBatteryLimit || hasExecutionLimit || pathDiffers)
                {
                    CreateOrUpdateTask();
                }
            }
        }
        catch
        {
            // Non-critical background task maintenance failure
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (TaskLock)
        {
            // Clean up legacy registry keys
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            catch
            {
                // Ignore legacy cleanup errors
            }

            if (enabled)
            {
                CreateOrUpdateTask();
            }
            else
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/delete /tn \"{TaskName}\" /f",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("无法启动 schtasks.exe 进程。");
                }
                process.WaitForExit();
                // ExitCode 1 means task not found, which we can safely ignore when disabling
                if (process.ExitCode != 0 && process.ExitCode != 1)
                {
                    throw new System.ComponentModel.Win32Exception(process.ExitCode, $"删除开机任务失败 (错误代码: {process.ExitCode})。");
                }
            }
        }
    }

    private static void CreateOrUpdateTask()
    {
        var exePath = GetExecutablePath();
        var tempXmlPath = Path.Combine(Path.GetTempPath(), $"SideTabScroller_Task_{Guid.NewGuid():N}.xml");
        try
        {
            // Windows Task Scheduler tasks created with schtasks /create default to:
            // StopIfGoingOnBatteries = true, DisallowStartIfOnBatteries = true, and ExecutionTimeLimit = PT72H.
            // When a laptop is unplugged from AC power, Task Scheduler terminates the process.
            // By defining the task via XML, we explicitly set battery restrictions to false and unlimited execution time.
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

            File.WriteAllText(tempXmlPath, xmlContent, System.Text.Encoding.Unicode);

            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/create /tn \"{TaskName}\" /xml \"{tempXmlPath}\" /f",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("无法启动 schtasks.exe 进程。");
            }
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new System.ComponentModel.Win32Exception(process.ExitCode, $"创建开机任务失败 (错误代码: {process.ExitCode})。");
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

    private static string EscapeXml(string text)
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
