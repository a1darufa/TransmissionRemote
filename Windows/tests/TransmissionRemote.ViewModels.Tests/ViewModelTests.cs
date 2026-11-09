using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using TransmissionKit;

namespace TransmissionRemote.ViewModels.Tests;

/// <summary>A connected app: stores, a connection to the fake daemon, the add flow and the main view model.</summary>
internal sealed class TestApp : IDisposable
{
    private readonly string _directory;

    public TestApp(Action<FakeDaemon>? setUp = null, bool showAddDialog = true)
    {
        _directory = Path.Combine(Path.GetTempPath(), "TestApp-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_directory);
        setUp?.Invoke(Daemon);
        var environment = new AppEnvironment(_directory, "1.0");
        Settings = new SettingsStore(environment);
        Settings.Update(settings => settings with { ShowAddTorrentDialog = showAddDialog });
        Servers = new ServerStore(environment, new FakeCredentialStore());
        Servers.Save(new ServerProfile { Name = "Test", Endpoint = new ServerEndpoint("127.0.0.1", 19091) });
        Connection = new ServerConnection(
            Settings, new NoClientCertificates(), new FakeNotifications(), null, Time, Daemon.CreateClient);
        AddFlow = new AddTorrentFlow(Connection, Settings);
        Main = new MainViewModel(Servers, Settings, Connection, AddFlow, Time);
    }

    public FakeDaemon Daemon { get; } = new();
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    public SettingsStore Settings { get; }
    public ServerStore Servers { get; }
    public ServerConnection Connection { get; }
    public AddTorrentFlow AddFlow { get; }
    public MainViewModel Main { get; }

    public string Directory => _directory;

    public async Task ConnectAsync(int expectedTorrents)
    {
        Main.Start();
        for (var attempt = 0; attempt < 500 && (!Connection.IsConnected || Connection.Torrents.Count != expectedTorrents); attempt++)
        {
            await Task.Delay(10);
        }
        Assert.IsTrue(Connection.IsConnected && Connection.Torrents.Count == expectedTorrents, "didn't connect");
    }

    public void Dispose()
    {
        Main.Dispose();
        Connection.Dispose();
        Daemon.Dispose();
        System.IO.Directory.Delete(_directory, recursive: true);
    }
}

[TestClass]
public sealed class MainViewModelTests
{
    [TestMethod]
    public async Task RowsFollowTheFilterSearchAndSortOrder()
    {
        using var app = new TestApp(daemon =>
        {
            daemon.AddTorrent(1, "Season 10");
            daemon.AddTorrent(2, "Season 2");
            daemon.AddTorrent(3, "Done", percentDone: 1, status: 6);
        });
        await app.ConnectAsync(expectedTorrents: 3);
        app.Main.SortBy(TorrentColumn.Name);
        app.Main.SortAscending = true;

        CollectionAssert.AreEqual(new[] { "Done", "Season 2", "Season 10" }, Names(app.Main.Rows));

        app.Main.SelectedFilter = new StatusSidebarFilter(StatusFilter.Downloading);
        CollectionAssert.AreEqual(new[] { "Season 2", "Season 10" }, Names(app.Main.Rows));

        app.Main.SearchText = "10";
        CollectionAssert.AreEqual(new[] { "Season 10" }, Names(app.Main.Rows));

        app.Main.SearchText = "";
        app.Main.SortBy(TorrentColumn.Name);
        CollectionAssert.AreEqual(new[] { "Season 10", "Season 2" }, Names(app.Main.Rows), "the same column flips the order");
        Assert.AreEqual(1, app.Main.Sidebar.CountFor(StatusFilter.Completed));
    }

    [TestMethod]
    public void NewestTorrentsComeFirstUntilTheUserPicksAColumn()
    {
        using var app = new TestApp();

        Assert.AreEqual(TorrentColumn.Added, app.Main.SortColumn);
        Assert.IsFalse(app.Main.SortAscending);

        app.Main.SortBy(TorrentColumn.Name);
        Assert.IsTrue(app.Main.SortAscending, "another column starts A to Z, not in the order of the one before");
    }

    [TestMethod]
    public async Task TheSortOrderIsRemembered()
    {
        using var app = new TestApp();

        app.Main.SortBy(TorrentColumn.Added);

        Assert.AreEqual("Added", app.Settings.Current.SortColumn);
    }

