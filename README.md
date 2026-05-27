# SideTab Scroller

SideTab Scroller is a Windows desktop utility for switching Chromium-based browser tabs with the mouse wheel while the pointer is over the side tab bar.

This is a modern C#/.NET rewrite inspired by [nurupo/chrome-mouse-wheel-tab-scroller](https://github.com/nurupo/chrome-mouse-wheel-tab-scroller). The original project watched the top Chrome tab strip; this version targets left or right vertical tab sidebars used by Microsoft Edge, Chrome, Brave, Vivaldi, and Chromium.

## Highlights

- Side tab hit testing instead of top-tab hit testing.
- Global low-level mouse wheel hook with optional event consumption.
- Sends `Ctrl+PageUp` / `Ctrl+PageDown` to switch tabs.
- Configurable sidebar side, width, top inset, and bottom inset.
- Optional autofocus and focus restoration.
- Modern WPF settings UI with a tray icon and startup toggle.
- JSON settings stored in `%APPDATA%\SideTabScroller\settings.json`.

## Build

Install the .NET 10 SDK, then run:

```powershell
dotnet build .\SideTabScroller.slnx -c Release
```

Publish a framework-dependent Windows x64 build:

```powershell
dotnet publish .\SideTabScroller\SideTabScroller.csproj -c Release -r win-x64 --self-contained false
```

The app output is under:

```text
SideTabScroller\bin\Release\net10.0-windows\win-x64\publish
```

## Usage

Run `SideTabScroller.exe`, enable vertical tabs in your browser, then hover the side tab bar and scroll the wheel. Tune `Sidebar width`, `Top inset`, and `Bottom inset` in the settings window if your browser theme, toolbar density, or sidebar width differs from the defaults.

If the browser is not focused, `Autofocus browser window` briefly focuses it before sending the tab-switch shortcut. Windows may block focus changes across elevated and non-elevated apps; running SideTab Scroller as administrator can help in those cases.

## License

GPL-3.0-only. See [LICENSE](LICENSE).
