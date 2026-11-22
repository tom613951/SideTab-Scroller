# 侧栏滚轮标签页切换器 (SideTab Scroller)

SideTab Scroller 是一个 Windows 桌面工具。当鼠标指针悬停在 Chromium 内核浏览器的垂直标签栏（侧栏）区域时，通过滚动鼠标滚轮快速切换标签页。

该项目使用 C# / .NET 10 编写，灵感来源于 [nurupo/chrome-mouse-wheel-tab-scroller](https://github.com/nurupo/chrome-mouse-wheel-tab-scroller)。原项目主要面向传统顶部水平标签栏，本项目专门适配 Microsoft Edge、Chrome、Brave、Vivaldi 等浏览器的左侧垂直标签栏。

## 功能特性

- **侧栏区域检测**：识别浏览器窗口左侧垂直标签栏范围，并支持自定义侧栏感应宽度、顶部避让（Top inset）和底部避让（Bottom inset）。
- **独立高优先级鼠标钩子**：基于独立后台线程与 Win32 低级鼠标钩子（`WH_MOUSE_LL` + `[UnmanagedCallersOnly]` 零封送回调）实现，与界面渲染和磁盘 I/O 完全隔离，避免拖慢全局鼠标响应；服务停用时自动卸载钩子以实现零拦截开销。
- **统一热缓存与高精度滚轮累加**：缓存当前悬停窗口与 DPI 边界，解决网页正文区滚动时重复查询导致的卡顿；内建 `WHEEL_DELTA`（120）累加器以适配高精度滚轮和触控板，并支持拦截已处理的滚轮事件防止页面同步滚动。
- **按键模拟与焦点控制**：支持 `Ctrl+Tab` / `Ctrl+Shift+Tab` 或 `Ctrl+PageDown` / `Ctrl+PageUp` 切换标签页，支持方向反转、修饰键忽略、自动聚焦浏览器及滚动后延迟恢复原窗口焦点。
- **零 WinForms 原生托盘与按需设置窗体**：移除 WinForms 与 `System.Drawing` 依赖，采用纯 Win32 原生托盘宿主（支持 `TaskbarCreated` 资源管理器重启后自动重建图标）；`--minimized` 静默后台启动时不加载 WPF / `WPF-UI`；设置界面基于 WPF UI 构建并按需懒加载，关闭即销毁视觉树并触发工作集回收。
- **多显示器与高 DPI 适配**：声明 `PerMonitorV2` DPI 感知，自动按窗口 DPI 缩放计算物理感应区域。
- **单实例运行保护**：通过命名互斥体（Mutex）防止重复启动，重复打开时通过 `AllowSetForegroundWindow` 与广播消息置顶唤醒设置窗口。
- **管理员权限与防穿透**：通过应用清单声明管理员权限以兼容高权限窗口；支持基于 `GetWindow(GW_HWNDNEXT)` 与 `DWMWA_CLOAKED` 的零分配 Z 序穿透，准确识别透明遮罩层（`WS_EX_TRANSPARENT`）下的浏览器并在首层不透明窗口处停止。
- **异步任务计划程序自启**：通过 Windows 任务计划程序（Task Scheduler）XML 模板异步注册无电池限制、无运行时间限制的提权自启任务，并支持旧版任务与注册表项平滑迁移。
- **现代 .NET 10 低开销实现**：采用 `[LibraryImport]` 编译期 P/Invoke 源生成、`System.Text.Json` 源生成、`FrozenSet<string>.GetAlternateLookup<ReadOnlySpan<char>>()` 零分配进程匹配、防抖原子配置文件保存（`%APPDATA%\SideTabScroller\settings.json`）及低内存 GC 配置（`System.GC.ConserveMemory`）。

## 架构与资源优化

- **后台常驻轻量化**：静默启动（`--minimized`）仅运行原生 Win32 托盘消息循环与独立高优先级鼠标钩子线程，不初始化 WPF 与 DirectX 渲染管线。
- **按需加载与关闭回收**：仅在打开设置界面时启动独立 WPF STA 线程；关闭设置窗口后解除主题监听并释放窗口资源，随后执行压缩 GC 与工作集裁剪（`SetProcessWorkingSetSizeEx`）。
- **低延迟钩子热路径**：采用统一单窗口缓存与 `stackalloc` 零堆分配窗口/进程检测，消除高速滚动与网页内容区滚动时的 GC 抖动和 DWM IPC 开销。

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

1. 运行 `SideTabScroller.exe`（需要确认 UAC 提权提示；传入 `--minimized` 可静默启动到系统托盘）。
2. 在浏览器中开启垂直标签页模式。
3. 将鼠标指针移动到左侧垂直标签栏区域，滚动鼠标滚轮即可切换标签页。
4. 如浏览器侧栏宽度或上下边距与默认值不一致，可在托盘图标右键菜单或双击托盘图标打开设置窗口进行调整。

## 开源协议

本项目采用 GPL-3.0 协议开源，详见 [LICENSE](LICENSE)。
