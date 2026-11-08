using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TransmissionKit.Tests;

[TestClass]
public sealed class EndpointSecurityTests
{
    [TestMethod]
    public void BuildsTheDefaultUrl() =>
        Assert.AreEqual(
            "http://100.64.12.34:9091/transmission/rpc",
            new ServerEndpoint("100.64.12.34").ToUri().AbsoluteUri);

    [TestMethod]
    public void BracketsIPv6Hosts() =>
        Assert.AreEqual(
            "https://[fd7a:115c:a1e0::1]/transmission/rpc",
            new ServerEndpoint("fd7a:115c:a1e0::1", Port: 443, UsesTls: true).ToUri().AbsoluteUri);

    [TestMethod]
    public void AcceptsBracketedAndPaddedHosts() =>
        Assert.AreEqual("http://[::1]:9091/transmission/rpc", new ServerEndpoint(" [::1] ").ToUri().AbsoluteUri);

    [TestMethod]
    [DataRow("", 9091, "/transmission/rpc")]
    [DataRow("http://nas.local", 9091, "/transmission/rpc")]
    [DataRow("nas local", 9091, "/transmission/rpc")]
    [DataRow("user@nas", 9091, "/transmission/rpc")]
    [DataRow("nas", 0, "/transmission/rpc")]
    [DataRow("nas", 70_000, "/transmission/rpc")]
    [DataRow("nas", 9091, "transmission/rpc")]
    public void RejectsInvalidEndpoints(string host, int port, string rpcPath)
    {
        var error = Assert.ThrowsExactly<TransmissionException>(() => new ServerEndpoint(host, port, rpcPath).ToUri());

        Assert.AreEqual(TransmissionErrorKind.InvalidEndpoint, error.Kind);
    }

    [TestMethod]
    [DataRow("100.64.12.34", TransportSecurity.Tailscale)]
    [DataRow("fd7a:115c:a1e0::53", TransportSecurity.Tailscale)]
    [DataRow("nas.tail1234.ts.net", TransportSecurity.Tailscale)]
    [DataRow("127.0.0.1", TransportSecurity.Loopback)]
    [DataRow("::1", TransportSecurity.Loopback)]
    [DataRow("localhost", TransportSecurity.Loopback)]
    [DataRow("192.168.1.20", TransportSecurity.LocalNetwork)]
    [DataRow("10.0.0.5", TransportSecurity.LocalNetwork)]
    [DataRow("172.20.1.1", TransportSecurity.LocalNetwork)]
    [DataRow("fe80::1", TransportSecurity.LocalNetwork)]
    [DataRow("mycloud.local", TransportSecurity.LocalNetwork)]
    [DataRow("mycloud", TransportSecurity.LocalNetwork)]
    [DataRow("1234", TransportSecurity.LocalNetwork)]
    [DataRow("8.8.8.8", TransportSecurity.UnencryptedInternet)]
    [DataRow("172.32.0.1", TransportSecurity.UnencryptedInternet)]
    [DataRow("100.128.0.1", TransportSecurity.UnencryptedInternet)]
    [DataRow("seedbox.example.com", TransportSecurity.UnencryptedInternet)]
    [DataRow("2001:db8::1", TransportSecurity.UnencryptedInternet)]
    public void ClassifiesPlainHttpHosts(string host, TransportSecurity expected) =>
        Assert.AreEqual(expected, new ServerEndpoint(host).TransportSecurity);

    [TestMethod]
    public void TlsIsAlwaysProtected()
    {
        var endpoint = new ServerEndpoint("seedbox.example.com", 443, UsesTls: true);

        Assert.AreEqual(TransportSecurity.Tls, endpoint.TransportSecurity);
        Assert.IsTrue(endpoint.TransportSecurity.IsProtected());
    }

    [TestMethod]
    public void NormalizesFingerprints() =>
        Assert.AreEqual("AB:CD:EF:01", CertificateFingerprint.Normalize("ab cd:EF01"));

    [TestMethod]
    public void BuildsTheBasicAuthorizationParameter() =>
        Assert.AreEqual("dXNlcjpwYTpzcw==", new Credentials("user", "pa:ss").BasicAuthorizationParameter);

    [TestMethod]
    public void CredentialsHideThePasswordWhenPrinted() =>
        Assert.DoesNotContain("pa:ss", new Credentials("user", "pa:ss").ToString());
}

[TestClass]
public sealed class RetryTests
{
    [TestMethod]
    [DataRow(0, 2)]
    [DataRow(1, 2)]
    [DataRow(2, 5)]
    [DataRow(3, 10)]
    [DataRow(6, 60)]
    [DataRow(500, 60)]
    public void BacksOffToAMinute(int failures, int expectedSeconds) =>
        Assert.AreEqual(TimeSpan.FromSeconds(expectedSeconds), RetryBackoff.DelayAfterFailures(failures));

