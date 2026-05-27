using System.Runtime.InteropServices;

namespace SideTabScroller.Native;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeRect
{
    public readonly int Left;
    public readonly int Top;
    public readonly int Right;
    public readonly int Bottom;

    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
