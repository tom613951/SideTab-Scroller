namespace SideTabScroller.Models;

public sealed record SwitchResult(
    DateTime At,
    string BrowserProcessName,
    SwitchDirection Direction);
