using System.Text;
using TransmissionKit;

namespace TransmissionRemote.ViewModels.Tests;

/// <summary>Importing servers from a made-up transgui.ini.</summary>
[TestClass]
public sealed class TransguiImporterTests
{
    /// <summary>
    /// The passwords are fake: "c2VjcmV0" is base64 for "secret", "cHJveHlwYXNz" for "proxypass".
    /// </summary>
    private const string Ini = """
        [Hosts]
        CurHost=Seedbox
        Count=2
        Host1=Home NAS
        Host2=Seedbox

        [Interface]
        MaxFoldersHistory=30

        [Connection.Home NAS]
        Host=192.168.1.20
        Port=9091
        UserName=admin
        Password=c2VjcmV0
        PathMap=/mnt/data=\\nas\data|/mnt/movies=M:\
        DownSpeeds=0,50,100
        UpSpeeds=0,10

        [Connection.Seedbox]
        Host=seedbox.example.com
        UseSSL=1
        Port=443
        RpcPath=/torrents/rpc
        UserName=me
        Password=-
        CertFile=C:\certs\client.pem
        UseProxy=1
        UseSockProxy=1
        ProxyHost=proxy.example.com
        ProxyPort=1080
        ProxyUser=proxyuser
        ProxyPass=cHJveHlwYXNz
        DownSpeeds=0,500,1000
        UpSpeeds=0,100

        [AddTorrent.Home NAS]
        FolderCount=2
        Folder0=/mnt/data/movies
        FolHit0=12
        LastDt0=03.11.2026
        Folder1=/mnt/data/linux
        FolHit1=3
        LastDt1=garbage
        """;

    private string _directory = null!;
    private FakeCredentialStore _credentials = null!;
    private ServerStore _servers = null!;
    private SettingsStore _settings = null!;

    [TestInitialize]
    public void CreateStores()
    {
        _directory = Path.Combine(Path.GetTempPath(), "TransguiImporterTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var environment = new AppEnvironment(_directory, "1.0");
        _credentials = new FakeCredentialStore();
        _servers = new ServerStore(environment, _credentials);
        _settings = new SettingsStore(environment);
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public void ImportsServersWithTheirPasswordsMappingsAndFolders()
    {
        var summary = Importer().Import(WriteIni(Ini));

        CollectionAssert.AreEqual(new[] { "Home NAS", "Seedbox" }, summary.ImportedNames.ToArray());
        Assert.IsEmpty(summary.AlreadyAddedNames);
        Assert.IsTrue(summary.FoundServers);
        Assert.IsTrue(summary.HasPathMappings);
        Assert.IsTrue(summary.HasClientCertificates);

        var nas = _servers.Servers.Single(server => server.Name == "Home NAS");
        Assert.AreEqual(new ServerEndpoint("192.168.1.20", 9091), nas.Endpoint);
        Assert.AreEqual("admin", nas.Username);
        Assert.AreEqual("secret", _credentials.GetPassword(nas.Id, CredentialKind.Server));
        Assert.AreEqual(@"\\nas\data", nas.PathMappings[0].LocalPath);
        Assert.AreEqual("/mnt/movies", nas.PathMappings[1].RemotePath);
        Assert.IsFalse(nas.Proxy.IsEnabled);
        Assert.AreEqual(summary.FirstImportedId, nas.Id);

        var movies = nas.RecentFolders.Single(folder => folder.Path == "/mnt/data/movies");
        var linux = nas.RecentFolders.Single(folder => folder.Path == "/mnt/data/linux");
        Assert.AreEqual(12, movies.UseCount);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero), movies.LastUsed);
        Assert.AreEqual(DateTimeOffset.MinValue, linux.LastUsed, "its date was unreadable");
        Assert.AreEqual("/mnt/data/movies", nas.LastUsedFolder);
    }

    [TestMethod]
    public void ImportsAProxyAndLeavesPasswordsTransguiAsksForUnset()
    {
        Importer().Import(WriteIni(Ini));

        var seedbox = _servers.Servers.Single(server => server.Name == "Seedbox");
        Assert.AreEqual(
            new ServerEndpoint("seedbox.example.com", 443, "/torrents/rpc", UsesTls: true), seedbox.Endpoint);
        Assert.IsNull(
            _credentials.GetPassword(seedbox.Id, CredentialKind.Server), "\"-\" means transgui asks every time");
        Assert.IsTrue(seedbox.Proxy.IsEnabled);
        Assert.IsTrue(seedbox.Proxy.UsesSocks5);
        Assert.AreEqual("proxy.example.com", seedbox.Proxy.Host);
        Assert.AreEqual(1080, seedbox.Proxy.Port);
        Assert.IsTrue(seedbox.Proxy.RequiresAuthentication);
        Assert.AreEqual("proxyuser", seedbox.Proxy.Username);
        Assert.AreEqual("proxypass", _credentials.GetPassword(seedbox.Id, CredentialKind.Proxy));
        Assert.IsNull(seedbox.ClientCertificateThumbprint);
    }

    [TestMethod]
    public void AFirstImportContinuesWithTheServerTransguiUsedLast()
    {
        var summary = Importer().Import(WriteIni(Ini));

        var seedbox = _servers.Servers.Single(server => server.Name == "Seedbox");
        Assert.AreEqual(seedbox.Id, summary.ActivatedServerId);
        Assert.AreEqual(seedbox.Id, _servers.ActiveServerId);
    }

