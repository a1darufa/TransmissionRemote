namespace TransmissionKit.Tests;

/// <summary>Importing servers from transgui.ini.</summary>
[TestClass]
public sealed class TransguiSettingsTests
{
    /// <summary>
    /// A made-up transgui.ini in the shape transgui 5.18 writes. The passwords are fake:
    /// "c2VjcmV0" is base64 for "secret", "cHJveHlwYXNz" for "proxypass".
    /// </summary>
    private const string Ini = """
        [Hosts]
        CurHost=Home NAS
        Count=2
        Host1=Home NAS
        Host2=Seedbox

        [Interface]
        MaxFoldersHistory=30

        [Connection.Home NAS]
        Host=192.168.1.20
        UseSSL=0
        Autoreconnect=1
        Port=9091
        UserName=admin
        Password=c2VjcmV0
        UseProxy=0
        PathMap=/mnt/HD/HD_a2/Torrents=/Volumes/Torrents|/mnt/HD/HD_a2/Movies=/Volumes/Movies
        DownSpeeds=0,50,100,500,1000
        UpSpeeds=0,10,50,100

        [Connection.Seedbox]
        Host=seedbox.example.com
        UseSSL=1
        Port=443
        RpcPath=/torrents/rpc
        UserName=me
        Password=-
        CertFile=/Users/me/client.pem
        UseProxy=1
        UseSockProxy=1
        ProxyHost=proxy.example.com
        ProxyPort=1080
        ProxyUser=proxyuser
        ProxyPass=cHJveHlwYXNz

        [AddTorrent.Home NAS]
        FolderCount=2
        Folder0=/mnt/HD/HD_a2/Torrents/Movies
        FolHit0=12
        FolExt0=
        LastDt0=03.11.2026
        Folder1=/mnt/HD/HD_a2/Torrents/Linux
        FolHit1=3
        LastDt1=garbage
        """;

    private static readonly TransguiSettings Settings = new(Ini);

    [TestMethod]
    public void ReadsTheConnectionList()
    {
        SequenceAssert.AreEqual(Settings.Connections.Select(connection => connection.Name), "Home NAS", "Seedbox");
        Assert.AreEqual("Home NAS", Settings.ActiveConnectionName);
        Assert.AreEqual(30, Settings.FolderHistoryLimit);
    }

    [TestMethod]
    public void ReadsAPlainHttpServer()
    {
        var nas = Settings.Connections[0];

        Assert.AreEqual("192.168.1.20", nas.Host);
        Assert.AreEqual(9091, nas.Port);
        Assert.AreEqual("/transmission/rpc", nas.RpcPath);
        Assert.IsFalse(nas.UsesTls);
        Assert.AreEqual("admin", nas.Username);
        Assert.AreEqual("secret", nas.Password);
        Assert.IsNull(nas.Proxy);
        Assert.AreEqual("0,50,100,500,1000", nas.DownloadSpeeds);
        SequenceAssert.AreEqual(
            nas.PathMappings,
            new TransguiMappedFolder("/mnt/HD/HD_a2/Torrents", "/Volumes/Torrents"),
            new TransguiMappedFolder("/mnt/HD/HD_a2/Movies", "/Volumes/Movies"));
    }

    [TestMethod]
    public void ReadsAnHttpsServerBehindAProxy()
    {
        var seedbox = Settings.Connections[^1];

        Assert.IsTrue(seedbox.UsesTls);
        Assert.AreEqual(443, seedbox.Port);
        Assert.AreEqual("/torrents/rpc", seedbox.RpcPath);
        Assert.IsNull(seedbox.Password, "\"-\" means transgui asks every time");
        Assert.IsTrue(seedbox.HasClientCertificate);
        Assert.AreEqual(
            new TransguiProxy("proxy.example.com", 1080, UsesSocks5: true, "proxyuser", "proxypass"),
            seedbox.Proxy);
    }

    [TestMethod]
    public void KeepsProxyPasswordsOutOfDescriptions()
    {
        var proxy = Settings.Connections[^1].Proxy;

        Assert.IsNotNull(proxy);
        Assert.DoesNotContain("proxypass", proxy.ToString());
    }

    [TestMethod]
    public void ReadsTheFolderHistory()
    {
        var folders = Settings.Connections[0].Folders;

        SequenceAssert.AreEqual(
            folders.Select(folder => folder.Path), "/mnt/HD/HD_a2/Torrents/Movies", "/mnt/HD/HD_a2/Torrents/Linux");
        SequenceAssert.AreEqual(folders.Select(folder => folder.UseCount), 12, 3);
        Assert.AreEqual(new DateOnly(2026, 11, 3), folders[0].LastUsed);
        Assert.IsNull(folders[^1].LastUsed);
    }

    [TestMethod]
    public void ReadsTheOldSingleConnectionFormat()
    {
        var old = new TransguiSettings("[Connection]\nHost=nas.local\nPort=9092\n");

        SequenceAssert.AreEqual(old.Connections.Select(connection => connection.Host), "nas.local");
        Assert.AreEqual(9092, old.Connections[0].Port);
    }

    [TestMethod]
    public void IgnoresConnectionsWithoutAHost()
    {
        var empty = new TransguiSettings("[Hosts]\nCount=1\nHost1=Broken\n[Connection.Broken]\nPort=9091\n");

        Assert.IsEmpty(empty.Connections);
    }
}
