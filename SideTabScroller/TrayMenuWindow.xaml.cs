using System.Windows;
using SideTabScroller.Models;
using Forms = System.Windows.Forms;

namespace SideTabScroller;

public partial class TrayMenuWindow : Window
{
    private readonly Action _showSettings;
    private readonly Action _toggleEnabled;
    private readonly Action _exitApplication;
    private bool _closingFromCommand;

    public TrayMenuWindow(Action showSettings, Action toggleEnabled, Action exitApplication)
    {
        InitializeComponent();

        _showSettings = showSettings;
        _toggleEnabled = toggleEnabled;
        _exitApplication = exitApplication;
    }

    public void Update(ScrollerSettings settings, bool hookRunning)
    {
        StatusText.Text = hookRunning
            ? settings.Enabled ? "正在监听侧边栏滚轮" : "已停用"
            : "鼠标钩子不可用";

        ToggleText.Text = settings.Enabled ? "停用" : "启用";
        ToggleGlyph.Text = settings.Enabled ? "\uE73E" : "\uE768";
    }

    public void ShowNearCursor()
    {
        var cursor = Forms.Control.MousePosition;
        var screen = Forms.Screen.FromPoint(cursor);
        var scale = 96.0 / Native.NativeMethods.GetDpiForSystem();

        Left = (cursor.X * scale) - Width + 12;
        Top = (cursor.Y * scale) - 8;

        var workArea = screen.WorkingArea;
        var workLeft = workArea.Left * scale;
        var workTop = workArea.Top * scale;
        var workRight = workArea.Right * scale;
        var workBottom = workArea.Bottom * scale;

        if (Left + Width > workRight)
        {
            Left = workRight - Width - 8;
        }

        if (Left < workLeft)
        {
            Left = workLeft + 8;
        }

        Show();
        UpdateLayout();

        if (Top + ActualHeight > workBottom)
        {
            Top = workBottom - ActualHeight - 8;
        }

        if (Top < workTop)
        {
            Top = workTop + 8;
        }

        Activate();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => RunCommand(_showSettings);

    private void ToggleButton_Click(object sender, RoutedEventArgs e) => RunCommand(_toggleEnabled);

    private void ExitButton_Click(object sender, RoutedEventArgs e) => RunCommand(_exitApplication);

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_closingFromCommand)
        {
            Close();
        }
    }

    private void RunCommand(Action command)
    {
        _closingFromCommand = true;
        Close();
        command();
    }
}
