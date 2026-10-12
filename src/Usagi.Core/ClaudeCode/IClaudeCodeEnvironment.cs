using Usagi.Core.Models;

namespace Usagi.Core.ClaudeCode;

/// <summary>Where credentials were found: "Claude Desktop", "Windows", or "WSL (Ubuntu)" etc.</summary>
/// <param name="IsWsl">True for a WSL distro: its CLI can't be driven from here (e.g. to sign in).</param>
/// <param name="IsDesktop">True for the Claude desktop app, whose sign-in is managed in the app itself, not the CLI.</param>
public sealed record ClaudeCredentialLocation(string DisplayName, bool IsWsl, bool IsDesktop = false);

/// <summary>Outcome of looking for Claude Code CLI's credentials.</summary>
/// <param name="Credentials">Usable (unexpired) credentials, or null.</param>
/// <param name="Location">Where <paramref name="Credentials"/> came from — or, if they're
/// null but a credentials file exists, where the expired one was found.</param>
public sealed record ClaudeCredentialLookup(ClaudeCodeCredentials? Credentials, ClaudeCredentialLocation? Location)
{
    /// <summary>How long a token has to have left for the CLI to be asked about it (see <see cref="CanAskCliForStatus"/>).</summary>
    public static readonly TimeSpan CliRenewalMargin = TimeSpan.FromMinutes(10);

    public bool CredentialsFileFound => Location is not null;

    /// <summary>
    /// Whether <c>claude auth status</c> is worth asking, and safe to: only the Windows CLI's own
    /// sign-in has an account for it to describe, and only with a token that isn't about to run
    /// out. Started on an expired (or nearly expired) token, the CLI may renew it, and a Claude Code
    /// running meanwhile (in an editor, say) that tries the same then fails with "another Claude Code
    /// process is refreshing it". This app never sets that off: it leaves renewing to Claude Code.
    /// </summary>
    public bool CanAskCliForStatus(DateTimeOffset now) =>
        Location is { IsDesktop: false, IsWsl: false } && Credentials is { } credentials && !credentials.IsExpired(now + CliRenewalMargin);
}

/// <summary>
/// Claude as installed on this machine (the desktop app, Claude Code on Windows or in WSL), as far
/// as the app deals with it: its credentials are read and the CLI's account status is asked for.
/// Read-only — signing in is done in Claude itself. Nothing here sends a prompt. Every member may
/// start a process, so call them off the UI thread.
/// </summary>
public interface IClaudeCodeEnvironment
{
    bool IsCliInstalled { get; }

    /// <summary>
    /// Finds the credentials wherever they currently live: the usable (unexpired) ones in priority
    /// order — a caller fetching usage tries each until one is accepted, so a token one source has gone
    /// stale on falls back to the next; a caller that only wants the main one takes the first. Never
    /// empty: if none are usable, yields a single lookup with null <see cref="ClaudeCredentialLookup.Credentials"/>
    /// whose <see cref="ClaudeCredentialLookup.Location"/> says where an expired one was found (or null if none exist).
    /// Enumerated lazily, so a later source (e.g. WSL, which starts a process) is only reached once the
    /// earlier ones are passed. Read-only: an expired token is reported, not refreshed.
    /// </summary>
    IEnumerable<ClaudeCredentialLookup> FindCredentialCandidates();

    /// <returns>The account status, or null if the CLI isn't installed or didn't answer.</returns>
    /// <remarks>Only for a sign-in that <see cref="ClaudeCredentialLookup.CanAskCliForStatus"/> allows.</remarks>
    ClaudeAuthStatus? GetAuthStatus();
}
