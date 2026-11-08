using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>
/// A server file or folder as this PC sees it, through the server's path mappings (Settings → Paths).
/// </summary>
/// <param name="Path">The mapped path, written the Windows way when the mapping's local folder is one.</param>
public sealed record LocalFileLocation(string Path)
{
    /// <summary>
    /// The item as it is on disk. Transmission can add ".part" to files it hasn't finished, so this falls back to
    /// that name when the plain one isn't there. Touches the disk.
    /// </summary>
    public string ExistingPath()
    {
        var partial = Path + ".part";
        return !File.Exists(Path) && !Directory.Exists(Path) && File.Exists(partial) ? partial : Path;
    }

    /// <summary>Whether the item, or its ".part" stand-in, is there right now. Touches the disk.</summary>
    public bool Exists()
    {
        var existing = ExistingPath();
        return File.Exists(existing) || Directory.Exists(existing);
    }
}

public enum LocalOpenResult
{
    Opened,

    /// <summary>No path mapping covers the item, so nothing happened.</summary>
    NotMapped,

    /// <summary>
    /// The item is a program, script or installer (<see cref="RunnableFile"/>). Ask, then open it with
    /// <c>allowRunnable: true</c> or show it in its folder.
    /// </summary>
    NeedsConfirmation,

    /// <summary>
    /// Windows couldn't open it, for example because the share isn't connected. Logged; the app suggests checking
    /// the share and the folder in Settings → Paths.
    /// </summary>
    Failed,
}

/// <summary>What <see cref="LocalFiles.Open(Guid, string, bool)"/> did.</summary>
/// <param name="LocalPath">The local path it opened or would open; null when the item isn't mapped.</param>
public sealed record LocalOpenOutcome(LocalOpenResult Result, string? LocalPath);

/// <summary>
/// Opens, shows and copies the paths of torrents' files on this PC, found through the server's path mappings.
/// </summary>
/// <remarks>
/// Members marked "touches the disk" can wait on a network share; a disconnected one can take many seconds to
/// answer. Use them for user actions, not while drawing a list or menu: <see cref="LocationFor"/> and
/// <see cref="IsAvailableLocally"/> only read the mappings.
/// </remarks>
public sealed class LocalFiles
{
    private readonly ServerStore _servers;
    private readonly IShell _shell;
    private readonly ILogger _logger;

    public LocalFiles(ServerStore servers, IShell shell, ILogger<LocalFiles>? logger = null)
    {
        _servers = servers ?? throw new ArgumentNullException(nameof(servers));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _logger = logger ?? NullLogger<LocalFiles>.Instance;
    }

    /// <summary>
    /// Where a server path is on this PC, or null when no path mapping covers it. Reads the saved profile, not the
    /// connection's copy, so new mappings apply without reconnecting.
    /// </summary>
    public LocalFileLocation? LocationFor(Guid serverId, string remotePath)
    {
        ArgumentNullException.ThrowIfNull(remotePath);
        // A ".." would climb out of the mapped folder. Transmission never reports one, so refuse it.
        if (remotePath.Split('/', '\\').Contains(".."))
        {
            return null;
        }
        if (_servers.Find(serverId)?.PathMappings.MappingFor(remotePath) is not { } mapping
            || mapping.LocalPathFor(remotePath) is not { } localPath)
        {
            return null;
        }
        return new LocalFileLocation(
            LocalPaths.IsWindowsPath(mapping.LocalPath) ? LocalPaths.NormalizedWindowsPath(localPath) : localPath);
    }

