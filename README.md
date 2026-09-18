# 侧栏滚轮标签页切换器 (SideTab Scroller)

SideTab Scroller 是一个 Windows 桌面工具。当鼠标指针悬停在 Chromium 内核浏览器的垂直标签栏（侧栏）区域时，通过滚动鼠标滚轮快速切换标签页。

该项目使用 C# / .NET 编写，灵感来源于 [nurupo/chrome-mouse-wheel-tab-scroller](https://github.com/nurupo/chrome-mouse-wheel-tab-scroller)。原项目主要面向传统顶部水平标签栏，本项目专门适配 Microsoft Edge、Chrome、Brave、Vivaldi 等浏览器的左侧垂直标签栏。

## 功能特性

- **侧栏区域检测**：识别浏览器窗口左侧垂直标签栏范围。
- **全局鼠标钩子**：基于 Win32 低级鼠标滚轮钩子（WH_MOUSE_LL）实现，支持拦截消费滚轮事件以避免页面同步上下滚动。
- **按键模拟**：支持通过 `Ctrl+Tab` / `Ctrl+Shift+Tab` 或 `Ctrl+PageDown` / `Ctrl+PageUp` 进行标签页切换。
- **自定义边距参数**：可自定义侧栏感应宽度、顶部留空（Top inset）和底部留空（Bottom inset）。
- **多显示器与高 DPI 适配**：自动计算屏幕 DPI 缩放比例，保持物理感应区域一致。
- **单实例运行保护**：通过命名互斥体（Mutex）防止重复启动，重复打开时唤醒已存在的后台窗口。
- **窗口对焦与还原**：支持滚动时自动激活浏览器窗口，并在滚动结束后自动恢复原先的窗口焦点。
- **界面与系统托盘**：基于 WPF UI 构建的设置界面，支持最小化到系统托盘运行。
- **管理员权限与防穿透**：默认通过应用清单声明管理员权限，避免 Windows UIPI 限制导致在高权限窗口处于焦点时失效；支持穿透上层透明遮罩窗口准确识别底层浏览器。
- **任务计划程序开机自启**：通过 Windows 任务计划程序（Task Scheduler）注册自启项，支持登录时静默提权运行。
- **配置持久化**：采用 JSON 文件保存用户配置，默认存储路径为 `%APPDATA%\SideTabScroller\settings.json`。

## 构建与发布

环境要求：.NET 10 SDK。

构建项目：

```powershell
dotnet build .\SideTabScroller.slnx -c Release
```

发布依赖框架单文件版本（Windows x64）：

```powershell
dotnet publish .\SideTabScroller\SideTabScroller.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

发布独立自包含单文件版本（Windows x64）：

```powershell
dotnet publish .\SideTabScroller\SideTabScroller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## 使用说明

1. 运行 `SideTabScroller.exe`（需要确认 UAC 提权提示）。
2. 在浏览器中开启垂直标签页模式。
3. 将鼠标指针移动到左侧垂直标签栏区域，滚动鼠标滚轮即可切换标签页。
4. 如浏览器侧栏宽度或上下边距与默认值不一致，可在托盘图标右键菜单中打开设置窗口进行调整。

## 开源协议

本项目采用 GPL-3.0 协议开源，详见 [LICENSE](LICENSE)。

