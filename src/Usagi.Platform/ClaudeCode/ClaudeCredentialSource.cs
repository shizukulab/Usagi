using Usagi.Core.ClaudeCode;
using Usagi.Core.Models;

namespace Usagi.Platform.ClaudeCode;

/// <summary>
/// A single place Claude credentials might live: the Claude desktop app, Claude Code CLI on
/// Windows, or a specific installed WSL distro. The app checks each when looking for credentials.
/// </summary>
internal sealed class ClaudeCredentialSource
{
    private readonly Func<ClaudeCodeCredentials?> _read;

    private ClaudeCredentialSource(ClaudeCredentialLocation location, Func<ClaudeCodeCredentials?> read)
    {
        Location = location;
        _read = read;
    }

    private ClaudeCredentialSource(string displayName, bool isWsl, Func<ClaudeCodeCredentials?> read)
        : this(new ClaudeCredentialLocation(displayName, isWsl), read)
    {
    }

    public ClaudeCredentialLocation Location { get; }

    public static ClaudeCredentialSource Desktop { get; } =
        new(new ClaudeCredentialLocation("Claude Desktop", IsWsl: false, IsDesktop: true), ClaudeDesktopCredentials.TryRead);

    public static ClaudeCredentialSource Windows { get; } =
        new("Windows", isWsl: false, ClaudeCodeCredentialReader.TryRead);

    public static ClaudeCredentialSource ForWsl(string distro) =>
        new($"WSL ({distro})", isWsl: true, () => WslClaudeCli.TryReadCredentials(distro));

    /// <summary>
    /// The Claude desktop app first — its token is long-lived and it refreshes it itself, so it
    /// rarely expires — then Windows-native Claude Code, then every installed WSL distro, in listed
    /// order. Lazy: wsl.exe is only run once the caller moves past the earlier sources, so a caller
    /// that stops at usable desktop or Windows credentials never starts it.
    /// </summary>
    public static IEnumerable<ClaudeCredentialSource> EnumerateAll()
    {
        yield return Desktop;
        yield return Windows;
        foreach (var distro in WslClaudeCli.ListDistros())
            yield return ForWsl(distro);
    }

    public ClaudeCodeCredentials? TryRead() => _read();
}