    [TestMethod]
    public void RetriesOnlyTransientFailures()
    {
        Assert.IsTrue(TransmissionException.Network(NetworkProblem.Timeout, "timed out").IsTransient);
        Assert.IsTrue(TransmissionException.HttpStatusCode(503).IsTransient);
        Assert.IsFalse(TransmissionException.HttpStatusCode(500).IsTransient);
        Assert.IsFalse(TransmissionException.Unauthorized().IsTransient);
        Assert.IsFalse(TransmissionException.HttpsHandshakeFailed(new HttpRequestException()).IsTransient);
        Assert.IsFalse(TransmissionException.InvalidResponse("not JSON").IsTransient);
    }

    [TestMethod]
    public void StopsPollingOnlyForErrorsTheUserMustFix()
    {
        Assert.IsTrue(TransmissionException.Unauthorized().RequiresUserAction);
        Assert.IsTrue(TransmissionException.NotTransmissionRpc().RequiresUserAction);
        Assert.IsFalse(TransmissionException.Network(NetworkProblem.ConnectionLost, "lost").RequiresUserAction);
        Assert.IsFalse(TransmissionException.RpcFailure("No such file").RequiresUserAction);
    }
}

[TestClass]
public sealed class ProxyConfigurationTests
{
    [TestMethod]
    public void HttpProxyCarriesItsLogin()
    {
        var proxy = new ProxyConfiguration(ProxyKind.Http, "proxy.lan", 3128, new Credentials("me", "secret"))
            .CreateWebProxy();

        Assert.AreEqual(new Uri("http://proxy.lan:3128"), proxy.Address);
        Assert.IsFalse(proxy.BypassProxyOnLocal);
        var login = proxy.Credentials as NetworkCredential;
        Assert.AreEqual("me", login?.UserName);
        Assert.AreEqual("secret", login?.Password);
    }

    [TestMethod]
    public void SocksProxyUsesVersion5()
    {
        var proxy = new ProxyConfiguration(ProxyKind.Socks5, "10.0.0.2", 1080).CreateWebProxy();

        Assert.AreEqual("socks5", proxy.Address?.Scheme);
        Assert.AreEqual(1080, proxy.Address?.Port);
        Assert.IsNull(proxy.Credentials);
    }

    [TestMethod]
    public void BracketsIPv6ProxyHosts() =>
        Assert.AreEqual(
            "[fd00::2]",
            new ProxyConfiguration(ProxyKind.Http, "fd00::2", 3128).CreateWebProxy().Address?.Host);
}

[TestClass]
public sealed class ClientIdentityTests
{
    private const string Password = "test-only-password";

    [TestMethod]
    public void ImportsACertificateWithItsKey()
    {
        using var identity = ClientIdentity.FromPkcs12(MakePkcs12(includeKey: true), Password);

        Assert.AreEqual("TransmissionRemote Test", identity.Summary);
        Assert.IsGreaterThan(DateTimeOffset.UtcNow, identity.ExpiryDate);
    }

    [TestMethod]
    public void ReportsAWrongPassword()
    {
        var error = Assert.ThrowsExactly<ClientIdentityImportException>(
            () => ClientIdentity.FromPkcs12(MakePkcs12(includeKey: true), "wrong"));

        Assert.AreEqual(ClientIdentityImportError.WrongPassword, error.Error);
    }

    [TestMethod]
    public void ReportsAFileWithoutAKey()
    {
        var error = Assert.ThrowsExactly<ClientIdentityImportException>(
            () => ClientIdentity.FromPkcs12(MakePkcs12(includeKey: false), Password));

        Assert.AreEqual(ClientIdentityImportError.NoPrivateKey, error.Error);
    }

    [TestMethod]
    public void ReportsAFileThatIsNotACertificate()
    {
        var error = Assert.ThrowsExactly<ClientIdentityImportException>(
            () => ClientIdentity.FromPkcs12("not a certificate"u8.ToArray(), Password));

        Assert.AreEqual(ClientIdentityImportError.Unreadable, error.Error);
    }

    [TestMethod]
    public void FingerprintsAreSha256OfTheCertificate()
    {
        using var certificate = MakeCertificate();

        var fingerprint = CertificateFingerprint.Sha256(certificate);

        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(certificate.RawData)), fingerprint.Replace(":", ""));
        Assert.AreEqual(95, fingerprint.Length, "32 bytes as AB:CD:… pairs");
    }

    private static X509Certificate2 MakeCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=TransmissionRemote Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    private static byte[] MakePkcs12(bool includeKey)
    {
        using var certificate = MakeCertificate();
        if (includeKey)
        {
            return certificate.Export(X509ContentType.Pkcs12, Password);
        }
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return publicOnly.Export(X509ContentType.Pkcs12, Password);
    }
}
