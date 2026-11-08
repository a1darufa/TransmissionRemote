using System.Net;
using Microsoft.Extensions.Logging;

namespace TransmissionKit.Tests;

[TestClass]
public sealed class ClientTests
{
    private const string CsrfToken = "token-123";

    private const string Transmission411 =
        """{"result":"success","arguments":{"version":"4.1.1","rpc-version":19,"rpc-version-semver":"6.0.1"}}""";

    private const string Transmission300 =
        """{"result":"success","arguments":{"version":"3.00 (bb6b5a062e)","rpc-version":16}}""";

    private const string EmptySuccess = """{"result":"success","arguments":{}}""";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task UpgradesToJsonRpcOnTransmission41()
    {
        var network = SimulateDaemon(Transmission411, otherwise: """{"jsonrpc":"2.0","id":2,"result":{}}""");
        using var client = MakeClient(network);

        var info = await client.ConnectAsync(TestContext.CancellationToken);
        await client.PerformAsync(TorrentAction.Start, [1], TestContext.CancellationToken);

        Assert.AreEqual("4.1.1", info.Version);
        Assert.AreEqual(RpcDialect.JsonRpc, client.Dialect);
        Assert.HasCount(3, network.Requests, "409 handshake, session-get, torrent_start");
        var last = network.Requests[^1];
        Assert.AreEqual("Basic dXNlcjpzZWNyZXQ=", last.Authorization);
        Assert.AreEqual(CsrfToken, last.SessionId);
        Assert.Contains("\"method\":\"torrent_start\"", last.Body);
        Assert.Contains("\"jsonrpc\":\"2.0\"", last.Body);
    }

    [TestMethod]
    public async Task AsksForTheVersionInTheLegacySpelling()
    {
        var network = SimulateDaemon(Transmission411);
        using var client = MakeClient(network);

        await client.ConnectAsync(TestContext.CancellationToken);

        var sessionGet = network.Requests[^1].Body;
        Assert.Contains("\"method\":\"session-get\"", sessionGet);
        Assert.Contains("\"fields\":[\"version\",\"rpc-version\",\"rpc-version-semver\"]", sessionGet);
    }

    [TestMethod]
    public async Task StaysOnLegacyForTransmission300()
    {
        var network = SimulateDaemon(Transmission300);
        using var client = MakeClient(network);

        await client.ConnectAsync(TestContext.CancellationToken);
        await client.RemoveAsync([4], deletingData: true, TestContext.CancellationToken);

        Assert.AreEqual(RpcDialect.Legacy, client.Dialect);
        var body = network.Requests[^1].Body;
        Assert.Contains("\"method\":\"torrent-remove\"", body);
        Assert.Contains("\"delete-local-data\":true", body);
    }

    [TestMethod]
    public async Task SkipsFieldsTheServerIsTooOldToKnow()
    {
        var network = SimulateDaemon(Transmission300, otherwise: """{"result":"success","arguments":{"torrents":[]}}""");
        using var client = MakeClient(network);

        await client.ConnectAsync(TestContext.CancellationToken);
        var torrents = await client.GetAllTorrentsAsync(TestContext.CancellationToken);

        Assert.IsEmpty(torrents);
        var body = network.Requests[^1].Body;
        Assert.Contains("\"labels\"", body, "Transmission 3.00 knows labels");
        Assert.DoesNotContain("\"group\"", body, "groups need Transmission 4.0");
    }

    [TestMethod]
    public async Task LegacyPreferenceIsHonoredOnNewServers()
    {
        using var client = MakeClient(SimulateDaemon(Transmission411), ProtocolPreference.Legacy);

        await client.ConnectAsync(TestContext.CancellationToken);

        Assert.AreEqual(RpcDialect.Legacy, client.Dialect);
    }

    [TestMethod]
    public async Task RejectsServersOlderThan240()
    {
        var network = SimulateDaemon("""{"result":"success","arguments":{"version":"2.22","rpc-version":13}}""");
        using var client = MakeClient(network);

        var error = await Assert.ThrowsExactlyAsync<TransmissionException>(
            () => client.ConnectAsync(TestContext.CancellationToken));

        Assert.AreEqual(TransmissionErrorKind.UnsupportedServer, error.Kind);
        Assert.AreEqual("2.22", error.Detail);
    }

