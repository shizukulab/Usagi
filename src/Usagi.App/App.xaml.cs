using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Usagi.App.Localization;
using Usagi.App.Services;
using Usagi.App.Settings;
using Usagi.App.Themes;
using Usagi.App.Tray;
using Usagi.App.ViewModels;
using Usagi.App.Views;
using Usagi.Core.Api;
using Usagi.Core.ClaudeCode;
using Usagi.Core.Usage;
using Usagi.Platform.ClaudeCode;
using Usagi.Platform.Notifications;
using Usagi.Platform.Startup;
using Usagi.Platform.TrayIcon;

namespace Usagi.App;

/// <summary>Composition root: builds the object graph on startup and tears it down on exit.</summary>
public partial class App : System.Windows.Application
{
    private const string AppName = "Usagi";

    // Usage is read from Anthropic's actual API (api.anthropic.com) using Claude Code
    // CLI's own OAuth credentials — not claude.ai's bot-protected web app — so a plain
    // HttpClient is sufficient; no embedded browser needed.
    // The default timeout (100 s) would leave a stalled refresh "refreshing", and refusing
    // manual refreshes, for that long.
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    // Held for the life of the process, so a second copy can tell one is already running.
    private Mutex? _singleInstanceMutex;
    private readonly FlyoutViewModel _flyoutViewModel = new();

    // Assigned in OnStartup, which WPF always runs before anything else here.
    private AppSettingsStore _settingsStore = null!;
    private AppStateStore _stateStore = null!;
    private IClaudeCodeEnvironment _claudeCode = null!;
    private UsageFetcher _usageFetcher = null!;
    private ILaunchAtLoginService _launchAtLoginService = null!;
    private TrayIconController _trayIconController = null!;
    private TaskbarBarController _taskbarBarController = null!;
    private ClickThroughController _clickThroughController = null!;
    private UsageRefreshCoordinator _coordinator = null!;
    private DispatcherTimer _minuteTimer = null!;
    private FlyoutWindow _flyoutWindow = null!;
    private DispatcherTimer _refreshTimer = null!;
    private CredentialsFileWatcher _credentialsWatcher = null!;
    private UpdateService _updateService = null!;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // A second copy would add a second tray icon, double the API calls and overwrite the
        // first one's settings file, so it just exits.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, $@"Local\{AppName}.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        // Safety net: this is a background tray app with no visible main window,
        // so an unhandled exception would otherwise silently kill it with no
        // indication to the user beyond the tray icon disappearing. Surface it
        // and keep running wherever the failure isn't fatal to the process.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _settingsStore = new AppSettingsStore();
        var settings = _settingsStore.Current;
        _stateStore = new AppStateStore();
        ThemeManager.Apply(settings.Theme);
        Loc.Apply(settings.Language);

