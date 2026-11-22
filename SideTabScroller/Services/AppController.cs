using SideTabScroller.Models;

namespace SideTabScroller.Services;

internal sealed class AppController : IDisposable
{
    private const int SaveDebounceMs = 250;

    private readonly SettingsStore _settingsStore;
    private readonly StartupManager _startupManager;
    private readonly BrowserTabSwitcher _tabSwitcher;
    private readonly MouseWheelHook _mouseWheelHook;
    private readonly Lock _settingsLock = new();
    private readonly Lock _startupStateLock = new();
    private readonly Timer _saveDebounceTimer;

    private ScrollerSettings _settings;
    private ScrollerSettingsSnapshot _snapshot;
    private SwitchResult? _lastSwitch;
    private bool _isStartupEnabled;
    private int _startupSequence;
    private bool _pendingSave;
    private volatile bool _disposed;

    public AppController()
    {
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _snapshot = new ScrollerSettingsSnapshot(_settings);
        _startupManager = new StartupManager();
        _tabSwitcher = new BrowserTabSwitcher(() => Volatile.Read(ref _snapshot), OnTabSwitched);
        _mouseWheelHook = new MouseWheelHook(_tabSwitcher.HandleWheel);
        _saveDebounceTimer = new Timer(_ => SafeFlushPendingSave(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event Action? StateChanged;

    public ScrollerSettingsSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public SwitchResult? LastSwitch => Volatile.Read(ref _lastSwitch);
    public string ConfigPath => _settingsStore.ConfigPath;
    public bool IsHookRunning => _mouseWheelHook.IsRunning;
    public string? HookLastError => _mouseWheelHook.LastError;
    public bool IsStartupEnabled => Volatile.Read(ref _isStartupEnabled);

    public ScrollerSettings GetSettingsCopy()
    {
        lock (_settingsLock)
        {
            return _settings.Clone();
        }
    }

    public void InitializeRuntime()
    {
        SyncHookWithEnabledState();
        NotifyStateChanged();
    }

    public async Task RefreshStartupStateAndEnsureTaskAsync()
    {
        int seq = Volatile.Read(ref _startupSequence);
        bool enabled = await _startupManager.IsEnabledAsync().ConfigureAwait(false);

        lock (_startupStateLock)
        {
            if (seq == Volatile.Read(ref _startupSequence))
            {
                Volatile.Write(ref _isStartupEnabled, enabled);
            }
        }

        NotifyStateChanged();

        if (enabled)
        {
            await _startupManager.EnsureTaskConfiguredAsync().ConfigureAwait(false);
        }
    }

    public async Task SetStartupEnabledAsync(bool enable)
    {
        int seq = Interlocked.Increment(ref _startupSequence);
        await _startupManager.SetEnabledAsync(enable).ConfigureAwait(false);
        bool actual = await _startupManager.IsEnabledAsync().ConfigureAwait(false);

        lock (_startupStateLock)
        {
            if (seq == Volatile.Read(ref _startupSequence))
            {
                Volatile.Write(ref _isStartupEnabled, actual);
            }
        }

        NotifyStateChanged();
    }

    public void UpdateSettings(ScrollerSettings updated, bool saveImmediately)
    {
        bool shouldRestartOrSyncHook;

        lock (_settingsLock)
        {
            updated.Normalize();
            shouldRestartOrSyncHook = updated.Enabled != _settings.Enabled;
            _settings = updated.Clone();
            Volatile.Write(ref _snapshot, new ScrollerSettingsSnapshot(_settings));

            if (saveImmediately)
            {
                _pendingSave = false;
                _saveDebounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _settingsStore.Save(_settings);
            }
            else
            {
                _pendingSave = true;
                _saveDebounceTimer.Change(SaveDebounceMs, Timeout.Infinite);
            }
        }

        if (shouldRestartOrSyncHook)
        {
            SyncHookWithEnabledState();
        }

        NotifyStateChanged();
    }

    public void ToggleEnabled()
    {
        lock (_settingsLock)
        {
            _settings.Enabled = !_settings.Enabled;
            _pendingSave = false;
            _saveDebounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            Volatile.Write(ref _snapshot, new ScrollerSettingsSnapshot(_settings));
            _settingsStore.Save(_settings);
        }

        SyncHookWithEnabledState();
        NotifyStateChanged();
    }

    public void ResetToDefaults()
    {
        var defaults = ScrollerSettings.CreateDefault();
        UpdateSettings(defaults, saveImmediately: true);
    }

    public void FlushPendingSave()
    {
        lock (_settingsLock)
        {
            if (!_pendingSave)
            {
                return;
            }

            _pendingSave = false;
            _saveDebounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _settingsStore.Save(_settings);
        }
    }

    private void SafeFlushPendingSave()
    {
        try
        {
            FlushPendingSave();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }

    public void HandlePowerResume()
    {
        if (Snapshot.Enabled)
        {
            try
            {
                _mouseWheelHook.Restart();
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
            }
        }
        else
        {
            _mouseWheelHook.Stop();
        }

        NotifyStateChanged();
    }

    private void SyncHookWithEnabledState()
    {
        if (Snapshot.Enabled)
        {
            _mouseWheelHook.Start();
        }
        else
        {
            _mouseWheelHook.Stop();
        }
    }

    private void OnTabSwitched(SwitchResult result)
    {
        Volatile.Write(ref _lastSwitch, result);
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        FlushPendingSave();
        _saveDebounceTimer.Dispose();
        _mouseWheelHook.Dispose();
        _tabSwitcher.Dispose();
    }
}
