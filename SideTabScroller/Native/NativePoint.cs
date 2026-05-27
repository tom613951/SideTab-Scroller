using System.Runtime.InteropServices;

namespace SideTabScroller.Native;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativePoint(int x, int y)
{
    public readonly int X = x;
    public readonly int Y = y;
}
