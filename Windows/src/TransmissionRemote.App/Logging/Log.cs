using Microsoft.Extensions.Logging;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Logging;

/// <summary>Every log message of the WinUI app, generated at compile time.</summary>
/// <remarks>
/// Event IDs: 3000s startup and shutdown, 3030s secrets and certificates. The core uses the 1000s and the view models the 2000s. Never log
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

    [LoggerMessage(3020, LogLevel.Information,
        "Handed this launch ({Kind}) to the copy already running (process {ProcessId}) and exiting")]
    public static partial void LaunchHandedOver(
        ILogger logger, Microsoft.Windows.AppLifecycle.ExtendedActivationKind kind, uint processId);

    [LoggerMessage(3021, LogLevel.Warning,
        "Couldn't hand this launch to the copy already running (process {ProcessId}); starting a second copy")]
    public static partial void LaunchNotHandedOver(ILogger logger, Exception exception, uint processId);

    [LoggerMessage(3022, LogLevel.Information, "Another launch ({Kind}) was handed to this copy")]
    public static partial void LaunchReceived(ILogger logger, Microsoft.Windows.AppLifecycle.ExtendedActivationKind kind);

    [LoggerMessage(3030, LogLevel.Error, "Couldn't read the {Kind} password of server {ServerId} from Credential Manager")]
    public static partial void CredentialNotRead(ILogger logger, Exception exception, CredentialKind kind, Guid serverId);

    [LoggerMessage(3031, LogLevel.Error, "Couldn't save the {Kind} password of server {ServerId} in Credential Manager")]
    public static partial void CredentialNotSaved(ILogger logger, Exception exception, CredentialKind kind, Guid serverId);

    [LoggerMessage(3032, LogLevel.Error, "Couldn't delete the {Kind} password of server {ServerId} from Credential Manager")]
    public static partial void CredentialNotDeleted(ILogger logger, Exception exception, CredentialKind kind, Guid serverId);

    [LoggerMessage(3033, LogLevel.Warning,
        "The client certificate of {Server} isn't in the certificate store, or has no private key; connecting without it")]
    public static partial void ClientCertificateMissing(ILogger logger, string server);

    [LoggerMessage(3034, LogLevel.Error, "Couldn't read the client certificate of {Server}; connecting without it")]
    public static partial void ClientCertificateNotRead(ILogger logger, Exception exception, string server);
}
