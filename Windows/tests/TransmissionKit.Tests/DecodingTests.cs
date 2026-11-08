namespace TransmissionKit.Tests;

[TestClass]
public sealed class DecodingTests
{
    /// <summary>Shape of a Transmission 3.00 torrent-get response.</summary>
    private const string LegacyTorrentResponse = """
        {
          "result": "success",
          "tag": 4,
          "arguments": {
            "torrents": [{
              "id": 12, "name": "ubuntu-24.04.iso", "hashString": "abc123", "status": 4,
              "error": 0, "errorString": "", "totalSize": 6000000000, "sizeWhenDone": 6000000000,
              "leftUntilDone": 3000000000, "percentDone": 0.5, "metadataPercentComplete": 1,
              "rateDownload": 1250000, "rateUpload": 50000, "eta": 2400, "uploadRatio": 0.12,
              "addedDate": 1700000000, "doneDate": 0, "downloadDir": "/mnt/HD/HD_a2/Transmission",
              "labels": ["linux"], "queuePosition": 2, "peersSendingToUs": 14, "peersGettingFromUs": 3,
              "isFinished": false, "bandwidthPriority": 1,
              "trackers": [
                {"announce": "https://torrent.ubuntu.com/announce", "id": 0, "tier": 0},
                {"announce": "https://ipv6.torrent.ubuntu.com/announce", "id": 1, "tier": 1}
              ]
            }]
          }
        }
        """;

    /// <summary>Shape of a Transmission 4.1 torrent_get response.</summary>
    private const string JsonRpcTorrentResponse = """
        {
          "jsonrpc": "2.0",
          "id": 4,
          "result": {
            "torrents": [{
              "id": 12, "name": "ubuntu-24.04.iso", "hash_string": "abc123", "status": 6,
              "error": 0, "error_string": "", "total_size": 6000000000, "size_when_done": 6000000000,
              "left_until_done": 0, "percent_done": 1.0, "rate_download": 0, "rate_upload": 50000,
              "eta": -1, "upload_ratio": 1.5, "added_date": 1700000000, "done_date": 1700003600,
              "download_dir": "/downloads", "labels": [], "queue_position": 0,
              "peers_sending_to_us": 0, "peers_getting_from_us": 3, "is_finished": false,
              "bandwidth_priority": 0, "trackers": []
            }]
          }
        }
        """;

    [TestMethod]
    public void DecodesALegacyTorrentList()
    {
        var torrent = FirstTorrent(LegacyTorrentResponse, RpcDialect.Legacy);

        Assert.AreEqual(12, torrent.Id);
        Assert.AreEqual(TorrentStatus.Downloading, torrent.Status);
        Assert.AreEqual(1_250_000, torrent.RateDownload);
        Assert.AreEqual(6_000_000_000, torrent.TotalSize);
        CollectionAssert.AreEqual(new[] { "linux" }, torrent.Labels.ToArray());
        Assert.IsNull(torrent.DoneDate, "0 means never");
        Assert.AreEqual(Priority.High, torrent.BandwidthPriority);
        CollectionAssert.AreEqual(
            new[] { "torrent.ubuntu.com", "ipv6.torrent.ubuntu.com" }, torrent.TrackerHosts.ToArray());
    }

