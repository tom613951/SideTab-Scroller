using System.Windows;
using SideTabScroller.Native;
using SideTabScroller.Services;

namespace SideTabScroller;

public partial class App : System.Windows.Application
{
    public void ConfigureExceptionHandling()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLog.Write(args.Exception);
            MessageBox.Show(
                ErrorLog.Format(args.Exception),
                "侧栏滚轮切换标签",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        // Ensure WPF's hidden parking window never shuts down the WPF Dispatcher on WM_QUERYENDSESSION
        // unless Windows actually confirms the session is ending; never veto WM_QUERYENDSESSION (always return 1).
        AttachParkingHwndFilter();

        const int wmQueryEndSession = (int)NativeMethods.WmQueryEndSession;
        const int wmEndSession = (int)NativeMethods.WmEndSession;

        System.Windows.Interop.ComponentDispatcher.ThreadFilterMessage += (ref System.Windows.Interop.MSG msg, ref bool handled) =>
        {
            if (msg.message == wmQueryEndSession)
            {
                handled = true;
            }
            else if (msg.message == wmEndSession && msg.wParam == 0)
            {
                // OS session end was cancelled by another app or user; prevent WPF CriticalShutdown
                handled = true;
            }
        };
    }

    private void AttachParkingHwndFilter()
    {
        try
        {
            var ensureHwndSource = typeof(Application).GetMethod(
                "EnsureHwndSource",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            ensureHwndSource?.Invoke(this, null);

            var parkingHwndField = typeof(Application).GetField(
                "_parkingHwnd",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var parkingHwnd = parkingHwndField?.GetValue(this);
            if (parkingHwnd != null)
            {
                var addHookMethod = parkingHwnd.GetType().GetMethod("AddHook");
                var hookDelegateType = parkingHwnd.GetType().Assembly.GetType("MS.Win32.HwndWrapperHook");
                if (addHookMethod != null && hookDelegateType != null)
                {
                    var hookHandler = Delegate.CreateDelegate(hookDelegateType, this, nameof(ParkingHwndHook));
                    addHookMethod.Invoke(parkingHwnd, [hookHandler]);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }

    private nint ParkingHwndHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == (int)NativeMethods.WmQueryEndSession)
        {
            // Never veto Windows shutdown/restart (return 1), and prevent WPF from calling CriticalShutdown prematurely
            handled = true;
            return new nint(1);
        }

        if (msg == (int)NativeMethods.WmEndSession && wParam == 0)
        {
            handled = true;
            return 0;
        }

        return 0;
    }
}