    /// <summary>
    /// Whether a path mapping covers the torrent's download folder, or its incomplete folder while that's in use,
    /// for enabling Open and Show in folder. Doesn't touch the disk.
    /// </summary>
    public bool IsAvailableLocally(Guid serverId, Torrent torrent, SessionSettings? session)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return LocationFor(serverId, RemotePath.Join(torrent.DownloadDir, torrent.Name)) is not null
            || (IncompleteFolder(torrent, session) is { } incomplete
                && LocationFor(serverId, RemotePath.Join(incomplete, torrent.Name)) is not null);
    }

    /// <summary>
    /// The server folder that holds the torrent's data now. With the daemon's incomplete folder turned on,
    /// unfinished torrents live there and move to their download folder when done (transgui issues #304, #1367).
    /// Torrents started before the setting stay where they were, so the incomplete folder is only used when the
    /// data is actually found there. Touches the disk.
    /// </summary>
    public string CurrentFolder(Guid serverId, Torrent torrent, SessionSettings? session)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        if (IncompleteFolder(torrent, session) is not { } incomplete)
        {
            return torrent.DownloadDir;
        }
        var candidate = LocationFor(serverId, RemotePath.Join(incomplete, torrent.Name));
        return candidate?.Exists() == true ? incomplete : torrent.DownloadDir;
    }

    /// <summary>
    /// The server path of the torrent's top-level item, or of a file inside it (a path relative to the folder, as
    /// in the torrent's file list), where it is now. Touches the disk.
    /// </summary>
    public string CurrentRemotePath(
        Guid serverId, Torrent torrent, SessionSettings? session, string? relativePath = null)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return RemotePath.Join(CurrentFolder(serverId, torrent, session), relativePath ?? torrent.Name);
    }

    /// <summary>Opens the torrent's file or folder with its default app. Touches the disk.</summary>
    public LocalOpenOutcome Open(
        Guid serverId, Torrent torrent, SessionSettings? session, bool allowRunnable = false) =>
        Open(serverId, CurrentRemotePath(serverId, torrent, session), allowRunnable);

    /// <summary>
    /// Opens a server file or folder with its default app on this PC. Programs, scripts and installers are only
    /// opened with <paramref name="allowRunnable"/>, after the user agreed. Touches the disk.
    /// </summary>
    public LocalOpenOutcome Open(Guid serverId, string remotePath, bool allowRunnable = false)
    {
        if (LocationFor(serverId, remotePath) is not { } location)
        {
            return new LocalOpenOutcome(LocalOpenResult.NotMapped, null);
        }
        var path = location.ExistingPath();
        if (!allowRunnable && RunnableFile.NeedsConfirmation(path))
        {
            return new LocalOpenOutcome(LocalOpenResult.NeedsConfirmation, path);
        }
        if (_shell.Open(path))
        {
            return new LocalOpenOutcome(LocalOpenResult.Opened, path);
        }
        Log.LocalFileNotOpened(_logger, StorageFiles.DisplayPath(path));
        return new LocalOpenOutcome(LocalOpenResult.Failed, path);
    }

    /// <summary>Selects the torrents' files or folders in File Explorer. Touches the disk.</summary>
    public bool Reveal(Guid serverId, IEnumerable<Torrent> torrents, SessionSettings? session)
    {
        ArgumentNullException.ThrowIfNull(torrents);
        return Reveal(serverId, torrents.Select(torrent => CurrentRemotePath(serverId, torrent, session)));
    }

    /// <summary>
    /// Selects server files or folders in File Explorer, skipping those no path mapping covers. Returns false when
    /// none was mapped, so nothing happened. Touches the disk.
    /// </summary>
    public bool Reveal(Guid serverId, IEnumerable<string> remotePaths)
    {
        ArgumentNullException.ThrowIfNull(remotePaths);
        List<string> paths = [.. remotePaths
            .Select(remotePath => LocationFor(serverId, remotePath))
            .OfType<LocalFileLocation>()
            .Select(location => location.ExistingPath())];
        if (paths.Count == 0)
        {
            return false;
        }
        _shell.Reveal(paths);
        return true;
    }

    /// <summary>
    /// Copies paths to the clipboard, one per line: the path on this PC when a path mapping covers it, otherwise
    /// the path on the server. Lines end with CR LF, which every Windows app pastes correctly.
    /// </summary>
    public void CopyPaths(Guid serverId, IEnumerable<string> remotePaths)
    {
        ArgumentNullException.ThrowIfNull(remotePaths);
        var paths = remotePaths.Select(remotePath => LocationFor(serverId, remotePath)?.Path ?? remotePath);
        _shell.CopyText(string.Join("\r\n", paths));
    }

    /// <summary>The incomplete folder when the torrent may be in it: unfinished, with the setting on.</summary>
    private static string? IncompleteFolder(Torrent torrent, SessionSettings? session) =>
        !torrent.IsComplete && session?.IncompleteDirEnabled == true && !string.IsNullOrEmpty(session.IncompleteDir)
            ? session.IncompleteDir
            : null;
}

/// <summary>Paths on the server, which may be a Linux daemon ("/data") or a Windows one ("D:\Torrents").</summary>
public static class RemotePath
{
    /// <summary>
    /// Appends a file or folder to a server folder with the server's separator, so the Files tab can build each
    /// file's path once: <c>Join("/data/", "Show/e01.mkv")</c> is "/data/Show/e01.mkv", <c>Join(@"D:\TV",
    /// "Show/e01.mkv")</c> is "D:\TV\Show\e01.mkv". Transmission lists a torrent's files with "/" on every server.
    /// </summary>
    public static string Join(string folder, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(relativePath);
        var child = relativePath.TrimStart('/', '\\');
        if (folder.Length == 0)
        {
            return child;
        }
        if (!IsWindowsPath(folder))
        {
            return folder.TrimEnd('/') + "/" + child;
        }
        return folder.TrimEnd('/', '\\') + "\\" + child.Replace('/', '\\');
    }

    /// <summary>
    /// A drive letter (D:) or a backslash marks a Windows daemon's path, the same rule as the core's path mappings.
    /// A leading "//" doesn't: on Linux that's just "/".
    /// </summary>
    internal static bool IsWindowsPath(string path)
    {
        var trimmed = path.Trim();
        var hasDriveLetter = trimmed.Length >= 2 && char.IsAsciiLetter(trimmed[0]) && trimmed[1] == ':';
        return hasDriveLetter || trimmed.Contains('\\', StringComparison.Ordinal);
    }
}

/// <summary>Turns the core's "/"-joined mapped paths into Windows paths.</summary>
internal static class LocalPaths
{
    /// <summary>
    /// A drive letter (C:) or a UNC path (\\server\share, or //server/share, which Windows accepts too) marks a
    /// local folder as a Windows path.
    /// </summary>
    public static bool IsWindowsPath(string path) =>
        RemotePath.IsWindowsPath(path) || path.Trim().StartsWith("//", StringComparison.Ordinal);

    /// <summary>
    /// Backslashes throughout, no doubled or trailing separators: the core appends subfolders with "/", so
    /// "M:\" and "Show/e01.mkv" arrive as "M:\/Show/e01.mkv" and leave as "M:\Show\e01.mkv".
    /// </summary>
    public static string NormalizedWindowsPath(string path)
    {
        var trimmed = path.Trim();
        var isUnc = trimmed.StartsWith(@"\\", StringComparison.Ordinal)
            || trimmed.StartsWith("//", StringComparison.Ordinal);
        var joined = string.Join('\\', trimmed.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries));
        if (isUnc)
        {
            return @"\\" + joined;
        }
        // "C:" alone means the current folder on drive C, so a drive's root keeps its backslash.
        return joined.Length == 2 && joined[1] == ':' ? joined + "\\" : joined;
    }
}
