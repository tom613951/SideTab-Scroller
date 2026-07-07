using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using SideTabScroller.Models;
using SideTabScroller.Native;
using SideTabScroller.Services;

namespace SideTabScroller;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly SettingsStore _settingsStore;
    private readonly StartupManager _startupManager;
    private readonly BrowserTabSwitcher _tabSwitcher;
    private readonly MouseWheelHook _mouseWheelHook;
    private readonly TrayIconService _trayIcon;
    private readonly DispatcherTimer _statusTimer;

    private ScrollerSettings _settings;
    private SwitchResult? _lastSwitch;
    private bool _allowClose;
    private bool _isLoading = true;
    private bool _isStartupEnabledCached;
    private uint _showMeMessage;

    public MainWindow()
    {
        InitializeComponent();
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _startupManager = new StartupManager();
        _tabSwitcher = new BrowserTabSwitcher(() => _settings, OnTabSwitched);
        _mouseWheelHook = new MouseWheelHook(_tabSwitcher.HandleWheel);
        _trayIcon = new TrayIconService(
            showSettings: ShowSettingsWindow,
            toggleEnabled: ToggleEnabled,
            exitApplication: ExitApplication,
            showContextMenu: ShowTrayContextMenu);

        ApplySettingsToUi();
        _mouseWheelHook.Start();
        _trayIcon.Update(_settings, _mouseWheelHook.IsRunning);

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => UpdateStatusText();
        _statusTimer.Start();
        UpdateStatusText();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _showMeMessage = NativeMethods.RegisterWindowMessage("SideTabScroller_ShowMeMessage");
        var source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _showMeMessage)
        {
            ShowSettingsWindow();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void ApplySettingsToUi()
    {
        _isLoading = true;

        EnabledSwitch.IsChecked = _settings.Enabled;
        ReverseSwitch.IsChecked = _settings.ReverseDirection;
        SwallowWheelSwitch.IsChecked = _settings.ConsumeHandledWheelEvents;
        IgnoreModifiersSwitch.IsChecked = _settings.IgnoreWhenKeyboardModifiersPressed;
        AutofocusSwitch.IsChecked = _settings.AutofocusBrowser;
        RestoreFocusSwitch.IsChecked = _settings.RestorePreviousFocus;
        SidebarWidthSlider.Value = _settings.SidebarWidth;
        TopInsetSlider.Value = _settings.TopInset;
        BottomInsetSlider.Value = _settings.BottomInset;
        FocusDelaySlider.Value = _settings.FocusDelayMilliseconds;
        BrowserProcessesBox.Text = string.Join(Environment.NewLine, _settings.BrowserProcessNames);
        
        _isStartupEnabledCached = _startupManager.IsEnabled();
        StartupSwitch.IsChecked = _isStartupEnabledCached;



        ShortcutCtrlTabRadio.IsChecked = _settings.ShortcutMode == TabSwitchShortcutMode.CtrlTab;
        ShortcutCtrlPageRadio.IsChecked = _settings.ShortcutMode == TabSwitchShortcutMode.CtrlPage;

        _isLoading = false;
    }

    private void PersistSettingsFromUi()
    {
        if (_isLoading)
        {
            return;
        }

        _settings.Enabled = EnabledSwitch.IsChecked == true;
        _settings.ReverseDirection = ReverseSwitch.IsChecked == true;
        _settings.ConsumeHandledWheelEvents = SwallowWheelSwitch.IsChecked == true;
        _settings.IgnoreWhenKeyboardModifiersPressed = IgnoreModifiersSwitch.IsChecked == true;
        _settings.AutofocusBrowser = AutofocusSwitch.IsChecked == true;
        _settings.RestorePreviousFocus = RestoreFocusSwitch.IsChecked == true;
        _settings.SidebarWidth = (int)Math.Round(SidebarWidthSlider.Value);
        _settings.TopInset = (int)Math.Round(TopInsetSlider.Value);
        _settings.BottomInset = (int)Math.Round(BottomInsetSlider.Value);
        _settings.FocusDelayMilliseconds = (int)Math.Round(FocusDelaySlider.Value);
        _settings.BrowserProcessNames = ParseBrowserProcessNames(BrowserProcessesBox.Text);



        _settings.ShortcutMode = ShortcutCtrlPageRadio.IsChecked == true
            ? TabSwitchShortcutMode.CtrlPage
            : TabSwitchShortcutMode.CtrlTab;

        _settings.Normalize();
        _settingsStore.Save(_settings);
        _trayIcon.Update(_settings, _mouseWheelHook.IsRunning);
        UpdateStatusText();
    }

    private static List<string> ParseBrowserProcessNames(string text)
    {
        return text
            .Split(['\r', '\n', ',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void SettingsControl_Changed(object sender, RoutedEventArgs e) => PersistSettingsFromUi();



    private void ShortcutModeRadio_Checked(object sender, RoutedEventArgs e) => PersistSettingsFromUi();

    private void BrowserProcessesBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => PersistSettingsFromUi();

    private void StartupSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        try
        {
            var enable = StartupSwitch.IsChecked == true;
            _startupManager.SetEnabled(enable);
            _isStartupEnabledCached = _startupManager.IsEnabled();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "开机启动", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _isLoading = true;
            StartupSwitch.IsChecked = _isStartupEnabledCached;
            _isLoading = false;
        }

        UpdateStatusText();
    }

    private void OpenConfigButton_Click(object sender, RoutedEventArgs e)
    {
        _settingsStore.Save(_settings);
        Process.Start(new ProcessStartInfo
        {
            FileName = _settingsStore.ConfigPath,
            UseShellExecute = true
        });
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _settings = ScrollerSettings.CreateDefault();
        _settingsStore.Save(_settings);
        ApplySettingsToUi();
        _trayIcon.Update(_settings, _mouseWheelHook.IsRunning);
        UpdateStatusText();
    }

    private void ToggleEnabled()
    {
        Dispatcher.Invoke(() =>
        {
            _settings.Enabled = !_settings.Enabled;
            _settingsStore.Save(_settings);
            ApplySettingsToUi();
            _trayIcon.Update(_settings, _mouseWheelHook.IsRunning);
            UpdateStatusText();
        });
    }

    private void ShowSettingsWindow()
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
    }

    private void ExitApplication()
    {
        Dispatcher.Invoke(() =>
        {
            _allowClose = true;
            _statusTimer.Stop();
            _mouseWheelHook.Dispose();
            _trayIcon.Dispose();
            Close();
            System.Windows.Application.Current.Shutdown();
        });
    }

    private void OnTabSwitched(SwitchResult result)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _lastSwitch = result;
            UpdateStatusText();
        });
    }

    private void UpdateStatusText()
    {
        StatusText.Text = _mouseWheelHook.IsRunning
            ? $"鼠标钩子 - {(_settings.Enabled ? "正在监听" : "已停用")}"
            : $"鼠标钩子不可用 - {_mouseWheelHook.LastError ?? "未知错误"}";

        LastActionText.Text = _lastSwitch is null
            ? "上次切换 - 暂无"
            : $"上次切换 - {FormatDirection(_lastSwitch.Direction)} - {_lastSwitch.BrowserProcessName}.exe - {_lastSwitch.At:HH:mm:ss}";

        ConfigPathText.Text = $"配置文件 - {_settingsStore.ConfigPath}";
        StartupStatusText.Text = _isStartupEnabledCached
            ? "开机启动已启用。"
            : "开机启动未启用。";
    }

    private static string FormatDirection(SwitchDirection direction)
    {
        return direction == SwitchDirection.Previous ? "上一个标签页" : "下一个标签页";
    }

    public void ShowTrayContextMenu()
    {
        var menu = (System.Windows.Controls.ContextMenu)Resources["TrayContextMenu"];
        
        var headerItem = (System.Windows.Controls.MenuItem)menu.Items[0];
        var headerStack = (System.Windows.Controls.StackPanel)headerItem.Header;
        var statusTextBlock = (System.Windows.Controls.TextBlock)headerStack.Children[1];
        
        statusTextBlock.Text = _mouseWheelHook.IsRunning
            ? _settings.Enabled ? "正在监听侧边栏滚轮" : "已停用"
            : "鼠标钩子不可用";

        var toggleItem = (System.Windows.Controls.MenuItem)menu.Items[3];
        toggleItem.Header = _settings.Enabled ? "停用" : "启用";
        
        var toggleIcon = (Wpf.Ui.Controls.SymbolIcon)toggleItem.Icon;
        toggleIcon.Symbol = _settings.Enabled ? Wpf.Ui.Controls.SymbolRegular.Pause24 : Wpf.Ui.Controls.SymbolRegular.Play24;

        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;

        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        Native.NativeMethods.SetForegroundWindow(helper.Handle);
    }

    private void TraySettings_Click(object sender, System.Windows.RoutedEventArgs e) => ShowSettingsWindow();
    private void TrayToggle_Click(object sender, System.Windows.RoutedEventArgs e) => ToggleEnabled();
    private void TrayExit_Click(object sender, System.Windows.RoutedEventArgs e) => ExitApplication();
}
