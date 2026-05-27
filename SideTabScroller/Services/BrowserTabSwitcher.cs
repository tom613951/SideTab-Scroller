using System.Diagnostics;
using SideTabScroller.Models;
using SideTabScroller.Native;

namespace SideTabScroller.Services;

internal sealed class BrowserTabSwitcher
{
    private const string ChromiumWindowClassPrefix = "Chrome_WidgetWin_";

    private readonly Func<ScrollerSettings> _settingsProvider;
    private readonly Action<SwitchResult> _onSwitch;

    public BrowserTabSwitcher(Func<ScrollerSettings> settingsProvider, Action<SwitchResult> onSwitch)
    {
        _settingsProvider = settingsProvider;
        _onSwitch = onSwitch;
    }

    public bool HandleWheel(NativePoint point, int wheelDelta)
    {
        var settings = _settingsProvider();
        if (!settings.Enabled || wheelDelta == 0)
        {
            return false;
        }

        if (settings.IgnoreWhenKeyboardModifiersPressed && NativeMethods.HasAnyKeyboardModifierDown())
        {
            return false;
        }

        if (!TryResolveBrowserWindow(point, settings, out var browserWindow))
        {
            return false;
        }

        if (!IsPointInsideSidebar(point, browserWindow.Bounds, settings))
        {
            return false;
        }

        var direction = ResolveDirection(wheelDelta, settings);
        if (!FocusAndSendShortcut(browserWindow.Handle, direction, settings))
        {
            return false;
        }

        _onSwitch(new SwitchResult(DateTime.Now, browserWindow.ProcessName, direction));
        return settings.ConsumeHandledWheelEvents;
    }

    private static SwitchDirection ResolveDirection(int wheelDelta, ScrollerSettings settings)
    {
        var wheelUpMeansPrevious = wheelDelta > 0;
        if (settings.ReverseDirection)
        {
            wheelUpMeansPrevious = !wheelUpMeansPrevious;
        }

        return wheelUpMeansPrevious ? SwitchDirection.Previous : SwitchDirection.Next;
    }

    private static bool FocusAndSendShortcut(IntPtr browserHandle, SwitchDirection direction, ScrollerSettings settings)
    {
        var previousForeground = NativeMethods.GetForegroundWindow();
        var browserAlreadyForeground = NativeMethods.IsSameRootWindow(previousForeground, browserHandle);

        if (!browserAlreadyForeground)
        {
            if (!settings.AutofocusBrowser)
            {
                return false;
            }

            if (NativeMethods.IsIconic(browserHandle))
            {
                NativeMethods.ShowWindow(browserHandle, NativeMethods.SwRestore);
            }

            NativeMethods.SetForegroundWindow(browserHandle);
            if (!WaitForForeground(browserHandle, settings.FocusDelayMilliseconds))
            {
                return false;
            }
        }

        var previous = direction == SwitchDirection.Previous;
        var sent = settings.ShortcutMode == TabSwitchShortcutMode.CtrlPage
            ? NativeMethods.SendCtrlPageKey(previous)
            : NativeMethods.SendCtrlTabKey(previous);

        if (!browserAlreadyForeground && settings.RestorePreviousFocus && previousForeground != IntPtr.Zero)
        {
            Thread.Sleep(45);
            NativeMethods.SetForegroundWindow(previousForeground);
        }

        return sent;
    }

    private static bool WaitForForeground(IntPtr browserHandle, int focusDelayMilliseconds)
    {
        var timeout = TimeSpan.FromMilliseconds(Math.Max(180, focusDelayMilliseconds + 120));
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            if (NativeMethods.IsSameRootWindow(NativeMethods.GetForegroundWindow(), browserHandle))
            {
                if (focusDelayMilliseconds > 0)
                {
                    Thread.Sleep(focusDelayMilliseconds);
                }

                return true;
            }

            Thread.Sleep(5);
        }

        return false;
    }

    private static bool TryResolveBrowserWindow(NativePoint point, ScrollerSettings settings, out BrowserWindow browserWindow)
    {
        var start = NativeMethods.WindowFromPoint(point);
        foreach (var candidate in EnumerateWindowCandidates(start))
        {
            if (candidate == IntPtr.Zero || !NativeMethods.IsWindowVisible(candidate))
            {
                continue;
            }

            var className = NativeMethods.GetWindowClassName(candidate);
            if (!className.StartsWith(ChromiumWindowClassPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            NativeMethods.GetWindowThreadProcessId(candidate, out var processId);
            if (processId == 0)
            {
                continue;
            }

            string processName;
            try
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch
            {
                continue;
            }

            if (!settings.BrowserProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!NativeMethods.TryGetVisibleWindowBounds(candidate, out var bounds))
            {
                continue;
            }

            browserWindow = new BrowserWindow(candidate, processName, bounds);
            return true;
        }

        browserWindow = default;
        return false;
    }

    private static IEnumerable<IntPtr> EnumerateWindowCandidates(IntPtr start)
    {
        var seen = new HashSet<IntPtr>();

        foreach (var candidate in new[]
        {
            start,
            NativeMethods.GetAncestor(start, NativeMethods.GaRoot),
            NativeMethods.GetAncestor(start, NativeMethods.GaRootOwner)
        })
        {
            if (candidate != IntPtr.Zero && seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        var parent = NativeMethods.GetParent(start);
        var depth = 0;
        while (parent != IntPtr.Zero && depth++ < 8)
        {
            if (seen.Add(parent))
            {
                yield return parent;
            }

            parent = NativeMethods.GetParent(parent);
        }
    }

    private static bool IsPointInsideSidebar(NativePoint point, NativeRect bounds, ScrollerSettings settings)
    {
        var availableWidth = Math.Max(1, bounds.Width);
        var sidebarWidth = Math.Min(settings.SidebarWidth, availableWidth / 2);
        var top = bounds.Top + Math.Min(settings.TopInset, Math.Max(0, bounds.Height - 1));
        var bottom = bounds.Bottom - Math.Min(settings.BottomInset, Math.Max(0, bounds.Height - 1));

        if (point.Y < top || point.Y > bottom)
        {
            return false;
        }

        var insideLeft = point.X >= bounds.Left && point.X <= bounds.Left + sidebarWidth;
        var insideRight = point.X <= bounds.Right && point.X >= bounds.Right - sidebarWidth;

        return settings.SidebarSide switch
        {
            SidebarSide.Left => insideLeft,
            SidebarSide.Right => insideRight,
            _ => insideLeft || insideRight
        };
    }

    private readonly record struct BrowserWindow(IntPtr Handle, string ProcessName, NativeRect Bounds);
}
