using Usagi.Core.ClaudeCode;

namespace Usagi.Platform.ClaudeCode;

/// <summary>
/// Finds Claude credentials wherever they currently live — the Claude desktop app first, then
/// Windows-native Claude Code, then any installed WSL distro. Read-only: an expired token is
/// reported, not refreshed (refreshing via the CLI would run a prompt and consume usage). The
/// desktop app and Claude Code each refresh their own token the next time they're used.
/// </summary>
internal static class ClaudeCredentialResolver
{
    /// <summary>
    /// Every usable (unexpired) credential, in source order, for a caller to try until one is accepted.
    /// Lazy: a source that starts a process (WSL) runs only once the caller enumerates past the earlier
    /// ones, so stopping at the first that works never touches it. If none are usable, yields a single
    /// lookup with null credentials, its location naming where an expired one was found (or null if none).
    /// </summary>
    public static IEnumerable<ClaudeCredentialLookup> ResolveCandidates()
    {
        ClaudeCredentialSource? firstExpired = null;
        var anyUsable = false;
        foreach (var source in ClaudeCredentialSource.EnumerateAll())
        {
            if (source.TryRead() is not { } credentials)
                continue;
            if (credentials.IsExpired(DateTimeOffset.Now))
            {
                firstExpired ??= source;
                continue;
            }

            anyUsable = true;
            yield return new ClaudeCredentialLookup(credentials, source.Location);
        }

        if (!anyUsable)
            yield return new ClaudeCredentialLookup(null, firstExpired?.Location);
    }
}
