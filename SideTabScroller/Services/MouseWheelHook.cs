using System.Diagnostics;
using System.Runtime.InteropServices;
using SideTabScroller.Native;

namespace SideTabScroller.Services;

internal sealed class MouseWheelHook : IDisposable
{
    private readonly Func<NativePoint, int, bool> _handleWheel;
    private NativeMethods.LowLevelMouseProc? _callback;
    private IntPtr _hookHandle;

    public MouseWheelHook(Func<NativePoint, int, bool> handleWheel)
    {
        _handleWheel = handleWheel;
    }

    public bool IsRunning => _hookHandle != IntPtr.Zero;
    public string? LastError { get; private set; }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _callback = HookCallback;
        using var currentProcess = Process.GetCurrentProcess();
        var moduleName = currentProcess.MainModule?.ModuleName;
        var moduleHandle = NativeMethods.GetModuleHandle(moduleName);

        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _callback, moduleHandle, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            LastError = NativeMethods.LastWin32Exception().Message;
        }
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _callback = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && wParam == new IntPtr(NativeMethods.WmMouseWheel))
            {
                var mouse = Marshal.PtrToStructure<NativeMethods.LowLevelMouseStruct>(lParam);
                var delta = unchecked((short)((mouse.MouseData >> 16) & 0xffff));

                if (_handleWheel(mouse.Point, delta))
                {
                    return new IntPtr(1);
                }
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }
}
