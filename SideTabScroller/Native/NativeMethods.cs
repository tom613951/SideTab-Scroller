using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SideTabScroller.Native;

internal static class NativeMethods
{
    internal const int WhMouseLl = 14;
    internal const int WmMouseWheel = 0x020A;
    internal const int GaRoot = 2;
    internal const int GaRootOwner = 3;
    internal const int SwRestore = 9;
    internal const int DwmwaExtendedFrameBounds = 9;

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

    internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, [Out] StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc callback, IntPtr moduleHandle, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hookHandle, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr windowHandle, int flags);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetParent(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rect);

    [DllImport("dwmapi.dll", SetLastError = true)]
    internal static extern int DwmGetWindowAttribute(IntPtr windowHandle, int attribute, out NativeRect rect, int attributeSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    internal static string GetWindowClassName(IntPtr windowHandle)
    {
        var builder = new StringBuilder(256);
        _ = GetClassName(windowHandle, builder, builder.Capacity);
        return builder.ToString();
    }

    internal static string? GetProcessName(uint processId)
    {
        var hProcess = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (hProcess == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var builder = new StringBuilder(1024);
            var size = (uint)builder.Capacity;
            if (QueryFullProcessImageName(hProcess, 0, builder, ref size))
            {
                var fullPath = builder.ToString();
                return System.IO.Path.GetFileNameWithoutExtension(fullPath);
            }
            return null;
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    internal static bool TryGetVisibleWindowBounds(IntPtr windowHandle, out NativeRect rect)
    {
        if (DwmGetWindowAttribute(windowHandle, DwmwaExtendedFrameBounds, out rect, Marshal.SizeOf<NativeRect>()) == 0)
        {
            return rect.Width > 0 && rect.Height > 0;
        }

        return GetWindowRect(windowHandle, out rect) && rect.Width > 0 && rect.Height > 0;
    }

    internal static bool IsSameRootWindow(IntPtr first, IntPtr second)
    {
        if (first == IntPtr.Zero || second == IntPtr.Zero)
        {
            return false;
        }

        var firstRoot = GetAncestor(first, GaRoot);
        var secondRoot = GetAncestor(second, GaRoot);
        return firstRoot == secondRoot;
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
        var inputs = new[]
        {
            CreateKeyInput(VkControl, keyUp: false),
            CreateKeyInput(pageKey, keyUp: false),
            CreateKeyInput(pageKey, keyUp: true),
            CreateKeyInput(VkControl, keyUp: true)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        return sent == inputs.Length;
    }

    internal static bool SendCtrlTabKey(bool previous)
    {
        var inputs = previous
            ? new[]
            {
                CreateKeyInput(VkControl, keyUp: false),
                CreateKeyInput(VkShift, keyUp: false),
                CreateKeyInput(VkTab, keyUp: false),
                CreateKeyInput(VkTab, keyUp: true),
                CreateKeyInput(VkShift, keyUp: true),
                CreateKeyInput(VkControl, keyUp: true)
            }
            : new[]
            {
                CreateKeyInput(VkControl, keyUp: false),
                CreateKeyInput(VkTab, keyUp: false),
                CreateKeyInput(VkTab, keyUp: true),
                CreateKeyInput(VkControl, keyUp: true)
            };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        return sent == inputs.Length;
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
                    ExtraInfo = UIntPtr.Zero
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
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Msg;
        public ushort ParamL;
        public ushort ParamH;
    }
}
