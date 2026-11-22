using System.Collections.Frozen;
using System.Text.Json.Serialization;
using System.Threading;

namespace SideTabScroller.Models;

public sealed class ScrollerSettings
{
    public bool Enabled { get; set; } = true;
    public bool ReverseDirection { get; set; }
    public bool ConsumeHandledWheelEvents { get; set; } = true;
    public bool IgnoreWhenKeyboardModifiersPressed { get; set; } = true;
    public bool AutofocusBrowser { get; set; } = true;
    public bool RestorePreviousFocus { get; set; } = true;

    public TabSwitchShortcutMode ShortcutMode { get; set; } = TabSwitchShortcutMode.CtrlTab;
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

    public ScrollerSettings Clone()
    {
        return new ScrollerSettings
        {
            Enabled = Enabled,
            ReverseDirection = ReverseDirection,
            ConsumeHandledWheelEvents = ConsumeHandledWheelEvents,
            IgnoreWhenKeyboardModifiersPressed = IgnoreWhenKeyboardModifiersPressed,
            AutofocusBrowser = AutofocusBrowser,
            RestorePreviousFocus = RestorePreviousFocus,
            ShortcutMode = ShortcutMode,
            SidebarWidth = SidebarWidth,
            TopInset = TopInset,
            BottomInset = BottomInset,
            FocusDelayMilliseconds = FocusDelayMilliseconds,
            BrowserProcessNames = [.. BrowserProcessNames]
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

internal sealed class ScrollerSettingsSnapshot
{
    private static int _nextVersion;

    private readonly FrozenSet<string> _browserProcessNames;
    private readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> _spanLookup;

    public int Version { get; }
    public bool Enabled { get; }
    public bool ReverseDirection { get; }
    public bool ConsumeHandledWheelEvents { get; }
    public bool IgnoreWhenKeyboardModifiersPressed { get; }
    public bool AutofocusBrowser { get; }
    public bool RestorePreviousFocus { get; }
    public TabSwitchShortcutMode ShortcutMode { get; }
    public int SidebarWidth { get; }
    public int TopInset { get; }
    public int BottomInset { get; }
    public int FocusDelayMilliseconds { get; }

    public ScrollerSettingsSnapshot(ScrollerSettings settings)
    {
        Version = Interlocked.Increment(ref _nextVersion);
        Enabled = settings.Enabled;
        ReverseDirection = settings.ReverseDirection;
        ConsumeHandledWheelEvents = settings.ConsumeHandledWheelEvents;
        IgnoreWhenKeyboardModifiersPressed = settings.IgnoreWhenKeyboardModifiersPressed;
        AutofocusBrowser = settings.AutofocusBrowser;
        RestorePreviousFocus = settings.RestorePreviousFocus;
        ShortcutMode = settings.ShortcutMode;
        SidebarWidth = settings.SidebarWidth;
        TopInset = settings.TopInset;
        BottomInset = settings.BottomInset;
        FocusDelayMilliseconds = settings.FocusDelayMilliseconds;

        _browserProcessNames = settings.BrowserProcessNames.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _spanLookup = _browserProcessNames.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public bool TryGetMatchedBrowserName(ReadOnlySpan<char> candidate, out string matchedName)
    {
        if (_spanLookup.TryGetValue(candidate, out var found))
        {
            matchedName = found;
            return true;
        }

        matchedName = string.Empty;
        return false;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ScrollerSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext
{
}
