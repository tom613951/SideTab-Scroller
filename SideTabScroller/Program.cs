using SideTabScroller.Native;
using SideTabScroller.Services;

namespace SideTabScroller;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                ErrorLog.Write(exception);
            }
        };

        try
        {
            Environment.SetEnvironmentVariable("DOTNET_TELEMETRY_OPTOUT", "1");
            Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1");
        }
        catch
        {
            // Ignore environment set failures
        }

        const string mutexName = @"Local\SideTabScroller-SingleInstance-Mutex";
        using var mutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            try
            {
                NativeMethods.AllowSetForegroundWindow(NativeMethods.AsfwAny);
                var msg = NativeMethods.RegisterWindowMessageW("SideTabScroller_ShowMeMessage");
                if (msg != 0)
                {
                    NativeMethods.PostMessageW(NativeMethods.HwndBroadcast, msg, 0, 0);
                }
            }
            catch
            {
                // Non-critical failure
            }

            return;
        }

        try
        {
            RunApplication(args);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
        finally
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch
            {
                // Ignore mutex release errors
            }
        }
    }

    private static void RunApplication(string[] args)
    {
        bool startMinimized = false;
        foreach (var arg in args)
        {
            if (arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
            {
                startMinimized = true;
                break;
            }
        }

        using var controller = new AppController();
        var windowHost = new SettingsWindowHost(controller);
        using var trayHost = new NativeTrayHost(controller, windowHost.Show);

        try
        {
            controller.InitializeRuntime();
            _ = Task.Run(() => controller.RefreshStartupStateAndEnsureTaskAsync());

            if (!startMinimized)
            {
                windowHost.Show();
            }

            // Reclaim startup allocations early when starting silently into tray
            if (startMinimized)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
                NativeMethods.TrimWorkingSet();
            }

            trayHost.RunMessageLoop();
        }
        finally
        {
            windowHost.Shutdown();
        }
    }
}
