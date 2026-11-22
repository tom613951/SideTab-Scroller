using System.Runtime;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using SideTabScroller.Native;

namespace SideTabScroller.Services;

internal interface ISettingsWindowBackend
{
    void Show();
    void Shutdown();
}

internal sealed class SettingsWindowHost
{
    private readonly AppController _controller;
    private readonly Lock _syncLock = new();
    private ISettingsWindowBackend? _backend;
    private bool _shuttingDown;

    public SettingsWindowHost(AppController controller)
    {
        _controller = controller;
    }

    public void Show()
    {
        ISettingsWindowBackend backend;
        lock (_syncLock)
        {
            if (_shuttingDown)
            {
                return;
            }

            _backend ??= CreateWpfBackend(_controller);
            backend = _backend;
        }

        backend.Show();
    }

    public void Shutdown()
    {
        ISettingsWindowBackend? backend;
        lock (_syncLock)
        {
            _shuttingDown = true;
            backend = _backend;
        }

        backend?.Shutdown();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ISettingsWindowBackend CreateWpfBackend(AppController controller) =>
        new WpfSettingsWindowBackend(controller);
}

internal sealed class WpfSettingsWindowBackend : ISettingsWindowBackend
{
    private readonly AppController _controller;
    private readonly Lock _syncLock = new();

    private Thread? _wpfThread;
    private Dispatcher? _dispatcher;
    private MainWindow? _window;
    private Timer? _trimTimer;
    private bool _shuttingDown;

    public WpfSettingsWindowBackend(AppController controller)
    {
        _controller = controller;
    }

    public void Show()
    {
        lock (_syncLock)
        {
            if (_shuttingDown)
            {
                return;
            }

            _trimTimer?.Dispose();
            _trimTimer = null;

            if (_dispatcher != null && !_dispatcher.HasShutdownStarted && !_dispatcher.HasShutdownFinished)
            {
                _dispatcher.BeginInvoke(ShowOrActivateCore);
                return;
            }

            _dispatcher = null;
            _wpfThread = null;

            using var dispatcherReady = new ManualResetEventSlim(false);
            Exception? initError = null;

            _wpfThread = new Thread(() =>
            {
                try
                {
                    StartWpfThread(dispatcherReady);
                }
                catch (Exception ex)
                {
                    initError = ex;
                    if (!dispatcherReady.IsSet)
                    {
                        dispatcherReady.Set();
                    }
                    ErrorLog.Write(ex);
                }
            })
            {
                IsBackground = true,
                Name = "WpfSettingsThread"
            };

            _wpfThread.SetApartmentState(ApartmentState.STA);
            _wpfThread.Start();
            dispatcherReady.Wait();

            if (initError != null)
            {
                _wpfThread = null;
                _dispatcher = null;
                throw new InvalidOperationException("无法初始化设置窗口界面。", initError);
            }
        }
    }

    public void Shutdown()
    {
        Dispatcher? dispatcher;
        Thread? thread;

        lock (_syncLock)
        {
            _shuttingDown = true;
            _trimTimer?.Dispose();
            _trimTimer = null;
            dispatcher = _dispatcher;
            thread = _wpfThread;
        }

        if (dispatcher != null && !dispatcher.HasShutdownStarted)
        {
            try
            {
                dispatcher.InvokeShutdown();
            }
            catch
            {
                // Ignore shutdown exceptions
            }
        }

        if (thread != null && Thread.CurrentThread != thread)
        {
            thread.Join(1500);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void StartWpfThread(ManualResetEventSlim dispatcherReady)
    {
        try
        {
            var app = Application.Current as App;
            if (app == null)
            {
                app = new App();
                _dispatcher = Dispatcher.CurrentDispatcher;
                app.InitializeComponent();
                app.ConfigureExceptionHandling();
                app.Startup += (_, _) =>
                {
                    dispatcherReady.Set();
                    ShowOrActivateCore();
                };
                app.Run();
                return;
            }

            // If Application already existed in this AppDomain after an unexpected Dispatcher error,
            // run a clean Dispatcher loop on this STA thread and still honor Show().
            _dispatcher = Dispatcher.CurrentDispatcher;
            dispatcherReady.Set();
            _dispatcher.BeginInvoke(ShowOrActivateCore);
            Dispatcher.Run();
        }
        finally
        {
            lock (_syncLock)
            {
                _window = null;
                _dispatcher = null;
                _wpfThread = null;
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ShowOrActivateCore()
    {
        if (_shuttingDown)
        {
            return;
        }

        if (_window != null)
        {
            if (!_window.IsVisible)
            {
                _window.Show();
            }

            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.Activate();
            return;
        }

        var window = new MainWindow(_controller);
        _window = window;
        if (Application.Current != null)
        {
            Application.Current.MainWindow = window;
        }

        window.Closed += (_, _) =>
        {
            lock (_syncLock)
            {
                if (ReferenceEquals(_window, window))
                {
                    _window = null;
                }

                if (Application.Current != null && ReferenceEquals(Application.Current.MainWindow, window))
                {
                    Application.Current.MainWindow = null;
                }

                if (!_shuttingDown)
                {
                    _trimTimer?.Dispose();
                    _trimTimer = new Timer(_ => TrimAfterWindowClose(), null, 1500, Timeout.Infinite);
                }
            }
        };

        window.Show();
        window.Activate();
    }

    private void TrimAfterWindowClose()
    {
        lock (_syncLock)
        {
            if (_shuttingDown || _window != null)
            {
                return;
            }
        }

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        NativeMethods.TrimWorkingSet();
    }
}
