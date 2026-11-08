namespace TransmissionKit.Tests;

[TestClass]
public sealed class TrackerSummaryTests
{
    [TestMethod]
    public void DecodesJsonRpcTrackerStats()
    {
        const string response = """
            {
              "jsonrpc": "2.0", "id": 7,
              "result": {
                "torrents": [{
                  "id": 3,
                  "tracker_stats": [{
                    "tier": 0, "is_backup": false, "seeder_count": 172, "leecher_count": 9,
                    "announce_state": 1, "has_announced": true, "last_announce_succeeded": true,
                    "last_announce_result": "Success"
                  }]
                }]
              }
            }
            """;

        var summary = TrackerSummary.FromJson(Json.Result(response, RpcDialect.JsonRpc).GetObjects("torrents").First());

        Assert.AreEqual(3, summary.TorrentId);
        Assert.AreEqual(172, summary.Seeders);
        Assert.AreEqual(9, summary.Leechers);
        Assert.AreEqual(TrackerStatus.Working, summary.Status);
    }

    [TestMethod]
    public void TakesTheLargestSwarmAnyTrackerReports()
    {
        var summary = TrackerSummary.Summarize(1,
        [
            new TrackerSummaryEntry { Tier = 0, SeederCount = 40, LeecherCount = 2 },
            new TrackerSummaryEntry { Tier = 1, SeederCount = 1_284, LeecherCount = -1 },
        ]);

        Assert.AreEqual(1_284, summary.Seeders);
        Assert.AreEqual(2, summary.Leechers);
    }

    [TestMethod]
    public void UnscrapedTrackersReportNoSwarm()
    {
        var summary = TrackerSummary.Summarize(1, [new TrackerSummaryEntry()]);

        Assert.IsNull(summary.Seeders);
        Assert.IsNull(summary.Leechers);
    }

    [TestMethod]
    public void StatusComesFromTheFirstTierAndSkipsBackups()
    {
        var summary = TrackerSummary.Summarize(1,
        [
            new TrackerSummaryEntry { Tier = 1, HasAnnounced = true, LastAnnounceSucceeded = true },
            new TrackerSummaryEntry { Tier = 0, IsBackup = true, HasAnnounced = true, LastAnnounceResult = "backup down" },
            new TrackerSummaryEntry { Tier = 0, HasAnnounced = true, LastAnnounceResult = "Connection refused" },
        ]);

        Assert.AreEqual(TrackerStatus.Failed, summary.Status);
        Assert.AreEqual("Connection refused", summary.FailureMessage);
    }

    [TestMethod]
    public void AnnouncingTrackersShowAsUpdating() =>
        Assert.AreEqual(
            TrackerStatus.Updating,
            TrackerSummary.Summarize(1, [new TrackerSummaryEntry { AnnounceState = 3, HasAnnounced = true }]).Status);

    [TestMethod]
    public void TrackersNotYetContactedHaveNoStatus()
    {
        Assert.AreEqual(TrackerStatus.NotContacted, TrackerSummary.Summarize(1, [new TrackerSummaryEntry()]).Status);
        Assert.AreEqual(TrackerStatus.NotContacted, TrackerSummary.Summarize(1, []).Status);
    }
}

/// <summary>
/// Transmission before 4.0 answers an empty trackerAdd or trackerRemove list with "invalid argument"
/// (and skips the remove after a failed add), so only real changes may be sent.
/// </summary>
[TestClass]
public sealed class TrackerEditingTests
{
    private const int Transmission300 = 16;
    private const int Transmission40 = 17;

    private static readonly TrackerStat[] Current =
    [
        TrackerStat.FromJson(Json.Normalized("""{"id": 0, "tier": 0, "announce": "http://one.example/announce"}""")),
        TrackerStat.FromJson(Json.Normalized("""{"id": 1, "tier": 1, "announce": "http://two.example/announce"}""")),
    ];

    [TestMethod]
    public void AddingOnlySendsNoRemoveList()
    {
        var mutation = TransmissionClient.TrackerMutation(
            7, Current,
            [["http://one.example/announce"], ["http://two.example/announce"], ["http://new.example/announce"]],
            Transmission300);

        Assert.IsNotNull(mutation);
        CollectionAssert.AreEqual(new[] { "http://new.example/announce" }, mutation.TrackerAdd?.ToArray());
        Assert.IsNull(mutation.TrackerRemove);
    }

