# 侧栏滚轮标签页切换器 (SideTab Scroller)

SideTab Scroller 是一款 Windows 桌面实用小工具，当你的鼠标指针悬停在 Chromium 内核浏览器的垂直标签栏（侧栏）上时，只需滚动鼠标轮即可快速切换标签页。

这是一个现代化的 C#/.NET 重写版本，设计灵感来源于 [nurupo/chrome-mouse-wheel-tab-scroller](https://github.com/nurupo/chrome-mouse-wheel-tab-scroller)。原版项目是针对 Chrome 顶部的标签栏；而本项目针对的是 Microsoft Edge、Chrome、Brave、Vivaldi 以及 Chromium 等浏览器正在使用的左侧或右侧垂直标签栏。

## 功能亮点

- **侧栏热区检测**：专门针对侧边栏（垂直标签栏）进行检测，而不是顶部的传统标签栏。
- **全局鼠标钩子**：使用全局低级鼠标滚轮钩子，并支持可选的事件拦截消费（防止页面跟着滚动）。
- **键盘快捷键模拟**：通过模拟发送 `Ctrl+PageUp` / `Ctrl+PageDown` 组合键来进行标签页切换。
- **高度可定制化**：可自定义侧栏的位置（左侧/右侧/自动）、侧栏宽度、顶部留空（Top inset）和底部留空（Bottom inset）。
- **自动对焦与恢复**：可选自动激活浏览器窗口并在完成切换后恢复之前的窗口焦点。
- **现代化 UI 设计**：使用现代化的 WPF 设置界面，带有系统托盘菜单以及开机自启开关。
- **本地配置存储**：采用 JSON 配置文件，存储在 `%APPDATA%\SideTabScroller\settings.json`。

## 构建与发布

确保已安装 .NET 10 SDK，然后运行以下命令进行构建：

```powershell
dotnet build .\SideTabScroller.slnx -c Release
```

发布适用于 Windows x64 的依赖框架的单文件版本：

```powershell
dotnet publish .\SideTabScroller\SideTabScroller.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

发布适用于 Windows x64 的完全独立自包含（Self-contained）的单文件版本：

```powershell
dotnet publish .\SideTabScroller\SideTabScroller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfContained=true -p:PublishReadyToRun=true
```

## 使用方法

1. 启动 `SideTabScroller.exe`。
2. 在浏览器中启用**垂直标签页**（侧栏标签）。
3. 将鼠标悬停在侧栏标签区域，滚动鼠标滚轮。
4. 如果你的浏览器主题、工具栏密度或侧栏宽度与默认值不同，可以在设置窗口中调整 `Sidebar width`（侧栏宽度）、`Top inset`（顶部留空）和 `Bottom inset`（底部留空）等参数。

*提示：如果浏览器当前未处于焦点状态，开启 `Autofocus browser window`（自动聚焦浏览器窗口）会在发送标签切换快捷键前短暂将浏览器置顶。如果由于管理员权限不同（如以管理员身份运行了浏览器）导致 Windows 限制跨进程焦点切换，建议以管理员身份运行 SideTab Scroller。*

## 开源协议

GPL-3.0-only。详见 [LICENSE](LICENSE)。
