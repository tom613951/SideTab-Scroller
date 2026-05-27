using SideTabScroller.Models;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace SideTabScroller.Services;

internal sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _toggleEnabledItem;

    public TrayIconService(Action showSettings, Action toggleEnabled, Action exitApplication)
    {
        _toggleEnabledItem = new Forms.ToolStripMenuItem("Disable", null, (_, _) => toggleEnabled());
        var openItem = new Forms.ToolStripMenuItem("Settings", null, (_, _) => showSettings());
        var exitItem = new Forms.ToolStripMenuItem("Exit", null, (_, _) => exitApplication());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(_toggleEnabledItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Text = "SideTab Scroller",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => showSettings();
    }

    public void Update(ScrollerSettings settings, bool hookRunning)
    {
        _toggleEnabledItem.Text = settings.Enabled ? "Disable" : "Enable";
        _notifyIcon.Text = hookRunning
            ? settings.Enabled ? "SideTab Scroller - listening" : "SideTab Scroller - disabled"
            : "SideTab Scroller - hook unavailable";
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
