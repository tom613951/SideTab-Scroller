using System.Diagnostics;
using System.Threading.Channels;
using SideTabScroller.Models;
using SideTabScroller.Native;

namespace SideTabScroller.Services;

internal sealed class BrowserTabSwitcher : IDisposable
{
    private const long WindowCacheTtlMs = 500;
    private const long ProcessCacheTtlMs = 15_000;
    private const int ProcessCacheCapacity = 64;
    private const long WheelAccumulatorResetMs = 250;
    private const int FocusRestoreDelayMs = 300;
    private const int MaxOverlayZOrderWalk = 128;

    private readonly Func<ScrollerSettingsSnapshot> _settingsProvider;
    private readonly Action<SwitchResult> _onSwitch;
    private readonly Channel<WheelTask> _channel;
    private readonly CancellationTokenSource _workerCts = new();
    private readonly Task _workerTask;

    private readonly Lock _focusLock = new();
    private readonly System.Threading.Timer _restoreTimer;
    private nint _originalForegroundWindow;
    private nint _lastTargetedBrowserWindow;

    // Accessed exclusively by the single hook callback thread
    private readonly Dictionary<uint, ProcessCacheEntry> _processCache = new(32);
    private WindowCacheEntry _lastWindow;
    private nint _wheelAccumHwnd;
    private int _wheelAccumulator;
    private long _wheelAccumLastTick;

    private volatile bool _disposed;

