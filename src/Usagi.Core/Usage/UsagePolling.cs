namespace Usagi.Core.Usage;

/// <summary>How often the free usage endpoint (token-free mode) may be asked.</summary>
public static class UsagePolling
{
    /// <summary>
    /// Fixed interval in token-free mode. How often the free usage endpoint may be asked isn't
    /// published — it can answer with a rate limit (HTTP 429), and when it would, or for how
    /// long, isn't known. Every 2 minutes has been seen to work; every minute has been seen not
    /// to. It's every 3: a margin over the 2 that worked, which were only watched for 14 minutes.
    /// What a log of the fetches showed on 2026-10-10:
    /// <list type="bullet">
    ///   <item>14 minutes of a call every 2 minutes: all went through.</item>
    ///   <item>Then five calls within 3 minutes (30 to 65 seconds apart) went through, and the
    ///   next, 2 minutes later, was answered 429. So was a retry 4½ minutes after that; one 9
    ///   minutes after it went through.</item>
    ///   <item>Then a call every minute, with one by hand in between: six went through in 4½
    ///   minutes, and the seventh, a minute later, was answered 429.</item>
    /// </list>
    /// Both times the 429 came to about the sixth call in 5 minutes — which is as far as two
    /// sightings go: what the rule is, they don't say.
    /// </summary>
    public const int TokenFreeRefreshIntervalSeconds = 180;

    /// <summary>
    /// After a 429 from the usage endpoint, the next attempt waits this long instead (refreshes
    /// asked for before then are held back), and keeps doing so until a call succeeds. The one
    /// limit timed (see <see cref="TokenFreeRefreshIntervalSeconds"/>) lasted more than 4½ minutes
    /// and less than 9.
    /// </summary>
    public const int RateLimitedRetrySeconds = 300;

    /// <summary>
    /// Shortest gap between two calls to the free usage endpoint. Refresh buttons, the tray menu,
    /// sign-in changes and Test connection all bypass the 3-minute timer; without this, a few
    /// clicks would ask it many times over. Shorter than the timer's interval, or a refresh by
    /// hand would hardly ever get through; the timer then starts over from that refresh, so the
    /// endpoint is still asked about once an interval. Not shorter than a minute, though: at 30
    /// seconds, a run of clicks was what was followed by a 429 (see
    /// <see cref="TokenFreeRefreshIntervalSeconds"/>) — and a click every minute would be too, if
    /// it were kept up.
    /// </summary>
    public static readonly TimeSpan MinUsageEndpointSpacing = TimeSpan.FromMinutes(1);
}
