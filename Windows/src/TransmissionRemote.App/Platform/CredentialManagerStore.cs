using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TransmissionRemote.App.Logging;
using TransmissionRemote.ViewModels;
using Windows.Security.Credentials;

namespace TransmissionRemote.App.Platform;

/// <summary>
/// Server and proxy passwords in Windows Credential Manager, one entry per server and kind, named
/// "TransmissionRemote:{server id}:{kind}". The entry's user name is the server's name, so the entry is
/// recognizable in Control Panel; lookups go by the entry name alone, so renaming a server loses nothing.
/// </summary>
internal sealed class CredentialManagerStore(ILogger<CredentialManagerStore> logger) : ICredentialStore
{
    /// <summary>What <see cref="PasswordVault"/> throws for an entry that doesn't exist (ERROR_NOT_FOUND).</summary>
    private const int NotFound = unchecked((int)0x80070490);

    private readonly PasswordVault _vault = new();

    public string? GetPassword(Guid serverId, CredentialKind kind)
    {
        try
        {
            var credential = Find(Resource(serverId, kind));
            credential?.RetrievePassword();
            return credential?.Password;
        }
        catch (COMException error)
        {
            Log.CredentialNotRead(logger, error, kind, serverId);
            return null;
        }
    }

    public void SetPassword(Guid serverId, CredentialKind kind, string password, string label)
    {
        ArgumentNullException.ThrowIfNull(password);
        var resource = Resource(serverId, kind);
        try
        {
            Remove(resource);
            // Credential Manager can't keep an empty password: no password means no entry.
            if (password.Length > 0)
            {
                var userName = string.IsNullOrWhiteSpace(label) ? Strings.TransmissionRemote : label;
                _vault.Add(new PasswordCredential(resource, userName, password));
            }
        }
        catch (COMException error)
        {
            Log.CredentialNotSaved(logger, error, kind, serverId);
        }
    }

    public void DeletePassword(Guid serverId, CredentialKind kind)
    {
        try
        {
            Remove(Resource(serverId, kind));
        }
        catch (COMException error)
        {
            Log.CredentialNotDeleted(logger, error, kind, serverId);
        }
    }

    private static string Resource(Guid serverId, CredentialKind kind) => $"TransmissionRemote:{serverId:D}:{kind}";

    private PasswordCredential? Find(string resource)
    {
        try
        {
            var credentials = _vault.FindAllByResource(resource);
            return credentials.Count > 0 ? credentials[0] : null;
        }
        catch (COMException error) when (error.HResult == NotFound)
        {
            return null;
        }
    }

    private void Remove(string resource)
    {
        while (Find(resource) is { } credential)
        {
            _vault.Remove(credential);
        }
    }
}
