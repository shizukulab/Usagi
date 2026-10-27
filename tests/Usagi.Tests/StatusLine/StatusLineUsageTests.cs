using Usagi.Core.Models;
using Usagi.Core.StatusLine;
using Xunit;

namespace Usagi.Tests.StatusLine;

public class StatusLineUsageTests
{
    private static readonly DateTimeOffset Received = new(2026, 10, 28, 8, 25, 0, TimeSpan.FromHours(9));

    [Fact]
    public void FromStatusLineInput_ReadsBothWindows()
    {
        // As Claude Code 2.1.296 sent it (other fields trimmed).
        const string json = """
        {
          "session_id": "fb4a3221", "model": { "display_name": "Opus 5.5" },
          "rate_limits": {
            "five_hour": { "used_percentage": 9, "resets_at": 1793158200 },
            "seven_day": { "used_percentage": 10.5, "resets_at": 1793401200 }
          }
        }
        """;

        var usage = StatusLineUsage.FromStatusLineInput(json, Received);

        Assert.NotNull(usage);
        Assert.Equal(9, usage!.SessionPercentage);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793158200), usage.SessionResetTime);
        Assert.Equal(10.5, usage.WeeklyPercentage);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793401200), usage.WeeklyResetTime);
        Assert.Equal(Received, usage.LastUpdated);
    }

    [Fact]
    public void FromStatusLineInput_CountsADroppedWindowAsZero()
    {
        // Claude Code drops a window once its reset time has passed.
        const string json = """{ "rate_limits": { "seven_day": { "used_percentage": 40, "resets_at": 1793401200 } } }""";

        var usage = StatusLineUsage.FromStatusLineInput(json, Received);

        Assert.Equal(0, usage!.SessionPercentage);
        Assert.Null(usage.SessionResetTime);
        Assert.Equal(40, usage.WeeklyPercentage);
    }

    [Theory]
    [InlineData("""{ "session_id": "x" }""")]           // early in a session, or not on Pro/Max
    [InlineData("""{ "rate_limits": {} }""")]
    [InlineData("""{ "rate_limits": { "five_hour": { "resets_at": 1 } } }""")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void FromStatusLineInput_ReturnsNull_WithoutUsage(string json)
    {
        Assert.Null(StatusLineUsage.FromStatusLineInput(json, Received));
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var usage = new ClaudeUsage
        {
            SessionPercentage = 9, SessionResetTime = Received.AddHours(3),
            WeeklyPercentage = 10, WeeklyResetTime = Received.AddDays(2),
            LastUpdated = Received
        };

        Assert.Equal(usage, StatusLineUsage.Deserialize(StatusLineUsage.Serialize(usage)));
        Assert.Null(StatusLineUsage.Deserialize("not json"));
    }

    [Fact]
    public void SameReading_IgnoresWhenItWasTaken()
    {
        var usage = new ClaudeUsage { SessionPercentage = 9, WeeklyPercentage = 10, LastUpdated = Received };

        Assert.True(StatusLineUsage.SameReading(usage, usage with { LastUpdated = Received.AddMinutes(1) }));
        Assert.False(StatusLineUsage.SameReading(usage, usage with { SessionPercentage = 10 }));
    }

    [Fact]
    public void Summary_ShowsBothWindows_AndAPassedSessionResetAsZero()
    {
        var usage = new ClaudeUsage
        {
            SessionPercentage = 9.4, SessionResetTime = Received.AddHours(1),
            WeeklyPercentage = 10, LastUpdated = Received
        };

        Assert.Equal("5h 9% · 7d 10%", StatusLineUsage.Summary(usage, Received));
        Assert.Equal("5h 0% · 7d 10%", StatusLineUsage.Summary(usage, Received.AddHours(2)));
    }
}
