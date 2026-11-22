using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SideTabScroller.Native;

internal static unsafe partial class NativeMethods
{
    internal const int WhMouseLl = 14;
    internal const int WheelDelta = 120;

    internal const uint WmNull = 0x0000;
    internal const uint WmDestroy = 0x0002;
    internal const uint WmClose = 0x0010;
    internal const uint WmQueryEndSession = 0x0011;
    internal const uint WmQuit = 0x0012;
    internal const uint WmEndSession = 0x0016;
    internal const uint WmContextMenu = 0x007B;
    internal const uint WmCommand = 0x0111;
    internal const uint WmLButtonDblClk = 0x0203;
    internal const uint WmRButtonUp = 0x0205;
    internal const uint WmMouseWheel = 0x020A;
    internal const uint WmPowerBroadcast = 0x0218;
    internal const uint WmApp = 0x8000;

    internal const int PbtApmResumeSuspend = 0x0007;
    internal const int PbtApmPowerStatusChange = 0x000A;
    internal const int PbtApmResumeAutomatic = 0x0012;

    internal const int GaRoot = 2;
    internal const int GaRootOwner = 3;
    internal const int GwHwndNext = 2;
    internal const int SwRestore = 9;
    internal const int DwmwaExtendedFrameBounds = 9;
    internal const int DwmwaCloaked = 14;
    internal const int GwlExstyle = -20;
    internal const int WsExTransparent = 0x20;
    internal const uint AsfwAny = unchecked((uint)-1);
    internal static readonly nint HwndBroadcast = new(0xffff);

    internal const uint PmNoRemove = 0x0000;

    internal const uint NimAdd = 0x00000000;
    internal const uint NimModify = 0x00000001;
    internal const uint NimDelete = 0x00000002;

    internal const uint NifMessage = 0x00000001;
    internal const uint NifIcon = 0x00000002;
    internal const uint NifTip = 0x00000004;

    internal const uint MfString = 0x00000000;
    internal const uint MfSeparator = 0x00000800;
    internal const uint MfGrayed = 0x00000001;

    internal const uint TpmRightButton = 0x0002;
    internal const uint TpmNonotify = 0x0080;
    internal const uint TpmReturnCmd = 0x0100;

    internal const uint ImageIcon = 1;
    internal const uint LrShared = 0x00008000;
    internal const int SmCxSmIcon = 49;
    internal const int SmCySmIcon = 50;
    internal const nint IdiApplication = 32512;

    internal const ushort VkControl = 0x11;
    internal const ushort VkTab = 0x09;
    internal const ushort VkPrior = 0x21;
    internal const ushort VkNext = 0x22;
    private const ushort VkShift = 0x10;
    private const ushort VkMenu = 0x12;
    private const ushort VkLWin = 0x5B;
    private const ushort VkRWin = 0x5C;

    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    internal const uint ProcessQueryLimitedInformation = 0x1000;

