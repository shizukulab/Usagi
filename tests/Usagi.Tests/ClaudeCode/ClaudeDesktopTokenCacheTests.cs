using Usagi.Core.ClaudeCode;
using Xunit;

namespace Usagi.Tests.ClaudeCode;

public class ClaudeDesktopTokenCacheTests
{
    private const string BaseKey = "acct:a|b:c:https://api.anthropic.com";

    // Before every expiry used below (they're small Unix-ms values), so those entries all count as unexpired.
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(500);

    [Fact]
    public void EncryptedKey_ReadsOsCryptKey()
    {
        const string localState = """{ "os_crypt": { "encrypted_key": "REFQSWFiYw==" } }""";
        Assert.Equal("REFQSWFiYw==", ClaudeDesktopTokenCache.EncryptedKey(localState));
    }

    [Fact]
    public void EncryptedKey_ReturnsNull_WhenMissing()
    {
        Assert.Null(ClaudeDesktopTokenCache.EncryptedKey("""{ "os_crypt": {} }"""));
        Assert.Null(ClaudeDesktopTokenCache.EncryptedKey("{}"));
        Assert.Null(ClaudeDesktopTokenCache.EncryptedKey("not json"));
    }

    [Fact]
    public void EncryptedCacheValues_ReturnsV2BeforeV1()
    {
        const string config = """
        { "oauth:tokenCache": "old==", "oauth:tokenCacheV2": "new==", "locale": "ja" }
        """;

        Assert.Equal(["new==", "old=="], ClaudeDesktopTokenCache.EncryptedCacheValues(config));
    }

    [Fact]
    public void EncryptedCacheValues_SkipsMissingOrEmpty()
    {
        Assert.Equal(["only=="], ClaudeDesktopTokenCache.EncryptedCacheValues("""{ "oauth:tokenCacheV2": "only==", "oauth:tokenCache": "" }"""));
        Assert.Empty(ClaudeDesktopTokenCache.EncryptedCacheValues("{}"));
        Assert.Empty(ClaudeDesktopTokenCache.EncryptedCacheValues("not json"));
    }

    [Fact]
    public void CacheSignature_IgnoresUnrelatedSettings_ButTracksTheTokenCache()
    {
        const string before = """{ "oauth:tokenCacheV2": "abc==", "quickWindowPosition": { "x": 1 } }""";
        const string moved = """{ "oauth:tokenCacheV2": "abc==", "quickWindowPosition": { "x": 2 } }""";
        const string renewed = """{ "oauth:tokenCacheV2": "def==", "quickWindowPosition": { "x": 2 } }""";

        // The desktop app rewrites config.json for window moves etc.; only a token change should count.
        Assert.Equal(ClaudeDesktopTokenCache.CacheSignature(before), ClaudeDesktopTokenCache.CacheSignature(moved));
        Assert.NotEqual(ClaudeDesktopTokenCache.CacheSignature(moved), ClaudeDesktopTokenCache.CacheSignature(renewed));
    }

    [Fact]
    public void SelectToken_PrefersInferenceAndProfileThenLatestExpiry()
    {
        var json = $$"""
        {
          "{{BaseKey}}:user:profile": { "token": "profile-only", "expiresAt": 9000 },
          "{{BaseKey}}:user:inference user:profile": { "token": "both-early", "expiresAt": 1000 },
          "{{BaseKey}}:user:inference user:file_upload user:profile": { "token": "both-late", "expiresAt": 2000 },
          "{{BaseKey}}:user:inference": { "token": "inference-only", "expiresAt": 8000 }
        }
        """;

        var credentials = ClaudeDesktopTokenCache.SelectToken(json, Now);

        // Both scopes beat either alone, even though the single-scope entries expire much later;
        // among the two "both" entries, the later expiry wins.
        Assert.Equal("both-late", credentials!.AccessToken);
        Assert.Equal(2000, credentials.ExpiresAtUnixMs);
    }

    [Fact]
    public void SelectToken_PrefersClaudeCodeSessionTokenOverLaterExpiringOnes()
    {
        var json = $$"""
        {
          "{{BaseKey}}:user:inference user:file_upload user:profile": { "token": "both-latest", "expiresAt": 9000 },
          "{{BaseKey}}:user:inference user:file_upload user:profile user:sessions:claude_code": { "token": "session", "expiresAt": 1000 }
        }
        """;

        // The Claude Code session token is the one meant for reading its usage, so it wins even
        // though a plain inference+profile token expires much later (that later one can be stale).
        Assert.Equal("session", ClaudeDesktopTokenCache.SelectToken(json, Now)!.AccessToken);
    }

    [Fact]
    public void RankTokens_RanksAcrossSeveralCaches()
    {
        var v2 = $$"""{ "{{BaseKey}}:user:inference user:profile": { "token": "v2-early", "expiresAt": 1000 } }""";
        var v1 = $$"""{ "{{BaseKey}}:user:inference user:profile": { "token": "v1-late", "expiresAt": 2000 } }""";

        // Neither cache alone is authoritative; the later-expiring entry wins regardless of which holds it.
        Assert.Equal(["v1-late", "v2-early"], ClaudeDesktopTokenCache.RankTokens([v2, v1], Now).Select(c => c.AccessToken));
    }

