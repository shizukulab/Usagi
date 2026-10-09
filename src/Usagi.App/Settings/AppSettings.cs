using System.Text.Json.Serialization;
using Usagi.App.Localization;
using Usagi.App.Themes;
using Usagi.Core.Usage;
using Usagi.Platform.TrayIcon;

namespace Usagi.App.Settings;

/// <summary>
/// Non-secret app settings persisted as JSON under %APPDATA%. There are no
/// secrets to store — usage data comes from Claude Code CLI's own credentials
/// file, which this app only ever reads. What the app merely remembers between
/// runs, rather than what the user chose, is in <see cref="AppState"/>.
/// </summary>
public sealed class AppSettings : IJsonOnDeserialized
{
    public const int MinRefreshIntervalSeconds = 10;
    public const int MaxRefreshIntervalSeconds = 3600;

    public const int MaxNotificationThresholds = 5;

    /// <summary>Lower bound of <see cref="FlyoutOpacityPercent"/>: below this the flyout is too faint to read or find.</summary>
    public const int MinFlyoutOpacityPercent = 20;
    public const int MaxFlyoutOpacityPercent = 100;

    /// <summary>Range of the <see cref="TaskbarBarDisplaySettings.Offset"/>.</summary>
    public const int MinTaskbarBarOffset = -3000;
    public const int MaxTaskbarBarOffset = 300;

    /// <summary>Used in the default (token-using) mode; ignored while <see cref="AvoidTokenUsage"/> is on.</summary>
    public int RefreshIntervalSeconds { get; set; } = 60;

    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Session usage percentages a notification is sent at. Use <see cref="EffectiveNotificationThresholds"/>.</summary>
    public List<int> NotificationThresholds { get; set; } = [.. DefaultNotificationThresholds];

    /// <summary>What <see cref="NotificationThresholds"/> starts as, and what resetting it restores.</summary>
    public static IReadOnlyList<int> DefaultNotificationThresholds { get; } = [75, 90, 95];

    /// <summary>
    /// Also notifies for the weekly window: at the same <see cref="NotificationThresholds"/>, and
    /// when it resets. Only while <see cref="NotificationsEnabled"/> is on.
    /// </summary>
    public bool WeeklyNotificationsEnabled { get; set; } = true;

    /// <summary>Whether a reset is shown as the time left until it or as the time of day it happens.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ResetTimeDisplay>))]
    public ResetTimeDisplay ResetTimeDisplay { get; set; } = ResetTimeDisplay.Remaining;

    /// <summary>What the tray icon draws.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<TrayIconStyle>))]
    public TrayIconStyle TrayIconStyle { get; set; } = TrayIconStyle.Ring;

    public bool ShowFlyoutOnStartup { get; set; } = true;

    /// <summary>Keeps the flyout above other windows (Topmost).</summary>
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>
    /// Lets the mouse through the flyout to whatever is behind it; holding Ctrl makes it clickable
    /// again. <see cref="TaskbarBarClickThrough"/> is the same for the taskbar bars.
    /// </summary>
    public bool FlyoutClickThrough { get; set; }

    public bool TaskbarBarClickThrough { get; set; }

    /// <summary>Shows the flyout cut down to one short line per usage row, about the size of the taskbar bars.</summary>
    public bool FlyoutCompact { get; set; }

    /// <summary>Shows the usage bars on the taskbar, left of the notification area.</summary>
    public bool ShowTaskbarBar { get; set; }

    /// <summary>
    /// Keeps the taskbar bars up while a full-screen app (a game, a video) covers the taskbar,
    /// drawn over that app where the taskbar would be. Off: they're hidden along with the taskbar.
    /// </summary>
    public bool ShowTaskbarBarOverFullScreen { get; set; }

    /// <summary>Which usage rows the flyout shows, compact or not. The taskbar bars have their own (<see cref="TaskbarBarVisibleRows"/>).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<UsageRows>))]
    public UsageRows VisibleRows { get; set; } = UsageRows.Both;

    /// <summary>
    /// Shows each row's short name ("5h") left of its bar in the compact flyout. Off makes it
    /// narrower; its tooltip still names the rows.
    /// </summary>
    public bool CompactShowLabels { get; set; } = true;

    /// <summary>
    /// Shows each row's bar in the compact flyout. Off leaves the number to say it; with the
    /// number off too, the bar stays (see <see cref="EffectiveCompactShowBar"/>).
    /// </summary>
    public bool CompactShowBar { get; set; } = true;

