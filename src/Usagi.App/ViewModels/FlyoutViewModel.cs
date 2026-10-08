using System.Collections.ObjectModel;
using Usagi.App.Localization;
using Usagi.App.Settings;
using Usagi.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Usagi.App.ViewModels;

/// <summary>How the creature (shown on the taskbar bars and in the flyout) is doing, from how much usage is left.</summary>
public enum MascotMood
{
    /// <summary>The session has just reset.</summary>
    Happy,
    Calm,
    Worried,
    Panic,

    /// <summary>A window is used up.</summary>
    Tired
}

public partial class FlyoutViewModel : ObservableObject
{
    /// <summary>Warning shown above the usage rows (credentials/API problems), or null when all is well.</summary>
    [ObservableProperty]
    private string? _bannerText;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _lastUpdatedText = Loc.Get("Flyout_NeverRefreshed");

    /// <summary>Exact timestamp, shown as the tooltip of the relative LastUpdatedText.</summary>
    [ObservableProperty]
    private string? _lastUpdatedToolTip;

    private DateTimeOffset? _lastUpdatedAt;
    private ClaudeUsage? _lastUsage;

    // A short note shown in place of the last-updated text until this time (see ShowFooterNote).
    private DateTimeOffset _footerNoteUntil;
    private static readonly TimeSpan FooterNoteDuration = TimeSpan.FromSeconds(4);

    /// <summary>The usage rows to show: those the user picked (<see cref="VisibleRows"/>).</summary>
    public ObservableCollection<UsageRowViewModel> Rows { get; } = [];

    /// <summary>Both usage rows whatever is shown, for the compact views' tooltips.</summary>
    public ObservableCollection<UsageRowViewModel> AllRows { get; } = [];

    /// <summary>Which rows <see cref="Rows"/> holds (a setting).</summary>
    [ObservableProperty]
    private UsageRows _visibleRows = UsageRows.Both;

    partial void OnVisibleRowsChanged(UsageRows value) => RefreshTimes();

    /// <summary>How the compact flyout lays out its rows' parts and the creature (settings: which are shown, and in what order).</summary>
    [ObservableProperty]
    private PartLayout _compactLayout = PartLayout.Default;

    // The taskbar bars' own choice of the same, set apart from the flyout's.

    /// <summary>The usage rows the taskbar bars show: those picked for them (<see cref="TaskbarVisibleRows"/>).</summary>
    public ObservableCollection<UsageRowViewModel> TaskbarRows { get; } = [];

    [ObservableProperty]
    private UsageRows _taskbarVisibleRows = UsageRows.Both;

    partial void OnTaskbarVisibleRowsChanged(UsageRows value) => RefreshTimes();

    [ObservableProperty]
    private PartLayout _taskbarLayout = PartLayout.Default;

    /// <summary>Whether the flyout shows the creature (a setting): in the full flyout's corner, and wherever the compact one's layout puts it.</summary>
    [ObservableProperty]
    private bool _showMascot;

    /// <summary>How much the creature moves (a setting).</summary>
    [ObservableProperty]
    private MascotAnimation _mascotAnimation = MascotAnimation.Subtle;

    /// <summary>Whether the creature wears rabbit ears (a setting).</summary>
    [ObservableProperty]
    private bool _mascotBunnyEars;

    /// <summary>Whether the creature has a carrot beside it (a setting).</summary>
    [ObservableProperty]
    private bool _mascotCarrot;

    /// <summary>How the creature's body shows the session's usage, if it does (a setting).</summary>
    [ObservableProperty]
    private MascotBodyGauge _mascotBodyGauge;

    /// <summary>The session usage the carrot is eaten down by and the body fades with, in percent.</summary>
    [ObservableProperty]
    private double _mascotSessionUsage;

    /// <summary>Whether the creature wakes with a stretch at a reset, starts at a jump in the usage, and follows the cursor (settings).</summary>
    [ObservableProperty]
    private bool _mascotWakesOnReset;

    [ObservableProperty]
    private bool _mascotStartlesOnJump;

    /// <summary>Whether the creature holds up a sign as the session passes one of <see cref="MascotSignThresholds"/> (a setting).</summary>
    [ObservableProperty]
    private bool _mascotHoldsSign;

    /// <summary>The session usages, in percent, it holds up a sign at: the notification thresholds.</summary>
    [ObservableProperty]
    private IReadOnlyList<int> _mascotSignThresholds = [];

    [ObservableProperty]
    private bool _mascotFollowsCursor;

    /// <summary>Whether the creature does something of its own every few minutes of a calm mood (a setting).</summary>
    [ObservableProperty]
    private bool _mascotIdleActs;

    /// <summary>How far the session's usage has to be up on the last reading, in points, to count as a jump.</summary>
    public const double UsageJumpPoints = 10;

    /// <summary>A count of the readings that came in that far up on the one before: each new one is a jump the creature can start at.</summary>
    [ObservableProperty]
    private int _mascotUsageJumps;

    /// <summary>The mood the usage puts the creature in, in the flyout and on the taskbar.</summary>
    [ObservableProperty]
    private MascotMood _mascotMood = MascotMood.Calm;

    /// <summary>Which usage window sets the creature's mood (a setting).</summary>
    [ObservableProperty]
    private MascotMoodSource _mascotMoodSource;

    partial void OnMascotMoodSourceChanged(MascotMoodSource value) => RefreshTimes();