    [TestMethod]
    public void DecodesAJsonRpcTorrentList()
    {
        var torrent = FirstTorrent(JsonRpcTorrentResponse, RpcDialect.JsonRpc);

        Assert.AreEqual("abc123", torrent.HashString);
        Assert.AreEqual(TorrentStatus.Seeding, torrent.Status);
        Assert.IsTrue(torrent.IsComplete);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1_700_003_600), torrent.DoneDate);
    }

    [TestMethod]
    public void OneOddFieldDoesNotHideTheTorrent()
    {
        var torrent = Torrent.FromJson(Json.Normalized(
            """{"id": 5, "name": 42, "status": 99, "rateDownload": "fast", "eta": 12.6, "isPrivate": 1}"""));

        Assert.AreEqual("", torrent.Name);
        Assert.AreEqual(TorrentStatus.Stopped, torrent.Status, "unknown statuses fall back to stopped");
        Assert.AreEqual(0, torrent.RateDownload);
        Assert.AreEqual(13, torrent.Eta, "doubles are rounded");
        Assert.IsTrue(torrent.IsPrivate, "0 and 1 count as booleans");
    }

    [TestMethod]
    public void ATorrentWithoutAnIdIsAnInvalidResponse()
    {
        var error = Assert.ThrowsExactly<TransmissionException>(
            () => Torrent.FromJson(Json.Normalized("""{"name": "orphan"}""")));

        Assert.AreEqual(TransmissionErrorKind.InvalidResponse, error.Kind);
    }

    [TestMethod]
    public void DecodesAJsonRpcError()
    {
        const string body = """
            {"jsonrpc": "2.0", "id": 1,
             "error": {"code": 7, "message": "backend error", "data": {"error_string": "Couldn't test port"}}}
            """;

        var error = Assert.ThrowsExactly<TransmissionException>(() => Json.Result(body, RpcDialect.JsonRpc));

        Assert.AreEqual(TransmissionErrorKind.RpcFailure, error.Kind);
        Assert.AreEqual("Couldn't test port", error.Detail);
    }

    [TestMethod]
    [DataRow(
        """
        {"id": 1,
         "files": [{"name": "a/b.mkv", "length": 10, "bytesCompleted": 5},
                   {"name": "a/c.txt", "length": 2, "bytesCompleted": 0}],
         "fileStats": [{"bytesCompleted": 5, "wanted": 1, "priority": 1},
                       {"bytesCompleted": 0, "wanted": 0, "priority": -1}]}
        """,
        DisplayName = "legacy integers")]
    [DataRow(
        """
        {"id": 1,
         "files": [{"name": "a/b.mkv", "length": 10, "bytes_completed": 5},
                   {"name": "a/c.txt", "length": 2, "bytes_completed": 0}],
         "file_stats": [{"bytes_completed": 5, "wanted": true, "priority": 1},
                        {"bytes_completed": 0, "wanted": false, "priority": -1}]}
        """,
        DisplayName = "JSON-RPC booleans")]
    public void FileStatsAcceptLegacyIntegersAndBooleans(string body)
    {
        var details = TorrentDetails.FromJson(Json.Normalized(body));

        CollectionAssert.AreEqual(new[] { true, false }, details.Files.Select(file => file.Wanted).ToArray());
        CollectionAssert.AreEqual(
            new[] { Priority.High, Priority.Low }, details.Files.Select(file => file.Priority).ToArray());
        CollectionAssert.AreEqual(new[] { "a/b.mkv", "a/c.txt" }, details.Files.Select(file => file.Path).ToArray());
        Assert.AreEqual(5, details.Files[0].BytesCompleted);
    }

    [TestMethod]
    public void DecodesThePiecesBitfield()
    {
        var details = TorrentDetails.FromJson(Json.Normalized("""{"id": 1, "pieces": "/wA=", "pieceCount": 16}"""));

        CollectionAssert.AreEqual(new byte[] { 0xFF, 0x00 }, details.PiecesBitfield);
    }

    [TestMethod]
    [DataRow("""{"address":"1.2.3.4","port":51413,"isUTP":true,"clientName":"qBittorrent"}""")]
    [DataRow("""{"address":"1.2.3.4","port":51413,"is_utp":true,"client_name":"qBittorrent"}""")]
    public void ThePeerUtpFlagDecodesInBothDialects(string body)
    {
        var peer = Peer.FromJson(Json.Normalized(body));

        Assert.IsTrue(peer.IsUtp);
        Assert.AreEqual("qBittorrent", peer.ClientName);
        Assert.AreEqual("1.2.3.4:51413", peer.Id);
    }

    [TestMethod]
    public void UnscrapedTrackerCountsShowAsZero()
    {
        var tracker = TrackerStat.FromJson(Json.Normalized(
            """{"id": 2, "announce": "udp://tracker.example:1337/announce", "seederCount": -1}"""));

        Assert.AreEqual(0, tracker.SeederCount);
        Assert.AreEqual("tracker.example", tracker.Host, "the host comes from the URL when the server leaves it out");
    }

    [TestMethod]
    public void SessionToleratesFloatSpeedLimitsAndTheOldEncryptionName()
    {
        // Transmission 4.1.0 briefly reported speed limits as doubles; 3.00 calls "allowed" "tolerated".
        var settings = SessionSettings.FromJson(Json.Normalized("""
            {"speed-limit-down": 250.0, "speed-limit-down-enabled": true, "encryption": "tolerated",
             "seedRatioLimit": 2, "download-dir": "/data"}
            """));

        Assert.AreEqual(250, settings.SpeedLimitDown);
        Assert.IsTrue(settings.SpeedLimitDownEnabled);
        Assert.AreEqual(EncryptionMode.Allowed, settings.Encryption);
        Assert.AreEqual(2.0, settings.SeedRatioLimit);
        Assert.AreEqual("/data", settings.DownloadDir);
        Assert.IsNull(settings.PeerPort);
    }

    [TestMethod]
    [DataRow("""{"cache-size-mb": 4}""", 4)]
    [DataRow("""{"cache_size_mb": 16}""", 16)]
    public void SessionDecodesTheDiskCacheSizeInBothDialects(string body, int expected) =>
        Assert.AreEqual(expected, SessionSettings.FromJson(Json.Normalized(body)).CacheSizeMb);

    [TestMethod]
    public void DecodesSessionStats()
    {
        var stats = SessionStats.FromJson(Json.Normalized("""
            {"activeTorrentCount": 2, "download-speed": 1000, "uploadSpeed": 20,
             "current-stats": {"uploadedBytes": 5, "downloadedBytes": 7, "secondsActive": 60}}
            """));

        Assert.AreEqual(2, stats.ActiveTorrentCount);
        Assert.AreEqual(1000, stats.DownloadSpeed);
        Assert.AreEqual(7, stats.CurrentStats?.DownloadedBytes);
        Assert.IsNull(stats.CumulativeStats);
    }

    [TestMethod]
    public void DecodesAnAddedOrDuplicateTorrent()
    {
        var added = AddTorrentResult.FromJson(Json.Normalized(
            """{"torrent-added": {"id": 3, "name": "a", "hashString": "h"}}"""));
        var duplicate = AddTorrentResult.FromJson(Json.Normalized(
            """{"torrent_duplicate": {"id": 4, "name": "b", "hash_string": "i"}}"""));

        Assert.AreEqual(3, added.Torrent?.Id);
        Assert.IsFalse(added.IsDuplicate);
        Assert.AreEqual("i", duplicate.Torrent?.HashString);
        Assert.IsTrue(duplicate.IsDuplicate);
    }

    [TestMethod]
    [DataRow("4.1.1", 19, "6.0.1", true)]
    [DataRow("4.0.6", 17, "5.3.0", false)]
    [DataRow("3.00", 16, null, false)]
    public void ServerInfoDetectsJsonRpcSupport(string version, int rpcVersion, string? semver, bool expected) =>
        Assert.AreEqual(expected, new ServerInfo(version, rpcVersion, semver).SupportsJsonRpc);

    private static Torrent FirstTorrent(string body, RpcDialect dialect) =>
        Torrent.FromJson(Json.Result(body, dialect).GetObjects("torrents").First());
}

