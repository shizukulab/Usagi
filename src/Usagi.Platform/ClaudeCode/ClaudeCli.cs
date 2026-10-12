using System.Diagnostics;
using System.Text;
using Usagi.Core.ClaudeCode;
using Usagi.Platform.Processes;

namespace Usagi.Platform.ClaudeCode;

/// <summary>
/// Talks to the Windows-installed Claude Code CLI for account info only: <c>claude auth status</c>
/// reads its local state, so it sends no prompt and consumes no usage. This app never signs in or
/// handles OAuth itself — the CLI writes its credentials file, the app reads it.
/// </summary>
internal static class ClaudeCli
{
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(15);

    // Where Claude Code's native installer puts the executable (no Node.js needed).
    private static readonly string NativeInstallPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");

    // Only a successful lookup is cached, so installing Claude Code while the app runs is picked up.
    private static string? _cachedPath;

    public static bool IsInstalled => Locate() is not null;

    /// <returns>The account status, or null if the CLI isn't installed or didn't answer.</returns>
    public static ClaudeAuthStatus? GetAuthStatus()
    {
        if (Locate() is not { } path)
            return null;

        var result = ProcessRunner.Run(path, [], StatusTimeout, Encoding.UTF8,
            startInfo => SetCommand(startInfo, path, ["auth", "status", "--json"]));
        return result is { } r ? ClaudeAuthStatus.Parse(r.StandardOutput) : null;
    }

    private static string? Locate()
    {
        if (_cachedPath is not null && File.Exists(_cachedPath))
            return _cachedPath;

        _cachedPath = File.Exists(NativeInstallPath)
            ? NativeInstallPath
            : FindOnPath("claude.exe") ?? FindOnPath("claude.cmd"); // .cmd = npm global install
        return _cachedPath;
    }

    // Searched here rather than with where.exe, which also looks in the current directory
    // (and costs a process). Only absolute PATH entries count, for the same reason.
    private static string? FindOnPath(string name)
    {
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in directories)
        {
            var directory = entry.Trim('"');
            try
            {
                if (!Path.IsPathFullyQualified(directory))
                    continue;
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry (invalid characters): skip it.
            }
        }

        return null;
    }

    /// <summary>Points <paramref name="startInfo"/> at the CLI, with <paramref name="arguments"/> (fixed words, no quoting needed).</summary>
    private static void SetCommand(ProcessStartInfo startInfo, string claudePath, string[] arguments)
    {
        if (!claudePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = claudePath;
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            return;
        }

        // npm's .cmd shims can't be started directly without a shell. With /s, cmd strips just
        // the outer pair of quotes, so the path stays quoted and a space or an & in it (a user
        // name can have either) isn't read as a separator. /d skips any AutoRun commands.
        startInfo.FileName = SystemExecutables.Cmd;
        startInfo.Arguments = $"/d /s /c \"\"{claudePath}\" {string.Join(' ', arguments)}\"";
    }
}
