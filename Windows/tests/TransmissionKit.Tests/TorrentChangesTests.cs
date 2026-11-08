using System.Diagnostics;
using System.Net;

namespace TransmissionKit.Tests;

[TestClass]
public sealed class TorrentChangesTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void ReplacesChangedTorrentsInPlace()
    {
        var changes = new TorrentChanges([Torrent(2, "b, renamed")], []);

        var result = changes.AppliedTo([Torrent(1, "a"), Torrent(2, "b"), Torrent(3, "c")]);

        CollectionAssert.AreEqual(new[] { "a", "b, renamed", "c" }, result.Select(torrent => torrent.Name).ToArray());
    }

    [TestMethod]
    public void DropsRemovedTorrents()
    {
        var result = new TorrentChanges([], [1, 99]).AppliedTo([Torrent(1, "a"), Torrent(2, "b")]);

        CollectionAssert.AreEqual(new[] { 2 }, result.Select(torrent => torrent.Id).ToArray());
    }

    [TestMethod]
    public void AddsNewTorrentsAtTheEndOnce()
    {
        var changes = new TorrentChanges([Torrent(5, "new"), Torrent(1, "a2"), Torrent(5, "new, later")], []);

        var result = changes.AppliedTo([Torrent(1, "a")]);

        CollectionAssert.AreEqual(new[] { "a2", "new, later" }, result.Select(torrent => torrent.Name).ToArray());
    }

    [TestMethod]
    public void ATorrentAddedAndRemovedInTheSameWindowStaysOut() =>
        Assert.IsEmpty(new TorrentChanges([Torrent(7, "gone")], [7]).AppliedTo([]));

    [TestMethod]
    [DataRow("""{"result":"success","arguments":{"version":"3.00","rpc-version":16}}""",
        """{"result":"success","arguments":{"torrents":[{"id":4}],"removed":[9]}}""", "recently-active")]
    [DataRow("""{"result":"success","arguments":{"version":"4.1.1","rpc-version":19,"rpc-version-semver":"6.0.1"}}""",
        """{"jsonrpc":"2.0","id":2,"result":{"torrents":[{"id":4}],"removed":[9]}}""", "recently_active")]
    public async Task AsksForRecentlyChangedTorrentsInEachDialect(string sessionInfo, string answer, string expectedIds)
    {
        var network = ClientTests.SimulateDaemon(sessionInfo, otherwise: answer);
        using var client = ClientTests.MakeClient(network);
        await client.ConnectAsync(TestContext.CancellationToken);

        var changes = await client.GetRecentlyChangedTorrentsAsync(TestContext.CancellationToken);

        CollectionAssert.AreEqual(new[] { 4 }, changes.Changed.Select(torrent => torrent.Id).ToArray());
        CollectionAssert.AreEqual(new[] { 9 }, changes.RemovedIds.ToArray());
        Assert.Contains($"\"ids\":\"{expectedIds}\"", network.Requests[^1].Body);
    }

    private static Torrent Torrent(int id, string name) => new() { Id = id, Name = name };
}

[TestClass]
public sealed class TorrentListRefreshPlanTests
{
    private static readonly long Start = Stopwatch.GetTimestamp();

    [TestMethod]
    public void StartsWithTheFullList() => Assert.IsTrue(new TorrentListRefreshPlan().NeedsFullList(Start));

    [TestMethod]
    public void AsksForChangesBetweenFrequentRefreshes()
    {
        var plan = new TorrentListRefreshPlan();
        plan.RecordFetch(Start, wasFullList: true);
        plan.RecordFetch(At(5), wasFullList: false);

        Assert.IsFalse(plan.NeedsFullList(At(10)));
    }

    [TestMethod]
    public void FetchesEverythingAfterAGapLongerThanTheWindowAllows()
    {
        var plan = new TorrentListRefreshPlan();
        plan.RecordFetch(Start, wasFullList: true);

        // A slow background interval, or the PC waking from sleep.
        Assert.IsTrue(plan.NeedsFullList(At(45)));
    }

    [TestMethod]
    public void FetchesEverythingOnceAMinuteAnyway()
    {
        var plan = new TorrentListRefreshPlan();
        plan.RecordFetch(Start, wasFullList: true);
        for (var seconds = 5; seconds <= 60; seconds += 5)
        {
            plan.RecordFetch(At(seconds), wasFullList: false);
        }

        Assert.IsTrue(plan.NeedsFullList(At(65)));
    }

    [TestMethod]
    public void ResettingStartsOver()
    {
        var plan = new TorrentListRefreshPlan();
        plan.RecordFetch(Start, wasFullList: true);

        plan.Reset();

        Assert.IsTrue(plan.NeedsFullList(At(1)));
    }

    private static long At(int seconds) => Start + (seconds * Stopwatch.Frequency);
}