    // The window the setting picks (by default, whichever is fuller) sets the mood, at the bars' own
    // color steps (70% and 90%); it's only happy while the session (or the week, if that alone is
    // followed) is as good as untouched.
    private static MascotMood MoodFor(MascotMoodSource source, double sessionPercentage, double weeklyPercentage)
    {
        var percentage = source switch
        {
            MascotMoodSource.Session => sessionPercentage,
            MascotMoodSource.Weekly => weeklyPercentage,
            _ => Math.Max(sessionPercentage, weeklyPercentage)
        };
        var fresh = (source == MascotMoodSource.Weekly ? weeklyPercentage : sessionPercentage) < 10;
        return percentage switch
        {
            >= 100 => MascotMood.Tired,
            >= 90 => MascotMood.Panic,
            >= 70 => MascotMood.Worried,
            _ => fresh ? MascotMood.Happy : MascotMood.Calm
        };
    }

    /// <summary>Shows each reset as the time of day it happens instead of the time left until it (a setting).</summary>
    [ObservableProperty]
    private bool _showResetClockTime;

    partial void OnShowResetClockTimeChanged(bool value) => RefreshTimes();

    public event Action? RefreshRequested;
    public event Action? SettingsRequested;

    [RelayCommand]
    private void Refresh() => RefreshRequested?.Invoke();

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    // No Sign in button here: the CLI's sign-in opens a console window, so a sign-in problem points to
    // the desktop app instead (the CLI's sign-in stays on the settings' Account page).
    public void SetBanner(string message) => BannerText = message;

    public void ClearBanner() => BannerText = null;

    public void ApplyUsage(ClaudeUsage usage, DateTimeOffset now)
    {
        if (_lastUsage is { } before && usage.EffectiveSessionPercentage(now) - before.EffectiveSessionPercentage(now) >= UsageJumpPoints)
            MascotUsageJumps++;
        _lastUsage = usage;
        _lastUpdatedAt = usage.LastUpdated;
        RenderUsage(now);
    }

    /// <summary>Rebuilds the code-built texts in the current language (after a language switch).</summary>
    public void RefreshLanguage()
    {
        if (_lastUsage is null)
        {
            LastUpdatedText = Loc.Get("Flyout_NeverRefreshed");
            return;
        }

        RenderUsage(DateTimeOffset.Now);
    }

    /// <summary>
    /// Rebuilds the rows as of now. The numbers only change with a refresh, which can be minutes
    /// apart, but the time left until a reset runs down in between.
    /// </summary>
    public void RefreshTimes()
    {
        if (_lastUsage is not null)
            RenderUsage(DateTimeOffset.Now);
    }

    private void RenderUsage(DateTimeOffset now)
    {
        var usage = _lastUsage!;
        var session = UsageRowViewModel.For(UsageRowKind.Session, usage.EffectiveSessionPercentage(now), usage.SessionResetTime, now, ShowResetClockTime);
        var weekly = UsageRowViewModel.For(UsageRowKind.Weekly, usage.WeeklyPercentage, usage.WeeklyResetTime, now, ShowResetClockTime);

        AllRows.Clear();
        AllRows.Add(session);
        AllRows.Add(weekly);

        Fill(Rows, VisibleRows, session, weekly);
        Fill(TaskbarRows, TaskbarVisibleRows, session, weekly);
        MascotMood = MoodFor(MascotMoodSource, usage.EffectiveSessionPercentage(now), usage.WeeklyPercentage);
        MascotSessionUsage = usage.EffectiveSessionPercentage(now);
        RefreshLastUpdatedText(now);
    }

    /// <summary>Puts the rows <paramref name="visible"/> picks into <paramref name="rows"/>.</summary>
    public static void Fill(ObservableCollection<UsageRowViewModel> rows, UsageRows visible, UsageRowViewModel session, UsageRowViewModel weekly)
    {
        rows.Clear();
        if (visible != UsageRows.WeeklyOnly)
            rows.Add(session);
        if (visible != UsageRows.SessionOnly)
            rows.Add(weekly);
    }

    /// <summary>
    /// Briefly shows <paramref name="note"/> where the last-updated text is (e.g. "Can refresh in
    /// 2 min"), then puts that text back. For passing information that doesn't warrant a banner.
    /// </summary>
    public void ShowFooterNote(string note)
    {
        LastUpdatedText = note;
        _footerNoteUntil = DateTimeOffset.Now + FooterNoteDuration;

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = FooterNoteDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_lastUsage is null)
                LastUpdatedText = Loc.Get("Flyout_NeverRefreshed");
            else
                RefreshLastUpdatedText(DateTimeOffset.Now);
        };
        timer.Start();
    }

    /// <summary>Recomputes the relative "Updated N min ago" text (minute granularity, so it
    /// rarely changes width) and its exact-time tooltip. Called periodically while the
    /// flyout is visible so the relative time stays current.</summary>
    public void RefreshLastUpdatedText(DateTimeOffset now)
    {
        if (_lastUpdatedAt is not { } updatedAt || now < _footerNoteUntil)
            return;

        var elapsed = now - updatedAt;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        LastUpdatedText = elapsed switch
        {
            { TotalMinutes: < 1 } => Loc.Get("Flyout_UpdatedJustNow"),
            { TotalHours: < 1 } => Loc.Format("Flyout_UpdatedMinutesAgo", (int)elapsed.TotalMinutes),
            { TotalDays: < 1 } => Loc.Format("Flyout_UpdatedHoursAgo", (int)elapsed.TotalHours),
            _ => Loc.Format("Flyout_UpdatedDaysAgo", (int)elapsed.TotalDays)
        };

        var localUpdatedAt = updatedAt.ToLocalTime();
        LastUpdatedToolTip = localUpdatedAt.Date == now.ToLocalTime().Date
            ? Loc.Format("Flyout_LastUpdatedAt", localUpdatedAt.ToString("HH:mm:ss"))
            : Loc.Format("Flyout_LastUpdatedAt", localUpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
    }
}
