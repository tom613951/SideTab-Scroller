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

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = @"Local\SideTabScroller-SingleInstance-Mutex";
        _mutex = new Mutex(true, mutexName, out bool createdNew);

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
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

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

            if (!e.Args.Any(arg => arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase)))
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
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
