using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace SideTabScroller.Services;

internal sealed class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SideTabScroller";

    public bool IsEnabled()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/query /tn \"SideTabScroller\"",
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

    public void SetEnabled(bool enabled)
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
            var exePath = GetExecutablePath();
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/create /tn \"SideTabScroller\" /tr \"\\\"{exePath}\\\" --minimized\" /sc onlogon /rl highest /f",
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
        else
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/delete /tn \"SideTabScroller\" /f",
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

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot resolve the application executable path.");
    }
}
