using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using Usagi.App.Settings;
using Usagi.Core.Updates;

namespace Usagi.App.Services;

/// <summary>
/// "Check for updates" on the About page: asks GitHub for the latest release when the button
/// is pressed, and remembers the answer (state.json) so the page shows it again after a
/// restart. Nothing runs in the background and nothing is downloaded or installed: a newer
/// release is offered as a link to its download page.
/// </summary>
public sealed class UpdateService
{
    public const string RepositoryOwner = "shizukulab";
    public const string RepositoryName = "Usagi";

    public static Uri RepositoryPage { get; } = new($"https://github.com/{RepositoryOwner}/{RepositoryName}");

    public static Uri ReleasesPage { get; } = new($"https://github.com/{RepositoryOwner}/{RepositoryName}/releases");

    private readonly AppStateStore _stateStore;
    private readonly UpdateChecker _checker;

    public UpdateService(HttpClient httpClient, AppStateStore stateStore)
    {
        _stateStore = stateStore;
        CurrentVersion = ReadCurrentVersion();
        var client = new GitHubReleaseClient(httpClient, RepositoryOwner, RepositoryName, $"Usagi/{CurrentVersion}");
        _checker = new UpdateChecker(client, CurrentVersion);
    }

    /// <summary>The running version, from the assembly's informational version (the release tag's, for a release build).</summary>
    public AppVersion CurrentVersion { get; }

    /// <summary>When the last check got an answer, or null if there never was one.</summary>
    public DateTimeOffset? LastChecked => _stateStore.Current.LastUpdateCheck;

    /// <summary>
    /// The newer release the last check found. Still newer than the running version: once it's
    /// installed (or anything newer is), it's no longer offered.
    /// </summary>
    public ReleaseInfo? AvailableUpdate
    {
        get
        {
            var state = _stateStore.Current;
            return AppVersion.TryParse(state.AvailableUpdateVersion, out var version)
                   && version > CurrentVersion
                   && Uri.TryCreate(state.AvailableUpdateUrl, UriKind.Absolute, out var url)
                ? new ReleaseInfo(version, $"v{version}", url)
                : null;
        }
    }

    public async Task<UpdateCheckResult> CheckAsync()
    {
        var result = await _checker.CheckAsync();

        // A failure leaves the last answer as it was.
        if (result.Outcome != UpdateCheckOutcome.Failed)
        {
            var state = _stateStore.Current;
            state.LastUpdateCheck = DateTimeOffset.Now;
            state.AvailableUpdateVersion = result.Release?.Version.ToString();
            state.AvailableUpdateUrl = result.Release?.PageUrl.AbsoluteUri;
            _stateStore.Save();
        }

        return result;
    }

    public static void OpenInBrowser(Uri url) => Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });

    // Release builds carry the tag's version (see .github/workflows/release.yml); a local build
    // carries Directory.Build.props's. .NET appends "+<commit>" to it, which TryParse drops.
    private static AppVersion ReadCurrentVersion()
    {
        var informational = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return AppVersion.TryParse(informational, out var version) ? version : new AppVersion(0, 0, 0);
    }
}
