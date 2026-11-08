using TransmissionKit;

namespace TransmissionRemote.ViewModels.Tests;

/// <summary>Mapping server paths to this PC, and opening, showing and copying them.</summary>
[TestClass]
public sealed class LocalFilesTests
{
    private string _directory = null!;
    private string _localDownloads = null!;
    private string _localIncomplete = null!;
    private ServerStore _servers = null!;
    private FakeShell _shell = null!;

    [TestInitialize]
    public void CreateFolders()
    {
        _directory = Path.Combine(Path.GetTempPath(), "LocalFilesTests-" + Guid.NewGuid().ToString("N"));
        _localDownloads = Path.Combine(_directory, "Downloads");
        _localIncomplete = Path.Combine(_directory, "Incomplete");
        Directory.CreateDirectory(_localDownloads);
        Directory.CreateDirectory(_localIncomplete);
        var environment = new AppEnvironment(Path.Combine(_directory, "Data"), "1.0");
        _servers = new ServerStore(environment, new FakeCredentialStore());
        _shell = new FakeShell();
    }

    [TestCleanup]
    public void DeleteFolders() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    [DataRow("/mnt/data", @"\\nas\data\", "/mnt/data/Show/e01.mkv", @"\\nas\data\Show\e01.mkv")]
    [DataRow("/mnt/data", "//nas/data", "/mnt/data/Show", @"\\nas\data\Show")]
    [DataRow("/mnt/movies", @"M:\", "/mnt/movies/Film.mkv", @"M:\Film.mkv")]
    [DataRow("/mnt/movies", @"M:\", "/mnt/movies", @"M:\")]
    [DataRow("/mnt/movies", "M:", "/mnt/movies/", @"M:\")]
    [DataRow("/mnt/movies", "C:/Users/me/Movies", "/mnt/movies/Film.mkv", @"C:\Users\me\Movies\Film.mkv")]
    [DataRow(@"D:\Torrents", @"\\pc\d\Torrents", @"D:\Torrents\Show\e01.mkv", @"\\pc\d\Torrents\Show\e01.mkv")]
    [DataRow(@"D:\Torrents", @"\\pc\d\Torrents", "d:/torrents/Show/e01.mkv", @"\\pc\d\Torrents\Show\e01.mkv")]
    public void WritesMappedPathsTheWindowsWay(
        string remoteFolder, string localFolder, string remotePath, string expected)
    {
        var serverId = SaveServer(new PathMapping(remoteFolder, localFolder));

        Assert.AreEqual(expected, Files().LocationFor(serverId, remotePath)?.Path);
    }

    [TestMethod]
    public void MapsOnlyPathsInsideAMappedFolder()
    {
        var serverId = SaveServer(new PathMapping("/mnt/data", @"\\nas\data"));

        Assert.IsNull(Files().LocationFor(serverId, "/mnt/database/file"));
        Assert.IsNull(Files().LocationFor(serverId, "/mnt/data/../etc/passwd"), "a .. would leave the mapped folder");
        Assert.IsNull(Files().LocationFor(Guid.NewGuid(), "/mnt/data/file"), "an unknown server has no mappings");
    }

    [TestMethod]
    public void UsesTheSavedMappingsRightAway()
    {
        var profile = new ServerProfile { Endpoint = new ServerEndpoint("nas") };
        _servers.Save(profile);
        var files = Files();
        Assert.IsNull(files.LocationFor(profile.Id, "/mnt/data/file"));

        _servers.Save(profile with { PathMappings = [new PathMapping("/mnt/data", @"\\nas\data")] });

        Assert.AreEqual(@"\\nas\data\file", files.LocationFor(profile.Id, "/mnt/data/file")?.Path);
    }

    [TestMethod]
    public void FindsUnfinishedTorrentsInTheIncompleteFolderOnlyWhenTheirDataIsThere()
    {
        var serverId = SaveServer(
            new PathMapping("/data/downloads", _localDownloads), new PathMapping("/data/incomplete", _localIncomplete));
        var session = new SessionSettings { IncompleteDir = "/data/incomplete", IncompleteDirEnabled = true };
        var started = Torrent("Old Show", leftUntilDone: 10);
        var unfinished = Torrent("New Show", leftUntilDone: 10);
        var finished = Torrent("New Show", leftUntilDone: 0);
        Directory.CreateDirectory(Path.Combine(_localIncomplete, "New Show"));
        var files = Files();
        var settingOff = session with { IncompleteDirEnabled = false };

        Assert.AreEqual("/data/incomplete", files.CurrentFolder(serverId, unfinished, session));
        Assert.AreEqual("/data/downloads", files.CurrentFolder(serverId, started, session), "began before the setting");
        Assert.AreEqual("/data/downloads", files.CurrentFolder(serverId, finished, session));
        Assert.AreEqual("/data/downloads", files.CurrentFolder(serverId, unfinished, settingOff));
        Assert.AreEqual("/data/downloads", files.CurrentFolder(serverId, unfinished, session: null));
        Assert.AreEqual(
            "/data/incomplete/New Show/e01.mkv",
            files.CurrentRemotePath(serverId, unfinished, session, "New Show/e01.mkv"));
    }

    [TestMethod]
    public void FindsAnUnfinishedFileByItsPartName()
    {
        var serverId = SaveServer(
            new PathMapping("/data/downloads", _localDownloads), new PathMapping("/data/incomplete", _localIncomplete));
        var session = new SessionSettings { IncompleteDir = "/data/incomplete", IncompleteDirEnabled = true };
        File.WriteAllText(Path.Combine(_localIncomplete, "film.mkv.part"), "");

        var files = Files();
        var unfinished = Torrent("film.mkv", leftUntilDone: 10);

        Assert.AreEqual("/data/incomplete", files.CurrentFolder(serverId, unfinished, session));
        Assert.AreEqual(LocalOpenResult.Opened, files.Open(serverId, "/data/incomplete/film.mkv").Result);
        Assert.AreEqual(Path.Combine(_localIncomplete, "film.mkv.part"), _shell.Opened.Single());
    }

    [TestMethod]
    public void TellsWhetherATorrentIsMappedWithoutTouchingTheDisk()
    {
        var serverId = SaveServer(new PathMapping("/data/incomplete", @"\\nas\incomplete"));
        var session = new SessionSettings { IncompleteDir = "/data/incomplete", IncompleteDirEnabled = true };
        var files = Files();

        Assert.IsTrue(files.IsAvailableLocally(serverId, Torrent("Show", leftUntilDone: 10), session));
        Assert.IsFalse(files.IsAvailableLocally(serverId, Torrent("Show", leftUntilDone: 0), session));
        Assert.IsFalse(files.IsAvailableLocally(serverId, Torrent("Show", leftUntilDone: 10), session: null));
    }

    [TestMethod]
    public void OpensMappedFilesWithTheirDefaultApp()
    {
        var serverId = SaveServer(new PathMapping("/data/downloads", _localDownloads));
        var film = Path.Combine(_localDownloads, "film.mkv");
        File.WriteAllText(film, "");

        var outcome = Files().Open(serverId, "/data/downloads/film.mkv");

        Assert.AreEqual(new LocalOpenOutcome(LocalOpenResult.Opened, film), outcome);
        Assert.AreEqual(film, _shell.Opened.Single());
        Assert.AreEqual(LocalOpenResult.NotMapped, Files().Open(serverId, "/elsewhere/film.mkv").Result);
    }

    [TestMethod]
    public void AsksBeforeOpeningAProgram()
    {
        var serverId = SaveServer(new PathMapping("/data/downloads", _localDownloads));
        var setup = Path.Combine(_localDownloads, "setup.exe");
        File.WriteAllText(setup, "");
        var files = Files();

        Assert.AreEqual(
            new LocalOpenOutcome(LocalOpenResult.NeedsConfirmation, setup),
            files.Open(serverId, "/data/downloads/setup.exe"));
        Assert.IsEmpty(_shell.Opened);

        var confirmed = files.Open(serverId, "/data/downloads/setup.exe", allowRunnable: true);
        Assert.AreEqual(LocalOpenResult.Opened, confirmed.Result);
        Assert.AreEqual(setup, _shell.Opened.Single());
    }

    [TestMethod]
    public void OpensAFolderNamedLikeAProgramWithoutAsking()
    {
        var serverId = SaveServer(new PathMapping("/data/downloads", _localDownloads));
        Directory.CreateDirectory(Path.Combine(_localDownloads, "Tools.exe"));

        Assert.AreEqual(LocalOpenResult.Opened, Files().Open(serverId, "/data/downloads/Tools.exe").Result);
    }

    // Tests that open or reveal use local folders: on Windows, a made-up \\server would be looked up on the network.
    [TestMethod]
    public void ReportsWhenWindowsCouldNotOpenAFile()
    {
        var serverId = SaveServer(new PathMapping("/data/downloads", _localDownloads));
        _shell.OpenSucceeds = false;

        Assert.AreEqual(
            new LocalOpenOutcome(LocalOpenResult.Failed, Path.Combine(_localDownloads, "film.mkv")),
            Files().Open(serverId, "/data/downloads/film.mkv"));
    }

    [TestMethod]
    public void RevealsOnlyMappedItems()
    {
        var serverId = SaveServer(new PathMapping("/data/downloads", _localDownloads));
        var files = Files();

        Assert.IsTrue(files.Reveal(serverId, ["/data/downloads/a.mkv", "/elsewhere/b.mkv", "/data/downloads/c"]));
        CollectionAssert.AreEqual(
            new[] { Path.Combine(_localDownloads, "a.mkv"), Path.Combine(_localDownloads, "c") },
            _shell.Revealed.Single().ToArray());

        Assert.IsFalse(files.Reveal(serverId, ["/elsewhere/b.mkv"]));
        Assert.HasCount(1, _shell.Revealed);
    }

    [TestMethod]
    public void CopiesLocalPathsWhereMappedAndServerPathsOtherwise()
    {
        var serverId = SaveServer(new PathMapping("/data/downloads", @"\\nas\downloads"));

        Files().CopyPaths(serverId, ["/data/downloads/Show/e01.mkv", "/elsewhere/b.mkv"]);

        Assert.AreEqual("\\\\nas\\downloads\\Show\\e01.mkv\r\n/elsewhere/b.mkv", _shell.Copied.Single());
    }

    [TestMethod]
    [DataRow("/data", "Show/e01.mkv", "/data/Show/e01.mkv")]
    [DataRow("/data/", "Show", "/data/Show")]
    [DataRow("/", "Show", "/Show")]
    [DataRow("", "Show", "Show")]
    [DataRow(@"D:\TV", "Show/e01.mkv", @"D:\TV\Show\e01.mkv")]
    [DataRow(@"D:\", "Show", @"D:\Show")]
    [DataRow("D:", "Show", @"D:\Show")]
    public void JoinsServerPathsWithTheServersSeparator(string folder, string relativePath, string expected)
    {
        Assert.AreEqual(expected, RemotePath.Join(folder, relativePath));
    }

    private LocalFiles Files() => new(_servers, _shell);

    private Guid SaveServer(params PathMapping[] mappings)
    {
        var profile = new ServerProfile { Endpoint = new ServerEndpoint("nas"), PathMappings = mappings };
        _servers.Save(profile);
        return profile.Id;
    }

    private static Torrent Torrent(string name, long leftUntilDone) =>
        new() { Id = 1, Name = name, DownloadDir = "/data/downloads", LeftUntilDone = leftUntilDone };
}

[TestClass]
public sealed class RunnableFileTests
{
    [TestMethod]
    [DataRow("setup.exe")]
    [DataRow(@"C:\Downloads\SETUP.EXE")]
    [DataRow("/data/run.ps1")]
    [DataRow(@"\\nas\share\Install.msi")]
    [DataRow("game.lnk")]
    [DataRow("readme.js")]
    [DataRow("package.msixbundle")]
    [DataRow("tool.jar")]
    [DataRow("setup.exe. ")]
    [DataRow(".exe")]
    public void RecognizesProgramsScriptsAndInstallers(string path)
    {
        Assert.IsTrue(RunnableFile.IsRunnable(path));
    }

    [TestMethod]
    [DataRow("film.mkv")]
    [DataRow("setup.exe.part")]
    [DataRow("exe")]
    [DataRow("notes.txt")]
    [DataRow(@"C:\Tools.exe\readme.txt")]
    [DataRow("disc.iso")]
    [DataRow("")]
    public void LeavesOrdinaryFilesAlone(string path)
    {
        Assert.IsFalse(RunnableFile.IsRunnable(path));
    }
}

/// <summary>Records what the app would have opened, shown or copied.</summary>
internal sealed class FakeShell : IShell
{
    public bool OpenSucceeds { get; set; } = true;
    public List<string> Opened { get; } = [];
    public List<IReadOnlyList<string>> Revealed { get; } = [];
    public List<string> Copied { get; } = [];

    public bool Open(string localPath)
    {
        Opened.Add(localPath);
        return OpenSucceeds;
    }

    public void Reveal(IReadOnlyList<string> localPaths) => Revealed.Add(localPaths);

    public void CopyText(string text) => Copied.Add(text);
}
