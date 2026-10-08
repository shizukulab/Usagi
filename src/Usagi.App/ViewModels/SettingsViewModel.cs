using Usagi.App.Localization;
using Usagi.App.Services;
using Usagi.App.Settings;
using Usagi.App.Themes;
using Usagi.App.Views;
using Usagi.Core.ClaudeCode;
using Usagi.Core.Usage;
using Usagi.Platform.Startup;
using Usagi.Platform.TrayIcon;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Usagi.App.ViewModels;

/// <summary>
/// One entry of a settings drop-down: the value it selects and its label, either a string key
/// (translated, so it follows the UI language) or fixed text (e.g. a language's own name).
/// </summary>
public sealed record ChoiceOption(object Value, string? TextKey = null, string? Text = null)
{
    public string Label => TextKey is { } key ? Loc.Get(key) : Text ?? string.Empty;
}

/// <summary>
/// State and actions for SettingsWindow. Every change is written to the
/// <see cref="AppSettingsStore"/> as soon as it's made (Windows 11 Settings style);
/// there is no Save/Cancel. The interval is the exception: it's written when stepped or
/// committed (focus leaves the box / the window closes), not on every keystroke.
/// A setting changed elsewhere while the window is open (the tray menu, a drag on the flyout
/// or the taskbar bars) is taken over from the store, so the window never shows a stale value.
/// <para>
/// One class per window, in a file per page: this one holds what the pages share and the
/// plain settings; the refresh interval, the notification thresholds and the account section
/// each have their own.
/// </para>
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsStore _settingsStore;
    private readonly ILaunchAtLoginService _launchAtLoginService;
    private readonly UsageFetcher _usageFetcher;
    private readonly IClaudeCodeEnvironment _claudeCode;
    private readonly AppSettings _initialSettings;

    // Set while the properties are being filled in from the store, which must not write them back.
    private bool _loading;

    // Set while this window's own change is being saved, which it needn't take over again.
    private bool _applying;

    public SettingsViewModel(
        AppSettingsStore settingsStore,
        ILaunchAtLoginService launchAtLoginService,
        UsageFetcher usageFetcher,
        IClaudeCodeEnvironment claudeCode,
        UpdateService updateService,
        FlyoutViewModel live)
    {
        _settingsStore = settingsStore;
        _launchAtLoginService = launchAtLoginService;
        _usageFetcher = usageFetcher;
        _claudeCode = claudeCode;
        AttachUpdateService(updateService);
        AttachPreviews(live);
        AttachSurfaceOptions();

        var settings = settingsStore.Current;
        _initialSettings = new AppSettings { AvoidTokenUsage = settings.AvoidTokenUsage, RefreshIntervalSeconds = settings.RefreshIntervalSeconds };
        TaskbarBarDisplays = [.. TaskbarBarDisplayViewModel.ForConnectedMonitors(settings, Apply)];
        Load();
        ShowLastAccount();

        settingsStore.Changed += OnSettingsChanged;
        Loc.LanguageChanged += OnUiLanguageChanged;
    }

    public static IReadOnlyList<ChoiceOption> ThemeOptions { get; } =
    [
        new(AppTheme.System, "Settings_ThemeSystem"),
        new(AppTheme.Light, "Settings_ThemeLight"),
        new(AppTheme.Dark, "Settings_ThemeDark"),
    ];

    // Language names stay in their own language so each is findable whatever the UI shows.
    public static IReadOnlyList<ChoiceOption> LanguageOptions { get; } =
    [
        new(AppLanguage.System, "Settings_LanguageSystem"),
        new(AppLanguage.English, Text: "English"),
        new(AppLanguage.Japanese, Text: "日本語"),
    ];

    public static IReadOnlyList<ChoiceOption> ResetTimeDisplayOptions { get; } =
    [
        new(ResetTimeDisplay.Remaining, "Settings_ResetTimeRemaining"),
        new(ResetTimeDisplay.Clock, "Settings_ResetTimeClock"),
    ];

    public static IReadOnlyList<ChoiceOption> TrayIconStyleOptions { get; } =
    [
        new(TrayIconStyle.Ring, "Settings_TrayIconRing"),
        new(TrayIconStyle.DoubleRing, "Settings_TrayIconDoubleRing"),
        new(TrayIconStyle.Number, "Settings_TrayIconNumber"),
        new(TrayIconStyle.NumberOnCreature, "Settings_TrayIconCreature"),
    ];

    public static IReadOnlyList<ChoiceOption> MascotAnimationOptions { get; } =
    [
        new(MascotAnimation.Off, "Settings_MascotAnimationOff"),
        new(MascotAnimation.Subtle, "Settings_MascotAnimationSubtle"),
        new(MascotAnimation.Lively, "Settings_MascotAnimationLively"),
    ];

    public static IReadOnlyList<ChoiceOption> MascotBodyGaugeOptions { get; } =
    [
        new(MascotBodyGauge.None, "Settings_MascotBodyGaugeNone"),
        new(MascotBodyGauge.Pale, "Settings_MascotBodyGaugePale"),
        new(MascotBodyGauge.Shadow, "Settings_MascotBodyGaugeShadow"),
        new(MascotBodyGauge.Dots, "Settings_MascotBodyGaugeDots"),
    ];

    public static IReadOnlyList<ChoiceOption> MascotMoodSourceOptions { get; } =
    [
        new(MascotMoodSource.Higher, "Settings_MascotMoodSourceHigher"),
        new(MascotMoodSource.Session, "Settings_MascotMoodSourceSession"),
        new(MascotMoodSource.Weekly, "Settings_MascotMoodSourceWeekly"),
    ];

    /// <summary>
    /// Whether how usage is fetched (mode or interval) differs from when the window opened,
    /// so the caller can refresh once on close rather than on every click.
    /// </summary>
    public bool FetchSettingsChanged =>
        _settingsStore.Current.AvoidTokenUsage != _initialSettings.AvoidTokenUsage
        || _settingsStore.Current.RefreshInterval != _initialSettings.RefreshInterval;

    [ObservableProperty]
    private bool _notificationsEnabled;

    [ObservableProperty]
    private bool _launchAtLoginEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartWithTaskbarBarOnly))]
    private bool _showFlyoutOnStartup;

    /// <summary>
    /// <see cref="ShowFlyoutOnStartup"/> the other way round, as the Taskbar bars page offers it to
    /// those who use the bars: they may well not want the window too, every time they sign in.
    /// </summary>
    public bool StartWithTaskbarBarOnly
    {
        get => !ShowFlyoutOnStartup;
        set => ShowFlyoutOnStartup = !value;
    }

    [ObservableProperty]
    private bool _alwaysOnTop;

    [ObservableProperty]
    private bool _flyoutCompact;

    [ObservableProperty]
    private bool _flyoutClickThrough;

    [ObservableProperty]
    private bool _weeklyNotificationsEnabled;

    [ObservableProperty]
    private ResetTimeDisplay _resetTimeDisplay;

    [ObservableProperty]
    private TrayIconStyle _trayIconStyle;

    [ObservableProperty]
    private bool _taskbarBarClickThrough;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAnimateMascot), nameof(CanMascotReact))]
    private bool _showTaskbarBar;

    [ObservableProperty]
    private bool _showTaskbarBarOverFullScreen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMascotReact))]
    private MascotAnimation _mascotAnimation;

    [ObservableProperty]
    private bool _mascotBunnyEars;

    [ObservableProperty]
    private bool _mascotCarrot;

    [ObservableProperty]
    private MascotBodyGauge _mascotBodyGauge;

    [ObservableProperty]
    private MascotMoodSource _mascotMoodSource;

    [ObservableProperty]
    private bool _mascotWakesOnReset;

    [ObservableProperty]
    private bool _mascotStartlesOnJump;

    [ObservableProperty]
    private bool _mascotHoldsSign;

    [ObservableProperty]
    private bool _mascotFollowsCursor;

    [ObservableProperty]
    private bool _mascotIdleActs;

    /// <summary>Whether the creature can do the things it does on its own account: it's shown, and it's animated at all.</summary>
    public bool CanMascotReact => CanAnimateMascot && MascotAnimation != MascotAnimation.Off;

    /// <summary>Whether there is a creature to animate or dress up: in the flyout, or on taskbar bars that are shown.</summary>
    public bool CanAnimateMascot => WindowOptions.ShowMascot || (ShowTaskbarBar && TaskbarOptions.ShowMascot);

    /// <summary>One row per connected monitor: whether its taskbar gets the bars, and where on it.</summary>
    public IReadOnlyList<TaskbarBarDisplayViewModel> TaskbarBarDisplays { get; }

    // Slider range (as doubles: Slider.Minimum/Maximum don't take an int).
    public double MinFlyoutOpacityPercent => AppSettings.MinFlyoutOpacityPercent;
    public double MaxFlyoutOpacityPercent => AppSettings.MaxFlyoutOpacityPercent;

    /// <summary>Flyout opacity in percent; the slider previews it live on an open flyout.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetFlyoutOpacityCommand))]
    private int _flyoutOpacityPercent;

    public double MinFlyoutScalePercent => FlyoutWindow.MinScale * 100;
    public double MaxFlyoutScalePercent => FlyoutWindow.MaxScale * 100;

    /// <summary>
    /// Flyout size in percent; the slider previews it live on an open flyout. Follows the
    /// flyout being resized by dragging while this window is open.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetFlyoutScaleCommand))]
    private int _flyoutScalePercent;

    /// <summary>A saved scale as the slider shows it (clamped: the file may have been hand-edited).</summary>
    public static int ToScalePercent(double scale) =>
        (int)Math.Round(Math.Clamp(scale, FlyoutWindow.MinScale, FlyoutWindow.MaxScale) * 100);

    [ObservableProperty]
    private AppTheme _theme;

    [ObservableProperty]
    private AppLanguage _language;

    partial void OnThemeChanged(AppTheme value)
    {
        ThemeManager.Apply(value);
        Apply();
    }

    partial void OnLanguageChanged(AppLanguage value)
    {
        Loc.Apply(value);
        Apply();
    }

    partial void OnNotificationsEnabledChanged(bool value) => Apply();

    partial void OnShowFlyoutOnStartupChanged(bool value) => Apply();

    partial void OnAlwaysOnTopChanged(bool value) => Apply();

    partial void OnWeeklyNotificationsEnabledChanged(bool value) => Apply();

    partial void OnResetTimeDisplayChanged(ResetTimeDisplay value) => Apply();

    partial void OnTrayIconStyleChanged(TrayIconStyle value) => Apply();

    partial void OnFlyoutClickThroughChanged(bool value) => Apply();

    partial void OnFlyoutCompactChanged(bool value) => Apply();

    partial void OnTaskbarBarClickThroughChanged(bool value) => Apply();

    partial void OnShowTaskbarBarChanged(bool value)
    {
        TaskbarOptions.IsAvailable = value;
        Apply();
    }

    partial void OnShowTaskbarBarOverFullScreenChanged(bool value) => Apply();

    partial void OnMascotAnimationChanged(MascotAnimation value) => Apply();

    partial void OnMascotBunnyEarsChanged(bool value) => Apply();

    partial void OnMascotCarrotChanged(bool value) => Apply();

    partial void OnMascotBodyGaugeChanged(MascotBodyGauge value) => Apply();

    partial void OnMascotMoodSourceChanged(MascotMoodSource value) => Apply();

    partial void OnMascotWakesOnResetChanged(bool value) => Apply();

    partial void OnMascotStartlesOnJumpChanged(bool value) => Apply();

    partial void OnMascotHoldsSignChanged(bool value) => Apply();

    partial void OnMascotFollowsCursorChanged(bool value) => Apply();

    partial void OnMascotIdleActsChanged(bool value) => Apply();

    partial void OnFlyoutOpacityPercentChanged(int value) => Apply();

    // Written here rather than in Apply: a drag saves a finer scale than the slider's whole
    // percent, which mustn't be rounded off by an unrelated setting being changed.
    partial void OnFlyoutScalePercentChanged(int value)
    {
        if (_loading || ToScalePercent(_settingsStore.Current.FlyoutScale) == value)
            return;
        _settingsStore.Current.FlyoutScale = value / 100.0;
        Apply();
    }

    partial void OnLaunchAtLoginEnabledChanged(bool value)
    {
        if (!_loading)
            _launchAtLoginService.SetEnabled(value);
    }

    [RelayCommand(CanExecute = nameof(CanResetFlyoutOpacity))]
    private void ResetFlyoutOpacity() => FlyoutOpacityPercent = 100;

    private bool CanResetFlyoutOpacity() => FlyoutOpacityPercent != 100;

    [RelayCommand(CanExecute = nameof(CanResetFlyoutScale))]
    private void ResetFlyoutScale() => FlyoutScalePercent = 100;

    private bool CanResetFlyoutScale() => FlyoutScalePercent != 100;

    // XAML text follows Loc on its own; strings built here have to be rebuilt. The account
    // text is rebuilt from the last lookup rather than re-running the (slow) CLI, so it
    // switches language at the same moment as everything else.
    private void OnUiLanguageChanged()
    {
        OnPropertyChanged(nameof(RefreshIntervalError));
        OnPropertyChanged(nameof(RefreshIntervalHint));
        OnPropertyChanged(nameof(TokensPerHourText));
        OnPropertyChanged(nameof(RefreshIntervalToolTip));
        OnPropertyChanged(nameof(ProbeModelText));
        OnPropertyChanged(nameof(NotificationThresholdsHint));
        OnPropertyChanged(nameof(ResetThresholdsToolTip));
        foreach (var display in TaskbarBarDisplays)
            display.RefreshLanguage();
        StatusMessage = null;
        RefreshAccountLanguage();
        WindowOptions.RefreshTexts();
        TaskbarOptions.RefreshTexts();
        RefreshUpdateLanguage();
        UpdatePreviews();
    }

    /// <summary>Call when the window closes: commits a half-typed interval and detaches.</summary>
    public void Close()
    {
        NormalizeRefreshInterval();
        _settingsStore.Changed -= OnSettingsChanged;
        Loc.LanguageChanged -= OnUiLanguageChanged;
        DetachPreviews();
    }

    // A change saved by someone else. This window's own are already what it shows.
    private void OnSettingsChanged()
    {
        if (!_applying)
            Load();
    }

    /// <summary>Fills the properties in from the store, without writing anything back.</summary>
    private void Load()
    {
        _loading = true;
        try
        {
            var settings = _settingsStore.Current;
            AvoidTokenUsage = settings.AvoidTokenUsage;
            RefreshIntervalSeconds = _lastValidRefreshInterval = settings.RefreshIntervalSeconds;
            NotificationsEnabled = settings.NotificationsEnabled;
            WeeklyNotificationsEnabled = settings.WeeklyNotificationsEnabled;
            ResetTimeDisplay = settings.ResetTimeDisplay;
            TrayIconStyle = settings.TrayIconStyle;
            ShowFlyoutOnStartup = settings.ShowFlyoutOnStartup;
            AlwaysOnTop = settings.AlwaysOnTop;
            FlyoutClickThrough = settings.FlyoutClickThrough;
            FlyoutCompact = settings.FlyoutCompact;
            TaskbarBarClickThrough = settings.TaskbarBarClickThrough;
            ShowTaskbarBar = settings.ShowTaskbarBar;
            ShowTaskbarBarOverFullScreen = settings.ShowTaskbarBarOverFullScreen;
            WindowOptions.Load(settings.VisibleRows, settings.EffectiveFlyoutPartOrder, part => part switch
            {
                DisplayPart.Mascot => settings.FlyoutShowMascot,
                DisplayPart.Labels => settings.CompactShowLabels,
                DisplayPart.Bar => settings.EffectiveCompactShowBar,
                DisplayPart.Percentage => settings.CompactShowPercentage,
                _ => settings.CompactShowResetTime
            });
            TaskbarOptions.Load(settings.TaskbarBarVisibleRows, settings.EffectiveTaskbarBarPartOrder, part => part switch
            {
                DisplayPart.Mascot => settings.TaskbarBarShowMascot,
                DisplayPart.Labels => settings.TaskbarBarShowLabels,
                DisplayPart.Bar => settings.EffectiveTaskbarBarShowBar,
                DisplayPart.Percentage => settings.TaskbarBarShowPercentage,
                _ => settings.TaskbarBarShowResetTime
            });
            TaskbarOptions.IsAvailable = ShowTaskbarBar; // (its change handler doesn't run when it's loaded as it was)
            MascotAnimation = settings.MascotAnimation;
            MascotBunnyEars = settings.MascotBunnyEars;
            MascotCarrot = settings.MascotCarrot;
            MascotBodyGauge = settings.MascotBodyGauge;
            MascotMoodSource = settings.MascotMoodSource;
            MascotWakesOnReset = settings.MascotWakesOnReset;
            MascotStartlesOnJump = settings.MascotStartlesOnJump;
            MascotHoldsSign = settings.MascotHoldsSign;
            MascotFollowsCursor = settings.MascotFollowsCursor;
            MascotIdleActs = settings.MascotIdleActs;
            LoadThresholds(settings.EffectiveNotificationThresholds);
            foreach (var display in TaskbarBarDisplays)
                display.Reload();
            FlyoutOpacityPercent = (int)Math.Round(settings.FlyoutOpacity * 100);
            FlyoutScalePercent = ToScalePercent(settings.FlyoutScale);
            LaunchAtLoginEnabled = _launchAtLoginService.IsEnabled;
            Theme = settings.Theme;
            Language = settings.Language;
        }
        finally
        {
            _loading = false;
        }

        UpdatePreviews();
    }

    /// <summary>Writes the current state to the store, which saves it and puts it into effect.</summary>
    private void Apply()
    {
        if (_loading)
            return;

        UpdatePreviews();

        _applying = true;
        try
        {
            _settingsStore.Update(settings =>
            {
                settings.RefreshIntervalSeconds = _lastValidRefreshInterval;
                settings.NotificationsEnabled = NotificationsEnabled;
                settings.NotificationThresholds = [.. NotificationThresholds];
                settings.WeeklyNotificationsEnabled = WeeklyNotificationsEnabled;
                settings.ResetTimeDisplay = ResetTimeDisplay;
                settings.TrayIconStyle = TrayIconStyle;
                settings.ShowFlyoutOnStartup = ShowFlyoutOnStartup;
                settings.AlwaysOnTop = AlwaysOnTop;
                settings.FlyoutClickThrough = FlyoutClickThrough;
                settings.FlyoutCompact = FlyoutCompact;
                settings.TaskbarBarClickThrough = TaskbarBarClickThrough;
                settings.ShowTaskbarBar = ShowTaskbarBar;
                settings.ShowTaskbarBarOverFullScreen = ShowTaskbarBarOverFullScreen;
                settings.VisibleRows = WindowOptions.Rows;
                settings.CompactShowLabels = WindowOptions.ShowLabels;
                settings.CompactShowBar = WindowOptions.ShowBar;
                settings.CompactShowPercentage = WindowOptions.ShowPercentage;
                settings.CompactShowResetTime = WindowOptions.ShowResetTime;
                settings.FlyoutShowMascot = WindowOptions.ShowMascot;
                settings.FlyoutPartOrder = [.. WindowOptions.Order];
                settings.TaskbarBarVisibleRows = TaskbarOptions.Rows;
                settings.TaskbarBarShowLabels = TaskbarOptions.ShowLabels;
                settings.TaskbarBarShowBar = TaskbarOptions.ShowBar;
                settings.TaskbarBarShowPercentage = TaskbarOptions.ShowPercentage;
                settings.TaskbarBarShowResetTime = TaskbarOptions.ShowResetTime;
                settings.TaskbarBarShowMascot = TaskbarOptions.ShowMascot;
                settings.TaskbarBarPartOrder = [.. TaskbarOptions.Order];
                settings.MascotAnimation = MascotAnimation;
                settings.MascotBunnyEars = MascotBunnyEars;
                settings.MascotCarrot = MascotCarrot;
                settings.MascotBodyGauge = MascotBodyGauge;
                settings.MascotMoodSource = MascotMoodSource;
                settings.MascotWakesOnReset = MascotWakesOnReset;
                settings.MascotStartlesOnJump = MascotStartlesOnJump;
                settings.MascotHoldsSign = MascotHoldsSign;
                settings.MascotFollowsCursor = MascotFollowsCursor;
                settings.MascotIdleActs = MascotIdleActs;
                settings.FlyoutOpacityPercent = FlyoutOpacityPercent;
                settings.AvoidTokenUsage = AvoidTokenUsage;
                settings.Theme = Theme;
                settings.Language = Language;
            });
        }
        finally
        {
            _applying = false;
        }
    }
}
