using System.Windows;
using SideTabScroller.Services;

namespace SideTabScroller;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLog.Write(args.Exception);
            System.Windows.MessageBox.Show(
                ErrorLog.Format(args.Exception),
                "SideTab Scroller",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                ErrorLog.Write(exception);
            }
        };

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            ErrorLog.Write(exception);
            System.Windows.MessageBox.Show(
                ErrorLog.Format(exception),
                "SideTab Scroller",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
