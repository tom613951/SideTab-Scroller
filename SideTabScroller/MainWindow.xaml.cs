using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SideTabScroller.Models;
using SideTabScroller.Services;

namespace SideTabScroller;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly AppController _controller;
    private bool _isLoading = true;
    private bool _isUpdatingStartup;
    private bool _isClosed;

    internal MainWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        try
        {
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
        }
        catch
        {
            // Ignore system theme watcher errors if OS theme notification fails
        }

        _controller.StateChanged += OnControllerStateChanged;
        SyncAllFromController();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel)
        {
            try
            {
                // UnWatch must run before WPF disposes the native HWND prior to OnClosed
                Wpf.Ui.Appearance.SystemThemeWatcher.UnWatch(this);
            }
            catch
            {
                // Ignore unwatch errors
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _controller.StateChanged -= OnControllerStateChanged;
        _controller.FlushPendingSave();
        base.OnClosed(e);
    }

    private void OnControllerStateChanged()
    {
        if (_isClosed || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (_isClosed)
            {
                return;
            }

            if (!_isLoading && !_isUpdatingStartup)
            {
                var snapshot = _controller.Snapshot;
                if (EnabledSwitch.IsChecked != snapshot.Enabled)
                {
                    _isLoading = true;
                    EnabledSwitch.IsChecked = snapshot.Enabled;
                    _isLoading = false;
                }

                _isLoading = true;
                StartupSwitch.IsChecked = _controller.IsStartupEnabled;
                _isLoading = false;
            }

            UpdateStatusText();
        });
    }

    private void SyncAllFromController()
    {
        _isLoading = true;
        try
        {
            var settings = _controller.GetSettingsCopy();
            EnabledSwitch.IsChecked = settings.Enabled;
            ReverseSwitch.IsChecked = settings.ReverseDirection;
            SwallowWheelSwitch.IsChecked = settings.ConsumeHandledWheelEvents;
            IgnoreModifiersSwitch.IsChecked = settings.IgnoreWhenKeyboardModifiersPressed;
            AutofocusSwitch.IsChecked = settings.AutofocusBrowser;
            RestoreFocusSwitch.IsChecked = settings.RestorePreviousFocus;
            SidebarWidthSlider.Value = settings.SidebarWidth;
            TopInsetSlider.Value = settings.TopInset;
            BottomInsetSlider.Value = settings.BottomInset;
            FocusDelaySlider.Value = settings.FocusDelayMilliseconds;
            BrowserProcessesBox.Text = string.Join(Environment.NewLine, settings.BrowserProcessNames);

            ShortcutCtrlTabRadio.IsChecked = settings.ShortcutMode == TabSwitchShortcutMode.CtrlTab;
            ShortcutCtrlPageRadio.IsChecked = settings.ShortcutMode == TabSwitchShortcutMode.CtrlPage;

            StartupSwitch.IsChecked = _controller.IsStartupEnabled;
        }
        finally
        {
            _isLoading = false;
        }

        UpdateStatusText();
    }

    private void PushSettingsToController(bool saveImmediately, bool normalizeEmptyProcessList)
    {
        if (_isLoading)
        {
            return;
        }

        var parsedProcesses = ParseBrowserProcessNames(BrowserProcessesBox.Text);
        if (!normalizeEmptyProcessList && parsedProcesses.Count == 0)
        {
            // While typing/clearing the box, retain existing process list until focus leaves
            parsedProcesses = _controller.GetSettingsCopy().BrowserProcessNames;
        }

        var updated = new ScrollerSettings
        {
            Enabled = EnabledSwitch.IsChecked == true,
            ReverseDirection = ReverseSwitch.IsChecked == true,
            ConsumeHandledWheelEvents = SwallowWheelSwitch.IsChecked == true,
            IgnoreWhenKeyboardModifiersPressed = IgnoreModifiersSwitch.IsChecked == true,
            AutofocusBrowser = AutofocusSwitch.IsChecked == true,
            RestorePreviousFocus = RestoreFocusSwitch.IsChecked == true,
            SidebarWidth = (int)Math.Round(SidebarWidthSlider.Value),
            TopInset = (int)Math.Round(TopInsetSlider.Value),
            BottomInset = (int)Math.Round(BottomInsetSlider.Value),
            FocusDelayMilliseconds = (int)Math.Round(FocusDelaySlider.Value),
            BrowserProcessNames = parsedProcesses,
            ShortcutMode = ShortcutCtrlPageRadio.IsChecked == true
                ? TabSwitchShortcutMode.CtrlPage
                : TabSwitchShortcutMode.CtrlTab
        };

        _controller.UpdateSettings(updated, saveImmediately);
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

    private void ImmediateSettingControl_Changed(object sender, RoutedEventArgs e)
        => PushSettingsToController(saveImmediately: true, normalizeEmptyProcessList: false);

    private void DebouncedSettingControl_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        => PushSettingsToController(saveImmediately: false, normalizeEmptyProcessList: false);

    private void BrowserProcessesBox_TextChanged(object sender, TextChangedEventArgs e)
        => PushSettingsToController(saveImmediately: false, normalizeEmptyProcessList: false);

    private void BrowserProcessesBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        PushSettingsToController(saveImmediately: true, normalizeEmptyProcessList: true);
        _isLoading = true;
        try
        {
            BrowserProcessesBox.Text = string.Join(Environment.NewLine, _controller.GetSettingsCopy().BrowserProcessNames);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async void StartupSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _isUpdatingStartup)
        {
            return;
        }

        _isUpdatingStartup = true;
        StartupSwitch.IsEnabled = false;
        bool enable = StartupSwitch.IsChecked == true;

        try
        {
            await _controller.SetStartupEnabledAsync(enable);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "开机启动", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _isLoading = true;
            StartupSwitch.IsChecked = _controller.IsStartupEnabled;
            _isLoading = false;
            StartupSwitch.IsEnabled = true;
            _isUpdatingStartup = false;
        }

        UpdateStatusText();
    }

    private void OpenConfigButton_Click(object sender, RoutedEventArgs e)
    {
        _controller.FlushPendingSave();
        Process.Start(new ProcessStartInfo
        {
            FileName = _controller.ConfigPath,
            UseShellExecute = true
        });
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _controller.ResetToDefaults();
        SyncAllFromController();
    }

    private void UpdateStatusText()
    {
        var snapshot = _controller.Snapshot;
        if (!snapshot.Enabled)
        {
            StatusText.Text = "鼠标钩子 - 已停用";
        }
        else if (_controller.IsHookRunning)
        {
            StatusText.Text = "鼠标钩子 - 正在监听";
        }
        else
        {
            StatusText.Text = $"鼠标钩子不可用 - {_controller.HookLastError ?? "未知错误"}";
        }

        var lastSwitch = _controller.LastSwitch;
        LastActionText.Text = lastSwitch is null
            ? "上次切换 - 暂无"
            : $"上次切换 - {FormatDirection(lastSwitch.Direction)} - {lastSwitch.BrowserProcessName}.exe - {lastSwitch.At:HH:mm:ss}";

        ConfigPathText.Text = $"配置文件 - {_controller.ConfigPath}";
        StartupStatusText.Text = _controller.IsStartupEnabled
            ? "开机启动已启用。"
            : "开机启动未启用。";
    }

    private static string FormatDirection(SwitchDirection direction)
    {
        return direction == SwitchDirection.Previous ? "上一个标签页" : "下一个标签页";
    }
}