    public BrowserTabSwitcher(Func<ScrollerSettingsSnapshot> settingsProvider, Action<SwitchResult> onSwitch)
    {
        _settingsProvider = settingsProvider;
        _onSwitch = onSwitch;
        _channel = Channel.CreateBounded<WheelTask>(new BoundedChannelOptions(8)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _restoreTimer = new System.Threading.Timer(RestoreTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
        _workerTask = Task.Run(RunWorkerAsync);
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

        var now = Environment.TickCount64;
        var start = NativeMethods.WindowFromPoint(point);
        var root = start != 0 ? NativeMethods.GetAncestor(start, NativeMethods.GaRoot) : 0;

        // 1. Unified fast-path cache check (eliminates cache thrashing on normal web-page scrolling)
        if (root != 0 &&
            root == _lastWindow.Hwnd &&
            _lastWindow.SettingsVersion == settings.Version &&
            now < _lastWindow.ExpiryTick64 &&
            _lastWindow.Bounds.Contains(point))
        {
            if (!_lastWindow.IsBrowser)
            {
                return false;
            }

            if (!IsPointInsideSidebar(point, _lastWindow.Bounds, settings, _lastWindow.Dpi))
            {
                // Cursor is over browser content area, not the vertical tab sidebar
                return false;
            }

            return TryConsumeAndEnqueueWheel(_lastWindow.Hwnd, _lastWindow.ProcessName!, wheelDelta, settings, now);
        }

        // 2. Cache miss: resolve whether cursor is over an unobscured browser window
        if (!TryResolveBrowserWindow(point, start, root, settings, out var browserWindow))
        {
            // Cache non-transparent non-browser windows using cheap GetWindowRect (zero DWM IPC)
            if (root != 0)
            {
                var exStyle = NativeMethods.GetWindowLongPtr(root, NativeMethods.GwlExstyle);
                if ((exStyle & NativeMethods.WsExTransparent) == 0 &&
                    NativeMethods.GetWindowRect(root, out var nbRect) &&
                    nbRect.Width > 0 &&
                    nbRect.Height > 0)
                {
                    _lastWindow = new WindowCacheEntry
                    {
                        Hwnd = root,
                        Bounds = nbRect,
                        Dpi = 0,
                        ProcessName = null,
                        IsBrowser = false,
                        SettingsVersion = settings.Version,
                        ExpiryTick64 = now + WindowCacheTtlMs
                    };
                }
            }

            return false;
        }

        var dpi = NativeMethods.GetDpiForWindow(browserWindow.Handle);
        if (dpi == 0) dpi = NativeMethods.GetDpiForSystem();
        if (dpi == 0) dpi = 96;

        _lastWindow = new WindowCacheEntry
        {
            Hwnd = browserWindow.Handle,
            Bounds = browserWindow.Bounds,
            Dpi = dpi,
            ProcessName = browserWindow.ProcessName,
            IsBrowser = true,
            SettingsVersion = settings.Version,
            ExpiryTick64 = now + WindowCacheTtlMs
        };

        if (!IsPointInsideSidebar(point, browserWindow.Bounds, settings, dpi))
        {
            return false;
        }

        return TryConsumeAndEnqueueWheel(browserWindow.Handle, browserWindow.ProcessName, wheelDelta, settings, now);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        _workerCts.Cancel();

        try
        {
            _workerTask.Wait(500);
        }
        catch
        {
            // Ignore cancellation/wait exceptions during teardown
        }

        _restoreTimer.Dispose();
        _workerCts.Dispose();
    }

    private bool TryConsumeAndEnqueueWheel(
        nint browserHandle,
        string processName,
        int wheelDelta,
        ScrollerSettingsSnapshot settings,
        long nowTick64)
    {
        int steps = AccumulateWheelSteps(browserHandle, wheelDelta, nowTick64);
        if (steps == 0)
        {
            // Swallow high-resolution sub-notch deltas inside the sidebar to prevent accidental page jitter
            return settings.ConsumeHandledWheelEvents;
        }

        var direction = ResolveDirection(wheelDelta, settings);
        int clampedSteps = Math.Min(steps, 4);
        for (int i = 0; i < clampedSteps; i++)
        {
            _channel.Writer.TryWrite(new WheelTask(browserHandle, direction, settings, processName, nowTick64));
        }

        return settings.ConsumeHandledWheelEvents;
    }

    private int AccumulateWheelSteps(nint hwnd, int wheelDelta, long nowTick64)
    {
        if (Math.Abs(wheelDelta) >= NativeMethods.WheelDelta)
        {
            _wheelAccumHwnd = hwnd;
            _wheelAccumulator = 0;
            _wheelAccumLastTick = nowTick64;
            return Math.Abs(wheelDelta) / NativeMethods.WheelDelta;
        }

        // High-precision wheel / touchpad: reset accumulator if window changed, timed out, or direction flipped
        if (_wheelAccumHwnd != hwnd ||
            nowTick64 - _wheelAccumLastTick > WheelAccumulatorResetMs ||
            (_wheelAccumulator != 0 && Math.Sign(_wheelAccumulator) != Math.Sign(wheelDelta)))
        {
            _wheelAccumHwnd = hwnd;
            _wheelAccumulator = 0;
        }

        _wheelAccumLastTick = nowTick64;
        _wheelAccumulator += wheelDelta;

        int steps = Math.Abs(_wheelAccumulator) / NativeMethods.WheelDelta;
        if (steps > 0)
        {
            _wheelAccumulator %= NativeMethods.WheelDelta;
        }

        return steps;
    }

    private async Task RunWorkerAsync()
    {
        var reader = _channel.Reader;
        var token = _workerCts.Token;

        try
        {
            while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var task))
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

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
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private void ProcessWheelTask(WheelTask task)
    {
        if (Environment.TickCount64 - task.CreatedAtTick64 > 400)
        {
            return;
        }

        if (FocusAndSendShortcut(task.BrowserHandle, task.Direction, task.Settings))
        {
            _onSwitch(new SwitchResult(DateTime.Now, task.ProcessName, task.Direction));
        }
    }

    internal static SwitchDirection ResolveDirection(int wheelDelta, ScrollerSettingsSnapshot settings)
    {
        var wheelUpMeansPrevious = wheelDelta > 0;
        if (settings.ReverseDirection)
        {
            wheelUpMeansPrevious = !wheelUpMeansPrevious;
        }

        return wheelUpMeansPrevious ? SwitchDirection.Previous : SwitchDirection.Next;
    }

    private bool FocusAndSendShortcut(nint browserHandle, SwitchDirection direction, ScrollerSettingsSnapshot settings)
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

                _restoreTimer.Change(Timeout.Infinite, Timeout.Infinite);

                if (settings.RestorePreviousFocus)
                {
                    if (_originalForegroundWindow == 0)
                    {
                        _originalForegroundWindow = previousForeground;
                    }
                }
                else
                {
                    _originalForegroundWindow = 0;
                }

                if (NativeMethods.IsIconic(browserHandle))
                {
                    NativeMethods.ShowWindow(browserHandle, NativeMethods.SwRestore);
                }

                NativeMethods.SetForegroundWindow(browserHandle);
                if (!WaitForForeground(browserHandle, settings.FocusDelayMilliseconds))
                {
                    _originalForegroundWindow = 0;
                    return false;
                }
            }
            else if (!settings.RestorePreviousFocus)
            {
                _originalForegroundWindow = 0;
            }

            var previous = direction == SwitchDirection.Previous;
            var sent = settings.ShortcutMode == TabSwitchShortcutMode.CtrlPage
                ? NativeMethods.SendCtrlPageKey(previous)
                : NativeMethods.SendCtrlTabKey(previous);

            if (settings.RestorePreviousFocus && _originalForegroundWindow != 0 && !_disposed)
            {
                _lastTargetedBrowserWindow = browserHandle;
                try
                {
                    _restoreTimer.Change(FocusRestoreDelayMs, Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                    // Safe during concurrent Dispose
                }
            }

            return sent;
        }
    }