    /// <summary>Shows each row's percentage as a number, right of its bar, in the compact flyout. Off leaves the bar to say it.</summary>
    public bool CompactShowPercentage { get; set; } = true;

    /// <summary>Shows when each row resets, right of its percentage, in the compact flyout.</summary>
    public bool CompactShowResetTime { get; set; } = true;

    // The taskbar bars' own choice of what to show, set apart from the flyout's above. A file from
    // before they had one leaves these out: they then follow the flyout's, as they used to, until a
    // change in the settings writes them down.
    private UsageRows? _taskbarBarVisibleRows;
    private bool? _taskbarBarShowLabels;
    private bool? _taskbarBarShowPercentage;
    private bool? _taskbarBarShowResetTime;

    /// <summary>Which usage rows the taskbar bars show.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<UsageRows>))]
    public UsageRows TaskbarBarVisibleRows { get => _taskbarBarVisibleRows ?? VisibleRows; set => _taskbarBarVisibleRows = value; }

    /// <summary>The same as <see cref="CompactShowLabels"/>, for the taskbar bars.</summary>
    public bool TaskbarBarShowLabels { get => _taskbarBarShowLabels ?? CompactShowLabels; set => _taskbarBarShowLabels = value; }

    /// <summary>The same as <see cref="CompactShowBar"/>, for the taskbar bars (see <see cref="EffectiveTaskbarBarShowBar"/>).</summary>
    public bool TaskbarBarShowBar { get; set; } = true;

    /// <summary>The same as <see cref="CompactShowPercentage"/>, for the taskbar bars.</summary>
    public bool TaskbarBarShowPercentage { get => _taskbarBarShowPercentage ?? CompactShowPercentage; set => _taskbarBarShowPercentage = value; }

    /// <summary>The same as <see cref="CompactShowResetTime"/>, for the taskbar bars.</summary>
    public bool TaskbarBarShowResetTime { get => _taskbarBarShowResetTime ?? CompactShowResetTime; set => _taskbarBarShowResetTime = value; }

    /// <summary>Shows a small creature beside the taskbar bars that acts out how much usage is left.</summary>
    public bool TaskbarBarShowMascot { get; set; } = true;

    /// <summary>Shows the same creature in the flyout: in its bottom-left corner, or beside the rows when compact.</summary>
    public bool FlyoutShowMascot { get; set; } = true;

    /// <summary>
    /// The order, left to right, of what the compact flyout shows: the creature and each row's
    /// parts. Each part is switched on and off on its own (<see cref="FlyoutShowMascot"/>,
    /// <see cref="CompactShowLabels"/>, ...); with only the creature on, it's there by itself.
    /// The full flyout keeps its own layout. Read through <see cref="EffectiveFlyoutPartOrder"/>.
    /// </summary>
    public List<DisplayPart> FlyoutPartOrder { get; set; } = [.. DefaultPartOrder];

    /// <summary>The same as <see cref="FlyoutPartOrder"/>, for the taskbar bars.</summary>
    public List<DisplayPart> TaskbarBarPartOrder { get; set; } = [.. DefaultPartOrder];

    /// <summary>The creature on the left, then a row's name, bar, percentage and reset time: the layout from before there was a choice.</summary>
    public static IReadOnlyList<DisplayPart> DefaultPartOrder { get; } =
        [DisplayPart.Mascot, DisplayPart.Labels, DisplayPart.Bar, DisplayPart.Percentage, DisplayPart.ResetTime];

