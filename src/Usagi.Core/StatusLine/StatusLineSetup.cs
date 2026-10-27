using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Usagi.Core.StatusLine;

/// <summary>
/// Puts the helper into Claude Code's status line setting, and takes it out again. A status line
/// the user already has keeps working: the helper goes in front of it as
/// <c>&lt;helper&gt; --tee | &lt;their command&gt;</c>, passing Claude Code's input through unchanged,
/// and taking the helper out leaves their command as it was. A pipe reads the same in Git Bash and
/// PowerShell, the two shells Claude Code runs status line commands in on Windows.
/// </summary>
public static class StatusLineSetup
{
    public const string TeeArgument = "--tee";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // Leave non-ASCII (hook messages in Japanese, say) readable rather than \u-escaped.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>The status line command with the helper in it, given the one there now (or null).</summary>
    /// <param name="helperInvocation">How to start the helper, as the shell should read it.</param>
    public static string Install(string? existingCommand, string helperInvocation)
    {
        if (string.IsNullOrWhiteSpace(existingCommand))
            return helperInvocation;

        return Parse(existingCommand) switch
        {
            { IsOurs: true, ChainedCommand: { } chained } => $"{helperInvocation} {TeeArgument} | {chained}",
            { IsOurs: true } => helperInvocation,
            _ => $"{helperInvocation} {TeeArgument} | {existingCommand.Trim()}"
        };
    }

    /// <summary>The status line command with the helper taken out: the user's own, or null if there was none.</summary>
    public static string? Uninstall(string? existingCommand)
    {
        if (string.IsNullOrWhiteSpace(existingCommand))
            return existingCommand;

        return Parse(existingCommand) switch
        {
            { IsOurs: true, ChainedCommand: { } chained } => chained,
            { IsOurs: true } => null,
            _ => existingCommand
        };
    }

    /// <summary>Whether the command starts the helper (alone or in front of another).</summary>
    public static bool IsInstalled(string? command) => !string.IsNullOrWhiteSpace(command) && Parse(command).IsOurs;

    /// <summary>
    /// Applies <paramref name="change"/> to the status line command in Claude Code's settings.json
    /// text, leaving everything else in it alone.
    /// </summary>
    /// <param name="settingsJson">The file's text, or null if there's no file yet.</param>
    /// <param name="change">Given the command now (null if none), returns the one wanted (null to remove it).</param>
    /// <returns>The new text, or null if nothing changes.</returns>
    /// <exception cref="JsonException">The file isn't a JSON object this can safely rewrite.</exception>
    public static string? UpdateSettings(string? settingsJson, Func<string?, string?> change)
    {
        var root = string.IsNullOrWhiteSpace(settingsJson)
            ? new JsonObject()
            : JsonNode.Parse(settingsJson) as JsonObject ?? throw new JsonException("settings.json isn't a JSON object.");

        var statusLine = root["statusLine"] as JsonObject;
        var current = statusLine?["command"] is JsonValue value && value.TryGetValue<string>(out var command) ? command : null;
        var wanted = change(current);
        if (wanted == current)
            return null;

        if (wanted is null)
        {
            root.Remove("statusLine");
        }
        else
        {
            if (statusLine is null)
            {
                statusLine = new JsonObject();
                root["statusLine"] = statusLine;
            }
            statusLine["type"] = "command";
            statusLine["command"] = wanted;
        }

        return root.ToJsonString(WriteOptions);
    }

    private readonly record struct ParsedCommand(bool IsOurs, string? ChainedCommand);

    // Looks at the program the command starts (quoted or not): the helper is recognised by its file name.
    private static ParsedCommand Parse(string command)
    {
        var text = command.Trim();
        string program;
        string rest;
        if (text.StartsWith('"'))
        {
            var close = text.IndexOf('"', 1);
            if (close < 0)
                return default;
            program = text[1..close];
            rest = text[(close + 1)..];
        }
        else
        {
            var space = text.IndexOfAny([' ', '\t']);
            program = space < 0 ? text : text[..space];
            rest = space < 0 ? "" : text[space..];
        }

        var fileName = program[(program.LastIndexOfAny(['/', '\\']) + 1)..];
        if (!fileName.Equals(StatusLineUsage.HelperFileName, StringComparison.OrdinalIgnoreCase))
            return default;

        rest = rest.Trim();
        if (!rest.StartsWith(TeeArgument, StringComparison.Ordinal))
            return new ParsedCommand(true, null);
        rest = rest[TeeArgument.Length..].TrimStart();
        return rest.StartsWith('|') && rest[1..].Trim() is { Length: > 0 } chained
            ? new ParsedCommand(true, chained)
            : new ParsedCommand(true, null);
    }
}
