using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using TransmissionKit;

namespace TransmissionRemote.ViewModels.Tests;

/// <summary>The info pane against the fake daemon: what it shows for a selection, and what its tabs send back.</summary>
[TestClass]
public sealed class InfoPaneViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private string _directory = null!;
    private FakeDaemon _daemon = null!;
    private FakeTimeProvider _time = null!;
    private FakeShell _shell = null!;
    private SettingsStore _settings = null!;
    private ServerStore _servers = null!;
    private ServerConnection _connection = null!;
    private HttpClient _http = null!;
    private InfoPaneViewModel _pane = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "InfoPaneViewModelTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var environment = new AppEnvironment(_directory, "1.0");
        _daemon = new FakeDaemon();
        _time = new FakeTimeProvider(Now);
        _shell = new FakeShell();
        _settings = new SettingsStore(environment);
        _servers = new ServerStore(environment, new FakeCredentialStore());
        _connection = new ServerConnection(
            _settings, new NoClientCertificates(), new FakeNotifications(), loggerFactory: null, _time, _daemon.CreateClient);
        // The country database is never downloaded here.
        _http = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var peerInfo = new PeerInfoService(
            _settings, environment, _http, address => Task.FromResult<string?>($"host-{address}"), time: _time);
        _pane = new InfoPaneViewModel(_connection, _settings, peerInfo, new LocalFiles(_servers, _shell), _time);
    }

    [TestCleanup]
    public void TearDown()
    {
        _pane.Dispose();
        _connection.Dispose();
        _http.Dispose();
        _daemon.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public async Task ShowsWhatTheSelectionCallsFor()
    {
        _daemon.AddTorrent(1, "a");
        _daemon.AddTorrent(2, "b");
        await ConnectAsync(expectedTorrents: 2);
        Assert.AreEqual(InfoPaneContent.NoTorrentSelected, _pane.Content);

        _connection.Selection = new HashSet<int> { 1, 2 };
        Assert.AreEqual((InfoPaneContent.TorrentsSelected, 2), (_pane.Content, _pane.SelectedCount));

        _connection.Selection = new HashSet<int> { 1 };
        Assert.AreEqual(InfoPaneContent.Tab, _pane.Content, "General shows the list's data at once");

        var answers = new TaskCompletionSource();
        _daemon.HoldAnswers = answers.Task;
        _pane.SelectedTab = DetailTab.Trackers;
        Assert.AreEqual(InfoPaneContent.Loading, _pane.Content, "the trackers aren't loaded yet");
        _daemon.HoldAnswers = null;
        answers.SetResult();
        await WaitUntil(() => _pane.Content == InfoPaneContent.Tab);

        _connection.Selection = new HashSet<int>();
        _pane.SelectedTab = DetailTab.Speed;
        Assert.AreEqual(InfoPaneContent.Speed, _pane.Content, "the session's speed needs no selection");
    }

    [TestMethod]
    public async Task TrackerRowsSayHowTheLastAnnounceWent()
    {
        _daemon.AddTorrent(1, "a");
        _daemon.Torrents[1]["trackerStats"] = new JsonArray(
            Tracker(1, hasAnnounced: true, succeeded: true, nextAnnounce: Now.AddMinutes(5)),
            Tracker(2, hasAnnounced: true, succeeded: false, nextAnnounce: Now.AddMinutes(-1)),
            Tracker(3, isBackup: true),
            Tracker(4));
        await ConnectAsync(expectedTorrents: 1);

        await SelectAsync(1, DetailTab.Trackers);

        CollectionAssert.AreEqual(
            new[] { TrackerAnnounce.Succeeded, TrackerAnnounce.Failed, TrackerAnnounce.Backup, TrackerAnnounce.NotYetAnnounced },
            _pane.Trackers.Select(row => row.Announce).ToArray());
        Assert.AreEqual(TimeSpan.FromMinutes(5), _pane.Trackers[0].NextAnnounceIn);
        Assert.IsNull(_pane.Trackers[1].NextAnnounceIn, "an announce time in the past shows nothing");
    }

    [TestMethod]
    public async Task PeersSortByDownloadSpeedAndByAnyColumnButCountry()
    {
        _daemon.AddTorrent(1, "a");
        _daemon.Torrents[1]["peers"] = new JsonArray(
            Peer("10.0.0.1", "Transmission 4.0.5", rateToClient: 100),
            Peer("10.0.0.2", "qBittorrent 5.1", rateToClient: 900));
        await ConnectAsync(expectedTorrents: 1);

        await SelectAsync(1, DetailTab.Peers);
        CollectionAssert.AreEqual(new[] { "10.0.0.2", "10.0.0.1" }, PeerAddresses());

        _pane.SortPeersBy(PeerColumn.Client);
        Assert.AreEqual((PeerColumn.Client, true), (_pane.PeerSortColumn, _pane.PeerSortAscending));
        CollectionAssert.AreEqual(new[] { "10.0.0.2", "10.0.0.1" }, PeerAddresses(), "qBittorrent before Transmission");

        _pane.SortPeersBy(PeerColumn.Client);
        CollectionAssert.AreEqual(new[] { "10.0.0.1", "10.0.0.2" }, PeerAddresses());

        _pane.SortPeersBy(PeerColumn.Country);
        Assert.AreEqual(PeerColumn.Client, _pane.PeerSortColumn, "countries don't sort");
    }

    [TestMethod]
    public async Task HostNamesFillInAsTheyResolveWhileTheSwitchIsOn()
    {
        _daemon.AddTorrent(1, "a");
        _daemon.Torrents[1]["peers"] = new JsonArray(Peer("10.0.0.1", "Transmission", rateToClient: 0));
        await ConnectAsync(expectedTorrents: 1);

        await SelectAsync(1, DetailTab.Peers);
        await WaitUntil(() => _pane.Peers.Single().HostName == "host-10.0.0.1");

        _pane.ResolvePeerHostNames = false;
        Assert.IsNull(_pane.Peers.Single().HostName);
        Assert.IsFalse(_settings.Current.ResolvePeerHostNames, "the switch is remembered");
    }

    [TestMethod]
    public async Task TheCountryBannerOffersTheDownloadOnlyWhileCountriesAreShown()
    {
        await ConnectAsync(expectedTorrents: 0);
        Assert.AreEqual(CountryBanner.OfferDownload, _pane.CountryBanner);

        _pane.HideCountriesCommand.Execute(null);

        Assert.AreEqual(CountryBanner.None, _pane.CountryBanner);
        Assert.IsFalse(_settings.Current.ShowPeerCountries);
    }

    [TestMethod]
    public async Task FoldersStartClosedAndStayOpenAcrossRefreshes()
    {
        AddShowTorrent();
        await ConnectAsync(expectedTorrents: 1);
        await SelectAsync(1, DetailTab.Files);
        CollectionAssert.AreEqual(new[] { "Show/Season 1/", "Show/readme.txt" }, FileIds());
        var folder = _pane.Files[0];

        _pane.SetExpanded(folder, expanded: true);
        CollectionAssert.AreEqual(
            new[] { "Show/Season 1/", "Show/Season 1/e01.mkv", "Show/Season 1/e02.mkv", "Show/readme.txt" }, FileIds());

        await _connection.RefreshAsync();
        CollectionAssert.AreEqual(
            new[] { "Show/Season 1/", "Show/Season 1/e01.mkv", "Show/Season 1/e02.mkv", "Show/readme.txt" }, FileIds());
        Assert.AreSame(folder, _pane.Files[0], "rows are updated in place, so the list keeps its selection");
        Assert.AreEqual(1, _pane.Files[1].Depth);
    }

    [TestMethod]
    public async Task CheckingAFolderWantsOrUnwantsEverythingInIt()
    {
        AddShowTorrent();
        await ConnectAsync(expectedTorrents: 1);
        await SelectAsync(1, DetailTab.Files);
        var folder = _pane.Files[0].Node;
        Assert.AreEqual(WantedState.Mixed, folder.WantedState);

        await _pane.ToggleWantedAsync(folder);
        CollectionAssert.AreEqual(new[] { 0, 1 }, Ids(_daemon.LastArguments["torrent-set"]["files-wanted"]), "mixed becomes wanted");

        await _pane.ToggleWantedAsync(folder with { WantedState = WantedState.All });
        CollectionAssert.AreEqual(new[] { 0, 1 }, Ids(_daemon.LastArguments["torrent-set"]["files-unwanted"]));
    }

    [TestMethod]
    public async Task MenuActionsCoverEveryFileUnderTheSelectionInOneRequest()
    {
        AddShowTorrent();
        await ConnectAsync(expectedTorrents: 1);
        await SelectAsync(1, DetailTab.Files);
        var nodes = _pane.Files.Select(row => row.Node).ToList();

        await _pane.SetPriorityAsync(nodes, Priority.High);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, Ids(_daemon.LastArguments["torrent-set"]["priority-high"]));

        await _pane.SetWantedAsync(nodes, wanted: false);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, Ids(_daemon.LastArguments["torrent-set"]["files-unwanted"]));
    }

    [TestMethod]
    public async Task CopiesTheServerPathWhenNoMappingCoversIt()
    {
        AddShowTorrent();
        await ConnectAsync(expectedTorrents: 1);
        await SelectAsync(1, DetailTab.Files);

        _pane.CopyPaths([_pane.Files[1].Node, _pane.Files[0].Node]);

        Assert.AreEqual("/data/downloads/Show/readme.txt\r\n/data/downloads/Show/Season 1", _shell.Copied.Single());
    }

    [TestMethod]
    public async Task AMagnetLinkWaitsForItsMetadata()
    {
        _daemon.AddTorrent(1, "magnet");
        _daemon.Torrents[1]["metadataPercentComplete"] = 0.5;
        await ConnectAsync(expectedTorrents: 1);

        await SelectAsync(1, DetailTab.Files);

        Assert.AreEqual(FilesContent.WaitingForMetadata, _pane.FilesContent);
    }

    [TestMethod]
    public async Task AnotherTorrentStartsWithItsOwnRowsAndClosedFolders()
    {
        AddShowTorrent();
        _daemon.AddTorrent(2, "b");
        await ConnectAsync(expectedTorrents: 2);
        await SelectAsync(1, DetailTab.Files);
        _pane.SetExpanded(_pane.Files[0], expanded: true);

        await SelectAsync(2, DetailTab.Files);
        Assert.IsEmpty(_pane.Files);

        await SelectAsync(1, DetailTab.Files);
        Assert.HasCount(2, _pane.Files, "the folder is closed again");
    }

    [TestMethod]
    public async Task TheSpeedChartFollowsTheRefreshes()
    {
        await ConnectAsync(expectedTorrents: 0);
        Assert.IsNull(_pane.SpeedChart, "one sample draws no line");

        _time.Advance(TimeSpan.FromSeconds(5));
        await _connection.RefreshAsync();

        Assert.AreEqual(2_000, _pane.SpeedChart?.CurrentDownload);
    }

    [TestMethod]
    public void TheInfoPaneKeepsItsMinimumHeight()
    {
        Assert.AreEqual(280, new AppSettings().EffectiveInfoPaneHeight);
        Assert.AreEqual(180, new AppSettings { InfoPaneHeight = 20 }.EffectiveInfoPaneHeight);
        Assert.AreEqual(280, new AppSettings { InfoPaneHeight = double.NaN }.EffectiveInfoPaneHeight);
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _http?.Dispose();
        _daemon?.Dispose();
    }

    /// <summary>"Show/Season 1/e01.mkv" and "e02.mkv", one of them unwanted, and "Show/readme.txt".</summary>
    private void AddShowTorrent()
    {
        _daemon.AddTorrent(1, "Show");
        _daemon.Torrents[1]["files"] = new JsonArray(
            File("Show/Season 1/e01.mkv", 1000), File("Show/Season 1/e02.mkv", 1000), File("Show/readme.txt", 10));
        _daemon.Torrents[1]["fileStats"] = new JsonArray(Stats(wanted: true), Stats(wanted: false), Stats(wanted: true));
    }

    private async Task ConnectAsync(int expectedTorrents)
    {
        var profile = new ServerProfile { Name = "Test daemon", Endpoint = new ServerEndpoint("127.0.0.1", 19091) };
        _connection.Connect(profile, password: null);
        await WaitUntil(() => _connection.IsConnected && _connection.LastUpdated is not null);
        Assert.HasCount(expectedTorrents, _connection.Torrents);
    }

    private async Task SelectAsync(int id, DetailTab tab)
    {
        _pane.SelectedTab = tab;
        _connection.Selection = new HashSet<int> { id };
        await WaitUntil(() => _pane.Details?.Id == id);
    }

    private string[] PeerAddresses() => [.. _pane.Peers.Select(row => row.Peer.Address)];

    private string[] FileIds() => [.. _pane.Files.Select(row => row.Node.Id)];

    private static int[] Ids(JsonNode? ids) => [.. ids!.AsArray().Select(id => (int)id!)];

    private static JsonObject File(string name, long length) =>
        new() { ["name"] = name, ["length"] = length, ["bytesCompleted"] = 0 };

    private static JsonObject Stats(bool wanted) => new() { ["wanted"] = wanted, ["priority"] = 0, ["bytesCompleted"] = 0 };

    private static JsonObject Peer(string address, string client, long rateToClient) => new()
    {
        ["address"] = address,
        ["port"] = 51413,
        ["clientName"] = client,
        ["flagStr"] = "DE",
        ["progress"] = 0.5,
        ["rateToClient"] = rateToClient,
        ["rateToPeer"] = 0,
    };

    private static JsonObject Tracker(
        int id, bool hasAnnounced = false, bool succeeded = false, bool isBackup = false, DateTimeOffset? nextAnnounce = null) => new()
        {
            ["id"] = id,
            ["tier"] = 0,
            ["announce"] = $"http://tracker{id}.invalid/announce",
            ["hasAnnounced"] = hasAnnounced,
            ["lastAnnounceSucceeded"] = succeeded,
            ["lastAnnounceResult"] = succeeded ? "Success" : "Connection refused",
            ["isBackup"] = isBackup,
            ["nextAnnounceTime"] = nextAnnounce?.ToUnixTimeSeconds() ?? 0,
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
}
