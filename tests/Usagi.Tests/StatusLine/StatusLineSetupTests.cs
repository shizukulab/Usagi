using System.Text.Json;
using System.Text.Json.Nodes;
using Usagi.Core.StatusLine;
using Xunit;

namespace Usagi.Tests.StatusLine;

public class StatusLineSetupTests
{
    private const string Helper = "C:/Apps/Usagi/UsagiStatusLine.exe";
    private const string Moved = "C:/Tools/Usagi/UsagiStatusLine.exe";
    private const string Theirs = "node ~/.claude/statusline.js";

    [Fact]
    public void Install_WithNoStatusLine_UsesTheHelperAlone()
    {
        Assert.Equal(Helper, StatusLineSetup.Install(null, Helper));
        Assert.Equal(Helper, StatusLineSetup.Install("  ", Helper));
    }

    [Fact]
    public void Install_PutsTheHelperInFrontOfTheUsersOwn()
    {
        Assert.Equal($"{Helper} --tee | {Theirs}", StatusLineSetup.Install(Theirs, Helper));
    }

    [Fact]
    public void Install_AgainOnlyUpdatesThePath()
    {
        Assert.Equal(Moved, StatusLineSetup.Install(Helper, Moved));
        Assert.Equal($"{Moved} --tee | {Theirs}", StatusLineSetup.Install($"{Helper} --tee | {Theirs}", Moved));
        // Quoted (a path with a space) and with backslashes is still recognised.
        Assert.Equal(Moved, StatusLineSetup.Install("\"C:\\Program Files\\Usagi\\UsagiStatusLine.exe\"", Moved));
    }

    [Fact]
    public void Uninstall_LeavesTheUsersOwnAsItWas()
    {
        Assert.Null(StatusLineSetup.Uninstall(Helper));
        Assert.Equal(Theirs, StatusLineSetup.Uninstall($"{Helper} --tee | {Theirs}"));
        Assert.Equal(Theirs, StatusLineSetup.Uninstall(Theirs));
        Assert.Null(StatusLineSetup.Uninstall(null));
    }

    [Fact]
    public void Uninstall_UndoesInstall_ForAPipelineOfTheirOwn()
    {
        const string pipeline = "cat | jq -r '.model.display_name'";

        Assert.Equal(pipeline, StatusLineSetup.Uninstall(StatusLineSetup.Install(pipeline, Helper)));
    }

    [Fact]
    public void IsInstalled_RecognisesOnlyTheHelper()
    {
        Assert.True(StatusLineSetup.IsInstalled(Helper));
        Assert.True(StatusLineSetup.IsInstalled($"{Helper} --tee | {Theirs}"));
        Assert.False(StatusLineSetup.IsInstalled(Theirs));
        Assert.False(StatusLineSetup.IsInstalled("NotUsagiStatusLine.exe"));
        Assert.False(StatusLineSetup.IsInstalled(null));
    }

    [Fact]
    public void UpdateSettings_AddsTheStatusLine_AndKeepsEverythingElse()
    {
        const string settings = """
        {
          "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "echo タスクが完了しました" } ] } ] },
          "theme": "dark"
        }
        """;

        var updated = StatusLineSetup.UpdateSettings(settings, existing => StatusLineSetup.Install(existing, Helper));

        var root = JsonNode.Parse(updated!)!.AsObject();
        Assert.Equal("command", (string?)root["statusLine"]!["type"]);
        Assert.Equal(Helper, (string?)root["statusLine"]!["command"]);
        Assert.Equal("dark", (string?)root["theme"]);
        Assert.Equal("echo タスクが完了しました", (string?)root["hooks"]!["Stop"]![0]!["hooks"]![0]!["command"]);
        // Non-ASCII stays readable rather than \u-escaped.
        Assert.Contains("タスクが完了しました", updated);
    }

    [Fact]
    public void UpdateSettings_KeepsTheStatusLinesOtherFields()
    {
        var settings = $$"""{ "statusLine": { "type": "command", "command": "{{Theirs}}", "padding": 2 } }""";

        var updated = StatusLineSetup.UpdateSettings(settings, existing => StatusLineSetup.Install(existing, Helper));

        var statusLine = JsonNode.Parse(updated!)!["statusLine"]!;
        Assert.Equal($"{Helper} --tee | {Theirs}", (string?)statusLine["command"]);
        Assert.Equal(2, (int)statusLine["padding"]!);
    }

    [Fact]
    public void UpdateSettings_RemovesTheStatusLine_WhenNothingIsLeft()
    {
        var settings = $$"""{ "statusLine": { "type": "command", "command": "{{Helper}}" }, "theme": "dark" }""";

        var updated = StatusLineSetup.UpdateSettings(settings, StatusLineSetup.Uninstall);

        var root = JsonNode.Parse(updated!)!.AsObject();
        Assert.False(root.ContainsKey("statusLine"));
        Assert.Equal("dark", (string?)root["theme"]);
    }

    [Fact]
    public void UpdateSettings_ReturnsNull_WhenNothingChanges()
    {
        var settings = $$"""{ "statusLine": { "type": "command", "command": "{{Helper}}" } }""";

        Assert.Null(StatusLineSetup.UpdateSettings(settings, existing => StatusLineSetup.Install(existing, Helper)));
        Assert.Null(StatusLineSetup.UpdateSettings("""{ "theme": "dark" }""", StatusLineSetup.Uninstall));
    }

    [Fact]
    public void UpdateSettings_CreatesTheFileContent_WhenThereIsNone()
    {
        var updated = StatusLineSetup.UpdateSettings(null, existing => StatusLineSetup.Install(existing, Helper));

        Assert.Equal(Helper, (string?)JsonNode.Parse(updated!)!["statusLine"]!["command"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{ // a comment\n \"theme\": \"dark\" }")]
    public void UpdateSettings_RefusesWhatItCantSafelyRewrite(string settings)
    {
        Assert.ThrowsAny<JsonException>(() => StatusLineSetup.UpdateSettings(settings, existing => StatusLineSetup.Install(existing, Helper)));
    }
}
