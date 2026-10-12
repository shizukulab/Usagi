using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Usagi.App.Localization;
using Usagi.App.Settings;
using Usagi.App.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Usagi.App.ViewModels;

/// <summary>The places the usage is shown, as the Display page has a tab for each.</summary>
public enum DisplaySurface { Window, Taskbar }

/// <summary>
/// One part a place can show (<see cref="DisplayPart"/>), as the Display page lists it and draws
/// it in the preview: whether it's shown, and whether it has to stay so.
/// </summary>
public sealed partial class PartItem : ObservableObject, IHoverable
{
    public PartItem(DisplayPart part) => Part = part;

    public DisplayPart Part { get; }

    public bool IsMascot => Part == DisplayPart.Mascot;
    public bool IsLabels => Part == DisplayPart.Labels;
    public bool IsBar => Part == DisplayPart.Bar;
    public bool IsPercentage => Part == DisplayPart.Percentage;
    public bool IsResetTime => Part == DisplayPart.ResetTime;

    /// <summary>Whether it's one of the parts that show how much is used, of which one is always shown.</summary>
    public bool ShowsUsage => Part is DisplayPart.Mascot or DisplayPart.Bar or DisplayPart.Percentage;

    [ObservableProperty]
    private bool _isShown = true;

    /// <summary>Whether it's the last part left showing the usage, so it can't be hidden.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    private bool _isLocked;

    /// <summary>Whether the cursor is on it, in the preview or the list: both mark it, so either shows which is which.</summary>
    [ObservableProperty]
    private bool _isHovered;

    public string Name => Loc.Get(Part switch
    {
        DisplayPart.Mascot => "Settings_Mascot",
        DisplayPart.Labels => "Settings_ItemLabels",
        DisplayPart.Bar => "Settings_PartBar",
        DisplayPart.Percentage => "Settings_PartPercentage",
        _ => "Settings_ItemResetTime"
    });

    public string Description => Loc.Get(IsLocked ? "Settings_PartLocked" : Part switch
    {
        DisplayPart.Mascot => "Settings_PartMascotDescription",
        DisplayPart.Labels => "Settings_ItemLabelsDescription",
        DisplayPart.Bar => "Settings_PartBarDescription",
        DisplayPart.Percentage => "Settings_PartPercentageDescription",
        _ => "Settings_ItemResetTimeDescription"
    });

    /// <summary>Its texts again, in the UI language as it now is.</summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
    }
}

/// <summary>
/// What one place (the compact flyout, or the taskbar bars) shows, as its tab on the Display page
/// edits it: which rows, and the parts — the creature and each row's name, bar, percentage and
/// reset time — each shown or hidden, in an order of the user's. One of the parts that show the
/// usage (the creature, the bar, the number) always stays shown, so the place never goes blank;
/// with only the creature left, it's there by itself.
/// </summary>
public sealed partial class SurfaceOptions : ObservableObject
{
    // Set while it's being filled in from the settings, which mustn't be saved back.
    private bool _quiet;

    public SurfaceOptions(DisplaySurface surface)
    {
        Surface = surface;
        Parts = [.. AppSettings.DefaultPartOrder.Select(part => new PartItem(part))];
        Parts.CollectionChanged += OnPartsMoved;
        Refresh();
    }

    /// <summary>Raised when a choice here changes, for the settings to be saved.</summary>
    public event Action? Changed;

    public DisplaySurface Surface { get; }

    public bool IsWindow => Surface == DisplaySurface.Window;

    /// <summary>The parts, in the order they're shown, left to right.</summary>
    public ObservableCollection<PartItem> Parts { get; }

    /// <summary>The rows the place shows, for the previews: the real ones (or made-up ones until the first refresh).</summary>
    public ObservableCollection<UsageRowViewModel> PreviewRows { get; } = [];

    [ObservableProperty]
    private UsageRows _rows;

    partial void OnRowsChanged(UsageRows value) => Save();

    /// <summary>Whether the place is shown at all: the taskbar bars can be switched off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOff))]
    private bool _isAvailable = true;

    public bool IsOff => !IsAvailable;

    /// <summary>Why a click did nothing (the last part showing the usage can't be hidden), until the next change.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    public bool HasMessage => Message is not null;

    public bool ShowMascot => IsShown(DisplayPart.Mascot);
    public bool ShowLabels => IsShown(DisplayPart.Labels);
    public bool ShowBar => IsShown(DisplayPart.Bar);
    public bool ShowPercentage => IsShown(DisplayPart.Percentage);
    public bool ShowResetTime => IsShown(DisplayPart.ResetTime);

    public IEnumerable<DisplayPart> Order => Parts.Select(item => item.Part);

    public bool IsShown(DisplayPart part) => Parts.First(item => item.Part == part).IsShown;

