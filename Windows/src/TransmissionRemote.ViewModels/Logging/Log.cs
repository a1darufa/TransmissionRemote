using Microsoft.Extensions.Logging;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>Every log message of the app's logic, generated at compile time.</summary>
/// <remarks>
/// Event IDs: 2000s saved data, 2100s the connection, 2200s adding torrents, 2300s other services.
/// The core's own messages use the 1000s. Never log passwords; write paths with
/// <see cref="StorageFiles.DisplayPath"/> so shared logs don't reveal the user's name.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(2000, LogLevel.Error, "Couldn't read the saved servers at {Path}")]
    public static partial void ServersUnreadable(ILogger logger, Exception exception, string path);

    [LoggerMessage(2001, LogLevel.Warning, "A saved server couldn't be read and was skipped")]
    public static partial void ServerEntryUnreadable(ILogger logger, Exception exception);

    [LoggerMessage(2002, LogLevel.Error, "Saved servers: {Reason}. Kept a copy at {CopyPath}")]
    public static partial void KeptUnreadableServers(ILogger logger, string reason, string copyPath);

    [LoggerMessage(2003, LogLevel.Error, "Saved servers: {Reason}, and keeping a copy failed")]
    public static partial void CouldNotKeepUnreadableServers(ILogger logger, Exception exception, string reason);

    [LoggerMessage(2004, LogLevel.Error, "Couldn't save the servers to {Path}")]
    public static partial void ServersNotSaved(ILogger logger, Exception exception, string path);

    [LoggerMessage(2010, LogLevel.Error, "The settings couldn't be read and were reset; see {Path}")]
    public static partial void SettingsUnreadable(ILogger logger, Exception exception, string path);

    [LoggerMessage(2011, LogLevel.Error, "Couldn't save the settings to {Path}")]
    public static partial void SettingsNotSaved(ILogger logger, Exception exception, string path);

    [LoggerMessage(2100, LogLevel.Information, "Connecting to {Server}")]
    public static partial void ConnectingToServer(ILogger logger, string server);

    [LoggerMessage(2101, LogLevel.Information, "Server unreachable ({Kind}: {Message}); retrying in {Delay}")]
    public static partial void ServerUnreachable(
        ILogger logger, TransmissionErrorKind kind, string message, TimeSpan delay);

    [LoggerMessage(2102, LogLevel.Error, "The connection to {Server} failed and stopped: {Message}")]
    public static partial void ConnectionFailed(ILogger logger, Exception exception, string server, string message);

    [LoggerMessage(2103, LogLevel.Warning, "Refreshing failed; the data on screen may be out of date: {Message}")]
    public static partial void RefreshFailed(ILogger logger, Exception exception, string message);

    [LoggerMessage(2104, LogLevel.Warning, "{Action} failed: {Message}")]
    public static partial void ActionFailed(ILogger logger, Exception exception, string action, string message);

    [LoggerMessage(2105, LogLevel.Warning, "The connection to {Server} is blocked: plain HTTP over the internet")]
    public static partial void BlockedByTransportPolicy(ILogger logger, string server);

    [LoggerMessage(2200, LogLevel.Warning, "Couldn't open {FileName} as a torrent file")]
    public static partial void TorrentFileUnreadable(ILogger logger, Exception exception, string fileName);

    [LoggerMessage(2201, LogLevel.Warning, "Couldn't add {Name}")]
    public static partial void AddFailed(ILogger logger, Exception exception, string name);

    [LoggerMessage(2300, LogLevel.Warning, "The update check failed")]
    public static partial void UpdateCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(2301, LogLevel.Error, "The country database couldn't be downloaded")]
    public static partial void CountryDatabaseDownloadFailed(ILogger logger, Exception exception);

    [LoggerMessage(2302, LogLevel.Warning, "The country database couldn't be loaded from {Path}")]
    public static partial void CountryDatabaseUnreadable(ILogger logger, Exception exception, string path);

    [LoggerMessage(2303, LogLevel.Warning, "Couldn't open {Path} on this PC")]
    public static partial void LocalFileNotOpened(ILogger logger, string path);

    [LoggerMessage(2304, LogLevel.Information,
        "Imported {ImportedCount} servers from {Path}; {SkippedCount} were already set up")]
    public static partial void TransguiImported(ILogger logger, int importedCount, int skippedCount, string path);

    [LoggerMessage(2305, LogLevel.Warning, "Couldn't read the Transmission Remote GUI settings at {Path}")]
    public static partial void TransguiFileUnreadable(ILogger logger, Exception exception, string path);

    [LoggerMessage(2306, LogLevel.Information,
        "Checked for updates: the newest release for this PC is {Newest}; this app is {CurrentVersion}")]
    public static partial void UpdateChecked(ILogger logger, string newest, string currentVersion);

    [LoggerMessage(2307, LogLevel.Information,
        "Downloaded the country database for {Month}: {Size} bytes in {Duration}")]
    public static partial void CountryDatabaseDownloaded(ILogger logger, string month, int size, TimeSpan duration);
}
