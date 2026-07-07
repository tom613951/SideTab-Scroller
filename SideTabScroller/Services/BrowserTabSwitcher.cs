using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SideTabScroller.Models;
using SideTabScroller.Native;

namespace SideTabScroller.Services;

internal sealed class BrowserTabSwitcher
{
    private const string ChromiumWindowClassPrefix = "Chrome_WidgetWin_";

    private readonly Func<ScrollerSettings> _settingsProvider;
    private readonly Action<SwitchResult> _onSwitch;
    private readonly Channel<WheelTask> _channel;

    private readonly object _focusLock = new();
    private IntPtr _originalForegroundWindow = IntPtr.Zero;
    private CancellationTokenSource? _restoreCts;

    private struct BrowserCache
    {
        public IntPtr Handle;
        public NativeRect Bounds;
        public uint Dpi;
        public string ProcessName;
        public long ExpiryTicks;
    }

    private struct NonBrowserCache
    {
        public IntPtr Handle;
        public NativeRect Bounds;
        public long ExpiryTicks;
    }

    private BrowserCache _browserCache;
    private NonBrowserCache _nonBrowserCache;

    public BrowserTabSwitcher(Func<ScrollerSettings> settingsProvider, Action<SwitchResult> onSwitch)
    {
        _settingsProvider = settingsProvider;
        _onSwitch = onSwitch;
        _channel = Channel.CreateBounded<WheelTask>(new BoundedChannelOptions(8)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        StartBackgroundWorker();
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

        var now = DateTime.UtcNow.Ticks;
        var direction = ResolveDirection(wheelDelta, settings);

        // 1. Check Browser Cache
        if (_browserCache.Handle != IntPtr.Zero && now < _browserCache.ExpiryTicks)
        {
            if (IsPointInsideSidebar(point, _browserCache.Bounds, settings, _browserCache.Dpi))
            {
                // Verify the window directly under cursor is still this browser (or its child)
                var currentUnderCursor = NativeMethods.WindowFromPoint(point);
                if (NativeMethods.IsSameRootWindow(currentUnderCursor, _browserCache.Handle))
                {
                    _channel.Writer.TryWrite(new WheelTask(_browserCache.Handle, direction, settings, _browserCache.ProcessName, now));
                    return settings.ConsumeHandledWheelEvents;
                }
            }
        }

        // 2. Check Non-Browser Cache
        if (_nonBrowserCache.Handle != IntPtr.Zero && now < _nonBrowserCache.ExpiryTicks)
        {
            var rect = _nonBrowserCache.Bounds;
            if (point.X >= rect.Left && point.X <= rect.Right &&
                point.Y >= rect.Top && point.Y <= rect.Bottom)
            {
                // Verify the window directly under cursor is still this non-browser window
                var currentUnderCursor = NativeMethods.WindowFromPoint(point);
                if (NativeMethods.IsSameRootWindow(currentUnderCursor, _nonBrowserCache.Handle))
                {
                    return false;
                }
            }
        }

        // 3. Cache Miss: Resolve window
        if (!TryResolveBrowserWindow(point, settings, out var browserWindow))
        {
            var start = NativeMethods.WindowFromPoint(point);
            var root = NativeMethods.GetAncestor(start, NativeMethods.GaRoot);
            if (root != IntPtr.Zero && NativeMethods.TryGetVisibleWindowBounds(root, out var nbBounds))
            {
                _nonBrowserCache = new NonBrowserCache
                {
                    Handle = root,
                    Bounds = nbBounds,
                    ExpiryTicks = now + TimeSpan.FromMilliseconds(800).Ticks
                };
            }
            return false;
        }

        var dpi = NativeMethods.GetDpiForWindow(browserWindow.Handle);
        if (dpi == 0) dpi = NativeMethods.GetDpiForSystem();
        if (dpi == 0) dpi = 96;

        // Cache the resolved browser bounds
        _browserCache = new BrowserCache
        {
            Handle = browserWindow.Handle,
            Bounds = browserWindow.Bounds,
            Dpi = dpi,
            ProcessName = browserWindow.ProcessName,
            ExpiryTicks = now + TimeSpan.FromMilliseconds(800).Ticks
        };

        // Check if cursor inside sidebar bounds
        if (!IsPointInsideSidebar(point, browserWindow.Bounds, settings, dpi))
        {
            return false;
        }

        _channel.Writer.TryWrite(new WheelTask(browserWindow.Handle, direction, settings, browserWindow.ProcessName, now));
        return settings.ConsumeHandledWheelEvents;
    }

    private void StartBackgroundWorker()
    {
        Task.Run(async () =>
        {
            var reader = _channel.Reader;
            while (await reader.WaitToReadAsync())
            {
                while (reader.TryRead(out var task))
                {
                    try
                    {
                        ProcessWheelTask(task);
                    }
                    catch (Exception ex)
                    {
                        ErrorLog.Write(ex);
                    }
                }
            }
        });
    }

    private void ProcessWheelTask(WheelTask task)
    {
        var ageTicks = DateTime.UtcNow.Ticks - task.CreatedAtTicks;
        if (ageTicks > TimeSpan.FromMilliseconds(400).Ticks)
        {
            // Drop expired scroll tasks
            return;
        }

        if (FocusAndSendShortcut(task.BrowserHandle, task.Direction, task.Settings))
        {
            _onSwitch(new SwitchResult(DateTime.Now, task.ProcessName, task.Direction));
        }
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

    private bool FocusAndSendShortcut(IntPtr browserHandle, SwitchDirection direction, ScrollerSettings settings)
    {
        lock (_focusLock)
        {
            var previousForeground = NativeMethods.GetForegroundWindow();
            var browserAlreadyForeground = NativeMethods.IsSameRootWindow(previousForeground, browserHandle);

            if (!browserAlreadyForeground)
            {
                if (!settings.AutofocusBrowser)
                {
                    return false;
                }

                // Cancel pending restore focus task
                _restoreCts?.Cancel();
                _restoreCts = null;

                // Save previous foreground if we haven't already AND restore focus is enabled
                if (settings.RestorePreviousFocus)
                {
                    if (_originalForegroundWindow == IntPtr.Zero)
                    {
                        _originalForegroundWindow = previousForeground;
                    }
                }
                else
                {
                    _originalForegroundWindow = IntPtr.Zero;
                }

                if (NativeMethods.IsIconic(browserHandle))
                {
                    NativeMethods.ShowWindow(browserHandle, NativeMethods.SwRestore);
                }

                NativeMethods.SetForegroundWindow(browserHandle);
                if (!WaitForForeground(browserHandle, settings.FocusDelayMilliseconds))
                {
                    _originalForegroundWindow = IntPtr.Zero;
                    return false;
                }
            }
            else
            {
                // If browser is already foreground, and restore focus is disabled, reset stale window
                if (!settings.RestorePreviousFocus)
                {
                    _originalForegroundWindow = IntPtr.Zero;
                }
            }

            var previous = direction == SwitchDirection.Previous;
            var sent = settings.ShortcutMode == TabSwitchShortcutMode.CtrlPage
                ? NativeMethods.SendCtrlPageKey(previous)
                : NativeMethods.SendCtrlTabKey(previous);

            if (settings.RestorePreviousFocus && _originalForegroundWindow != IntPtr.Zero)
            {
                // Cancel previous restore task
                _restoreCts?.Cancel();
                _restoreCts = new CancellationTokenSource();
                var token = _restoreCts.Token;
                var savedOriginal = _originalForegroundWindow;

                Task.Delay(300, token).ContinueWith(t =>
                {
                    if (t.IsCanceled) return;
                    lock (_focusLock)
                    {
                        if (_originalForegroundWindow == savedOriginal)
                        {
                            // Verify that browser is still the foreground window before switching back
                            var currentForeground = NativeMethods.GetForegroundWindow();
                            if (NativeMethods.IsSameRootWindow(currentForeground, browserHandle))
                            {
                                NativeMethods.SetForegroundWindow(savedOriginal);
                            }
                            _originalForegroundWindow = IntPtr.Zero;
                        }
                    }
                }, TaskScheduler.Default);
            }

            return sent;
        }
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

    private static readonly ConcurrentDictionary<uint, (string Name, long ExpiryTicks)> ProcessNameCache = new();

    private static string? GetProcessNameWithCache(uint processId)
    {
        var now = DateTime.UtcNow.Ticks;
        if (ProcessNameCache.TryGetValue(processId, out var cached) && cached.ExpiryTicks > now)
        {
            return cached.Name;
        }

        var name = NativeMethods.GetProcessName(processId);
        if (name != null)
        {
            var expiry = now + TimeSpan.FromSeconds(3).Ticks;
            ProcessNameCache[processId] = (name, expiry);

            // Clean up expired entries occasionally (1% chance)
            if (Random.Shared.Next(100) == 0)
            {
                var expiredKeys = ProcessNameCache.Where(kvp => kvp.Value.ExpiryTicks <= now).Select(kvp => kvp.Key).ToList();
                foreach (var key in expiredKeys)
                {
                    ProcessNameCache.TryRemove(key, out _);
                }
            }

            return name;
        }

        return null;
    }

    private static bool IsBrowserWindow(IntPtr hWnd, ScrollerSettings settings, out string processName, out NativeRect bounds)
    {
        processName = string.Empty;
        bounds = default;

        // Skip invalid, invisible or minimized windows
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd))
        {
            return false;
        }

        var className = NativeMethods.GetWindowClassName(hWnd);
        if (!className.StartsWith(ChromiumWindowClassPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hWnd, out var processId);
        if (processId == 0)
        {
            return false;
        }

        var name = GetProcessNameWithCache(processId);
        if (name == null || !settings.BrowserProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!NativeMethods.TryGetVisibleWindowBounds(hWnd, out bounds))
        {
            return false;
        }

        processName = name;
        return true;
    }

    private static bool TryResolveBrowserWindow(NativePoint point, ScrollerSettings settings, out BrowserWindow browserWindow)
    {
        // 1. Try resolving window directly under cursor (fast path)
        var start = NativeMethods.WindowFromPoint(point);
        foreach (var candidate in EnumerateWindowCandidates(start))
        {
            if (IsBrowserWindow(candidate, settings, out var processName, out var bounds))
            {
                browserWindow = new BrowserWindow(candidate, processName, bounds);
                return true;
            }
        }

        // If the top-level window covering the cursor is opaque (not transparent/click-through),
        // we should NOT do Z-order fallback search because the browser is obscured.
        var rootWindow = NativeMethods.GetAncestor(start, NativeMethods.GaRoot);
        if (rootWindow != IntPtr.Zero)
        {
            var exStyle = NativeMethods.GetWindowLongPtr(rootWindow, NativeMethods.GwlExstyle).ToInt64();
            var isTransparent = (exStyle & NativeMethods.WsExTransparent) != 0;
            if (!isTransparent)
            {
                browserWindow = default;
                return false;
            }
        }

        // 2. Fallback: Enumerate top-level windows in Z-order to find browser window containing the point (overlay workaround)
        IntPtr foundHandle = IntPtr.Zero;
        string foundProcessName = string.Empty;
        NativeRect foundBounds = default;

        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            if (IsBrowserWindow(hWnd, settings, out var procName, out var rect))
            {
                if (point.X >= rect.Left && point.X <= rect.Right &&
                    point.Y >= rect.Top && point.Y <= rect.Bottom)
                {
                    foundHandle = hWnd;
                    foundProcessName = procName;
                    foundBounds = rect;
                    return false; // stop enumeration
                }
            }
            return true; // continue enumeration
        }, IntPtr.Zero);

        if (foundHandle != IntPtr.Zero)
        {
            browserWindow = new BrowserWindow(foundHandle, foundProcessName, foundBounds);
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

    private static bool IsPointInsideSidebar(NativePoint point, NativeRect bounds, ScrollerSettings settings, uint dpi)
    {
        var scale = dpi / 96.0;

        var availableWidth = Math.Max(1, bounds.Width);
        var scaledSidebarWidth = (int)Math.Round(settings.SidebarWidth * scale);
        var sidebarWidth = Math.Min(scaledSidebarWidth, availableWidth / 2);
        
        var scaledTopInset = (int)Math.Round(settings.TopInset * scale);
        var top = bounds.Top + Math.Min(scaledTopInset, Math.Max(0, bounds.Height - 1));
        
        var scaledBottomInset = (int)Math.Round(settings.BottomInset * scale);
        var bottom = bounds.Bottom - Math.Min(scaledBottomInset, Math.Max(0, bounds.Height - 1));

        if (point.Y < top || point.Y > bottom)
        {
            return false;
        }

        return point.X >= bounds.Left && point.X <= bounds.Left + sidebarWidth;
    }

    private readonly record struct WheelTask(
        IntPtr BrowserHandle,
        SwitchDirection Direction,
        ScrollerSettings Settings,
        string ProcessName,
        long CreatedAtTicks
    );

    private readonly record struct BrowserWindow(IntPtr Handle, string ProcessName, NativeRect Bounds);
}