    private void RestoreTimerCallback(object? state)
    {
        if (_disposed)
        {
            return;
        }

        lock (_focusLock)
        {
            var savedOriginal = _originalForegroundWindow;
            var targetBrowser = _lastTargetedBrowserWindow;
            _originalForegroundWindow = 0;
            _lastTargetedBrowserWindow = 0;

            if (savedOriginal == 0 || targetBrowser == 0)
            {
                return;
            }

            if (!NativeMethods.IsWindow(savedOriginal) || !NativeMethods.IsWindowVisible(savedOriginal))
            {
                return;
            }

            var currentForeground = NativeMethods.GetForegroundWindow();
            if (NativeMethods.IsSameRootWindow(currentForeground, targetBrowser))
            {
                NativeMethods.SetForegroundWindow(savedOriginal);
            }
        }
    }

    private static bool WaitForForeground(nint browserHandle, int focusDelayMilliseconds)
    {
        var timeoutMs = Math.Max(180, focusDelayMilliseconds + 120);
        var startTick = Environment.TickCount64;

        while (Environment.TickCount64 - startTick < timeoutMs)
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

    private string? GetMatchedBrowserProcessName(uint processId, ScrollerSettingsSnapshot settings)
    {
        var now = Environment.TickCount64;
        if (_processCache.TryGetValue(processId, out var cached) && now < cached.ExpiryTick64)
        {
            if (cached.SettingsVersion == settings.Version)
            {
                return cached.MatchedBrowserName;
            }

            if (cached.RawName != null)
            {
                var reMatched = settings.TryGetMatchedBrowserName(cached.RawName.AsSpan(), out var foundName)
                    ? foundName
                    : null;
                _processCache[processId] = cached with
                {
                    SettingsVersion = settings.Version,
                    MatchedBrowserName = reMatched
                };
                return reMatched;
            }
            // Negative-cached entry under an older SettingsVersion: fall through to re-resolve
        }

        if (_processCache.Count >= ProcessCacheCapacity)
        {
            PruneProcessCache(now);
        }

        Span<char> nameBuffer = stackalloc char[512];
        if (!NativeMethods.TryGetProcessName(processId, nameBuffer, out var processNameSpan))
        {
            _processCache[processId] = new ProcessCacheEntry(null, null, settings.Version, now + ProcessCacheTtlMs);
            return null;
        }

        if (settings.TryGetMatchedBrowserName(processNameSpan, out var matched))
        {
            _processCache[processId] = new ProcessCacheEntry(matched, matched, settings.Version, now + ProcessCacheTtlMs);
            return matched;
        }

        // Negative-cache non-browser PIDs without retaining a raw string unless needed
        _processCache[processId] = new ProcessCacheEntry(null, null, settings.Version, now + ProcessCacheTtlMs);
        return null;
    }

    private void PruneProcessCache(long now)
    {
        Span<uint> toRemove = stackalloc uint[ProcessCacheCapacity];
        int removeCount = 0;

        foreach (var kvp in _processCache)
        {
            if (now >= kvp.Value.ExpiryTick64)
            {
                toRemove[removeCount++] = kvp.Key;
            }
        }

        if (removeCount == 0)
        {
            _processCache.Clear();
            return;
        }

        for (int i = 0; i < removeCount; i++)
        {
            _processCache.Remove(toRemove[i]);
        }
    }

    private bool IsBrowserWindow(nint hWnd, ScrollerSettingsSnapshot settings, out string processName, out NativeRect bounds)
    {
        processName = string.Empty;
        bounds = default;

        if (hWnd == 0 || !NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd))
        {
            return false;
        }

        if (!NativeMethods.IsChromiumWindowClass(hWnd))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hWnd, out var processId);
        if (processId == 0)
        {
            return false;
        }