    [TestMethod]
    public void SkipsServersAlreadySetUpAndKeepsTheActiveOne()
    {
        var existing = new ServerProfile { Name = "My NAS", Endpoint = new ServerEndpoint(" 192.168.1.20 ") };
        _servers.Save(existing);

        var summary = Importer().Import(WriteIni(Ini));

        CollectionAssert.AreEqual(new[] { "Seedbox" }, summary.ImportedNames.ToArray());
        CollectionAssert.AreEqual(new[] { "Home NAS" }, summary.AlreadyAddedNames.ToArray());
        Assert.IsNull(summary.ActivatedServerId);
        Assert.AreEqual(existing.Id, _servers.ActiveServerId);
        Assert.HasCount(2, _servers.Servers);
        Assert.IsNull(_credentials.GetPassword(existing.Id, CredentialKind.Server), "a skipped server is untouched");
    }

    [TestMethod]
    public void ADifferentPortOrRpcPathIsADifferentServer()
    {
        _servers.Save(new ServerProfile { Endpoint = new ServerEndpoint("192.168.1.20", 9092) });
        _servers.Save(new ServerProfile { Endpoint = new ServerEndpoint("seedbox.example.com", 443) });

        var summary = Importer().Import(WriteIni(Ini));

        CollectionAssert.AreEqual(new[] { "Home NAS", "Seedbox" }, summary.ImportedNames.ToArray());
    }

    [TestMethod]
    public void TakesTheSpeedMenusFromTheServerTransguiUsedLast()
    {
        Importer().Import(WriteIni(Ini));

        Assert.AreEqual("0,500,1000", _settings.Current.DownloadSpeedPresets);
        Assert.AreEqual("0,100", _settings.Current.UploadSpeedPresets);
        Assert.AreEqual(30, _settings.Current.FolderHistoryLimit);
    }

    [TestMethod]
    [DataRow("MaxFoldersHistory=5000", 999)]
    [DataRow("MaxFoldersHistory=0", 50)]
    [DataRow("MaxFoldersHistory=lots", 50)]
    public void KeepsTheFolderHistoryLimitSensible(string line, int expected)
    {
        Importer().Import(WriteIni($"""
            [Interface]
            {line}
            [Connection]
            Host=nas
            """));

        Assert.AreEqual(expected, _settings.Current.FolderHistoryLimit);
        Assert.AreEqual(SpeedLimitPresets.DefaultList, _settings.Current.DownloadSpeedPresets);
    }

    [TestMethod]
    public void AFileWithoutServersIsReportedAndChangesNothing()
    {
        var announced = 0;
        _settings.Changed += (_, _) => announced++;

        var summary = Importer().Import(WriteIni("[Interface]\nLanguage=English\n"));

        Assert.IsFalse(summary.FoundServers);
        Assert.IsEmpty(_servers.Servers);
        Assert.AreEqual(0, announced);
    }

    [TestMethod]
    public void AMissingFileThrows()
    {
        Assert.Throws<FileNotFoundException>(() => Importer().Import(Path.Combine(_directory, "missing.ini")));
    }

    [TestMethod]
    public void ReadsAFileSavedWithAByteOrderMarkOrAsUtf16()
    {
        const string text = "[Connection]\nHost=nas\n";
        byte[][] files =
        [
            [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)],
            [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)],
            [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(text)],
        ];

        foreach (var file in files)
        {
            Assert.AreEqual(text, TransguiImporter.DecodeText(file));
        }
    }

    [TestMethod]
    public void ReadsUtf8NamesAndFallsBackForAnAnsiFile()
    {
        Assert.AreEqual("Host1=Сервер", TransguiImporter.DecodeText(Encoding.UTF8.GetBytes("Host1=Сервер")));

        // "Café" saved as ANSI: 0xE9 isn't valid UTF-8. Which letter it becomes depends on the PC's code page.
        byte[] ansi = [.. "[Hosts]\nCount=1\nHost1=Caf"u8, 0xE9, .. "\n[Connection.Caf"u8, 0xE9, .. "]\nHost=nas\n"u8];
        var summary = Importer().Import(WriteIni(ansi));

        var name = summary.ImportedNames.Single();
        Assert.HasCount(4, name);
        Assert.StartsWith("Caf", name);
        Assert.DoesNotContain('\uFFFD', name);
    }

    [TestMethod]
    public void ListsWhereTransguiKeepsItsSettings()
    {
        var localAppData = Path.Combine("Users", "me", "AppData", "Local");
        const string programFiles = "Program Files";
        const string programFilesX86 = "Program Files (x86)";

        var files = TransguiImporter.LikelySettingsFiles(localAppData, programFiles, programFilesX86, "");

        CollectionAssert.AreEqual(
            new[]
            {
                Path.Combine(localAppData, "Transmission Remote GUI", "transgui.ini"),
                Path.Combine(programFiles, "Transmission Remote GUI", "transgui.ini"),
                Path.Combine(programFilesX86, "Transmission Remote GUI", "transgui.ini"),
            },
            files.ToArray());
    }

    private TransguiImporter Importer() => new(_servers, _settings);

    private string WriteIni(string text) => WriteIni(Encoding.UTF8.GetBytes(text));

    private string WriteIni(byte[] contents)
    {
        var path = Path.Combine(_directory, "transgui.ini");
        File.WriteAllBytes(path, contents);
        return path;
    }
}
