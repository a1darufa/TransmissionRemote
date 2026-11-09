using System.Collections.Specialized;
using TransmissionKit;

namespace TransmissionRemote.ViewModels.Tests;

[TestClass]
public sealed class TorrentRowSyncTests
{
    private static Torrent[] Torrents(params int[] ids) => [.. ids.Select(id => new Torrent { Id = id, Name = $"t{id}" })];

    private static TorrentRowCollection RowsFor(params int[] ids)
    {
        var rows = new TorrentRowCollection();
        TorrentRowSync.Apply(rows, Torrents(ids));
        return rows;
    }

    /// <summary>Records what the list control would be told.</summary>
    private static List<NotifyCollectionChangedAction> Watch(TorrentRowCollection rows)
    {
        var changes = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, e) => changes.Add(e.Action);
        return changes;
    }

    private static int[] Ids(TorrentRowCollection rows) => [.. rows.Select(row => row.Id)];

    [TestMethod]
    public void ARefreshWithTheSameOrderOnlyUpdatesTheRows()
    {
        var rows = RowsFor(1, 2, 3);
        var first = rows[0];
        var changes = Watch(rows);

        Torrent[] newer = [.. Torrents(1, 2, 3).Select(torrent => torrent with { RateDownload = 1000 })];
        TorrentRowSync.Apply(rows, newer);

        Assert.IsEmpty(changes);
        Assert.AreSame(first, rows[0], "rows keep their identity, so the selection stays");
        Assert.AreSame(newer[0], rows[0].Torrent);
    }

    [TestMethod]
    public void ATorrentThatSortsFarLowerIsTakenOutAndPutBackOnce()
    {
        var ids = Enumerable.Range(1, 200).ToArray();
        var rows = RowsFor(ids);
        var moving = rows[0];
        var changes = Watch(rows);

        // The first torrent slowed down and now sorts last: one row changes place, not the 199 in between.
        TorrentRowSync.Apply(rows, Torrents([.. ids.Skip(1), 1]));

        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add }, changes);
        CollectionAssert.AreEqual(ids.Skip(1).Append(1).ToArray(), Ids(rows));
        Assert.AreSame(moving, rows[^1]);
    }

    [TestMethod]
    public void AnotherSortOrderReplacesTheRowsAtOnce()
    {
        var ids = Enumerable.Range(1, 1000).ToArray();
        var rows = RowsFor(ids);
        var someRow = rows[500];
        var changes = Watch(rows);

        TorrentRowSync.Apply(rows, Torrents([.. ids.Reverse()]));

        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Reset }, changes);
        CollectionAssert.AreEqual(ids.Reverse().ToArray(), Ids(rows));
        Assert.AreSame(someRow, rows.Single(row => row.Id == someRow.Id), "even a reset keeps the row objects");
    }

    [TestMethod]
    public void TheFirstLoadOfManyTorrentsIsOneReset()
    {
        var rows = new TorrentRowCollection();
        var changes = Watch(rows);

        TorrentRowSync.Apply(rows, Torrents([.. Enumerable.Range(1, 500)]));

        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Reset }, changes);
        Assert.HasCount(500, rows);
    }

    [TestMethod]
    public void ASmallReorderMovesRowsWithoutAReset()
    {
        var rows = new TorrentRowCollection();
        TorrentRowSync.Apply(rows, [new Torrent { Id = 1, Name = "a" }, new Torrent { Id = 2, Name = "b" }]);
        var firstRow = rows[0];
        var changes = Watch(rows);

        TorrentRowSync.Apply(rows, [new Torrent { Id = 2, Name = "b" }, new Torrent { Id = 1, Name = "a, renamed" }, new Torrent { Id = 3, Name = "c" }]);

        CollectionAssert.AreEqual(new[] { 2, 1, 3 }, Ids(rows));
        Assert.AreSame(firstRow, rows[1]);
        Assert.AreEqual("a, renamed", rows[1].Torrent.Name);
        CollectionAssert.DoesNotContain(changes, NotifyCollectionChangedAction.Reset, "a reset would lose the scroll position");
    }

    [TestMethod]
    public void GoneTorrentsDropOutAndNewOnesGoInAtTheirPlace()
    {
        var rows = RowsFor(1, 2, 3, 4);
        var changes = Watch(rows);

        TorrentRowSync.Apply(rows, Torrents(1, 5, 3, 4));

        CollectionAssert.AreEqual(new[] { 1, 5, 3, 4 }, Ids(rows));
        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add }, changes);
    }

    [TestMethod]
    public void TheRowsThatStayAreTheLongestRunAlreadyInOrder()
    {
        var rows = RowsFor(1, 2, 3, 4, 5);

        var staying = TorrentRowSync.RowsAlreadyInOrder(rows, Torrents(2, 3, 1, 4, 5));

        CollectionAssert.AreEquivalent(new[] { 2, 3, 4, 5 }, staying.ToArray());
    }

    [TestMethod]
    public void AnyChangeEndsInTheNewOrderWithTheSameRowObjects()
    {
        var random = new Random(20261108);
        for (var round = 0; round < 300; round++)
        {
            var before = Enumerable.Range(1, random.Next(0, 60)).Where(_ => random.Next(4) > 0).ToArray();
            var rows = RowsFor(before);
            var objects = rows.ToDictionary(row => row.Id);

            // Shuffle a little or a lot, drop some, add some.
            var after = before.Where(_ => random.Next(5) > 0).Concat(Enumerable.Range(100, random.Next(0, 5))).ToList();
            for (var swaps = random.Next(0, after.Count); swaps > 0; swaps--)
            {
                var (i, j) = (random.Next(after.Count), random.Next(after.Count));
                (after[i], after[j]) = (after[j], after[i]);
            }
            TorrentRowSync.Apply(rows, Torrents([.. after]));

            CollectionAssert.AreEqual(after.ToArray(), Ids(rows), $"round {round}");
            foreach (var row in rows.Where(row => objects.ContainsKey(row.Id)))
            {
                Assert.AreSame(objects[row.Id], row, $"round {round}: row {row.Id} was replaced");
            }
        }
    }
}