    [TestMethod]
    public async Task SelectionEnablesTheTorrentCommands()
    {
        using var app = new TestApp(daemon => daemon.AddTorrent(1, "a"));
        await app.ConnectAsync(expectedTorrents: 1);
        Assert.IsFalse(app.Main.StartCommand.CanExecute(null));

        app.Connection.Selection = new HashSet<int> { 1 };
        await app.Main.StartCommand.ExecuteAsync(null);

        Assert.IsTrue(app.Main.HasSelection);
        Assert.AreEqual("[1]", app.Daemon.LastArguments["torrent-start"]["ids"]!.ToJsonString());
    }

    [TestMethod]
    public async Task TheSubtitleNamesTheServerAndProtocol()
    {
        using var app = new TestApp();
        await app.ConnectAsync(expectedTorrents: 0);

        Assert.AreEqual("Transmission 3.00 · legacy RPC", app.Main.ServerSummary);
    }

    [TestMethod]
    public async Task StatusCountsChangeInPlace()
    {
        using var app = new TestApp(daemon =>
        {
            daemon.AddTorrent(1, "a");
            daemon.AddTorrent(2, "b", percentDone: 1, status: 6);
        });
        var items = app.Main.StatusItems;
        var all = items[0];
        Assert.HasCount(10, items);
        Assert.AreEqual(StatusFilter.All, all.Filter);
        Assert.AreEqual(0, all.Count);

        await app.ConnectAsync(expectedTorrents: 2);

        Assert.AreSame(all, app.Main.StatusItems[0], "the items stay the same objects");
        Assert.AreEqual(2, all.Count);
        Assert.AreEqual(1, items.Single(item => item.Filter == StatusFilter.Seeding).Count);
        Assert.AreEqual(new StatusSidebarFilter(StatusFilter.Seeding), items.Single(item => item.Filter == StatusFilter.Seeding).SidebarFilter);
    }

    [TestMethod]
    public void TheSidebarAndInfoPaneTogglesAreRemembered()
    {
        using var app = new TestApp();
        var changed = new List<string?>();
        app.Main.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        app.Main.ShowSidebar = false;
        app.Main.ShowInfoPane = false;

        Assert.IsFalse(app.Settings.Current.ShowSidebar);
        Assert.IsFalse(app.Settings.Current.ShowDetailPane);
        CollectionAssert.AreEqual(new[] { nameof(MainViewModel.ShowSidebar), nameof(MainViewModel.ShowInfoPane) }, changed);
    }

    [TestMethod]
    public async Task ServerWideCommandsWaitForAConnection()
    {
        using var app = new TestApp();
        Assert.IsFalse(app.Main.StartAllCommand.CanExecute(null));
        Assert.IsFalse(app.Main.ToggleAlternativeSpeedCommand.CanExecute(null));
        Assert.IsFalse(app.Main.DisconnectCommand.CanExecute(null), "already disconnected");
        Assert.IsTrue(app.Main.ReconnectCommand.CanExecute(null), "a server is set up");

        await app.ConnectAsync(expectedTorrents: 0);

        Assert.IsTrue(app.Main.IsConnected);
        Assert.IsTrue(app.Main.StartAllCommand.CanExecute(null));
        Assert.IsTrue(app.Main.StopAllCommand.CanExecute(null));
        Assert.IsTrue(app.Main.DisconnectCommand.CanExecute(null));
        Assert.AreEqual("Test", app.Main.ServerName);
    }

