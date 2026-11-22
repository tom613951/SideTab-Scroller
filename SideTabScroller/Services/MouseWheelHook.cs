using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SideTabScroller.Native;

namespace SideTabScroller.Services;

internal sealed unsafe class MouseWheelHook : IDisposable
{
    private static MouseWheelHook? _activeInstance;

    private readonly Func<NativePoint, int, bool> _handleWheel;
    private readonly object _lifecycleLock = new();

    private Thread? _hookThread;
    private uint _hookThreadId;
    private nint _hookHandle;
    private string? _lastError;
    private volatile bool _disposed;

    public MouseWheelHook(Func<NativePoint, int, bool> handleWheel)
    {
        _handleWheel = handleWheel;
    }

    public bool IsRunning => Volatile.Read(ref _hookHandle) != 0;
    public string? LastError
    {
        get => Volatile.Read(ref _lastError);
        private set => Volatile.Write(ref _lastError, value);
    }

    public void Start()
    {
        lock (_lifecycleLock)
        {
            StartCore();
        }
    }

    public void Stop()
    {
        lock (_lifecycleLock)
        {
            StopCore();
        }
    }

    public void Restart()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            StopCore();
            StartCore();
        }
    }

    private void StartCore()
    {
        if (_disposed || IsRunning)
        {
            return;
        }

        LastError = null;
        using var ready = new ManualResetEventSlim(false);
        string? startupError = null;

        var thread = new Thread(() => HookThreadProc(ready, ref startupError))
        {
            IsBackground = true,
            Name = "MouseWheelHookThread",
            Priority = ThreadPriority.AboveNormal
        };

        _hookThread = thread;
        thread.Start();
        ready.Wait();

        if (!string.IsNullOrEmpty(startupError))
        {
            LastError = startupError;
            _hookThread = null;
            _hookThreadId = 0;
        }
    }

    private void StopCore()
    {
        var threadToJoin = _hookThread;
        uint threadId = _hookThreadId;
        if (threadToJoin == null)
        {
            return;
        }

        if (threadId != 0)
        {
            NativeMethods.PostThreadMessageW(threadId, NativeMethods.WmQuit, 0, 0);
        }

        if (Thread.CurrentThread != threadToJoin && threadToJoin.Join(1500))
        {
            _hookThread = null;
            _hookThreadId = 0;
        }
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopCore();
        }
    }

    private void HookThreadProc(ManualResetEventSlim ready, ref string? startupError)
    {
        nint handle = 0;

        try
        {
            _hookThreadId = NativeMethods.GetCurrentThreadId();

            // Force creation of the Win32 message queue before installing WH_MOUSE_LL
            NativeMethods.PeekMessageW(out _, 0, 0, 0, NativeMethods.PmNoRemove);

            Volatile.Write(ref _activeInstance, this);
            handle = NativeMethods.SetWindowsHookExW(NativeMethods.WhMouseLl, &HookCallback, 0, 0);
            Volatile.Write(ref _hookHandle, handle);

            if (handle == 0)
            {
                startupError = NativeMethods.LastWin32Exception().Message;
                Volatile.Write(ref _activeInstance, null);
                ready.Set();
                return;
            }

            ready.Set();

            while (NativeMethods.GetMessageW(out var msg, 0, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessageW(ref msg);
            }
        }
        catch (Exception ex)
        {
            startupError ??= ex.Message;
            if (!ready.IsSet)
            {
                ready.Set();
            }
            ErrorLog.Write(ex);
        }
        finally
        {
            if (handle != 0)
            {
                NativeMethods.UnhookWindowsHookEx(handle);
            }

            Volatile.Write(ref _hookHandle, 0);
            if (ReferenceEquals(Volatile.Read(ref _activeInstance), this))
            {
                Volatile.Write(ref _activeInstance, null);
            }
            _hookThreadId = 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        var instance = Volatile.Read(ref _activeInstance);
        nint hookHandle = instance != null ? Volatile.Read(ref instance._hookHandle) : 0;

        if (nCode >= 0 && wParam == (nint)NativeMethods.WmMouseWheel && instance != null)
        {
            try
            {
                var mouse = *(NativeMethods.LowLevelMouseStruct*)lParam;
                var delta = unchecked((short)((mouse.MouseData >> 16) & 0xffff));

                if (instance._handleWheel(mouse.Point, delta))
                {
                    return 1;
                }
            }
            catch (Exception ex)
            {
                instance.LastError = ex.Message;
            }
        }

        return NativeMethods.CallNextHookEx(hookHandle, nCode, wParam, lParam);
    }
}
