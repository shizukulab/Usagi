using System.IO;
using System.Security.Cryptography;
using System.Text;
using Usagi.Core.ClaudeCode;
using Usagi.Core.Models;

namespace Usagi.Platform.ClaudeCode;

/// <summary>
/// Reads the OAuth token the Claude desktop app (Electron) caches under %APPDATA%\Claude, so the
/// app can show usage for someone who runs the desktop app but not the CLI. The token is stored
/// the way Chromium stores cookies: a per-user AES key wrapped with Windows DPAPI in "Local State",
/// and the cache itself AES-GCM-encrypted in config.json. The DPAPI key is bound to the current
/// Windows user, so only they can read it. Read-only: nothing is written back, and the desktop app
/// keeps the token fresh on its own. The JSON parsing and entry selection live in
/// <see cref="ClaudeDesktopTokenCache"/>; this class only locates the files and decrypts.
/// </summary>
internal static class ClaudeDesktopCredentials
{
    private const string DpapiPrefix = "DPAPI";
    private const string OsCryptPrefix = "v10";
    private const int GcmNonceLength = 12;
    private const int GcmTagLength = 16;

    public static ClaudeCodeCredentials? TryRead()
    {
        foreach (var directory in DataDirectories())
            if (TryReadFrom(directory) is { } credentials)
                return credentials;
        return null;
    }

    private static ClaudeCodeCredentials? TryReadFrom(string directory)
    {
        if (ReadFile(Path.Combine(directory, ClaudeDesktopTokenCache.ConfigFileName)) is not { } configJson ||
            ReadFile(Path.Combine(directory, ClaudeDesktopTokenCache.LocalStateFileName)) is not { } localStateJson ||
            ClaudeDesktopTokenCache.EncryptedKey(localStateJson) is not { } encryptedKey ||
            UnwrapKey(encryptedKey) is not { } key)
        {
            return null;
        }

        // Decrypt every cache and choose across them together: the usable token isn't always in the
        // newest layout, so stopping at the first cache can pick a stale entry the API then rejects.
        var caches = ClaudeDesktopTokenCache.EncryptedCacheValues(configJson)
            .Select(value => Decrypt(value, key))
            .OfType<string>();
        return ClaudeDesktopTokenCache.SelectToken(caches);
    }

    // %APPDATA%\Claude for a normal install, plus the Microsoft Store (MSIX) build's redirected
    // AppData under each Claude_* package.
    private static IEnumerable<string> DataDirectories()
    {
        yield return ClaudeDesktopTokenCache.ConfigDirectory;

        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        string[] claudePackages;
        try
        {
            claudePackages = Directory.Exists(packages) ? Directory.GetDirectories(packages, "Claude_*") : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var package in claudePackages)
            yield return Path.Combine(package, "LocalCache", "Roaming", "Claude");
    }

    private static string? ReadFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The AES key, DPAPI-wrapped and base64'd in "Local State" behind a "DPAPI" marker.
    private static byte[]? UnwrapKey(string encryptedKeyBase64)
    {
        try
        {
            var wrapped = Convert.FromBase64String(encryptedKeyBase64);
            var prefix = Encoding.ASCII.GetBytes(DpapiPrefix);
            if (wrapped.Length <= prefix.Length || !wrapped.AsSpan(0, prefix.Length).SequenceEqual(prefix))
                return null;
            return ProtectedData.Unprotect(wrapped[prefix.Length..], optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    // A cache blob: "v10" + 12-byte nonce + ciphertext + 16-byte GCM tag, base64'd.
    private static string? Decrypt(string valueBase64, byte[] key)
    {
        try
        {
            var raw = Convert.FromBase64String(valueBase64);
            var prefix = Encoding.ASCII.GetBytes(OsCryptPrefix);
            if (raw.Length < prefix.Length + GcmNonceLength + GcmTagLength ||
                !raw.AsSpan(0, prefix.Length).SequenceEqual(prefix))
            {
                return null;
            }

            var nonce = raw.AsSpan(prefix.Length, GcmNonceLength);
            var tag = raw.AsSpan(raw.Length - GcmTagLength, GcmTagLength);
            var cipher = raw.AsSpan(prefix.Length + GcmNonceLength, raw.Length - prefix.Length - GcmNonceLength - GcmTagLength);
            var plaintext = new byte[cipher.Length];
            using var aes = new AesGcm(key, GcmTagLength);
            aes.Decrypt(nonce, cipher, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }
}