    private const string ChromiumWindowClassPrefix = "Chrome_WidgetWin_";

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint OpenProcess(uint processAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, char* lpExeName, ref uint lpdwSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll")]
    internal static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetProcessWorkingSetSizeEx(nint hProcess, nint dwMinimumWorkingSetSize, nint dwMaximumWorkingSetSize, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint SetWindowsHookExW(
        int idHook,
        delegate* unmanaged[Stdcall]<int, nint, nint, nint> callback,
        nint moduleHandle,
        uint threadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(nint hookHandle);

    [LibraryImport("user32.dll")]
    internal static partial nint CallNextHookEx(nint hookHandle, int nCode, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessageW(string lpString);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostThreadMessageW(uint idThread, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial nint GetWindowLong32(nint hWnd, int nIndex);

    internal static nint GetWindowLongPtr(nint hWnd, int nIndex)
    {
        return nint.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
    }

    [LibraryImport("user32.dll")]
    internal static partial nint WindowFromPoint(NativePoint point);

    [LibraryImport("user32.dll")]
    internal static partial nint GetAncestor(nint windowHandle, int flags);

    [LibraryImport("user32.dll")]
    internal static partial nint GetParent(nint windowHandle);

    [LibraryImport("user32.dll")]
    internal static partial nint GetWindow(nint windowHandle, uint uCmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint windowHandle);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint windowHandle, out NativeRect rect);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint windowHandle, int attribute, out NativeRect rect, int attributeSize);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint windowHandle, int attribute, out int value, int attributeSize);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint windowHandle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint windowHandle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint windowHandle, int command);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllowSetForegroundWindow(uint dwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint windowHandle);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial int GetClassNameW(nint windowHandle, char* lpClassName, int nMaxCount);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint inputCount, Input* inputs, int inputSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial ushort RegisterClassExW(WndClassExW* lpWndClass);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterClassW(string lpClassName, nint hInstance);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int GetMessageW(out Win32Msg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekMessageW(out Win32Msg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(ref Win32Msg lpMsg);

    [LibraryImport("user32.dll")]
    internal static partial nint DispatchMessageW(ref Win32Msg lpMsg);

    [LibraryImport("user32.dll")]
    internal static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Shell_NotifyIconW(uint dwMessage, NotifyIconDataW* lpData);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AppendMenuW(nint hMenu, uint uFlags, nuint uIdNewItem, string? lpNewItem);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyMenu(nint hMenu);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hWnd, nint lptpm);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out NativePoint lpPoint);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint LoadImageW(nint hInst, nint name, uint type, int cx, int cy, uint fuLoad);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint ExtractIconExW(string lpszFile, int nIconIndex, nint* phiconLarge, nint* phiconSmall, uint nIcons);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(nint hIcon);

    internal static bool IsChromiumWindowClass(nint windowHandle)
    {
        char* buffer = stackalloc char[32];
        int length = GetClassNameW(windowHandle, buffer, 32);
        if (length < ChromiumWindowClassPrefix.Length)
        {
            return false;
        }

        return new ReadOnlySpan<char>(buffer, length).StartsWith(ChromiumWindowClassPrefix, StringComparison.Ordinal);
    }

    internal static bool TryGetProcessName(uint processId, Span<char> buffer, out ReadOnlySpan<char> processName)
    {
        processName = default;
        var hProcess = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (hProcess == 0)
        {
            return false;
        }

        try
        {
            var size = (uint)buffer.Length;
            fixed (char* ptr = buffer)
            {
                if (!QueryFullProcessImageNameW(hProcess, 0, ptr, ref size) || size == 0)
                {
                    return false;
                }
            }

            ReadOnlySpan<char> fullPath = buffer[..(int)size];
            int lastSlash = fullPath.LastIndexOfAny('\\', '/');
            ReadOnlySpan<char> fileName = lastSlash >= 0 ? fullPath[(lastSlash + 1)..] : fullPath;
            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName[..^4];
            }

            if (fileName.IsEmpty)
            {
                return false;
            }

            processName = fileName;
            return true;
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    internal static bool IsWindowCloaked(nint windowHandle)
    {
        return DwmGetWindowAttribute(windowHandle, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    internal static bool TryGetVisibleWindowBounds(nint windowHandle, out NativeRect rect)
    {
        if (DwmGetWindowAttribute(windowHandle, DwmwaExtendedFrameBounds, out rect, sizeof(NativeRect)) == 0)
        {
            return rect.Width > 0 && rect.Height > 0;
        }

        return GetWindowRect(windowHandle, out rect) && rect.Width > 0 && rect.Height > 0;
    }

    internal static bool IsSameRootWindow(nint first, nint second)
    {
        if (first == 0 || second == 0)
        {
            return false;
        }

        return GetAncestor(first, GaRoot) == GetAncestor(second, GaRoot);
    }

    internal static bool HasAnyKeyboardModifierDown()
    {
        return IsKeyDown(VkControl)
            || IsKeyDown(VkShift)
            || IsKeyDown(VkMenu)
            || IsKeyDown(VkLWin)
            || IsKeyDown(VkRWin);
    }

    internal static bool SendCtrlPageKey(bool pageUp)
    {
        var pageKey = pageUp ? VkPrior : VkNext;
        Input* inputs = stackalloc Input[4];
        inputs[0] = CreateKeyInput(VkControl, keyUp: false);
        inputs[1] = CreateKeyInput(pageKey, keyUp: false);
        inputs[2] = CreateKeyInput(pageKey, keyUp: true);
        inputs[3] = CreateKeyInput(VkControl, keyUp: true);

        return SendInput(4, inputs, sizeof(Input)) == 4;
    }

    internal static bool SendCtrlTabKey(bool previous)
    {
        Input* inputs = stackalloc Input[6];
        uint count;

        if (previous)
        {
            inputs[0] = CreateKeyInput(VkControl, keyUp: false);
            inputs[1] = CreateKeyInput(VkShift, keyUp: false);
            inputs[2] = CreateKeyInput(VkTab, keyUp: false);
            inputs[3] = CreateKeyInput(VkTab, keyUp: true);
            inputs[4] = CreateKeyInput(VkShift, keyUp: true);
            inputs[5] = CreateKeyInput(VkControl, keyUp: true);
            count = 6;
        }
        else
        {
            inputs[0] = CreateKeyInput(VkControl, keyUp: false);
            inputs[1] = CreateKeyInput(VkTab, keyUp: false);
            inputs[2] = CreateKeyInput(VkTab, keyUp: true);
            inputs[3] = CreateKeyInput(VkControl, keyUp: true);
            count = 4;
        }

        return SendInput(count, inputs, sizeof(Input)) == count;
    }

    internal static void TrimWorkingSet()
    {
        try
        {
            SetProcessWorkingSetSizeEx(GetCurrentProcess(), new nint(-1), new nint(-1), 0);
        }
        catch
        {
            // Working set trimming is strictly opportunistic
        }
    }

    internal static int GetSmallIconMetric(int index, uint dpi)
    {
        try
        {
            return GetSystemMetricsForDpi(index, dpi);
        }
        catch (EntryPointNotFoundException)
        {
            return GetSystemMetrics(index);
        }
    }

    internal static Win32Exception LastWin32Exception()
    {
        return new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static bool IsKeyDown(ushort virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
    }

    private static Input CreateKeyInput(ushort virtualKey, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = 0,
                    Flags = keyUp ? KeyEventKeyUp : 0,
                    Time = 0,
                    ExtraInfo = 0
                }
            }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LowLevelMouseStruct
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WndClassExW
    {
        public uint CbSize;
        public uint Style;
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public nint HInstance;
        public nint HIcon;
        public nint HCursor;
        public nint HbrBackground;
        public char* LpszMenuName;
        public char* LpszClassName;
        public nint HIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconDataW
    {
        public uint CbSize;
        public nint HWnd;
        public uint UID;
        public uint UFlags;
        public uint UCallbackMessage;
        public nint HIcon;
        public fixed char SzTip[128];
        public uint DwState;
        public uint DwStateMask;
        public fixed char SzInfo[256];
        public uint UTimeoutOrVersion;
        public fixed char SzInfoTitle[64];
        public uint DwInfoFlags;
        public Guid GuidItem;
        public nint HBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HardwareInput
    {
        public uint Msg;
        public ushort ParamL;
        public ushort ParamH;
    }
}