    [Fact]
    public void SelectToken_FallsBackToInferenceWhenNoEntryHasProfile()
    {
        var json = $$"""
        {
          "{{BaseKey}}:user:inference": { "token": "inference-early", "expiresAt": 1000 },
          "{{BaseKey}}:user:inference user:sessions": { "token": "inference-late", "expiresAt": 5000 }
        }
        """;

        Assert.Equal("inference-late", ClaudeDesktopTokenCache.SelectToken(json, Now)!.AccessToken);
    }

    [Fact]
    public void SelectToken_SkipsEntriesWithoutAToken()
    {
        var json = $$"""
        {
          "{{BaseKey}}:user:inference user:profile": { "expiresAt": 9000 },
          "{{BaseKey}}:user:inference": { "token": "   ", "expiresAt": 9000 },
          "{{BaseKey}}:user:profile": { "token": "only-real", "expiresAt": 1000 }
        }
        """;

        Assert.Equal("only-real", ClaudeDesktopTokenCache.SelectToken(json, Now)!.AccessToken);
    }

    [Fact]
    public void SelectToken_ToleratesMissingExpiry()
    {
        var json = $$"""{ "{{BaseKey}}:user:inference user:profile": { "token": "no-expiry" } }""";

        var credentials = ClaudeDesktopTokenCache.SelectToken(json, Now);

        Assert.Equal("no-expiry", credentials!.AccessToken);
        Assert.Null(credentials.ExpiresAtUnixMs);
    }

    [Fact]
    public void SelectToken_ReturnsNull_WhenEmptyOrMalformed()
    {
        Assert.Null(ClaudeDesktopTokenCache.SelectToken("{}", Now));
        Assert.Null(ClaudeDesktopTokenCache.SelectToken("not json", Now));
        Assert.Null(ClaudeDesktopTokenCache.SelectToken("[]", Now));
    }

    [Fact]
    public void SelectToken_PrefersAnUnexpiredTokenOverAnExpiredSessionToken()
    {
        var json = $$"""
        {
          "{{BaseKey}}:user:inference user:file_upload user:profile user:sessions:claude_code": { "token": "session", "expiresAt": 1000 },
          "{{BaseKey}}:user:inference user:file_upload user:profile": { "token": "long-lived", "expiresAt": 9000 }
        }
        """;

        // The desktop app only renews the session token while Claude Code runs inside it, so it can
        // sit expired for days; an expired token is never worth trying ahead of a live one.
        var afterSessionExpired = DateTimeOffset.FromUnixTimeMilliseconds(2000);
        Assert.Equal("long-lived", ClaudeDesktopTokenCache.SelectToken(json, afterSessionExpired)!.AccessToken);
    }

    [Fact]
    public void RankTokens_ListsEveryTokenOnce_SoARejectedOneCanFallBack()
    {
        var v2 = $$"""
        {
          "{{BaseKey}}:user:inference user:file_upload user:profile": { "token": "stale", "expiresAt": 8000 },
          "{{BaseKey}}:user:profile": { "token": "shared", "expiresAt": 3000 }
        }
        """;
        var v1 = $$"""
        {
          "{{BaseKey}}:user:inference user:file_upload user:profile": { "token": "good", "expiresAt": 9000 },
          "{{BaseKey}}:user:profile": { "token": "shared", "expiresAt": 3000 }
        }
        """;

        // A live-looking token can still be rejected, so the rest stay available behind it.
        Assert.Equal(["good", "stale", "shared"], ClaudeDesktopTokenCache.RankTokens([v2, v1], Now).Select(c => c.AccessToken));
    }

    [Fact]
    public void CurrentAccountId_ReadsLastKnownAccount()
    {
        Assert.Equal("acct-1", ClaudeDesktopTokenCache.CurrentAccountId("""{ "lastKnownAccountUuid": "acct-1", "locale": "ja" }"""));
        Assert.Null(ClaudeDesktopTokenCache.CurrentAccountId("""{ "lastKnownAccountUuid": "" }"""));
        Assert.Null(ClaudeDesktopTokenCache.CurrentAccountId("{}"));
        Assert.Null(ClaudeDesktopTokenCache.CurrentAccountId("not json"));
    }

    [Fact]
    public void RankTokens_LeavesOutAnotherAccountsEntries()
    {
        const string json = """
        {
          "acct:old|client:org:https://api.anthropic.com:user:inference user:profile": { "token": "old-account", "expiresAt": 9000 },
          "acct:new|client:org:https://api.anthropic.com:user:inference user:profile": { "token": "new-account", "expiresAt": 1000 },
          "unrecognised-key:user:inference": { "token": "unknown-owner", "expiresAt": 2000 }
        }
        """;

        // After switching accounts the old one's token can still be accepted, which would show the
        // wrong account's usage without any error; a key that names no account is kept.
        Assert.Equal(["new-account", "unknown-owner"], ClaudeDesktopTokenCache.RankTokens([json], Now, "new").Select(c => c.AccessToken));
        Assert.Equal(3, ClaudeDesktopTokenCache.RankTokens([json], Now).Count);
    }
}
