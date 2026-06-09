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
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return value?.Contains(GetExecutablePath(), StringComparison.OrdinalIgnoreCase) == true;
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{GetExecutablePath()}\" --minimized", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath
            ?? Assembly.GetEntryAssembly()?.Location
            ?? throw new InvalidOperationException("Cannot resolve the application executable path.");
    }
}
