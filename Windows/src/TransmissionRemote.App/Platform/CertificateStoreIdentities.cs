using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using TransmissionKit;
using TransmissionRemote.App.Logging;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Platform;

/// <summary>
/// Client certificates for servers behind a reverse proxy that asks for one. They live in the user's
/// certificate store (Personal), found by the thumbprint saved with the server.
/// </summary>
internal sealed class CertificateStoreIdentities(ILogger<CertificateStoreIdentities> logger) : IClientCertificateProvider
{
    public ClientIdentity? IdentityFor(ServerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.ClientCertificateThumbprint))
        {
            return null;
        }
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            var matches = store.Certificates.Find(
                X509FindType.FindByThumbprint, profile.ClientCertificateThumbprint, validOnly: false);
            var certificate = matches.FirstOrDefault(match => match.HasPrivateKey);
            foreach (var other in matches.Where(match => !ReferenceEquals(match, certificate)))
            {
                other.Dispose();
            }
            if (certificate is null)
            {
                Log.ClientCertificateMissing(logger, profile.DisplayName);
                return null;
            }
            return ClientIdentity.FromCertificate(certificate);
        }
        catch (CryptographicException error)
        {
            Log.ClientCertificateNotRead(logger, error, profile.DisplayName);
            return null;
        }
    }
}
