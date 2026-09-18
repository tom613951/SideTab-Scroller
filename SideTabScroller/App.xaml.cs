using System;
using System.Linq;
using System.Threading;
using System.Windows;
using SideTabScroller.Native;
using SideTabScroller.Services;

namespace SideTabScroller;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
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
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        _ownsMutex = createdNew;

        if (!createdNew)
        {
            try
            {
                var msg = NativeMethods.RegisterWindowMessage("SideTabScroller_ShowMeMessage");
                NativeMethods.PostMessage(NativeMethods.HwndBroadcast, msg, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                // Non-critical failure
            }

            _mutex.Dispose();
            _mutex = null;
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        AttachParkingHwndFilter();

        const int wmQueryEndSession = 0x0011;
        const int wmEndSession = 0x0012;

        System.Windows.Interop.ComponentDispatcher.ThreadFilterMessage += (ref System.Windows.Interop.MSG msg, ref bool handled) =>
        {
            if (msg.message is wmQueryEndSession or wmEndSession)
            {
                // Suppress WM_QUERYENDSESSION and WM_ENDSESSION from reaching message loop handlers
                handled = true;
            }
        };

        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLog.Write(args.Exception);
            System.Windows.MessageBox.Show(
                ErrorLog.Format(args.Exception),
                "侧栏滚轮切换标签",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        SessionEnding += (_, args) =>
        {
            // Cancel WPF's built-in CriticalShutdown
            args.Cancel = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                ErrorLog.Write(exception);
            }
        };

        try
        {
            var window = new MainWindow();
            MainWindow = window;

            if (e.Args.Any(arg => arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase)))
            {
                // Force native Win32 window handle creation so WndProc is active and can receive wakeup broadcast message
                new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            }
            else
            {
                window.Show();
            }
        }
        catch (Exception exception)
        {
            ErrorLog.Write(exception);
            System.Windows.MessageBox.Show(
                ErrorLog.Format(exception),
                "侧栏滚轮切换标签",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mutex != null)
        {
            if (_ownsMutex)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch
                {
                    // Ignore release errors
                }
            }
            _mutex.Dispose();
            _mutex = null;
        }
        base.OnExit(e);
    }

    private void AttachParkingHwndFilter()
    {
        try
        {
            var ensureHwndSource = typeof(System.Windows.Application).GetMethod(
                "EnsureHwndSource",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            ensureHwndSource?.Invoke(this, null);

            var parkingHwndField = typeof(System.Windows.Application).GetField(
                "_parkingHwnd",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var parkingHwnd = parkingHwndField?.GetValue(this);
            if (parkingHwnd != null)
            {
                var addHookMethod = parkingHwnd.GetType().GetMethod("AddHook");
                if (addHookMethod != null)
                {
                    var hookDelegateType = parkingHwnd.GetType().Assembly.GetType("MS.Win32.HwndWrapperHook");
                    if (hookDelegateType != null)
                    {
                        var hookHandler = Delegate.CreateDelegate(hookDelegateType, this, nameof(ParkingHwndHook));
                        addHookMethod.Invoke(parkingHwnd, [hookHandler]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }

    private IntPtr ParkingHwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmQueryEndSession = 0x0011;
        const int wmEndSession = 0x0012;

        if (msg == wmQueryEndSession)
        {
            handled = true;
            // Return 1 to indicate ready, never return 0 (veto)
            return new IntPtr(1);
        }

        if (msg == wmEndSession)
        {
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }
}
