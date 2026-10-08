using System.IO;
using System.Windows.Threading;
using Usagi.Core.ClaudeCode;

namespace Usagi.App.Services;

/// <summary>
/// Raises <see cref="Changed"/> (on the UI thread) shortly after the credentials the app reads
/// change, so the tracker recovers right away instead of waiting for the next timer tick:
/// <list type="bullet">
///   <item>Claude Code CLI's Windows credentials file — rewritten when the user signs in with
///   <c>claude auth login</c>, or when Claude Code renews an expired token the next time it's used.</item>
///   <item>The Claude desktop app's config.json — but only when its token cache changes. The
///   desktop app rewrites that file for many other reasons (window position, settings), and
///   reacting to each would refresh for nothing (and, in token-using mode, send a prompt).</item>
/// </list>
/// WSL credentials and the Microsoft Store build of the desktop app aren't watched; the periodic
/// refresh still picks them up.
/// </summary>
public sealed class CredentialsFileWatcher : IDisposable
{
    // The writer may update the file in several steps; wait for it to settle before re-reading.
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1);

    private readonly FileSystemWatcher? _cliWatcher;
    private readonly FileSystemWatcher? _desktopWatcher;
    private readonly DispatcherTimer _debounceTimer;
    private readonly string _desktopConfigPath = Path.Combine(ClaudeDesktopTokenCache.ConfigDirectory, ClaudeDesktopTokenCache.ConfigFileName);

    // What the desktop app's token cache looked like last time, so an unrelated rewrite of its config is ignored.
    private string _desktopCacheSignature;

    // Set by a CLI file event: that file holds nothing but credentials, so any change counts.
    private bool _cliChanged;

    public CredentialsFileWatcher()
    {
        _debounceTimer = new DispatcherTimer { Interval = Debounce };
        _debounceTimer.Tick += (_, _) => OnSettled();

        _desktopCacheSignature = ReadDesktopCacheSignature() ?? string.Empty;
        _cliWatcher = Watch(ClaudeCodeCredentialReader.CredentialsFilePath, () => _cliChanged = true);
        _desktopWatcher = Watch(_desktopConfigPath, () => { });
    }

    public event Action? Changed;

    public void Dispose()
    {
        _cliWatcher?.Dispose();
        _desktopWatcher?.Dispose();
        _debounceTimer.Stop();
    }

    /// <summary>Watches one file, or returns null if its folder doesn't exist (that app never ran here; the periodic refresh covers a later install).</summary>
    private FileSystemWatcher? Watch(string path, Action onEvent)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory))
            return null;

        var watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime
        };
        // FileSystemWatcher raises events on a thread-pool thread.
        FileSystemEventHandler handler = (_, _) => _debounceTimer.Dispatcher.BeginInvoke(() =>
        {
            onEvent();
            _debounceTimer.Stop();
            _debounceTimer.Start();
        });
        watcher.Changed += handler;
        watcher.Created += handler;
        watcher.Renamed += (sender, e) => handler(sender, e);
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void OnSettled()
    {
        _debounceTimer.Stop();

        // Unreadable (e.g. caught mid-write): keep the last signature rather than count it as a change.
        var signature = ReadDesktopCacheSignature() ?? _desktopCacheSignature;
        var desktopChanged = signature != _desktopCacheSignature;
        _desktopCacheSignature = signature;

        if (_cliChanged || desktopChanged)
        {
            _cliChanged = false;
            Changed?.Invoke();
        }
    }

    // Reads the still-encrypted cache values only; nothing is decrypted here. Null if the file can't be read.
    private string? ReadDesktopCacheSignature()
    {
        try
        {
            return ClaudeDesktopTokenCache.CacheSignature(File.ReadAllText(_desktopConfigPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
