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

        try
        {
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
                process?.WaitForExit();
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
                process?.WaitForExit();
            }
        }
        catch
        {
            // Fail silently
        }
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath
            ?? Assembly.GetEntryAssembly()?.Location
            ?? throw new InvalidOperationException("Cannot resolve the application executable path.");
    }
}
