using System.Globalization;
using System.Text.Json;
using Usagi.Core.Models;

namespace Usagi.Core.StatusLine;

/// <summary>
/// Usage as Claude Code itself reports it to a status line: the JSON it passes on stdin carries
/// <c>rate_limits.five_hour</c> and <c>rate_limits.seven_day</c> (percentage used, reset time in
/// Unix seconds) once a session has had its first response, for Pro and Max subscribers. The
/// helper (UsagiStatusLine.exe) saves that to <see cref="SnapshotPath"/>, where the app picks it
/// up — no token is read and no request is sent.
/// </summary>
public static class StatusLineUsage
{
    /// <summary>The helper Claude Code runs as its status line, shipped next to Usagi.exe.</summary>
    public const string HelperFileName = "UsagiStatusLine.exe";

    /// <summary>Where the helper leaves the latest usage it was given.</summary>
    public static string SnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Usagi", "statusline-usage.json");

    private static readonly JsonSerializerOptions SnapshotOptions = new() { WriteIndented = true };

    /// <summary>
    /// The usage in one status line input, or null if it carries none (not yet, early in a session,
    /// or not on this plan). A window Claude Code left out — it drops one once its reset time has
    /// passed — counts as 0% with no reset time.
    /// </summary>
    public static ClaudeUsage? FromStatusLineInput(string json, DateTimeOffset receivedAt)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("rate_limits", out var limits) ||
                limits.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var session = ReadWindow(limits, "five_hour");
            var weekly = ReadWindow(limits, "seven_day");
            if (session is null && weekly is null)
                return null;

            return new ClaudeUsage
            {
                SessionPercentage = session?.Percentage ?? 0,
                SessionResetTime = session?.ResetsAt,
                WeeklyPercentage = weekly?.Percentage ?? 0,
                WeeklyResetTime = weekly?.ResetsAt,
                LastUpdated = receivedAt
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (double Percentage, DateTimeOffset? ResetsAt)? ReadWindow(JsonElement limits, string name)
    {
        if (!limits.TryGetProperty(name, out var window) ||
            window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("used_percentage", out var used) ||
            !used.TryGetDouble(out var percentage))
        {
            return null;
        }

        DateTimeOffset? resetsAt = window.TryGetProperty("resets_at", out var reset) && reset.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        return (percentage, resetsAt);
    }

    /// <summary>Whether two readings show the same numbers, whenever each was taken.</summary>
    public static bool SameReading(ClaudeUsage a, ClaudeUsage b) => a with { LastUpdated = b.LastUpdated } == b;

    public static string Serialize(ClaudeUsage usage) => JsonSerializer.Serialize(usage, SnapshotOptions);

    /// <returns>The saved usage, or null if the text isn't a saved reading.</returns>
    public static ClaudeUsage? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ClaudeUsage>(json, SnapshotOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>What the helper shows as the status line when it's the only one: "5h 9% · 7d 10%".</summary>
    public static string Summary(ClaudeUsage usage, DateTimeOffset now) => string.Format(
        CultureInfo.InvariantCulture, "5h {0:0}% · 7d {1:0}%", usage.EffectiveSessionPercentage(now), usage.WeeklyPercentage);
}
