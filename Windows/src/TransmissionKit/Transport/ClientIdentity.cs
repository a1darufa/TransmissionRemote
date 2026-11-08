using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TransmissionKit;

/// <summary>
/// A client certificate and its private key, for HTTPS reverse proxies that ask for one (mutual
/// TLS), like Transmission Remote GUI's "Client Certificate" option. Imported from a PKCS #12
/// (.pfx or .p12) file, the format Windows' certificate export and <c>openssl pkcs12 -export</c> write.
/// </summary>
public sealed class ClientIdentity : IDisposable
{
    private readonly X509Certificate2Collection _certificates;

    private ClientIdentity(X509Certificate2 leaf, X509Certificate2Collection certificates)
    {
        _certificates = certificates;
        Summary = leaf.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        ExpiryDate = new DateTimeOffset(leaf.NotAfter.ToUniversalTime(), TimeSpan.Zero);
        var intermediates = new X509Certificate2Collection();
        foreach (var certificate in certificates)
        {
            if (!ReferenceEquals(certificate, leaf))
            {
                intermediates.Add(certificate);
            }
        }
        CertificateContext = SslStreamCertificateContext.Create(leaf, intermediates, offline: true);
    }

    /// <summary>The certificate's subject, usually its common name, to show which one is chosen.</summary>
    public string Summary { get; }

    public DateTimeOffset ExpiryDate { get; }

    /// <summary>What to answer a server's client certificate request with, intermediates included.</summary>
    internal SslStreamCertificateContext CertificateContext { get; }

    /// <summary>Loads a PKCS #12 file into memory; the key is never added to a certificate store.</summary>
    /// <exception cref="ClientIdentityImportException">
    /// The file is unreadable, the password is wrong or there's no private key.
    /// </exception>
    public static ClientIdentity FromPkcs12(byte[] data, string password)
    {
        ArgumentNullException.ThrowIfNull(data);
        // PKCS #12 is a DER SEQUENCE; anything else is not a certificate file at all.
        if (data.Length == 0 || data[0] != 0x30)
        {
            throw new ClientIdentityImportException(ClientIdentityImportError.Unreadable);
        }

        X509Certificate2Collection certificates;
        try
        {
            certificates = X509CertificateLoader.LoadPkcs12Collection(data, password, KeyStorageFlags);
        }
        catch (CryptographicException error)
        {
            // .NET reports a wrong password and a damaged file the same way; a file that starts
            // like PKCS #12 is far more often a password problem.
            throw new ClientIdentityImportException(ClientIdentityImportError.WrongPassword, error);
        }

        var leaf = certificates.FirstOrDefault(certificate => certificate.HasPrivateKey);
        if (leaf is null)
        {
            foreach (var certificate in certificates)
            {
                certificate.Dispose();
            }
            throw new ClientIdentityImportException(ClientIdentityImportError.NoPrivateKey);
        }
        return new ClientIdentity(leaf, certificates);
    }

    /// <summary>
    /// Ephemeral keys stay in memory on Windows and Linux. macOS doesn't support them, but there
    /// the library only runs in tests.
    /// </summary>
    private static X509KeyStorageFlags KeyStorageFlags =>
        OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;

    public void Dispose()
    {
        foreach (var certificate in _certificates)
        {
            certificate.Dispose();
        }
    }
}

public enum ClientIdentityImportError
{
    WrongPassword,
    Unreadable,
    NoPrivateKey,
}

public sealed class ClientIdentityImportException : Exception
{
    public ClientIdentityImportException()
        : this(ClientIdentityImportError.Unreadable)
    {
    }

    public ClientIdentityImportException(string message)
        : base(message)
    {
    }

    public ClientIdentityImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ClientIdentityImportException(ClientIdentityImportError error, Exception? innerException = null)
        : base(DescribeError(error), innerException)
    {
        Error = error;
    }

    public ClientIdentityImportError Error { get; }

    private static string DescribeError(ClientIdentityImportError error) => error switch
    {
        ClientIdentityImportError.WrongPassword => "The certificate file's password is wrong.",
        ClientIdentityImportError.NoPrivateKey => "The certificate file has no private key.",
        _ => "The file isn't a PKCS #12 certificate file.",
    };
}

/// <summary>SHA-256 fingerprints of TLS certificates, for pinning a self-signed reverse proxy.</summary>
public static class CertificateFingerprint
{
    /// <summary>SHA-256 of the certificate's DER bytes, formatted like <c>AB:CD:…</c>.</summary>
    public static string Sha256(X509Certificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return Normalize(Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())));
    }

    /// <summary>Accepts fingerprints with or without colons, in any letter case.</summary>
    public static string Normalize(string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        var hex = new string(fingerprint.Where(char.IsAsciiHexDigit).Select(char.ToUpperInvariant).ToArray());
        return string.Join(':', hex.Chunk(2).Select(pair => new string(pair)));
    }
}
