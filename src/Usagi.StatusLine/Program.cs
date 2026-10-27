using System.Text;
using Usagi.Core.Models;
using Usagi.Core.StatusLine;

// Claude Code runs this as its status line, with the session's JSON on stdin. The usage in it is
// saved for Usagi.exe; what's printed is the status line. With --tee, another status line comes
// after this one in a pipe: the input is passed on to it unchanged, and it prints the status line.
// Nothing here may break Claude Code's status line, so every failure is swallowed.

var tee = args.Contains(StatusLineSetup.TeeArgument);
using var input = new MemoryStream();
try
{
    Console.OpenStandardInput().CopyTo(input);
}
catch (IOException)
{
}

var bytes = input.ToArray();
var now = DateTimeOffset.Now;
ClaudeUsage? usage = null;
try
{
    usage = StatusLineUsage.FromStatusLineInput(Encoding.UTF8.GetString(bytes), now);
    if (usage is not null)
        Save(usage, now);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
}

using var output = Console.OpenStandardOutput();
if (tee)
    output.Write(bytes);
else if (usage is not null)
    output.Write(Encoding.UTF8.GetBytes(StatusLineUsage.Summary(usage, now)));

return 0;

// Several Claude Code sessions can run this at once, so each writes its own temporary file. An
// unchanged reading is only rewritten once a minute: Usagi.exe reacts to every write, and the
// status line updates many times a minute while Claude Code is busy.
static void Save(ClaudeUsage usage, DateTimeOffset now)
{
    var path = StatusLineUsage.SnapshotPath;
    if (File.Exists(path) &&
        StatusLineUsage.Deserialize(File.ReadAllText(path)) is { } saved &&
        StatusLineUsage.SameReading(saved, usage) &&
        now - saved.LastUpdated < TimeSpan.FromMinutes(1))
    {
        return;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var temporaryPath = $"{path}.{Environment.ProcessId}.tmp";
    File.WriteAllText(temporaryPath, StatusLineUsage.Serialize(usage));
    File.Move(temporaryPath, path, overwrite: true);
}
