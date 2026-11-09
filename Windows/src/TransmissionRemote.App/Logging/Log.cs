using Microsoft.Extensions.Logging;

namespace TransmissionRemote.App.Logging;

/// <summary>Every log message of the WinUI app, generated at compile time.</summary>
/// <remarks>
/// Event IDs: 3000s startup and shutdown. The core uses the 1000s and the view models the 2000s. Never log
/// passwords; write paths with <see cref="ViewModels.LogPath.Display"/> so shared logs don't reveal the user's
/// name.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(3000, LogLevel.Information,
        "Transmission Remote {Version} started on {Windows}, {Architecture}, {DotNet}; data folder {DataFolder}")]
    public static partial void Started(
        ILogger logger, string version, string windows, string architecture, string dotNet, string dataFolder);

    [LoggerMessage(3001, LogLevel.Information, "Transmission Remote is exiting")]
    public static partial void Exiting(ILogger logger);

    [LoggerMessage(3010, LogLevel.Critical, "An error nothing caught on the UI thread; the app exits")]
    public static partial void UnhandledUiError(ILogger logger, Exception exception);

    [LoggerMessage(3011, LogLevel.Critical, "An error nothing caught on a background thread; the app exits")]
    public static partial void UnhandledBackgroundError(ILogger logger, Exception? exception);

    [LoggerMessage(3012, LogLevel.Critical, "A background task failed and nothing observed its error")]
    public static partial void UnobservedTaskError(ILogger logger, Exception exception);

    [LoggerMessage(3013, LogLevel.Error, "Couldn't open the logs folder {Path}")]
    public static partial void LogsFolderNotOpened(ILogger logger, Exception exception, string path);
}