    [TestMethod]
    [DataRow(401, TransmissionErrorKind.Unauthorized)]
    [DataRow(403, TransmissionErrorKind.IpNotAllowed)]
    [DataRow(407, TransmissionErrorKind.ProxyUnauthorized)]
    [DataRow(421, TransmissionErrorKind.HostNotAllowed)]
    [DataRow(302, TransmissionErrorKind.RedirectRefused)]
    [DataRow(500, TransmissionErrorKind.HttpStatus)]
    public async Task MapsHttpFailuresToActionableErrors(int status, TransmissionErrorKind expected)
    {
        using var client = MakeClient(new FakeHttpHandler(_ => FakeHttpHandler.Reply((HttpStatusCode)status)));

        var error = await Assert.ThrowsExactlyAsync<TransmissionException>(
            () => client.ConnectAsync(TestContext.CancellationToken));

        Assert.AreEqual(expected, error.Kind);
    }

    [TestMethod]
    public async Task KeepsTheServersMessageWhenTheIpAddressIsRefused()
    {
        var network = new FakeHttpHandler(_ =>
            FakeHttpHandler.Reply(HttpStatusCode.Forbidden, "<h1>403: Forbidden</h1><p>Unauthorized IP Address.</p>"));
        using var client = MakeClient(network);

        var error = await Assert.ThrowsExactlyAsync<TransmissionException>(
            () => client.ConnectAsync(TestContext.CancellationToken));

        Assert.Contains("Unauthorized IP Address", error.Detail!);
    }

    [TestMethod]
    public async Task ExplainsFailedHttpsHandshakes()
    {
        using var client = MakeClient(FailingNetwork(HttpRequestError.SecureConnectionError), usesTls: true);

        var error = await Assert.ThrowsExactlyAsync<TransmissionException>(
            () => client.ConnectAsync(TestContext.CancellationToken));

        Assert.AreEqual(TransmissionErrorKind.HttpsHandshakeFailed, error.Kind);
        Assert.IsInstanceOfType<HttpRequestException>(error.InnerException, "the original error is kept for the log");
    }

    [TestMethod]
    public async Task LeavesPlainHttpFailuresUnchanged()
    {
        using var client = MakeClient(FailingNetwork(HttpRequestError.SecureConnectionError), usesTls: false);

        var error = await Assert.ThrowsExactlyAsync<TransmissionException>(
            () => client.ConnectAsync(TestContext.CancellationToken));

        Assert.AreEqual(TransmissionErrorKind.Network, error.Kind);
        Assert.AreEqual(NetworkProblem.Other, error.NetworkProblem);
    }

    [TestMethod]
    [DataRow(HttpRequestError.NameResolutionError, NetworkProblem.NameNotResolved)]
    [DataRow(HttpRequestError.ConnectionError, NetworkProblem.CannotConnect)]
    [DataRow(HttpRequestError.ProxyTunnelError, NetworkProblem.ProxyFailed)]
    public async Task NamesTheNetworkProblem(HttpRequestError failure, NetworkProblem expected)
    {
        using var client = MakeClient(FailingNetwork(failure));

        var error = await Assert.ThrowsExactlyAsync<TransmissionException>(
            () => client.ConnectAsync(TestContext.CancellationToken));

        Assert.AreEqual(expected, error.NetworkProblem);
        Assert.IsTrue(error.IsTransient);
    }

    [TestMethod]
    public async Task SurfacesRpcFailureMessages()
    {
        var network = SimulateDaemon(
            """{"result":"success","arguments":{"version":"3.00","rpc-version":16}}""",
            otherwise: """{"result":"No such file or directory","arguments":{}}""");
        using var client = MakeClient(network);
        await client.ConnectAsync(TestContext.CancellationToken);

        var error = await Assert.ThrowsExactlyAsync<TransmissionException>(
            () => client.SetLocationAsync([1], "/missing", movingData: true, TestContext.CancellationToken));

        Assert.AreEqual(TransmissionErrorKind.RpcFailure, error.Kind);
        Assert.AreEqual("No such file or directory", error.Message);
    }

