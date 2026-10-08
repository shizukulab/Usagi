using Usagi.Core.ClaudeCode;

namespace Usagi.Platform.ClaudeCode;

/// <summary>The Claude Code CLI as installed on this PC: Windows-native, or inside a WSL distro.</summary>
public sealed class ClaudeCodeEnvironment : IClaudeCodeEnvironment
{
    public bool IsCliInstalled => ClaudeCli.IsInstalled;

    public IEnumerable<ClaudeCredentialLookup> FindCredentialCandidates() => ClaudeCredentialResolver.ResolveCandidates();

    public ClaudeAuthStatus? GetAuthStatus() => ClaudeCli.GetAuthStatus();
}