[TestClass]
public sealed class SessionDiffTests
{
    [TestMethod]
    public void OnlyChangedWritableKeysAreSent()
    {
        var original = new SessionSettings
        {
            SpeedLimitDown = 100,
            SpeedLimitDownEnabled = false,
            DownloadDir = "/data",
            Version = "4.1.1",
        };
        var edited = original with { SpeedLimitDownEnabled = true, Version = "tampered" };

        Assert.AreEqual(new SessionSettings { SpeedLimitDownEnabled = true }, edited.ChangesFrom(original));
    }

    [TestMethod]
    public void TheDiskCacheSizeIsWritable()
    {
        var original = new SessionSettings { CacheSizeMb = 4 };

        Assert.AreEqual(new SessionSettings { CacheSizeMb = 16 }, (original with { CacheSizeMb = 16 }).ChangesFrom(original));
    }

    [TestMethod]
    public void NoChangesProduceAnEmptyPatch()
    {
        var settings = new SessionSettings { PeerPort = 51413 };

        Assert.IsTrue(settings.ChangesFrom(settings).IsEmpty);
    }

    [TestMethod]
    public void EncryptionChangesSurviveTheDiff()
    {
        var original = new SessionSettings { Encryption = EncryptionMode.Preferred };

        var patch = (original with { Encryption = EncryptionMode.Allowed }).ChangesFrom(original);

        Assert.AreEqual(EncryptionMode.Allowed, patch.Encryption);
    }
}