    // From before each part had a switch of its own, when the creature could be left by itself
    // with a switch of that name. Read from such a file and turned into the parts' switches as it's
    // loaded (OnDeserialized); never written again.
    [JsonPropertyName("FlyoutMascotOnly"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LegacyFlyoutMascotOnly { get; set; }

    [JsonPropertyName("TaskbarBarMascotOnly"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LegacyTaskbarBarMascotOnly { get; set; }

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (LegacyFlyoutMascotOnly && FlyoutShowMascot)
            CompactShowLabels = CompactShowBar = CompactShowPercentage = CompactShowResetTime = false;
        if (LegacyTaskbarBarMascotOnly && TaskbarBarShowMascot)
            TaskbarBarShowLabels = TaskbarBarShowBar = TaskbarBarShowPercentage = TaskbarBarShowResetTime = false;
        LegacyFlyoutMascotOnly = LegacyTaskbarBarMascotOnly = false;
    }

    /// <summary>How much the creature moves, wherever it's shown.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<MascotAnimation>))]
    public MascotAnimation MascotAnimation { get; set; } = MascotAnimation.Subtle;

    /// <summary>Puts a headband of rabbit ears on the creature, wherever it's shown.</summary>
    public bool MascotBunnyEars { get; set; }

    /// <summary>
    /// Lays a carrot beside the creature, wherever it's shown, that it eats down as the session
    /// is used up: what's left of the carrot is what's left of the session.
    /// </summary>
    public bool MascotCarrot { get; set; }

    /// <summary>
    /// Makes the creature's own body a gauge of the session, wherever it's shown: as much of it
    /// as is used up fades, from the top down, drawn the way chosen here.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<MascotBodyGauge>))]
    public MascotBodyGauge MascotBodyGauge { get; set; } = MascotBodyGauge.None;

    /// <summary>Which usage window the creature's mood follows, wherever it's shown.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<MascotMoodSource>))]
    public MascotMoodSource MascotMoodSource { get; set; } = MascotMoodSource.Session;

    // Things the creature does on its own account, each only while it's animated at all.

    /// <summary>When a reset ends its sleep, it wakes with a stretch rather than just being awake.</summary>
    public bool MascotWakesOnReset { get; set; }

    /// <summary>It starts when a refresh finds the session's usage well up on the one before.</summary>
    public bool MascotStartlesOnJump { get; set; }

    /// <summary>
    /// When the session's usage passes one of the <see cref="EffectiveNotificationThresholds"/>, it
    /// holds up a sign with that percentage on it for a moment.
    /// </summary>
    public bool MascotHoldsSign { get; set; }

    /// <summary>Its eyes turn to the cursor while that's nearby.</summary>
    public bool MascotFollowsCursor { get; set; }

    /// <summary>Every few minutes of a calm mood, it does something of its own for a while: reads, skips rope, hums a tune.</summary>
    public bool MascotIdleActs { get; set; }

    /// <summary>
    /// Per-monitor placement of the taskbar bars, by the monitor's device name (e.g. "\\.\DISPLAY2").
    /// A monitor without an entry shows them, unshifted.
    /// </summary>
    public Dictionary<string, TaskbarBarDisplaySettings> TaskbarBarDisplays { get; set; } = [];

    /// <summary>How opaque the flyout is, in percent: 100 is solid, lower lets what's behind it show through.</summary>
    public int FlyoutOpacityPercent { get; set; } = MaxFlyoutOpacityPercent;

    /// <summary>
    /// On (default): only the free usage endpoint is used (no usage consumed), every 5 minutes —
    /// a usage tracker shouldn't quietly spend the usage it tracks.
    /// Off: usage is read from a one-token Messages API prompt on every refresh, which consumes a
    /// tiny amount of usage but isn't rate-limited like the usage endpoint, so it can refresh often.
    /// </summary>
    public bool AvoidTokenUsage { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
    public AppTheme Theme { get; set; } = AppTheme.System;

    [JsonConverter(typeof(JsonStringEnumConverter<AppLanguage>))]
    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>Size the flyout was last dragged to, relative to its designed size (1 = 100%). Clamped when applied.</summary>
    public double FlyoutScale { get; set; } = 1;

    /// <summary>The effective refresh interval for the current mode (clamped: the file may have been hand-edited).</summary>
    [JsonIgnore]
    public TimeSpan RefreshInterval => AvoidTokenUsage
        ? TimeSpan.FromSeconds(UsagePolling.TokenFreeRefreshIntervalSeconds)
        : TimeSpan.FromSeconds(Math.Clamp(RefreshIntervalSeconds, MinRefreshIntervalSeconds, MaxRefreshIntervalSeconds));

    /// <summary>
    /// <see cref="NotificationThresholds"/> as usable values: within 1–100, each once, ascending,
    /// at most <see cref="MaxNotificationThresholds"/> (the file may have been hand-edited).
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<int> EffectiveNotificationThresholds =>
        [.. NotificationThresholds.Where(threshold => threshold is >= 1 and <= 100).Distinct().Order().Take(MaxNotificationThresholds)];

    /// <summary><see cref="FlyoutOpacityPercent"/> as a WPF opacity (clamped: the file may have been hand-edited).</summary>
    [JsonIgnore]
    public double FlyoutOpacity => Math.Clamp(FlyoutOpacityPercent, MinFlyoutOpacityPercent, MaxFlyoutOpacityPercent) / 100.0;

    // Something always shows how much is used — the creature, the bar or the number: with none of
    // them on, a place would be just names and times, or nothing at all. The settings window won't
    // switch off the one left on; a hand-edited file that has none on gets the bar.

    /// <summary>Whether the compact flyout's rows have their bar: <see cref="CompactShowBar"/>, or anyway with nothing else to show the usage.</summary>
    [JsonIgnore]
    public bool EffectiveCompactShowBar => CompactShowBar || (!CompactShowPercentage && !FlyoutShowMascot);

    /// <summary>Whether the taskbar bars' rows have their bar: <see cref="TaskbarBarShowBar"/>, or anyway with nothing else to show the usage.</summary>
    [JsonIgnore]
    public bool EffectiveTaskbarBarShowBar => TaskbarBarShowBar || (!TaskbarBarShowPercentage && !TaskbarBarShowMascot);

    /// <summary><see cref="FlyoutPartOrder"/>, each part once (see <see cref="NormalizePartOrder"/>).</summary>
    [JsonIgnore]
    public IReadOnlyList<DisplayPart> EffectiveFlyoutPartOrder => NormalizePartOrder(FlyoutPartOrder);

    /// <summary><see cref="TaskbarBarPartOrder"/>, each part once (see <see cref="NormalizePartOrder"/>).</summary>
    [JsonIgnore]
    public IReadOnlyList<DisplayPart> EffectiveTaskbarBarPartOrder => NormalizePartOrder(TaskbarBarPartOrder);

    /// <summary>
    /// An order as it can be used: every part once, in the order given, with any that are missing
    /// (from a hand-edited file, or one written before a part existed) added where they are by default.
    /// </summary>
    public static IReadOnlyList<DisplayPart> NormalizePartOrder(IEnumerable<DisplayPart>? order)
    {
        var parts = (order ?? []).Where(Enum.IsDefined).Distinct().ToList();
        foreach (var part in DefaultPartOrder)
        {
            if (parts.Contains(part))
                continue;
            var at = DefaultPartOrder.TakeWhile(p => p != part).Count(parts.Contains);
            parts.Insert(at, part);
        }
        return parts;
    }
}

/// <summary>What a place can show, each switched on and off on its own and put in an order of the user's.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DisplayPart>))]
public enum DisplayPart
{
    /// <summary>The creature: beside the rows, or between their parts.</summary>
    Mascot,

    /// <summary>Each row's short name ("5h").</summary>
    Labels,

    /// <summary>Each row's bar.</summary>
    Bar,

    /// <summary>Each row's percentage as a number.</summary>
    Percentage,

    /// <summary>When each row resets.</summary>
    ResetTime
}

public enum ResetTimeDisplay { Remaining, Clock }

/// <summary>Which of the two usage windows are shown.</summary>
public enum UsageRows { Both, SessionOnly, WeeklyOnly }

/// <summary>How much the creature moves.</summary>
public enum MascotAnimation
{
    /// <summary>Not at all: its face still follows the usage, but it holds a pose.</summary>
    Off,

    /// <summary>Now and then: when its mood changes, and briefly once a minute.</summary>
    Subtle,

    /// <summary>All the time.</summary>
    Lively
}

/// <summary>How the part of the creature's body that stands for used-up usage is drawn.</summary>
public enum MascotBodyGauge
{
    /// <summary>Its body isn't a gauge: all of it is drawn as ever.</summary>
    None,

    /// <summary>In its own color, mostly transparent.</summary>
    Pale,

    /// <summary>As a gray shadow, like the eaten end of the carrot.</summary>
    Shadow,

    /// <summary>Every other block of it, like a checkerboard.</summary>
    Dots
}

/// <summary>Which usage window sets the creature's mood.</summary>
public enum MascotMoodSource
{
    /// <summary>The session alone.</summary>
    Session,

    /// <summary>The week alone.</summary>
    Weekly,

    /// <summary>Whichever of the two is fuller.</summary>
    Higher
}

/// <summary>Whether one monitor's taskbar gets the usage bars, and where on it.</summary>
public sealed class TaskbarBarDisplaySettings
{
    public bool Show { get; set; } = true;

    /// <summary>
    /// How far the bars are shifted from where they're placed automatically, in device-independent
    /// pixels (negative is left). Clamped when applied.
    /// </summary>
    public int Offset { get; set; }
}