    [TestMethod]
    public void RemovingOnlySendsNoAddList()
    {
        var mutation = TransmissionClient.TrackerMutation(7, Current, [["http://one.example/announce"]], Transmission300);

        Assert.IsNotNull(mutation);
        CollectionAssert.AreEqual(new[] { 1 }, mutation.TrackerRemove?.ToArray());
        Assert.IsNull(mutation.TrackerAdd);
    }

    [TestMethod]
    public void UnchangedListsSendNothing()
    {
        var mutation = TransmissionClient.TrackerMutation(
            7, Current, [["http://one.example/announce"], ["http://two.example/announce"]], Transmission300);

        Assert.IsNull(mutation);
    }

    [TestMethod]
    public void EmptyListsAreLeftOutOfTheRequest()
    {
        var mutation = TransmissionClient.TrackerMutation(7, Current, [["http://one.example/announce"]], Transmission300);

        Assert.IsNotNull(mutation);
        Assert.IsFalse(mutation.ToJson().ContainsKey("trackerAdd"));
    }

    [TestMethod]
    public void Transmission40ReplacesTheWholeList()
    {
        var mutation = TransmissionClient.TrackerMutation(
            7, Current,
            [["http://one.example/announce", "http://backup.example/announce"], ["http://two.example/announce"]],
            Transmission40);

        Assert.IsNotNull(mutation);
        Assert.AreEqual(
            "http://one.example/announce\nhttp://backup.example/announce\n\nhttp://two.example/announce",
            mutation.TrackerList);
        Assert.IsNull(mutation.TrackerAdd);
        Assert.IsNull(mutation.TrackerRemove);
    }
}

[TestClass]
public sealed class BandwidthGroupTests
{
    private static readonly BandwidthGroup Group = new(
        "Linux ISOs", HonorsSessionLimits: false, SpeedLimitDownEnabled: true, SpeedLimitDown: 2_000,
        SpeedLimitUpEnabled: false, SpeedLimitUp: 500);

    [TestMethod]
    public void LegacyGroupKeysAreKebabCaseExceptHonorsSessionLimits() =>
        CollectionAssert.AreEquivalent(
            new[]
            {
                "name", "honorsSessionLimits", "speed-limit-down-enabled", "speed-limit-down",
                "speed-limit-up-enabled", "speed-limit-up",
            },
            EncodedKeys(RpcDialect.Legacy));

    [TestMethod]
    public void JsonRpcGroupKeysAreSnakeCase() =>
        CollectionAssert.AreEquivalent(
            new[]
            {
                "name", "honors_session_limits", "speed_limit_down_enabled", "speed_limit_down",
                "speed_limit_up_enabled", "speed_limit_up",
            },
            EncodedKeys(RpcDialect.JsonRpc));

    [TestMethod]
    public void DecodesTransmission40Groups()
    {
        const string response = """
            {"result": "success", "arguments": {"group": [{
              "name": "Linux ISOs", "honorsSessionLimits": false,
              "speed-limit-down-enabled": true, "speed-limit-down": 2000,
              "speed-limit-up-enabled": false, "speed-limit-up": 500
            }]}}
            """;

        var groups = Json.Result(response, RpcDialect.Legacy).GetObjects("group").Select(BandwidthGroup.FromJson);

        Assert.AreEqual(Group, groups.Single());
    }

    [TestMethod]
    public void DecodesTransmission41GroupsWithDoubleSpeeds()
    {
        // Transmission 4.1.0 briefly sent the limits as doubles.
        const string response = """
            {"jsonrpc": "2.0", "id": 3, "result": {"group": [{
              "name": "Linux ISOs", "honors_session_limits": false,
              "speed_limit_down_enabled": true, "speed_limit_down": 2000.0,
              "speed_limit_up_enabled": false, "speed_limit_up": 500.0
            }]}}
            """;

        var groups = Json.Result(response, RpcDialect.JsonRpc).GetObjects("group").Select(BandwidthGroup.FromJson);

        Assert.AreEqual(Group, groups.Single());
    }

    private static string[] EncodedKeys(RpcDialect dialect)
    {
        var envelope = Json.Encoded(dialect, RpcMethod.GroupSet, Group.ToJson(), KeyScope.BandwidthGroup);
        var parameters = envelope[dialect == RpcDialect.JsonRpc ? "params" : "arguments"]!.AsObject();
        return parameters.Select(pair => pair.Key).ToArray();
    }
}
