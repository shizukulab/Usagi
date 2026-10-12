using Usagi.Core.Usage;

namespace Usagi.App.Settings;

/// <summary>Owns the app's single <see cref="AppState"/> instance (state.json).</summary>
public sealed class AppStateStore : IUsageFetchState
{
    private const string FileName = "state.json";

    public AppState Current { get; } = JsonFile.Load<AppState>(FileName) ?? new AppState();

    public void Save() => JsonFile.Save(FileName, Current);

    DateTimeOffset? IUsageFetchState.LastUsageEndpointCall
    {
        get => Current.LastUsageEndpointCall;
        set => Current.LastUsageEndpointCall = value;
    }

    bool IUsageFetchState.UsageEndpointRateLimited
    {
        get => Current.UsageEndpointRateLimited;
        set => Current.UsageEndpointRateLimited = value;
    }
}
