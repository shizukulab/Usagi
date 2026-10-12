using Usagi.Core.ClaudeCode;
using Usagi.Core.Models;
using Xunit;

namespace Usagi.Tests.ClaudeCode;

public class ClaudeCredentialLookupTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 22, 0, 0, TimeSpan.Zero);
    private static readonly ClaudeCredentialLocation Windows = new("Windows", IsWsl: false);

    private static ClaudeCodeCredentials ExpiringIn(TimeSpan left) => new("token", Now.Add(left).ToUnixTimeMilliseconds());

    [Fact]
    public void CanAskCliForStatus_ReturnsTrue_ForWindowsSignInWithTimeLeft()
    {
        var lookup = new ClaudeCredentialLookup(ExpiringIn(TimeSpan.FromHours(2)), Windows);

        Assert.True(lookup.CanAskCliForStatus(Now));
    }

    [Fact]
    public void CanAskCliForStatus_ReturnsTrue_WhenExpiryUnknown()
    {
        var lookup = new ClaudeCredentialLookup(new ClaudeCodeCredentials("token", null), Windows);

        Assert.True(lookup.CanAskCliForStatus(Now));
    }

    [Fact]
    public void CanAskCliForStatus_ReturnsFalse_WhenTokenExpired()
    {
        // An expired token isn't even passed on as credentials, only where it was found.
        var lookup = new ClaudeCredentialLookup(null, Windows);

        Assert.False(lookup.CanAskCliForStatus(Now));
    }

    [Fact]
    public void CanAskCliForStatus_ReturnsFalse_WhenTokenAboutToExpire()
    {
        var lookup = new ClaudeCredentialLookup(ExpiringIn(TimeSpan.FromMinutes(5)), Windows);

        Assert.False(lookup.CanAskCliForStatus(Now));
    }

    [Fact]
    public void CanAskCliForStatus_ReturnsFalse_WhenNotSignedIn()
    {
        var lookup = new ClaudeCredentialLookup(null, null);

        Assert.False(lookup.CanAskCliForStatus(Now));
    }

    [Theory]
    [InlineData(true, false)]  // the Claude desktop app
    [InlineData(false, true)]  // Claude Code in WSL
    public void CanAskCliForStatus_ReturnsFalse_ForSignInElsewhere(bool isDesktop, bool isWsl)
    {
        var lookup = new ClaudeCredentialLookup(ExpiringIn(TimeSpan.FromHours(2)), new ClaudeCredentialLocation("Elsewhere", isWsl, isDesktop));

        Assert.False(lookup.CanAskCliForStatus(Now));
    }
}