    /// <summary>Shows or hides a part — unless it's the last one showing the usage, which says why instead.</summary>
    [RelayCommand]
    private void Toggle(PartItem item)
    {
        if (item.IsLocked)
        {
            Message = Loc.Get("Settings_PartLockedMessage");
            return;
        }
        item.IsShown = !item.IsShown;
        Refresh();
        Save();
    }

    /// <summary>Moves a part to another place in the order (from a drag, in the preview or the list).</summary>
    [RelayCommand]
    private void Move((int From, int To) move) => Parts.Move(move.From, move.To);

    private void OnPartsMoved(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Refresh();
        Save();
    }

    /// <summary>Fills it in from the settings (with the parts as in effect: see <see cref="AppSettings.EffectiveCompactShowBar"/>), without raising <see cref="Changed"/>.</summary>
    public void Load(UsageRows rows, IReadOnlyList<DisplayPart> order, Func<DisplayPart, bool> shown)
    {
        _quiet = true;
        try
        {
            Rows = rows;
            for (var i = 0; i < order.Count; i++)
            {
                var at = Parts.IndexOf(Parts.First(item => item.Part == order[i]));
                if (at != i)
                    Parts.Move(at, i);
            }
            foreach (var item in Parts)
                item.IsShown = shown(item.Part);
            Refresh();
        }
        finally
        {
            _quiet = false;
        }
    }

    public void RefreshTexts()
    {
        foreach (var item in Parts)
            item.RefreshTexts();
        if (Message is not null)
            Message = Loc.Get("Settings_PartLockedMessage");
    }

    // What follows from which parts are shown: which one is locked.
    private void Refresh()
    {
        var showingUsage = Parts.Count(item => item.ShowsUsage && item.IsShown);
        foreach (var item in Parts)
            item.IsLocked = item.ShowsUsage && item.IsShown && showingUsage == 1;
    }

    private void Save()
    {
        if (_quiet)
            return;
        Message = null;
        Changed?.Invoke();
    }
}

// The Display page: a tab for each place the usage is shown — the flyout and the taskbar bars —
// with a preview of it above its parts.
public partial class SettingsViewModel
{
    public SurfaceOptions WindowOptions { get; } = new(DisplaySurface.Window);

    public SurfaceOptions TaskbarOptions { get; } = new(DisplaySurface.Taskbar);

    /// <summary>The Display page's tab.</summary>
    [ObservableProperty]
    private DisplaySurface _selectedSurface;

    /// <summary>Switches the taskbar bars on, from their tab, which has nothing to choose while they're off.</summary>
    [RelayCommand]
    private void TurnOnTaskbarBar() => ShowTaskbarBar = true;

    private void AttachSurfaceOptions()
    {
        WindowOptions.Changed += OnSurfaceOptionsChanged;
        TaskbarOptions.Changed += OnSurfaceOptionsChanged;
    }

    private void OnSurfaceOptionsChanged()
    {
        // Whether there's a creature anywhere decides what the Character page lets be changed.
        OnPropertyChanged(nameof(CanAnimateMascot));
        OnPropertyChanged(nameof(CanMascotReact));
        Apply();
    }

    /// <summary>The usage as the flyout has it, so the preview shows the real numbers and the real creature.</summary>
    public FlyoutViewModel Live { get; private set; } = null!;

    private void AttachPreviews(FlyoutViewModel live)
    {
        Live = live;
        live.AllRows.CollectionChanged += OnLiveRowsChanged;
    }

    private void DetachPreviews() => Live.AllRows.CollectionChanged -= OnLiveRowsChanged;

    // The flyout rebuilds its rows in place (Clear, then one Add each); the preview follows once both are in.
    private void OnLiveRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Live.AllRows.Count is 0 or 2)
            UpdatePreviews();
    }

    /// <summary>Redraws both previews from the settings as this window now has them.</summary>
    private void UpdatePreviews()
    {
        var (session, weekly) = PreviewRows();
        FlyoutViewModel.Fill(WindowOptions.PreviewRows, WindowOptions.Rows, session, weekly);
        FlyoutViewModel.Fill(TaskbarOptions.PreviewRows, TaskbarOptions.Rows, session, weekly);
    }

    // The real rows once there are some; until the first refresh, made-up ones that show the layout.
    private (UsageRowViewModel Session, UsageRowViewModel Weekly) PreviewRows()
    {
        if (Live.AllRows is [var session, var weekly])
            return (session, weekly);

        var now = DateTimeOffset.Now;
        var clock = ResetTimeDisplay == ResetTimeDisplay.Clock;
        return (UsageRowViewModel.For(UsageRowKind.Session, 42, now.AddHours(2.5), now, clock),
                UsageRowViewModel.For(UsageRowKind.Weekly, 18, now.AddDays(3).AddHours(4), now, clock));
    }
}