    [TestMethod]
    public async Task RetriesOnceWhenAKeptAliveConnectionWasDropped()
    {
        var attempts = 0;
        var network = new FakeHttpHandler(request =>
        {
            // The proxy closed the idle connection just as the first request went out.
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new HttpRequestException(HttpRequestError.ResponseEnded, "The response ended prematurely.");
            }
            return request.SessionId == CsrfToken
                ? FakeHttpHandler.Reply(HttpStatusCode.OK, Transmission300)
                : FakeHttpHandler.Reply(HttpStatusCode.Conflict, sessionId: CsrfToken);
        });
        using var client = MakeClient(network);

        var info = await client.ConnectAsync(TestContext.CancellationToken);

        Assert.StartsWith("3.00", info.Version);
    }

    [TestMethod]
    public async Task ExplainsAWebPageInsteadOfTheRpc()
    {
        var network = new FakeHttpHandler(_ =>
            FakeHttpHandler.Reply(HttpStatusCode.OK, "\n<!DOCTYPE html><html><body>My NAS</body></html>"));
        using var client = MakeClient(network);

        var error = await Assert.ThrowsExactlyAsync<TransmissionException>(
            () => client.ConnectAsync(TestContext.CancellationToken));

        Assert.AreEqual(TransmissionErrorKind.NotTransmissionRpc, error.Kind);
    }

    [TestMethod]
    public async Task TimesOutWhenTheServerDoesNotAnswer()
    {
        var network = new FakeHttpHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return FakeHttpHandler.Reply(HttpStatusCode.OK);
        });
        var options = Options() with { Timeout = TimeSpan.FromMilliseconds(100) };
        using var client = new TransmissionClient(options, network, logger: null);

        var error = await Assert.ThrowsExactlyAsync<TransmissionException>(
            () => client.ConnectAsync(TestContext.CancellationToken));

        Assert.AreEqual(NetworkProblem.Timeout, error.NetworkProblem);
    }

    [TestMethod]
    public async Task CancellingIsNotReportedAsAnError()
    {
        var network = new FakeHttpHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return FakeHttpHandler.Reply(HttpStatusCode.OK);
        });
        using var client = MakeClient(network);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.ConnectAsync(cancellation.Token));
    }

    [TestMethod]
    public void RejectsAnInvalidAddressBeforeConnecting()
    {
        var options = new TransmissionClientOptions(new ServerEndpoint("http://nas.local"));

        var error = Assert.ThrowsExactly<TransmissionException>(() => new TransmissionClient(options));

        Assert.AreEqual(TransmissionErrorKind.InvalidEndpoint, error.Kind);
    }

    /// <summary>A daemon that demands a CSRF token, then answers session-get with <paramref name="sessionInfo"/>.</summary>
    internal static FakeHttpHandler SimulateDaemon(string sessionInfo, string otherwise = EmptySuccess) =>
        new(request =>
        {
            if (request.SessionId != CsrfToken)
            {
                return FakeHttpHandler.Reply(HttpStatusCode.Conflict, sessionId: CsrfToken);
            }
            var isSessionGet = request.Body.Contains("session-get", StringComparison.Ordinal);
            return FakeHttpHandler.Reply(HttpStatusCode.OK, isSessionGet ? sessionInfo : otherwise);
        });

    internal static TransmissionClientOptions Options(
        ProtocolPreference preference = ProtocolPreference.Automatic,
        bool usesTls = false) =>
        new(new ServerEndpoint("100.64.12.34", UsesTls: usesTls))
        {
            Credentials = new Credentials("user", "secret"),
            ProtocolPreference = preference,
        };

    internal static TransmissionClient MakeClient(
        FakeHttpHandler network,
        ProtocolPreference preference = ProtocolPreference.Automatic,
        bool usesTls = false,
        ILogger? logger = null) =>
        new(Options(preference, usesTls), network, logger);

    private static FakeHttpHandler FailingNetwork(HttpRequestError failure) =>
        new(_ => throw new HttpRequestException(failure, $"Simulated {failure}."));
}