    [TestMethod]
    public void ReconnectingNeedsAServer()
    {
        using var app = new TestApp();

        app.Servers.Remove(app.Servers.Servers.Single().Id);

        Assert.IsFalse(app.Main.HasActiveServer);
        Assert.IsFalse(app.Main.ReconnectCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task TheSpeedLimitMenuChecksTheLimitInForce()
    {
        using var app = new TestApp(daemon => daemon.DownloadLimitEnabled = true);
        app.Settings.Update(settings => settings with { DownloadSpeedPresets = "0, 250, 100, 100, x" });
        await app.ConnectAsync(expectedTorrents: 0);

        var download = app.Main.GlobalSpeedLimitChoices(TransferDirection.Download);
        var upload = app.Main.GlobalSpeedLimitChoices(TransferDirection.Upload);

        CollectionAssert.AreEqual(
            new SpeedLimitChoice[] { new(null, false), new(100, true), new(250, false) }, download.ToArray());
        Assert.IsTrue(upload[0].IsCurrent, "no upload limit: Unlimited is checked");
        Assert.HasCount(12, upload, "Unlimited and the 11 default speeds");
    }

    [TestMethod]
    public void TheColumnLayoutIsRemembered()
    {
        using var app = new TestApp();
        var announced = new List<string?>();
        app.Main.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        app.Main.SetColumnVisible(TorrentColumn.Id, isVisible: true);
        app.Main.MoveColumn(TorrentColumn.Id, 0);
        app.Main.SetColumnVisible(TorrentColumn.Eta, isVisible: false);
        app.Main.SetColumnWidth(TorrentColumn.Name, 412.4);
        app.Main.SetColumnWidth(TorrentColumn.Size, 3);

        var reloaded = new SettingsStore(new AppEnvironment(app.Directory, "1.0")).Current;
        Assert.AreEqual("Id", reloaded.TorrentColumns[0]);
        Assert.DoesNotContain("Eta", reloaded.TorrentColumns.ToList());
        Assert.AreEqual(412, TorrentColumns.Width(reloaded, TorrentColumn.Name));
        Assert.AreEqual(60, TorrentColumns.Width(reloaded, TorrentColumn.Size), "kept at the minimum");
        Assert.AreEqual(TorrentColumn.Id, app.Main.ColumnChoices[0].Column);
        Assert.Contains(nameof(MainViewModel.VisibleColumns), announced);
    }

    [TestMethod]
    public async Task CopyMagnetLinkPutsTheSelectionsLinksOnTheClipboard()
    {
        using var app = new TestApp(daemon =>
        {
            daemon.AddTorrent(1, "a");
            daemon.AddTorrent(2, "b");
        });
        await app.ConnectAsync(expectedTorrents: 2);
        var shell = new RecordingShell();

        app.Connection.Selection = new HashSet<int> { 2, 1 };
        await app.Main.CopyMagnetLinksAsync(shell);

        Assert.AreEqual(
            $"magnet:?xt=urn:btih:{1:x40}&dn=a{Environment.NewLine}magnet:?xt=urn:btih:{2:x40}&dn=b", shell.Copied);
    }

    [TestMethod]
    public async Task AFailedCopyBecomesTheActionError()
    {
        using var app = new TestApp(daemon => daemon.AddTorrent(1, "a"));
        await app.ConnectAsync(expectedTorrents: 1);
        app.Connection.Selection = new HashSet<int> { 1 };
        var shell = new RecordingShell();

        app.Daemon.FailWith = System.Net.HttpStatusCode.InternalServerError;
        await app.Main.CopyMagnetLinksAsync(shell);

        Assert.IsNull(shell.Copied);
        Assert.IsInstanceOfType<TransmissionException>(app.Connection.ActionError);
    }

    [TestMethod]
    public async Task RowChangesAreAnnouncedBeforeAndAfter()
    {
        using var app = new TestApp(daemon =>
        {
            daemon.AddTorrent(1, "b");
            daemon.AddTorrent(2, "a");
        });
        await app.ConnectAsync(expectedTorrents: 2);
        var events = new List<string>();
        app.Main.RowsUpdating += (_, _) => events.Add($"updating {app.Main.Rows[0].Torrent.Name}");
        app.Main.RowsUpdated += (_, _) => events.Add($"updated {app.Main.Rows[0].Torrent.Name}");

        app.Main.SortBy(TorrentColumn.Name);
        app.Main.SortAscending = true;

        CollectionAssert.Contains(events, "updated a");
        Assert.StartsWith("updating", events[0]);
        Assert.AreEqual(events.Count(e => e.StartsWith("updating", StringComparison.Ordinal)), events.Count(e => e.StartsWith("updated", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void EnterOnARowShowsTheInfoPane()
    {
        using var app = new TestApp();
        app.Main.ShowInfoPane = false;

        app.Main.ShowInfoForSelection();

        Assert.IsTrue(app.Settings.Current.ShowDetailPane);
    }

    private static string[] Names(IEnumerable<TorrentRow> rows) => rows.Select(row => row.Torrent.Name).ToArray();

    private sealed class RecordingShell : IShell
    {
        public string? Copied { get; private set; }

        public bool Open(string localPath) => false;

        public void Reveal(IReadOnlyList<string> localPaths)
        {
        }

        public void CopyText(string text) => Copied = text;
    }
}

[TestClass]
public sealed class AddTorrentTests
{
    private const string Magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Ubuntu";

    [TestMethod]
    public void PastedLinksAreQueuedForTheDialog()
    {
        using var app = new TestApp();

        var found = app.AddFlow.OpenLinks($"some text\n{Magnet}\nhttps://example.com/file.torrent\n");

        Assert.IsTrue(found);
        Assert.HasCount(2, app.AddFlow.Pending);
        Assert.AreEqual("Ubuntu", app.AddFlow.Current?.Name);
        Assert.AreEqual("file.torrent", app.AddFlow.Pending[1].Name);
    }

    [TestMethod]
    public void TextWithoutLinksIsReported()
    {
        using var app = new TestApp();

        app.AddFlow.OpenLink("not a link");

        Assert.AreEqual(AddProblemKind.NotALink, app.AddFlow.Problem?.Kind);
    }

    [TestMethod]
    public void AFileThatIsNotATorrentIsReported()
    {
        using var app = new TestApp();
        var path = Path.Combine(app.Directory, "fake.torrent");
        File.WriteAllText(path, "not bencode");

        app.AddFlow.OpenFile(path);

        Assert.AreEqual(AddProblemKind.NotATorrentFile, app.AddFlow.Problem?.Kind);
        Assert.AreEqual("fake.torrent", app.AddFlow.Problem?.Subject);
    }

    [TestMethod]
    public async Task WithoutTheDialogTorrentsAreAddedOneAtATime()
    {
        using var app = new TestApp(showAddDialog: false);
        await app.ConnectAsync(expectedTorrents: 0);

        app.AddFlow.OpenLinks($"{Magnet}\n{Magnet.Replace("dn=Ubuntu", "dn=Other", StringComparison.Ordinal)}");
        await app.AddFlow.QuietAdding;

        Assert.IsEmpty(app.AddFlow.Pending);
        Assert.AreEqual(2, app.Daemon.Methods.Count(method => method == "torrent-add"));
    }

    [TestMethod]
    public async Task TheDialogSendsTheChosenOptionsAndRemembersTheFolder()
    {
        using var app = new TestApp();
        await app.ConnectAsync(expectedTorrents: 0);
        var pending = PendingTorrentWithFiles();
        var dialog = new AddTorrentViewModel(pending, app.Connection, app.Servers, app.Settings);
        Assert.AreEqual("/data/downloads", dialog.Folder, "the server's default folder");

        dialog.ToggleWanted(dialog.FileTree.Single(node => node.Name == "extras.txt"));
        dialog.Folder = "/data/movies/ ";
        dialog.Priority = Priority.High;
        await dialog.AddCommand.ExecuteAsync(null);

        var arguments = app.Daemon.LastArguments["torrent-add"];
        Assert.AreEqual("/data/movies", (string?)arguments["download-dir"]);
        Assert.AreEqual(1, (int?)arguments["bandwidthPriority"]);
        Assert.AreEqual("[1,2]", arguments["files-unwanted"]!.ToJsonString(), "the extras and the padding file");
        Assert.IsNotNull(dialog.Result);
        CollectionAssert.AreEqual(
            new[] { "/data/movies" }, app.Servers.Servers.Single().SuggestedFolders.ToArray());
    }

    [TestMethod]
    public async Task ARenamedTorrentIsRenamedRightAfterAdding()
    {
        using var app = new TestApp(daemon => daemon.AddedName = "Movie");
        await app.ConnectAsync(expectedTorrents: 0);
        var dialog = new AddTorrentViewModel(PendingTorrentWithFiles(), app.Connection, app.Servers, app.Settings)
        {
            Name = "Movie (2026)",
        };

        await dialog.AddCommand.ExecuteAsync(null);

        var rename = app.Daemon.LastArguments["torrent-rename-path"];
        Assert.AreEqual("Movie", (string?)rename["path"]);
        Assert.AreEqual("Movie (2026)", (string?)rename["name"]);
    }

    [TestMethod]
    public async Task LabelsAreSetAfterAddingOnTransmission300()
    {
        using var app = new TestApp();
        await app.ConnectAsync(expectedTorrents: 0);
        var dialog = new AddTorrentViewModel(PendingTorrentWithFiles(), app.Connection, app.Servers, app.Settings)
        {
            LabelsText = "linux, iso",
        };

        await dialog.AddCommand.ExecuteAsync(null);

        Assert.IsFalse(app.Daemon.LastArguments["torrent-add"].ContainsKey("labels"), "3.00 can't label while adding");
        Assert.AreEqual("""["linux","iso"]""", app.Daemon.LastArguments["torrent-set"]["labels"]!.ToJsonString());
    }

    /// <summary>A two-file torrent with a padding file between, made with the core's own parser.</summary>
    private static PendingTorrent PendingTorrentWithFiles()
    {
        var bencoded = "d4:infod5:filesl"
            + "d6:lengthi1000e4:pathl9:movie.mkveed6:lengthi24e4:pathl4:.pad2:24ee"
            + "d6:lengthi10e4:pathl10:extras.txteee"
            + "4:name5:Movie12:piece lengthi16384e6:pieces20:aaaaaaaaaaaaaaaaaaaaee";
        var contents = System.Text.Encoding.ASCII.GetBytes(bencoded);
        return PendingTorrent.FromFile(contents, TorrentMetainfo.Parse(contents));
    }
}

[TestClass]
public sealed class DialogViewModelTests
{
    [TestMethod]
    public void TrackerTextSplitsIntoTiersAndFlagsBadLines()
    {
        var tiers = EditTrackersViewModel.ParseTiers("http://a/announce\r\n udp://b:80 \n\n\nhttps://c/x\n");

        Assert.HasCount(2, tiers);
        CollectionAssert.AreEqual(new[] { "http://a/announce", "udp://b:80" }, tiers[0].ToArray());

        using var app = new TestApp();
        var editor = new EditTrackersViewModel(app.Connection, 1) { Text = "http://ok/announce\nftp://bad/\nnot a url" };
        CollectionAssert.AreEqual(new[] { "ftp://bad/", "not a url" }, editor.InvalidLines.ToArray());
    }

    [TestMethod]
    public void OptionsSendOnlyChangedOptionalSettings()
    {
        using var app = new TestApp();
        var options = new TorrentOptionsViewModel(app.Connection, [3, 1]);
        var details = new TorrentDetails { Id = 1, DownloadLimited = true, DownloadLimit = 500, SequentialDownload = true };

        var mutation = options.MutationFor(details);

        CollectionAssert.AreEqual(new[] { 1, 3 }, mutation.Ids.ToArray());
        Assert.IsTrue(mutation.DownloadLimited);
        Assert.AreEqual(500, mutation.DownloadLimit);
        Assert.IsTrue(mutation.SequentialDownload, "changed from unknown");
        Assert.IsNull(mutation.Group, "the server has no groups");
    }

    [TestMethod]
    public async Task SessionSettingsApplyOnlyWhatChanged()
    {
        using var app = new TestApp();
        await app.ConnectAsync(expectedTorrents: 0);
        var draft = new SessionSettingsViewModel(app.Connection);
        draft.Sync();
        Assert.IsTrue(draft.IsLoaded);
        Assert.IsFalse(draft.ApplyCommand.CanExecute(null));

        draft.Settings = draft.Settings with { SpeedLimitDownEnabled = true };
        await draft.ApplyCommand.ExecuteAsync(null);

        var sent = app.Daemon.LastArguments["session-set"];
        Assert.HasCount(1, sent);
        Assert.IsTrue((bool?)sent["speed-limit-down-enabled"]);
    }

    [TestMethod]
    public void TheServerEditorRefusesAnInvalidAddress()
    {
        using var app = new TestApp();
        var editor = new ServerEditorViewModel(app.Servers)
        {
            Profile = new ServerProfile { Endpoint = new ServerEndpoint("http://nas.local") },
        };

        Assert.AreEqual(ServerProfileProblem.InvalidEndpoint, editor.Problem);
        Assert.IsFalse(editor.SaveCommand.CanExecute(null));

        editor.Profile = editor.Profile with { Endpoint = new ServerEndpoint("nas.local") };
        Assert.AreEqual(ServerProfileProblem.None, editor.Problem);
        Assert.AreEqual(TransportSecurity.LocalNetwork, editor.TransportSecurity);
    }

    [TestMethod]
    public void SavingAServerStoresItsPasswordSeparately()
    {
        using var app = new TestApp();
        var editor = new ServerEditorViewModel(app.Servers)
        {
            Profile = new ServerProfile { Name = " NAS ", Endpoint = new ServerEndpoint("nas.local"), Username = "me" },
            Password = "test-only",
        };

        editor.SaveCommand.Execute(null);

        var saved = app.Servers.Servers.Single(server => server.Name == "NAS");
        Assert.AreEqual("test-only", app.Servers.Password(saved.Id));
    }
}
