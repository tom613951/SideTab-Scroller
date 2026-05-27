namespace SideTabScroller.Models;

public sealed class ScrollerSettings
{
    public bool Enabled { get; set; } = true;
    public bool ReverseDirection { get; set; }
    public bool ConsumeHandledWheelEvents { get; set; } = true;
    public bool IgnoreWhenKeyboardModifiersPressed { get; set; } = true;
    public bool AutofocusBrowser { get; set; } = true;
    public bool RestorePreviousFocus { get; set; } = true;
    public SidebarSide SidebarSide { get; set; } = SidebarSide.Auto;
    public int SidebarWidth { get; set; } = 104;
    public int TopInset { get; set; } = 64;
    public int BottomInset { get; set; } = 8;
    public int FocusDelayMilliseconds { get; set; } = 25;
    public List<string> BrowserProcessNames { get; set; } = [];

    public static ScrollerSettings CreateDefault()
    {
        return new ScrollerSettings
        {
            BrowserProcessNames =
            [
                "msedge",
                "chrome",
                "brave",
                "vivaldi",
                "chromium"
            ]
        };
    }

    public void Normalize()
    {
        SidebarWidth = Math.Clamp(SidebarWidth, 32, 400);
        TopInset = Math.Clamp(TopInset, 0, 400);
        BottomInset = Math.Clamp(BottomInset, 0, 240);
        FocusDelayMilliseconds = Math.Clamp(FocusDelayMilliseconds, 0, 500);

        BrowserProcessNames = BrowserProcessNames
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (BrowserProcessNames.Count == 0)
        {
            BrowserProcessNames = CreateDefault().BrowserProcessNames;
        }
    }
}
