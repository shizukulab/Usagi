namespace Usagi.Core.Updates;

public enum UpdateCheckOutcome
{
    UpToDate,
    UpdateAvailable,
    Failed
}

/// <param name="Release">The newer release, when <see cref="Outcome"/> is <see cref="UpdateCheckOutcome.UpdateAvailable"/>.</param>
public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, ReleaseInfo? Release = null, string? Error = null);

/// <summary>
/// Tells whether a newer version than the running one has been released. Only ever run when the
/// user asks (the "Check for updates" button): the app doesn't look on its own or notify.
/// </summary>
public sealed class UpdateChecker(IReleaseSource source, AppVersion currentVersion)
{
    public AppVersion CurrentVersion => currentVersion;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        ReleaseInfo? release;
        try
        {
            release = await source.GetLatestReleaseAsync(ct);
        }
        catch (UpdateCheckException e)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.Failed, Error: e.Message);
        }

        return release is not null && release.Version > currentVersion
            ? new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, release)
            : new UpdateCheckResult(UpdateCheckOutcome.UpToDate);
    }
}
