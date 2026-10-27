using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Usagi.Core.ClaudeCode;
using Usagi.Core.Models;
using Usagi.Core.StatusLine;

namespace Usagi.Platform.ClaudeCode;

/// <summary>What became of putting the helper into (or taking it out of) Claude Code's settings.</summary>
public enum StatusLineSetupOutcome
{
    /// <summary>The setting was already as wanted.</summary>
    Unchanged,

    /// <summary>settings.json was rewritten.</summary>
    Updated,

    /// <summary>UsagiStatusLine.exe isn't next to Usagi.exe, so there's nothing to register.</summary>
    HelperMissing,

    /// <summary>settings.json couldn't be read, parsed or written; it was left alone.</summary>
    SettingsUnavailable
}

/// <summary>
/// Registers UsagiStatusLine.exe as Claude Code's status line in its user settings
/// (settings.json under the Claude config folder), and reads back the usage it saves. The helper is
/// referred to where it is, next to Usagi.exe, so registering again on every start keeps the path
/// right after the app's folder moves.
/// </summary>
public static class ClaudeStatusLineInstaller
{
    public static string SettingsPath => Path.Combine(ClaudeCodeCredentialReader.ClaudeConfigDirectory, "settings.json");

    public static StatusLineSetupOutcome Install()
    {
        var helper = Path.Combine(AppContext.BaseDirectory, StatusLineUsage.HelperFileName);
        if (!File.Exists(helper))
            return StatusLineSetupOutcome.HelperMissing;
        var invocation = Invocation(helper);
        return Update(existing => StatusLineSetup.Install(existing, invocation));
    }

    public static StatusLineSetupOutcome Uninstall() => Update(StatusLineSetup.Uninstall);

    /// <summary>The latest usage the helper saved, or null if there's none (or it can't be read just now).</summary>
    public static ClaudeUsage? ReadSavedUsage()
    {
        try
        {
            var path = StatusLineUsage.SnapshotPath;
            return File.Exists(path) ? StatusLineUsage.Deserialize(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static StatusLineSetupOutcome Update(Func<string?, string?> change)
    {
        try
        {
            var path = SettingsPath;
            var current = File.Exists(path) ? File.ReadAllText(path) : null;
            if (StatusLineSetup.UpdateSettings(current, change) is not { } updated)
                return StatusLineSetupOutcome.Unchanged;

            // Written beside the file and swapped in, so Claude Code never reads half of it.
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = path + ".usagi.tmp";
            File.WriteAllText(temporaryPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
            return StatusLineSetupOutcome.Updated;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return StatusLineSetupOutcome.SettingsUnavailable;
        }
    }

    // Claude Code runs the command in Git Bash, or PowerShell without it. Forward slashes survive
    // Git Bash (backslashes are escapes there), and an unquoted path starts the program in both —
    // quoting would need "&" in PowerShell only. A path with a space is shortened to its 8.3 form,
    // and quoted only as a last resort (then it works in Git Bash alone).
    private static string Invocation(string helperPath)
    {
        var path = helperPath;
        if (path.Contains(' ') && ShortPath(path) is { } shortPath)
            path = shortPath;
        path = path.Replace('\\', '/');
        return path.Contains(' ') ? $"\"{path}\"" : path;
    }

    private static string? ShortPath(string path)
    {
        var buffer = new StringBuilder(1024);
        var length = GetShortPathName(path, buffer, buffer.Capacity);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, int bufferLength);
}
