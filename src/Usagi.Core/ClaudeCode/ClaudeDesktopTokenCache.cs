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
/// <see cref="SelectToken"/>. Everything here only parses JSON, so it stays testable off Windows.
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
    // poor signal of which is live (a dead token can outlast a good one). The Claude Code session
    // token carries user:sessions:claude_code — it's the one meant for reading Claude Code usage, so
    // it's preferred outright. Failing that, fall back to scope coverage (user:inference, then
    // user:profile) and the latest expiry, matching how other monitors pick a token.
    private const string SessionScope = "user:sessions:claude_code";
    private const string InferenceScope = "user:inference";
    private const string ProfileScope = "user:profile";

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

    /// <summary>Picks the token from a single decrypted cache (see the multi-cache overload).</summary>
    public static ClaudeCodeCredentials? SelectToken(string decryptedJson) => SelectToken([decryptedJson]);

    /// <summary>
    /// Picks the token to use across every decrypted cache: the Claude Code session token
    /// (<c>user:sessions:claude_code</c>) first, otherwise the entry whose scopes best cover what the
    /// usage endpoint needs (<c>user:inference</c>, then <c>user:profile</c>), and among equals the one
    /// that expires latest. Several caches are considered together because the newest layout isn't always
    /// where the usable token is. Entries without a token are skipped; null if none has one.
    /// </summary>
    public static ClaudeCodeCredentials? SelectToken(IEnumerable<string> decryptedCaches)
    {
        ClaudeCodeCredentials? best = null;
        (bool Session, bool Inference, bool Profile, long Expiry) bestRank = default;

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
                        string.IsNullOrWhiteSpace(token))
                    {
                        continue;
                    }

                    long? expiresAt = entry.Value.TryGetProperty("expiresAt", out var exp) && exp.TryGetInt64(out var value)
                        ? value
                        : null;
                    // The entry's key spells out its scopes, so a substring check is enough to rank it.
                    var rank = (entry.Name.Contains(SessionScope), entry.Name.Contains(InferenceScope), entry.Name.Contains(ProfileScope), expiresAt ?? long.MinValue);
                    if (best is null || rank.CompareTo(bestRank) > 0)
                    {
                        best = new ClaudeCodeCredentials(token, expiresAt);
                        bestRank = rank;
                    }
                }
            }
        }

        return best;
    }
}