        var matchedName = GetMatchedBrowserProcessName(processId, settings);
        if (matchedName == null)
        {
            return false;
        }

        if (!NativeMethods.TryGetVisibleWindowBounds(hWnd, out bounds))
        {
            return false;
        }

        processName = matchedName;
        return true;
    }

    private bool TryResolveBrowserWindow(
        NativePoint point,
        nint start,
        nint rootWindow,
        ScrollerSettingsSnapshot settings,
        out BrowserWindow browserWindow)
    {
        // 1. Direct candidate path along parent chain (zero heap allocation via stackalloc)
        Span<nint> seen = stackalloc nint[11];
        int seenCount = 0;

        TryAddCandidate(start, seen, ref seenCount);
        TryAddCandidate(rootWindow, seen, ref seenCount);
        if (start != 0)
        {
            TryAddCandidate(NativeMethods.GetAncestor(start, NativeMethods.GaRootOwner), seen, ref seenCount);
            var parent = NativeMethods.GetParent(start);
            int depth = 0;
            while (parent != 0 && depth++ < 8 && seenCount < seen.Length)
            {
                TryAddCandidate(parent, seen, ref seenCount);
                parent = NativeMethods.GetParent(parent);
            }
        }

        for (int i = 0; i < seenCount; i++)
        {
            if (IsBrowserWindow(seen[i], settings, out var processName, out var bounds))
            {
                browserWindow = new BrowserWindow(seen[i], processName, bounds);
                return true;
            }
        }

        // 2. Only fall back to Z-order walk when the top window under cursor is click-through (WS_EX_TRANSPARENT)
        if (rootWindow != 0)
        {
            var rootExStyle = NativeMethods.GetWindowLongPtr(rootWindow, NativeMethods.GwlExstyle);
            if ((rootExStyle & NativeMethods.WsExTransparent) == 0)
            {
                browserWindow = default;
                return false;
            }

            var current = NativeMethods.GetWindow(rootWindow, NativeMethods.GwHwndNext);
            int visibleEvaluated = 0;
            int totalWalked = 0;
            while (current != 0 && visibleEvaluated < MaxOverlayZOrderWalk && totalWalked++ < 1024)
            {
                if (NativeMethods.IsWindowVisible(current) &&
                    !NativeMethods.IsIconic(current) &&
                    !NativeMethods.IsWindowCloaked(current))
                {
                    visibleEvaluated++;
                    if (NativeMethods.GetWindowRect(current, out var roughRect) && roughRect.Contains(point))
                    {
                        var exStyle = NativeMethods.GetWindowLongPtr(current, NativeMethods.GwlExstyle);
                        if ((exStyle & NativeMethods.WsExTransparent) != 0)
                        {
                            current = NativeMethods.GetWindow(current, NativeMethods.GwHwndNext);
                            continue;
                        }

                        // Stop at the first non-transparent visible window covering the cursor
                        if (IsBrowserWindow(current, settings, out var procName, out var bounds) && bounds.Contains(point))
                        {
                            browserWindow = new BrowserWindow(current, procName, bounds);
                            return true;
                        }

                        break;
                    }
                }

                current = NativeMethods.GetWindow(current, NativeMethods.GwHwndNext);
            }
        }

        browserWindow = default;
        return false;
    }

    private static void TryAddCandidate(nint candidate, Span<nint> seen, ref int seenCount)
    {
        if (candidate == 0 || seenCount >= seen.Length)
        {
            return;
        }

        for (int i = 0; i < seenCount; i++)
        {
            if (seen[i] == candidate)
            {
                return;
            }
        }

        seen[seenCount++] = candidate;
    }

    internal static bool IsPointInsideSidebar(NativePoint point, NativeRect bounds, ScrollerSettingsSnapshot settings, uint dpi)
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

    private struct WindowCacheEntry
    {
        public nint Hwnd;
        public NativeRect Bounds;
        public uint Dpi;
        public string? ProcessName;
        public bool IsBrowser;
        public int SettingsVersion;
        public long ExpiryTick64;
    }

    private readonly record struct ProcessCacheEntry(
        string? RawName,
        string? MatchedBrowserName,
        int SettingsVersion,
        long ExpiryTick64);

    private readonly record struct WheelTask(
        nint BrowserHandle,
        SwitchDirection Direction,
        ScrollerSettingsSnapshot Settings,
        string ProcessName,
        long CreatedAtTick64);

    private readonly record struct BrowserWindow(nint Handle, string ProcessName, NativeRect Bounds);
}
