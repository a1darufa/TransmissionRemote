using System.Net;
using Microsoft.Extensions.Logging;

namespace TransmissionKit.Tests;

/// <summary>Logs must say which call failed and why, without ever containing the password.</summary>
[TestClass]
public sealed class LoggingTests
{
    private const string Transmission411 =
        """{"result":"success","arguments":{"version":"4.1.1","rpc-version":19,"rpc-version-semver":"6.0.1"}}""";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task LogsTheServerVersionAndProtocolOnConnect()
    {
        var logger = new CapturingLogger();
        using var client = ClientTests.MakeClient(ClientTests.SimulateDaemon(Transmission411), logger: logger);

        await client.ConnectAsync(TestContext.CancellationToken);

        var connected = logger.Entries.Single(entry => entry.EventId == 1001);
        Assert.AreEqual(LogLevel.Information, connected.Level);
        Assert.Contains("4.1.1", connected.Message);
        Assert.Contains("JSON-RPC", connected.Message);
    }

    [TestMethod]
    public async Task LogsEachCallWithItsOutcomeAndDuration()
    {
        var logger = new CapturingLogger();
        using var client = ClientTests.MakeClient(ClientTests.SimulateDaemon(Transmission411), logger: logger);

        await client.ConnectAsync(TestContext.CancellationToken);

        Assert.IsTrue(logger.Entries.Any(entry => entry.EventId == 1102), "the 409 handshake is logged");
        var call = logger.Entries.Single(entry => entry.EventId == 1100);
        Assert.Contains("session-get", call.Message);
        Assert.Contains("HTTP 200", call.Message);
        Assert.Contains(" ms", call.Message);
    }

    [TestMethod]
    public async Task LogsAFailedCallAsAWarningWithTheException()
    {
        var logger = new CapturingLogger();
        var network = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.Unauthorized));
        using var client = ClientTests.MakeClient(network, logger: logger);

        await Assert.ThrowsExactlyAsync<TransmissionException>(() => client.ConnectAsync(TestContext.CancellationToken));

        var failure = logger.Entries.Single(entry => entry.EventId == 1101);
        Assert.AreEqual(LogLevel.Warning, failure.Level);
        Assert.Contains("session-get", failure.Message);
        Assert.Contains(nameof(TransmissionErrorKind.Unauthorized), failure.Message);
        Assert.IsInstanceOfType<TransmissionException>(failure.Exception);
    }

    [TestMethod]
    public async Task NeverLogsThePassword()
    {
        var logger = new CapturingLogger();
        var network = new FakeHttpHandler(_ => FakeHttpHandler.Reply(HttpStatusCode.Unauthorized));
        var options = ClientTests.Options() with { Credentials = new Credentials("user", "hunter2-secret") };
        using var client = new TransmissionClient(options, network, logger);

        await Assert.ThrowsExactlyAsync<TransmissionException>(() => client.ConnectAsync(TestContext.CancellationToken));

        Assert.IsNotEmpty(logger.Entries);
        foreach (var entry in logger.Entries)
        {
            Assert.DoesNotContain("hunter2", entry.Message);
            Assert.DoesNotContain("aHVudGVyMi", entry.Message, "nor its base64 form");
            Assert.DoesNotContain("hunter2", entry.Exception?.ToString() ?? "");
        }
        Assert.DoesNotContain("hunter2", options.ToString(), "options are safe to log too");
    }
}
