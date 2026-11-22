using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SideTabScroller.Native;

namespace SideTabScroller.Services;

internal sealed unsafe class NativeTrayHost : IDisposable
{
    private const string WindowClassName = "SideTabScroller_TrayHostWindow";
    private const uint TrayIconId = 1;
    private const uint WmTrayCallback = NativeMethods.WmApp + 1;
    private const uint WmUpdateTray = NativeMethods.WmApp + 2;
    private const uint WmRequestExit = NativeMethods.WmApp + 3;

    private const nuint CmdOpenSettings = 1001;
    private const nuint CmdToggleEnabled = 1002;
    private const nuint CmdExit = 1003;

    private static NativeTrayHost? _activeHost;

    private readonly AppController _controller;
    private readonly Action _showSettings;
    private readonly nint _hInstance;
    private readonly uint _taskbarCreatedMessage;
    private readonly uint _showMeMessage;
    private readonly nint _hwnd;
    private readonly nint _iconHandle;
    private readonly bool _ownsIcon;

    private bool _iconAdded;
    private bool _disposed;

    public NativeTrayHost(AppController controller, Action showSettings)
    {
        _controller = controller;
        _showSettings = showSettings;
        _hInstance = NativeMethods.GetModuleHandleW(null);
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
        _showMeMessage = NativeMethods.RegisterWindowMessageW("SideTabScroller_ShowMeMessage");

        (_iconHandle, _ownsIcon) = LoadTrayIcon();
        _activeHost = this;

        try
        {
            fixed (char* classNamePtr = WindowClassName)
            {
                var wc = new NativeMethods.WndClassExW
                {
                    CbSize = (uint)sizeof(NativeMethods.WndClassExW),
                    LpfnWndProc = &WndProc,
                    HInstance = _hInstance,
                    LpszClassName = classNamePtr
                };

                if (NativeMethods.RegisterClassExW(&wc) == 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != 1410) // ERROR_CLASS_ALREADY_EXISTS
                    {
                        throw NativeMethods.LastWin32Exception();
                    }
                }
            }

            // Must be a top-level hidden window (hWndParent = 0), NOT HWND_MESSAGE,
            // so HWND_BROADCAST, TaskbarCreated, WM_POWERBROADCAST, and WM_QUERYENDSESSION are delivered.
            _hwnd = NativeMethods.CreateWindowExW(
                0,
                WindowClassName,
                "SideTabScroller Tray Host",
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                _hInstance,
                0);

            if (_hwnd == 0)
            {
                throw NativeMethods.LastWin32Exception();
            }

            AddTrayIcon();
            UpdateTooltip();

            _controller.StateChanged += OnControllerStateChanged;
        }
        catch
        {
            CleanupHandlesOnFailure();
            throw;
        }
    }

    public void RunMessageLoop()
    {
        while (NativeMethods.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }
    }

    public void RequestExit()
    {
        if (!_disposed && _hwnd != 0)
        {
            NativeMethods.PostMessageW(_hwnd, WmRequestExit, 0, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.StateChanged -= OnControllerStateChanged;

        if (_hwnd != 0)
        {
            RemoveTrayIcon();
            NativeMethods.DestroyWindow(_hwnd);
        }

        NativeMethods.UnregisterClassW(WindowClassName, _hInstance);

        if (_ownsIcon && _iconHandle != 0)
        {
            NativeMethods.DestroyIcon(_iconHandle);
        }

        if (ReferenceEquals(_activeHost, this))
        {
            _activeHost = null;
        }
    }

    private void CleanupHandlesOnFailure()
    {
        if (_hwnd != 0)
        {
            RemoveTrayIcon();
            NativeMethods.DestroyWindow(_hwnd);
        }

        NativeMethods.UnregisterClassW(WindowClassName, _hInstance);

        if (_ownsIcon && _iconHandle != 0)
        {
            NativeMethods.DestroyIcon(_iconHandle);
        }

        if (ReferenceEquals(_activeHost, this))
        {
            _activeHost = null;
        }
    }

    private void OnControllerStateChanged()
    {
        if (!_disposed && _hwnd != 0)
        {
            NativeMethods.PostMessageW(_hwnd, WmUpdateTray, 0, 0);
        }
    }

    private void AddTrayIcon()
    {
        // Classic tray callback mode (without NIM_SETVERSION=4) delivers clean WM_LBUTTONDBLCLK / WM_RBUTTONUP in lParam
        var nid = CreateNotifyIconData(
            NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip,
            GetTooltipText());
        nid.UCallbackMessage = WmTrayCallback;
        nid.HIcon = _iconHandle;

        if (NativeMethods.Shell_NotifyIconW(NativeMethods.NimAdd, &nid))
        {
            _iconAdded = true;
        }
    }

    private void UpdateTooltip()
    {
        if (!_iconAdded || _hwnd == 0)
        {
            return;
        }

        var nid = CreateNotifyIconData(NativeMethods.NifTip, GetTooltipText());
        NativeMethods.Shell_NotifyIconW(NativeMethods.NimModify, &nid);
    }

    private void RemoveTrayIcon()
    {
        if (!_iconAdded || _hwnd == 0)
        {
            return;
        }

        var nid = CreateNotifyIconData(0, null);
        NativeMethods.Shell_NotifyIconW(NativeMethods.NimDelete, &nid);
        _iconAdded = false;
    }

    private NativeMethods.NotifyIconDataW CreateNotifyIconData(uint flags, string? tip)
    {
        var nid = new NativeMethods.NotifyIconDataW
        {
            CbSize = (uint)sizeof(NativeMethods.NotifyIconDataW),
            HWnd = _hwnd,
            UID = TrayIconId,
            UFlags = flags
        };

        if (!string.IsNullOrEmpty(tip))
        {
            int copyLen = Math.Min(tip.Length, 127);
            tip.AsSpan(0, copyLen).CopyTo(new Span<char>(nid.SzTip, 128));
            nid.SzTip[copyLen] = '\0';
        }

        return nid;
    }

    private string GetTooltipText()
    {
        var snapshot = _controller.Snapshot;
        if (!snapshot.Enabled)
        {
            return "侧栏滚轮切换标签 - 已停用";
        }

        return _controller.IsHookRunning
            ? "侧栏滚轮切换标签 - 正在监听"
            : "侧栏滚轮切换标签 - 钩子不可用";
    }

    private string GetStatusText()
    {
        var snapshot = _controller.Snapshot;
        if (!snapshot.Enabled)
        {
            return "状态: 已停用";
        }

        return _controller.IsHookRunning
            ? "状态: 正在监听侧边栏滚轮"
            : "状态: 鼠标钩子不可用";
    }

    private void ShowContextMenu()
    {
        nint hMenu = NativeMethods.CreatePopupMenu();
        if (hMenu == 0)
        {
            return;
        }

        try
        {
            NativeMethods.AppendMenuW(hMenu, NativeMethods.MfString | NativeMethods.MfGrayed, 0, GetStatusText());
            NativeMethods.AppendMenuW(hMenu, NativeMethods.MfSeparator, 0, null);
            NativeMethods.AppendMenuW(hMenu, NativeMethods.MfString, CmdOpenSettings, "打开设置");
            NativeMethods.AppendMenuW(
                hMenu,
                NativeMethods.MfString,
                CmdToggleEnabled,
                _controller.Snapshot.Enabled ? "停用" : "启用");
            NativeMethods.AppendMenuW(hMenu, NativeMethods.MfSeparator, 0, null);
            NativeMethods.AppendMenuW(hMenu, NativeMethods.MfString, CmdExit, "退出");

            NativeMethods.GetCursorPos(out var pt);
            NativeMethods.SetForegroundWindow(_hwnd);

            int cmd = NativeMethods.TrackPopupMenuEx(
                hMenu,
                NativeMethods.TpmRightButton | NativeMethods.TpmReturnCmd | NativeMethods.TpmNonotify,
                pt.X,
                pt.Y,
                _hwnd,
                0);

            // Required by TrackPopupMenu documentation to dismiss properly on next click
            NativeMethods.PostMessageW(_hwnd, NativeMethods.WmNull, 0, 0);

            // Destroy menu before invoking commands
            NativeMethods.DestroyMenu(hMenu);
            hMenu = 0;

            switch ((nuint)cmd)
            {
                case CmdOpenSettings:
                    BeginShowSettings();
                    break;
                case CmdToggleEnabled:
                    _controller.ToggleEnabled();
                    break;
                case CmdExit:
                    NativeMethods.PostQuitMessage(0);
                    break;
            }
        }
        finally
        {
            if (hMenu != 0)
            {
                NativeMethods.DestroyMenu(hMenu);
            }
        }
    }

    private void BeginShowSettings()
    {
        try
        {
            _showSettings();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            var host = _activeHost;
            if (host == null)
            {
                return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
            }

            if (msg == host._showMeMessage && host._showMeMessage != 0)
            {
                host.BeginShowSettings();
                return 0;
            }

            if (msg == host._taskbarCreatedMessage && host._taskbarCreatedMessage != 0)
            {
                host.AddTrayIcon();
                host.UpdateTooltip();
                return 0;
            }

            switch (msg)
            {
                case WmTrayCallback:
                {
                    if (lParam == (nint)NativeMethods.WmLButtonDblClk)
                    {
                        host.BeginShowSettings();
                    }
                    else if (lParam == (nint)NativeMethods.WmRButtonUp)
                    {
                        host.ShowContextMenu();
                    }
                    return 0;
                }

                case NativeMethods.WmContextMenu:
                    host.ShowContextMenu();
                    return 0;

                case WmUpdateTray:
                    host.UpdateTooltip();
                    return 0;

                case WmRequestExit:
                    NativeMethods.PostQuitMessage(0);
                    return 0;

                case NativeMethods.WmPowerBroadcast:
                {
                    int powerEvent = unchecked((int)wParam);
                    if (powerEvent is NativeMethods.PbtApmResumeAutomatic
                        or NativeMethods.PbtApmResumeSuspend
                        or NativeMethods.PbtApmPowerStatusChange)
                    {
                        ThreadPool.QueueUserWorkItem(static state => ((AppController)state!).HandlePowerResume(), host._controller);
                    }
                    return 1;
                }

                case NativeMethods.WmQueryEndSession:
                    // Never block OS shutdown/restart
                    return 1;

                case NativeMethods.WmEndSession:
                    if (wParam != 0)
                    {
                        NativeMethods.PostQuitMessage(0);
                    }
                    return 0;

                case NativeMethods.WmDestroy:
                    if (!host._disposed)
                    {
                        NativeMethods.PostQuitMessage(0);
                    }
                    return 0;
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }

        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private (nint Handle, bool OwnsHandle) LoadTrayIcon()
    {
        uint dpi = NativeMethods.GetDpiForSystem();
        if (dpi == 0) dpi = 96;

        int cx = Math.Max(16, NativeMethods.GetSmallIconMetric(NativeMethods.SmCxSmIcon, dpi));
        int cy = Math.Max(16, NativeMethods.GetSmallIconMetric(NativeMethods.SmCySmIcon, dpi));

        // 1. Load embedded Win32 application icon resource (standard .NET SDK ApplicationIcon ID is 32512)
        nint icon = NativeMethods.LoadImageW(_hInstance, NativeMethods.IdiApplication, NativeMethods.ImageIcon, cx, cy, 0);
        if (icon != 0)
        {
            return (icon, true);
        }

        // 2. Fallback: extract first small icon from the executable path
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            nint smallIcon = 0;
            uint extracted = NativeMethods.ExtractIconExW(exePath, 0, null, &smallIcon, 1);
            if (extracted > 0 && smallIcon != 0)
            {
                return (smallIcon, true);
            }
        }

        // 3. Final fallback: shared system application icon
        nint sysIcon = NativeMethods.LoadImageW(0, NativeMethods.IdiApplication, NativeMethods.ImageIcon, cx, cy, NativeMethods.LrShared);
        return (sysIcon, false);
    }
}