        _claudeCode = new ClaudeCodeEnvironment();
        _usageFetcher = new UsageFetcher(new ClaudeCodeUsageClient(_httpClient), _claudeCode, _stateStore);
        _launchAtLoginService = new RunKeyLaunchAtLoginService(
            appName: AppName,
            executablePathProvider: () => Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0]);

        _flyoutWindow = new FlyoutWindow
        {
            DataContext = _flyoutViewModel,
            SavedPosition = _stateStore.Current.FlyoutPosition
        };
        _flyoutWindow.ScaleSaved += scale => _settingsStore.Update(s => s.FlyoutScale = scale);
        _flyoutWindow.CompactChanged += compact => _settingsStore.Update(s => s.FlyoutCompact = compact);
        _flyoutWindow.PositionSaved += SaveFlyoutPosition;
        _flyoutViewModel.RefreshRequested += RefreshNow;
        _flyoutViewModel.SettingsRequested += OpenSettingsWindow;
        Loc.LanguageChanged += _flyoutViewModel.RefreshLanguage;
        // The view model was built (as a field) before the saved language was applied above.
        _flyoutViewModel.RefreshLanguage();

        _trayIconController = new TrayIconController(new TrayIconRenderer());
        _trayIconController.Clicked += _flyoutWindow.Toggle;
        _trayIconController.RefreshRequested += RefreshNow;
        _trayIconController.SettingsRequested += OpenSettingsWindow;
        _trayIconController.CanResetPosition = () => _flyoutWindow.SavedPosition is not null;
        _trayIconController.ResetPositionRequested += () =>
        {
            _flyoutWindow.ResetPosition();
            SaveFlyoutPosition(null);
        };
        _trayIconController.ExitRequested += () => Shutdown();

        _taskbarBarController = new TaskbarBarController(_flyoutViewModel, _settingsStore);
        _taskbarBarController.Clicked += _flyoutWindow.Toggle;
        // A right-click on the flyout or the taskbar bars opens the tray icon's menu there.
        _taskbarBarController.MenuRequested += _trayIconController.ShowMenu;
        _flyoutWindow.MenuRequested += _trayIconController.ShowMenu;

        _clickThroughController = new ClickThroughController();
        _clickThroughController.Add(() => _settingsStore.Current.FlyoutClickThrough, () => [_flyoutWindow]);
        _clickThroughController.Add(() => _settingsStore.Current.TaskbarBarClickThrough, () => _taskbarBarController.Strips);
        ApplyAppearance();
        _trayIconController.IsFlyoutCompact = () => _settingsStore.Current.FlyoutCompact;
        _trayIconController.FlyoutCompactToggled += () => _settingsStore.Update(s => s.FlyoutCompact = !s.FlyoutCompact);
        _trayIconController.IsFlyoutClickThrough = () => _settingsStore.Current.FlyoutClickThrough;
        _trayIconController.IsTaskbarBarClickThrough = () => _settingsStore.Current.TaskbarBarClickThrough;
        _trayIconController.IsTaskbarBarShown = () => _settingsStore.Current.ShowTaskbarBar;
        _trayIconController.FlyoutClickThroughToggled += () => _settingsStore.Update(s => s.FlyoutClickThrough = !s.FlyoutClickThrough);
        _trayIconController.TaskbarBarClickThroughToggled += () => _settingsStore.Update(s => s.TaskbarBarClickThrough = !s.TaskbarBarClickThrough);

        _coordinator = new UsageRefreshCoordinator(
            _usageFetcher, _settingsStore, _stateStore, _flyoutViewModel, new ToastNotificationService(), _trayIconController.UpdateIcon);

        _coordinator.ShowSavedUsage();

        _updateService = new UpdateService(_httpClient, _stateStore);

        // Refreshes can be minutes apart; the "resets in" times shown in between must still run down.
        _minuteTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _minuteTimer.Tick += (_, _) => _flyoutViewModel.RefreshTimes();
        _minuteTimer.Start();

        _refreshTimer = new DispatcherTimer { Interval = settings.RefreshInterval };
        _refreshTimer.Tick += (_, _) => RefreshNow();
        _refreshTimer.Start();

        // Wherever a setting is changed (the settings window, the tray menu, a drag or a
        // double-click on the flyout or the taskbar bars), this is what puts it into effect.
        _settingsStore.Changed += () =>
        {
            // Setting Interval restarts the timer, so only touch it when it actually changed.
            if (_refreshTimer.Interval != _settingsStore.Current.RefreshInterval)
                _refreshTimer.Interval = _settingsStore.Current.RefreshInterval;
            ApplyAppearance();
        };

        // Picks up a finished sign-in (or Claude Code renewing its token) immediately.
        _credentialsWatcher = new CredentialsFileWatcher();
        _credentialsWatcher.Changed += RefreshNow;

        RefreshOnStartup(settings.ShowFlyoutOnStartup);
    }

    // The flyout opens once the first refresh is in rather than straight away: opened empty, it
    // shows only its frame (in compact mode, just the creature) and the rows pop in a moment later.
    // async void for the same reason as RefreshNow.
    private async void RefreshOnStartup(bool showFlyout)
    {
        try
        {
            await _coordinator.RefreshAsync();
        }
        finally
        {
            // Unless the user has already opened it from the tray icon meanwhile.
            if (showFlyout && !_flyoutWindow.IsVisible && !Dispatcher.HasShutdownStarted)
                _flyoutWindow.Toggle();
        }

        // So the settings window's Account page is already filled in the first time it opens.
        await SettingsViewModel.PreloadAccountAsync(_claudeCode);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _refreshTimer?.Stop();
        _minuteTimer?.Stop();
        ThemeManager.Shutdown();
        _trayIconController?.Dispose();
        _taskbarBarController?.Dispose();
        _clickThroughController?.Dispose();
        _credentialsWatcher?.Dispose();
        _httpClient.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    // async void on purpose: an unexpected exception then reaches
    // DispatcherUnhandledException (and the banner) instead of vanishing with a Task.
    private async void RefreshNow() => await _coordinator.RefreshAsync();

    /// <summary>
    /// Puts the settings for how things look and behave onto the windows, the tray icon and the
    /// taskbar bars: once at startup, and again whenever one changes.
    /// </summary>
    private void ApplyAppearance()
    {
        var settings = _settingsStore.Current;

        _flyoutWindow.Topmost = settings.AlwaysOnTop;
        _flyoutWindow.Compact = settings.FlyoutCompact;
        // These two restart an animation or a layout when set, so only when they actually changed.
        if (_flyoutWindow.RestingOpacity != settings.FlyoutOpacity)
            _flyoutWindow.RestingOpacity = settings.FlyoutOpacity;
        if (_flyoutWindow.Scale != settings.FlyoutScale)
            _flyoutWindow.Scale = settings.FlyoutScale;

        _flyoutViewModel.ShowResetClockTime = settings.ResetTimeDisplay == ResetTimeDisplay.Clock;
        _flyoutViewModel.VisibleRows = settings.VisibleRows;
        _flyoutViewModel.CompactLayout = PartLayout.ForFlyout(settings);
        _flyoutViewModel.TaskbarVisibleRows = settings.TaskbarBarVisibleRows;
        _flyoutViewModel.TaskbarLayout = PartLayout.ForTaskbarBar(settings);
        _flyoutViewModel.ShowMascot = settings.FlyoutShowMascot;
        _flyoutViewModel.MascotAnimation = settings.MascotAnimation;
        _flyoutViewModel.MascotBunnyEars = settings.MascotBunnyEars;
        _flyoutViewModel.MascotCarrot = settings.MascotCarrot;
        _flyoutViewModel.MascotBodyGauge = settings.MascotBodyGauge;
        _flyoutViewModel.MascotMoodSource = settings.MascotMoodSource;
        _flyoutViewModel.MascotWakesOnReset = settings.MascotWakesOnReset;
        _flyoutViewModel.MascotStartlesOnJump = settings.MascotStartlesOnJump;
        _flyoutViewModel.MascotHoldsSign = settings.MascotHoldsSign;
        if (!_flyoutViewModel.MascotSignThresholds.SequenceEqual(settings.EffectiveNotificationThresholds))
            _flyoutViewModel.MascotSignThresholds = settings.EffectiveNotificationThresholds;
        _flyoutViewModel.MascotFollowsCursor = settings.MascotFollowsCursor;
        _flyoutViewModel.MascotIdleActs = settings.MascotIdleActs;

        _trayIconController.Style = settings.TrayIconStyle;

        _taskbarBarController.Enabled = settings.ShowTaskbarBar;
        _taskbarBarController.Refresh();
        _clickThroughController.Refresh();
    }

    private void SaveFlyoutPosition(ScreenPoint? position)
    {
        _stateStore.Current.FlyoutPosition = position;
        _stateStore.Save();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _flyoutViewModel.SetBanner(Loc.Format("Error_Unexpected", e.Exception.Message));
        e.Handled = true;
    }

    private void OpenSettingsWindow()
    {
        // Reachable from both the flyout and the tray menu; never stack a second copy.
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var viewModel = new SettingsViewModel(_settingsStore, _launchAtLoginService, _usageFetcher, _claudeCode, _updateService, _flyoutViewModel);
        viewModel.ConnectionTested += result => _coordinator.ApplyResult(result);
        _settingsWindow = new SettingsWindow(viewModel);
        // Modeless, like Obsidian's settings: the flyout and tray menu stay usable while it's open.
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;

            // Settings apply as they're changed; fetching with a new mode/interval waits until the
            // window closes so stepping the interval or flipping the mode doesn't send a prompt each click.
            // Skipped when the app is exiting (tray "Exit" closes this window during shutdown).
            if (viewModel.FetchSettingsChanged && !Dispatcher.HasShutdownStarted)
                RefreshNow();
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }
}
