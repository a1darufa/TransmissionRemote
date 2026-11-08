using System.Text.Json.Nodes;

namespace TransmissionKit.Tests;

[TestClass]
public sealed class KeyMappingTests
{
    [TestMethod]
    [DataRow("rateDownload", "rate_download")]
    [DataRow("filesWanted", "files_wanted")]
    [DataRow("rpcVersionSemver", "rpc_version_semver")]
    [DataRow("ids", "ids")]
    public void JsonRpcUsesSnakeCase(string camel, string expected) =>
        Assert.AreEqual(expected, RpcDialect.JsonRpc.WireName(camel, KeyScope.TorrentArguments));

    [TestMethod]
    [DataRow("rateDownload", "rateDownload")]
    [DataRow("fileCount", "file-count")]
    [DataRow("peerLimit", "peer-limit")]
    [DataRow("primaryMimeType", "primary-mime-type")]
    public void LegacyTorrentFieldsAreMostlyCamelCase(string camel, string expected) =>
        Assert.AreEqual(expected, RpcDialect.Legacy.WireName(camel, KeyScope.TorrentFields));

    [TestMethod]
    [DataRow("filesWanted", "files-wanted")]
    [DataRow("downloadDir", "download-dir")]
    [DataRow("deleteLocalData", "delete-local-data")]
    [DataRow("bandwidthPriority", "bandwidthPriority")]
    [DataRow("seedRatioLimit", "seedRatioLimit")]
    public void LegacyTorrentArgumentsUseKebabCaseExceptions(string camel, string expected) =>
        Assert.AreEqual(expected, RpcDialect.Legacy.WireName(camel, KeyScope.TorrentArguments));

    [TestMethod]
    [DataRow("speedLimitDown", "speed-limit-down")]
    [DataRow("altSpeedTimeBegin", "alt-speed-time-begin")]
    [DataRow("seedRatioLimit", "seedRatioLimit")]
    [DataRow("seedRatioLimited", "seedRatioLimited")]
    [DataRow("cacheSizeMb", "cache-size-mb")]
    public void LegacySessionKeysAreKebabCaseExceptRatio(string camel, string expected) =>
        Assert.AreEqual(expected, RpcDialect.Legacy.WireName(camel, KeyScope.Session));

    [TestMethod]
    public void JsonRpcSessionKeysAreSnakeCase() =>
        Assert.AreEqual("cache_size_mb", RpcDialect.JsonRpc.WireName("cacheSizeMb", KeyScope.Session));

    [TestMethod]
    [DataRow("rate_download", "rateDownload")]
    [DataRow("rate-download", "rateDownload")]
    [DataRow("rateDownload", "rateDownload")]
    [DataRow("isUTP", "isUtp")]
    [DataRow("is_utp", "isUtp")]
    [DataRow("torrent-added", "torrentAdded")]
    [DataRow("total_size", "totalSize")]
    public void ServerKeysNormalizeToCamelCase(string wire, string expected) =>
        Assert.AreEqual(expected, KeyCase.CamelCase(wire));

    [TestMethod]
    public void MethodNamesFollowTheDialect()
    {
        Assert.AreEqual("torrent_set_location", RpcDialect.JsonRpc.WireName(RpcMethod.TorrentSetLocation));
        Assert.AreEqual("torrent-set-location", RpcDialect.Legacy.WireName(RpcMethod.TorrentSetLocation));
        Assert.AreEqual("queue-move-top", RpcDialect.Legacy.WireName(RpcMethod.QueueMoveTop));
    }

    [TestMethod]
    public void EveryMethodHasAName()
    {
        foreach (var method in Enum.GetValues<RpcMethod>())
        {
            Assert.IsFalse(string.IsNullOrEmpty(method.SnakeCaseName()));
        }
    }

    [TestMethod]
    public void TorrentFieldsUseTheirCanonicalNames()
    {
        Assert.AreEqual("hashString", TorrentField.HashString.CamelName());
        Assert.AreEqual("eta", TorrentField.Eta.CamelName());
        Assert.AreEqual("webseedsSendingToUs", TorrentField.WebseedsSendingToUs.CamelName());
        Assert.AreEqual(17, TorrentField.Group.MinimumRpcVersion());
        Assert.AreEqual(ServerInfo.MinimumRpcVersion, TorrentField.Name.MinimumRpcVersion());
    }
}

[TestClass]
public sealed class EnvelopeTests
{
    private static readonly TorrentMutation SampleMutation =
        new([3]) { FilesWanted = [0, 2], BandwidthPriority = Priority.High };

    [TestMethod]
    public void JsonRpcEnvelope()
    {
        var envelope = Json.Encoded(
            RpcDialect.JsonRpc, RpcMethod.TorrentSet, SampleMutation.ToJson(), KeyScope.TorrentArguments);

        Assert.AreEqual("2.0", (string?)envelope["jsonrpc"]);
        Assert.AreEqual("torrent_set", (string?)envelope["method"]);
        Assert.AreEqual(7, (int?)envelope["id"]);
        var parameters = envelope["params"]!.AsObject();
        Assert.AreEqual("[0,2]", parameters["files_wanted"]!.ToJsonString());
        Assert.AreEqual(1, (int?)parameters["bandwidth_priority"]);
        Assert.AreEqual("[3]", parameters["ids"]!.ToJsonString());
        Assert.HasCount(3, parameters, "unset properties must not be sent");
    }

    [TestMethod]
    public void LegacyEnvelope()
    {
        var envelope = Json.Encoded(
            RpcDialect.Legacy, RpcMethod.TorrentSet, SampleMutation.ToJson(), KeyScope.TorrentArguments);

        Assert.IsNull(envelope["jsonrpc"]);
        Assert.AreEqual("torrent-set", (string?)envelope["method"]);
        Assert.AreEqual(7, (int?)envelope["tag"]);
        var arguments = envelope["arguments"]!.AsObject();
        Assert.AreEqual("[0,2]", arguments["files-wanted"]!.ToJsonString());
        Assert.AreEqual(1, (int?)arguments["bandwidthPriority"]);
    }

    [TestMethod]
    public void RequestsWithoutParametersSendAnEmptyObject()
    {
        var envelope = Json.Encoded(RpcDialect.Legacy, RpcMethod.SessionStats, [], KeyScope.Plain);

        Assert.AreEqual("{}", envelope["arguments"]!.ToJsonString());
    }

    [TestMethod]
    public void EncryptionUsesTheLegacySpellingOnOldServers()
    {
        var settings = new SessionSettings { Encryption = EncryptionMode.Allowed };

        var legacy = Json.Encoded(RpcDialect.Legacy, RpcMethod.SessionSet, settings.ToJson(RpcDialect.Legacy), KeyScope.Session);
        var modern = Json.Encoded(RpcDialect.JsonRpc, RpcMethod.SessionSet, settings.ToJson(RpcDialect.JsonRpc), KeyScope.Session);

        Assert.AreEqual("tolerated", (string?)legacy["arguments"]!["encryption"]);
        Assert.AreEqual("allowed", (string?)modern["params"]!["encryption"]);
    }

    [TestMethod]
    public void NestedObjectsAreRenamedToo()
    {
        var parameters = new JsonObject { ["outer"] = new JsonObject { ["downloadDir"] = "/data" } };

        var envelope = Json.Encoded(RpcDialect.JsonRpc, RpcMethod.TorrentAdd, parameters, KeyScope.TorrentArguments);

        Assert.AreEqual("/data", (string?)envelope["params"]!["outer"]!["download_dir"]);
    }
}
