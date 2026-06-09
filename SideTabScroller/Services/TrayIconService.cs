using SideTabScroller.Models;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace SideTabScroller.Services;

internal sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Drawing.Icon _icon;
    private readonly Action _showSettings;
    private readonly Action _toggleEnabled;
    private readonly Action _exitApplication;
    private readonly Action _showContextMenu;
    private ScrollerSettings? _settings;
    private bool _hookRunning;

    public TrayIconService(Action showSettings, Action toggleEnabled, Action exitApplication, Action showContextMenu)
    {
        _showSettings = showSettings;
        _toggleEnabled = toggleEnabled;
        _exitApplication = exitApplication;
        _showContextMenu = showContextMenu;
        _icon = LoadIcon();

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "侧栏滚轮切换标签",
            Visible = true
        };

        _notifyIcon.MouseUp += NotifyIcon_MouseUp;
        _notifyIcon.DoubleClick += (_, _) => showSettings();
    }

    public void Update(ScrollerSettings settings, bool hookRunning)
    {
        _settings = settings;
        _hookRunning = hookRunning;
        _notifyIcon.Text = hookRunning
            ? settings.Enabled ? "侧栏滚轮切换标签 - 正在监听" : "侧栏滚轮切换标签 - 已停用"
            : "侧栏滚轮切换标签 - 钩子不可用";
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private void NotifyIcon_MouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Right)
        {
            WpfApplication.Current.Dispatcher.Invoke(_showContextMenu);
        }
    }

    private static Drawing.Icon LoadIcon()
    {
        var resource = WpfApplication.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        if (resource is null)
        {
            return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
        }

        using var stream = resource.Stream;
        return new Drawing.Icon(stream);
    }
}
