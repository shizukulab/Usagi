using Usagi.App.Localization;
using Usagi.App.Services;
using Usagi.Core.Updates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Usagi.App.ViewModels;

/// <summary>Where the About page's update card is at; its icon and colors follow this.</summary>
public enum UpdateStatus
{
    /// <summary>Never checked.</summary>
    NotChecked,
    Checking,
    UpToDate,
    UpdateAvailable,
    Failed
}

// The About page: the app's version, the update card (Check for updates / Download) and links.
public partial class SettingsViewModel
{
    // Long enough for the spinner to read as "checked" rather than flicker when GitHub answers at once.
    private static readonly TimeSpan MinimumCheckDuration = TimeSpan.FromMilliseconds(700);

    private UpdateService _updateService = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateTitle), nameof(UpdateDetail), nameof(IsUpdateAvailable), nameof(IsCheckingForUpdates))]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    private UpdateStatus _updateStatus;

    public string AppVersionText => Loc.Format("About_Version", _updateService.CurrentVersion);

    public bool IsUpdateAvailable => UpdateStatus == UpdateStatus.UpdateAvailable;

    public bool IsCheckingForUpdates => UpdateStatus == UpdateStatus.Checking;

    public string UpdateTitle => UpdateStatus switch
    {
        UpdateStatus.Checking => Loc.Get("About_Checking"),
        UpdateStatus.UpToDate => Loc.Get("About_UpToDate"),
        UpdateStatus.UpdateAvailable => Loc.Format("About_UpdateAvailable", _updateService.AvailableUpdate?.Version),
        UpdateStatus.Failed => Loc.Get("About_CheckFailed"),
        _ => Loc.Get("About_NotChecked")
    };

    public string UpdateDetail => UpdateStatus switch
    {
        UpdateStatus.Checking => Loc.Get("About_CheckingDetail"),
        UpdateStatus.UpToDate => LastCheckedText,
        UpdateStatus.UpdateAvailable => Loc.Format("About_UpdateAvailableDetail", _updateService.CurrentVersion),
        UpdateStatus.Failed => Loc.Get("About_CheckFailedDetail"),
        _ => Loc.Get("About_NotCheckedDetail")
    };

    // "Last checked: today, 10:32" — the date only when it wasn't today.
    private string LastCheckedText
    {
        get
        {
            if (_updateService.LastChecked is not { } checkedAt)
                return string.Empty;

            var local = checkedAt.ToLocalTime();
            return local.Date == DateTime.Today
                ? Loc.Format("About_LastCheckedToday", local.ToString("t", Loc.Culture))
                : Loc.Format("About_LastChecked", local.ToString("g", Loc.Culture));
        }
    }

    private void AttachUpdateService(UpdateService updateService)
    {
        _updateService = updateService;
        UpdateStatus = updateService.AvailableUpdate is not null ? UpdateStatus.UpdateAvailable
            : updateService.LastChecked is not null ? UpdateStatus.UpToDate
            : UpdateStatus.NotChecked;
    }

    private void RefreshUpdateLanguage()
    {
        OnPropertyChanged(nameof(AppVersionText));
        OnPropertyChanged(nameof(UpdateTitle));
        OnPropertyChanged(nameof(UpdateDetail));
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        UpdateStatus = UpdateStatus.Checking;

        var check = _updateService.CheckAsync();
        await Task.WhenAll(check, Task.Delay(MinimumCheckDuration));

        UpdateStatus = check.Result.Outcome switch
        {
            UpdateCheckOutcome.UpdateAvailable => UpdateStatus.UpdateAvailable,
            UpdateCheckOutcome.UpToDate => UpdateStatus.UpToDate,
            _ => UpdateStatus.Failed
        };
    }

    private bool CanCheckForUpdates() => UpdateStatus != UpdateStatus.Checking;

    [RelayCommand]
    private void DownloadUpdate() =>
        UpdateService.OpenInBrowser(_updateService.AvailableUpdate?.PageUrl ?? UpdateService.ReleasesPage);

    [RelayCommand]
    private static void OpenReleaseNotes() => UpdateService.OpenInBrowser(UpdateService.ReleasesPage);

    [RelayCommand]
    private static void OpenRepository() => UpdateService.OpenInBrowser(UpdateService.RepositoryPage);
}
