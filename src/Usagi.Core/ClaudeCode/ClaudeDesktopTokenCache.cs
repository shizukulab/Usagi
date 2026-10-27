using System.Text.Json;
using Usagi.Core.Models;

namespace Usagi.Core.ClaudeCode;

/// <summary>
/// Parses the OAuth token the Claude desktop app caches, so usage can be read without the CLI
/// installed. The desktop app keeps a long-lived token and refreshes it itself, so a token found
/// this way rarely expires — unlike the CLI's ~8-hour one, which is why it's tried first.
///
/// This holds only the parts that don't need Windows crypto: the desktop app stores the cache the
/// way Chromium stores cookies (a DPAPI-wrapped AES key in "Local State", the cache itself
/// AES-GCM-encrypted in config.json), so platform code decrypts and hands the plaintext to
/// <see cref="RankTokens"/>. Everything here only parses JSON, so it stays testable off Windows.
/// Read-only throughout: nothing is written back.
/// </summary>
public static class ClaudeDesktopTokenCache
{
    public const string ConfigFileName = "config.json";
    public const string LocalStateFileName = "Local State";

    /// <summary>Where a regular (non-Store) install of the desktop app keeps config.json and "Local State".</summary>
    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");

    // config.json keeps the token cache under these keys. V2 is the current layout; the older key
    // is still read so a desktop app that hasn't migrated to it is still picked up. Newest first.
    public static readonly string[] CacheKeys = ["oauth:tokenCacheV2", "oauth:tokenCache"];

    // The cache can hold several entries, including stale ones the API now rejects, and expiry is a
    // poor signal of which is live (a dead token can outlast a good one), so every entry is offered
    // in rank order for the caller to try. An expired entry always ranks last: the desktop app only
    // renews the Claude Code session token while Claude Code runs inside it, so that one can sit
    // expired for days next to long-lived tokens that still work. Among unexpired entries, the
    // session token (user:sessions:claude_code, the one meant for reading Claude Code usage) comes
    // first, then scope coverage (user:inference, then user:profile), then the latest expiry.
    private const string SessionScope = "user:sessions:claude_code";
    private const string InferenceScope = "user:inference";
    private const string ProfileScope = "user:profile";

    // Each entry's key starts "acct:<account uuid>|", naming whose token it is.
    private const string AccountKeyPrefix = "acct:";

    /// <summary>The base64 blob under <c>os_crypt.encrypted_key</c> in the desktop app's "Local State", or null.</summary>
    public static string? EncryptedKey(string localStateJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(localStateJson);
            return doc.RootElement.TryGetProperty("os_crypt", out var osCrypt) &&
                   osCrypt.TryGetProperty("encrypted_key", out var key) &&
                   key.ValueKind == JsonValueKind.String
                ? key.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The account the desktop app is signed in to (<c>lastKnownAccountUuid</c> in config.json), or
    /// null. Its token cache can still hold entries for an account signed in before, whose tokens
    /// keep working — so without this filter the app could quietly show that other account's usage.
    /// </summary>
    public static string? CurrentAccountId(string configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("lastKnownAccountUuid", out var id) &&
                   id.ValueKind == JsonValueKind.String &&
                   id.GetString() is { Length: > 0 } accountId
                ? accountId
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A cheap fingerprint of the token cache in config.json, without decrypting it. The desktop app
    /// rewrites config.json for many reasons (window position, settings); this only changes when the
    /// cached tokens do, so a file watcher can ignore the rest.
    /// </summary>
    public static string CacheSignature(string configJson) => string.Join('\n', EncryptedCacheValues(configJson));

    /// <summary>The still-encrypted base64 cache blobs from config.json, newest layout first.</summary>
    public static IReadOnlyList<string> EncryptedCacheValues(string configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return [];

            var values = new List<string>();
            foreach (var cacheKey in CacheKeys)
                if (root.TryGetProperty(cacheKey, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    value.GetString() is { Length: > 0 } blob)
                {
                    values.Add(blob);
                }
            return values;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The first token <see cref="RankTokens"/> offers from a single decrypted cache, or null.</summary>
    public static ClaudeCodeCredentials? SelectToken(string decryptedJson, DateTimeOffset now, string? accountId = null)
        => RankTokens([decryptedJson], now, accountId).FirstOrDefault();

    /// <summary>
    /// Every token across the decrypted caches, best first: unexpired before expired, then the Claude
    /// Code session token (<c>user:sessions:claude_code</c>), then the entries whose scopes best cover
    /// what the usage endpoint needs (<c>user:inference</c>, then <c>user:profile</c>), and among equals
    /// the one that expires latest. Several caches are considered together because the newest layout
    /// isn't always where the usable token is. Entries without a token are skipped, and a token found
    /// in more than one cache is listed once. Given <paramref name="accountId"/> (see
    /// <see cref="CurrentAccountId"/>), entries keyed to another account are left out; an entry whose
    /// key doesn't name an account is kept, so a change in the key format doesn't hide every token.
    /// </summary>
    public static IReadOnlyList<ClaudeCodeCredentials> RankTokens(IEnumerable<string> decryptedCaches, DateTimeOffset now, string? accountId = null)
    {
        var ranked = new List<((bool Live, bool Session, bool Inference, bool Profile, long Expiry) Rank, ClaudeCodeCredentials Credentials)>();

        foreach (var decryptedJson in decryptedCaches)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(decryptedJson);
            }
            catch (JsonException)
            {
                continue;
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Object ||
                        !entry.Value.TryGetProperty("token", out var tokenElement) ||
                        tokenElement.GetString() is not { } token ||
                        string.IsNullOrWhiteSpace(token) ||
                        IsForAnotherAccount(entry.Name, accountId))
                    {
                        continue;
                    }

                    long? expiresAt = entry.Value.TryGetProperty("expiresAt", out var exp) && exp.TryGetInt64(out var value)
                        ? value
                        : null;
                    var credentials = new ClaudeCodeCredentials(token, expiresAt);
                    // The entry's key spells out its scopes, so a substring check is enough to rank it.
                    var rank = (!credentials.IsExpired(now), entry.Name.Contains(SessionScope), entry.Name.Contains(InferenceScope),
                        entry.Name.Contains(ProfileScope), expiresAt ?? long.MinValue);
                    ranked.Add((rank, credentials));
                }
            }
        }

        return ranked
            .OrderByDescending(item => item.Rank)
            .DistinctBy(item => item.Credentials.AccessToken)
            .Select(item => item.Credentials)
            .ToList();
    }

    private static bool IsForAnotherAccount(string entryKey, string? accountId)
    {
        if (accountId is null || !entryKey.StartsWith(AccountKeyPrefix, StringComparison.Ordinal))
            return false;
        var separator = entryKey.IndexOf('|', AccountKeyPrefix.Length);
        if (separator < 0)
            return false;
        var entryAccount = entryKey.AsSpan(AccountKeyPrefix.Length, separator - AccountKeyPrefix.Length);
        return !entryAccount.Equals(accountId, StringComparison.OrdinalIgnoreCase);
    }
}
