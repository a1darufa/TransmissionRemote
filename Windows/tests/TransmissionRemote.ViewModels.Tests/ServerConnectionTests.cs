using System.Net;
using Microsoft.Extensions.Time.Testing;
using TransmissionKit;

namespace TransmissionRemote.ViewModels.Tests;

[TestClass]
public sealed class ServerConnectionTests : IDisposable
{
    private string _directory = null!;
    private FakeDaemon _daemon = null!;
    private FakeTimeProvider _time = null!;
    private FakeNotifications _notifications = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "ServerConnectionTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _daemon = new FakeDaemon();
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
        _notifications = new FakeNotifications();
    }

    [TestCleanup]
    public void TearDown()
    {
        _daemon.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public async Task ConnectsAndLoadsTheTorrentsSessionAndStats()
    {
        _daemon.AddTorrent(1, "ubuntu.iso");
        using var connection = MakeConnection();

        connection.Connect(Profile(), password: "test-password");
        await WaitUntil(() => connection.Torrents.Count == 1);

        Assert.AreEqual(ConnectionStatus.Connected, connection.Status);
        Assert.AreEqual("3.00", connection.ServerInfo?.Version);
        Assert.AreEqual(RpcDialect.Legacy, connection.Dialect);
        Assert.AreEqual("/data/downloads", connection.Session?.DownloadDir);
        Assert.AreEqual(2000, connection.Stats?.DownloadSpeed);
        Assert.HasCount(1, connection.SpeedHistory.Samples);
    }

    [TestMethod]
    public async Task LaterRefreshesFetchOnlyWhatChanged()
    {
        _daemon.AddTorrent(1, "a");
        _daemon.AddTorrent(2, "b");
        using var connection = MakeConnection();
        connection.Connect(Profile(), password: null);
        await WaitUntil(() => connection.Torrents.Count == 2);

        _daemon.Torrents[2]["name"] = "b, renamed";
        _daemon.Torrents.Remove(1);
        _daemon.RecentlyChanged = [2];
        _daemon.RecentlyRemoved.Add(1);
        await connection.RefreshAsync();

        Assert.AreEqual("b, renamed", connection.Torrents.Single().Name);
        CollectionAssert.Contains(_daemon.Methods.ToArray(), "torrent-get");
    }

    [TestMethod]
    public async Task SelectingOneTorrentLoadsTheVisibleTabsDetails()
    {
        _daemon.AddTorrent(7, "linux.iso");
        using var connection = MakeConnection();
        connection.Connect(Profile(), password: null);
        await WaitUntil(() => connection.Torrents.Count == 1);

        connection.DetailTab = DetailTab.Files;
        connection.Selection = new HashSet<int> { 7 };
        await WaitUntil(() => connection.Details is not null);

        Assert.AreEqual(7, connection.Details!.Id);
        Assert.AreEqual("linux.iso", connection.SingleSelectedTorrent?.Name);
    }

    [TestMethod]
    public async Task AWrongPasswordStopsWithAnEditServerRemedy()
    {
        _daemon.FailWith = HttpStatusCode.Unauthorized;
        using var connection = MakeConnection();

        connection.Connect(Profile(), password: "wrong");
        await WaitUntil(() => connection.Status == ConnectionStatus.Failed);

        Assert.AreEqual(FailureRemedy.EditServer, connection.Failure?.Remedy);
        Assert.AreEqual(TransmissionErrorKind.Unauthorized, (connection.Failure?.Error as TransmissionException)?.Kind);
    }

    [TestMethod]
    public async Task AnUnreachableServerIsRetriedWithBackoff()
    {
        _daemon.UnreachableRequests = 1;
        _daemon.AddTorrent(1, "a");
        using var connection = MakeConnection();

        connection.Connect(Profile(), password: null);
        await WaitUntil(() => connection.Reconnect?.NextAttempt is not null);

        Assert.AreEqual(ConnectionStatus.Reconnecting, connection.Status);
        Assert.AreEqual(_time.GetUtcNow() + TimeSpan.FromSeconds(2), connection.Reconnect!.NextAttempt);
        Assert.AreEqual(NetworkProblem.CannotConnect, connection.Reconnect.Error.NetworkProblem);

        _time.Advance(TimeSpan.FromSeconds(2));
        await WaitUntil(() => connection.Torrents.Count == 1);
        Assert.AreEqual(ConnectionStatus.Connected, connection.Status);
        Assert.IsNull(connection.Reconnect);
    }

    [TestMethod]
    public async Task TryNowSkipsTheWait()
    {
        _daemon.UnreachableRequests = 1;
        using var connection = MakeConnection();
        connection.Connect(Profile(), password: null);
        await WaitUntil(() => connection.Reconnect?.NextAttempt is not null);

        connection.RetryNow();

        await WaitUntil(() => connection.Status == ConnectionStatus.Connected);
    }

    [TestMethod]
    public void PlainHttpOverTheInternetIsBlockedUntilAllowed()
    {
        using var connection = MakeConnection();

        connection.Connect(Profile() with { Endpoint = new ServerEndpoint("seedbox.example.com") }, password: null);

        Assert.AreEqual(ConnectionStatus.Failed, connection.Status);
        Assert.AreEqual(FailureRemedy.AllowUnencryptedConnection, connection.Failure?.Remedy);
        Assert.IsEmpty(_daemon.Methods, "nothing may be sent before the user allows it");
    }

    [TestMethod]
    public async Task AFailedActionIsReportedAndTheListStillRefreshes()
    {
        _daemon.AddTorrent(1, "a");
        using var connection = MakeConnection();
        connection.Connect(Profile(), password: null);
        await WaitUntil(() => connection.Torrents.Count == 1);
        _daemon.ActionFailure = "No such file or directory";
        var refreshesBefore = _daemon.Methods.Count(method => method == "session-stats");

        await connection.SetLocationAsync([1], "/missing/", movingData: true);

        Assert.AreEqual("No such file or directory", connection.ActionError?.Message);
        Assert.IsGreaterThan(refreshesBefore, _daemon.Methods.Count(method => method == "session-stats"));
    }

    [TestMethod]
    public async Task FinishedDownloadsAreAnnouncedOnceButNotTheOnesFinishedBeforeConnecting()
    {
        _daemon.AddTorrent(1, "already done", percentDone: 1, status: 6);
        _daemon.AddTorrent(2, "in progress");
        using var connection = MakeConnection();
        connection.Connect(Profile(), password: null);
        await WaitUntil(() => connection.Torrents.Count == 2);

        _daemon.AddTorrent(2, "in progress", percentDone: 1, status: 6);
        await connection.RefreshAsync();
        await connection.RefreshAsync();

        CollectionAssert.AreEqual(new[] { "in progress" }, _notifications.Completed);
    }

    [TestMethod]
    public async Task DisconnectingClearsEverything()
    {
        _daemon.AddTorrent(1, "a");
        using var connection = MakeConnection();
        connection.Connect(Profile(), password: null);
        await WaitUntil(() => connection.Torrents.Count == 1);

        connection.Disconnect();

        Assert.AreEqual(ConnectionStatus.Disconnected, connection.Status);
        Assert.IsEmpty(connection.Torrents);
        Assert.IsNull(connection.Session);
    }

    private ServerConnection MakeConnection() => new(
        new SettingsStore(new AppEnvironment(_directory, "1.0")),
        new NoClientCertificates(),
        _notifications,
        loggerFactory: null,
        _time,
        _daemon.CreateClient);

    private static ServerProfile Profile() => new()
    {
        Name = "Test daemon",
        Endpoint = new ServerEndpoint("127.0.0.1", 19091),
        Username = "tester",
    };

    /// <summary>The connection works in the background; give it a moment to get there.</summary>
    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
        Assert.IsTrue(condition(), "timed out");
    }

    public void Dispose() => _daemon?.Dispose();
}
