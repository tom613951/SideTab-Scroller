# 侧栏滚轮标签页切换器 (SideTab Scroller)

SideTab Scroller 是一款 Windows 桌面实用小工具，当你的鼠标指针悬停在 Chromium 内核浏览器的垂直标签栏（侧栏）上时，只需滚动鼠标轮即可快速切换标签页。

这是一个现代化的 C#/.NET 重写版本，设计灵感来源于 [nurupo/chrome-mouse-wheel-tab-scroller](https://github.com/nurupo/chrome-mouse-wheel-tab-scroller)。原版项目是针对 Chrome 顶部的标签栏；而本项目针对的是 Microsoft Edge、Chrome、Brave、Vivaldi 以及 Chromium 等浏览器正在使用的左侧或右侧垂直标签栏。

## 功能亮点

- **侧栏热区检测**：专门针对侧边栏（垂直标签栏）进行检测，而不是顶部的传统标签栏。
- **全局鼠标钩子**：使用全局低级鼠标滚轮钩子，并支持可选的事件拦截消费（防止页面跟着滚动）。
- **键盘快捷键模拟**：通过模拟发送 `Ctrl+PageUp` / `Ctrl+PageDown` 组合键来进行标签页切换。
- **高度可定制化**：可自定义鼠标感应侧边位置（左侧/右侧/双侧自动）、感应宽度、顶部留空（Top inset）和底部留空（Bottom inset）。
- **高 DPI 屏幕完美支持**：自动适配多显示器高 DPI 缩放，确保感应区物理范围在任何分辨率下均保持一致。
- **单实例运行保护**：引入命名 Mutex，防止多开冲突，且重复启动时会自动唤醒并置顶先前在后台的实例设置窗口。
- **自动对焦与恢复（防抖优化）**：可选自动激活浏览器窗口，并在滚动结束 300ms 后自动恢复之前的窗口焦点，防止快速滚动时焦点剧烈交替冲突。
- **现代化 UI 设计**：使用基于 **WPF UI** 的全新 Windows 11 / WinUI 3 风格设置界面，支持 Mica 材质背景，并带有原生 Fluent 样式的系统托盘右键菜单。
- **管理员权限运行（完美兼容）**：默认以管理员权限运行，以绕过 Windows UIPI 限制，确保在任何置顶的高权限软件（如代理客户端 `GUI.for.SingBox`、各类调试终端、游戏等）处于焦点状态时均能正常工作。
- **任务计划程序开机启动**：开机自启机制升级为通过 **Windows 任务计划程序 (Task Scheduler)** 注册，在用户登录时静默提权启动，彻底解决 Windows 安全策略对高权限程序注册表启动项的拦截。
- **高性能与透明遮罩穿透**：通过 Win32 原生 `OpenProcess` API 搭配高速线程安全缓存与异步消息队列实现无延迟解析，解决滚轮触发时的鼠标移动卡顿；引入 Z 轴窗口深度与透明度遍历机制，即便侧栏上方被其他软件的透明遮罩覆盖，仍能精准识别底层浏览器，且不会误伤普通盖在其上的非透明窗口。
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

1. 启动 `SideTabScroller.exe`（请同意系统的 UAC 管理员提权提示）。
2. 在浏览器中启用**垂直标签页**（侧栏标签）。
3. 将鼠标悬停在侧栏标签区域，滚动鼠标滚轮。
4. 如果你的浏览器主题、工具栏密度、侧栏侧向或侧栏宽度与默认值不同，可以在设置窗口中选择触发侧边，或者调整 `Sidebar width`（侧栏宽度）、`Top inset`（顶部留空）和 `Bottom inset`（底部留空）等参数。

## 开源协议

GPL-3.0-only。详见 [LICENSE](LICENSE)。

