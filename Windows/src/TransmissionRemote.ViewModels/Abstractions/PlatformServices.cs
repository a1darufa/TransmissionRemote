using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>Which secret of a server is meant.</summary>
public enum CredentialKind
{
    /// <summary>The daemon's RPC password.</summary>
    Server,

    /// <summary>The password for the server's proxy.</summary>
    Proxy,
}

/// <summary>
/// Secrets kept by the operating system, never in the app's JSON files. The Windows app stores them in
/// Credential Manager (<c>Windows.Security.Credentials.PasswordVault</c>).
/// </summary>
public interface ICredentialStore
{
    string? GetPassword(Guid serverId, CredentialKind kind);

    /// <param name="label">A readable name for the entry, such as the server's name.</param>
    void SetPassword(Guid serverId, CredentialKind kind, string password, string label);

    void DeletePassword(Guid serverId, CredentialKind kind);
}

/// <summary>
/// Client certificates for HTTPS reverse proxies that ask for one. The Windows app imports them into the
/// user's certificate store and finds them by the thumbprint saved in <see cref="ServerProfile"/>.
/// </summary>
public interface IClientCertificateProvider
{
    /// <summary>The certificate with its private key, or null when the profile has none or it's gone.</summary>
    ClientIdentity? IdentityFor(ServerProfile profile);
}

/// <summary>Toast notifications (<c>AppNotificationManager</c> in the Windows app).</summary>
public interface INotificationService
{
    void ShowDownloadComplete(Torrent torrent);
}

/// <summary>Opening and revealing local files, and the clipboard.</summary>
public interface IShell
{
    /// <summary>Opens a file or folder with its default app. Returns false when that failed.</summary>
    bool Open(string localPath);

    /// <summary>Shows the items selected in File Explorer.</summary>
    void Reveal(IReadOnlyList<string> localPaths);

    void CopyText(string text);
}

/// <summary>Where the app keeps its files, and what it is.</summary>
/// <param name="DataDirectory">For example <c>%LOCALAPPDATA%\TransmissionRemote</c>.</param>
/// <param name="AppVersion">The app's version, such as "1.0".</param>
public sealed record AppEnvironment(string DataDirectory, string AppVersion);
