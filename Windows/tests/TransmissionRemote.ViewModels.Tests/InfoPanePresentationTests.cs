using System.Collections.ObjectModel;
using TransmissionKit;

namespace TransmissionRemote.ViewModels.Tests;

[TestClass]
public sealed class GeneralFieldsTests
{
    private static readonly Torrent Downloading = new() { Id = 1, Name = "a", Eta = 120 };

    [TestMethod]
    public void BeforeTheDetailsLoadOnlyWhatTheListKnowsShows()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                GeneralField.Status, GeneralField.Progress, GeneralField.Downloaded, GeneralField.Uploaded,
                GeneralField.Ratio, GeneralField.Speed, GeneralField.Peers, GeneralField.Remaining,
            },
            GeneralFields.Transfer(Downloading, details: null).ToArray());
        CollectionAssert.AreEqual(
            new[] { GeneralField.Name, GeneralField.Location, GeneralField.TotalSize, GeneralField.Hash, GeneralField.Added },
            GeneralFields.About(Downloading, details: null).ToArray());
    }

    [TestMethod]
    public void OptionalRowsAppearOnlyWithSomethingToShow()
    {
        var finished = Downloading with { Eta = -1, DoneDate = DateTimeOffset.UnixEpoch, IsPrivate = true };
        var details = new TorrentDetails
        {
            Id = 1,
            CorruptEver = 5,
            PieceCount = 10,
            PieceSize = 16384,
            Creator = "mktorrent",
            DateCreated = DateTimeOffset.UnixEpoch,
        };

        var transfer = GeneralFields.Transfer(finished, details);
        var about = GeneralFields.About(finished, details);

        CollectionAssert.DoesNotContain(transfer.ToArray(), GeneralField.Remaining, "no estimate");
        CollectionAssert.IsSubsetOf(
            new[] { GeneralField.Corrupt, GeneralField.DownloadingFor, GeneralField.SeedingFor }, transfer.ToArray());
        CollectionAssert.IsSubsetOf(
            new[] { GeneralField.Completed, GeneralField.Private, GeneralField.Pieces, GeneralField.CreatedBy, GeneralField.CreatedOn },
            about.ToArray());
        CollectionAssert.DoesNotContain(about.ToArray(), GeneralField.Comment, "the comment is empty");
        CollectionAssert.DoesNotContain(
            GeneralFields.Transfer(finished, details with { CorruptEver = 0 }).ToArray(), GeneralField.Corrupt);
    }
}

[TestClass]
public sealed class PieceAvailabilityTests
{
    [TestMethod]
    public void EachColumnShowsTheShareOfItsPieces()
    {
        // Pieces 0-3 present, 4-7 missing, 8-9 half: 1111 0000 | 10.
        byte[] bitfield = [0b1111_0000, 0b1000_0000];

        var shares = PieceAvailability.ColumnShares(bitfield, pieceCount: 10, columns: 5);

        CollectionAssert.AreEqual(new[] { 1.0, 1.0, 0.0, 0.0, 0.5 }, shares);
    }

    [TestMethod]
    public void MoreColumnsThanPiecesRepeatEachPiece()
    {
        byte[] bitfield = [0b1000_0000];

        CollectionAssert.AreEqual(new[] { 1.0, 1.0, 0.0, 0.0 }, PieceAvailability.ColumnShares(bitfield, 2, 4));
    }

    [TestMethod]
    public void TheShareOfAllPiecesIgnoresPaddingBits()
    {
        byte[] bitfield = [0xFF, 0b1111_1111];

        Assert.AreEqual(1.0, PieceAvailability.HaveFraction(bitfield, 10));
        Assert.AreEqual(0.5, PieceAvailability.HaveFraction([0b1111_0000], 8));
    }

    [TestMethod]
    public void AShortOrMissingBitfieldMeansMissingPieces()
    {
        Assert.AreEqual(0.0, PieceAvailability.HaveFraction([], 8));
        CollectionAssert.AreEqual(new[] { 0.0, 0.0 }, PieceAvailability.ColumnShares([], 4, 2));
        Assert.IsEmpty(PieceAvailability.ColumnShares([0xFF], 8, 0));
    }
}

[TestClass]
public sealed class PeerFlagsTests
{
    [TestMethod]
    public void ReadsTheLettersInOrderAndSkipsUnknownOnes()
    {
        CollectionAssert.AreEqual(
            new[] { PeerFlagLetter.DownloadingFrom, PeerFlagLetter.Encrypted, PeerFlagLetter.Incoming, PeerFlagLetter.UnchokedByUsButNotInterested },
            PeerFlags.Parse("DEZI?").ToArray());
        CollectionAssert.AreEqual(
            new[] { PeerFlagLetter.WouldDownloadFrom, PeerFlagLetter.WouldUploadTo, PeerFlagLetter.ConnectedOverUtp },
            PeerFlags.Parse("duT").ToArray());
    }
}

[TestClass]
public sealed class SpeedChartTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void NeedsTwoSamplesToDrawALine()
    {
        Assert.IsNull(SpeedChart.For([]));
        Assert.IsNull(SpeedChart.For([new SpeedSample(Start, 1000, 0)]));
    }

    [TestMethod]
    public void ScalesToRoundSpeedsAboveThePeak()
    {
        var chart = SpeedChart.For([Sample(0, 1_300_000, 100_000), Sample(5, 800_000, 200_000)])!;

        Assert.AreEqual(1_500_000, chart.Scale);
        CollectionAssert.AreEqual(new long[] { 500_000, 1_000_000, 1_500_000 }, chart.GridLines.ToArray());
        Assert.AreEqual((800_000L, 200_000L), (chart.CurrentDownload, chart.CurrentUpload));
        Assert.AreEqual((1_300_000L, 200_000L), (chart.PeakDownload, chart.PeakUpload));
    }

    [TestMethod]
    public void SpreadsThePointsAcrossTheRecordedTime()
    {
        var chart = SpeedChart.For([Sample(0, 0, 0), Sample(10, 5_000, 0), Sample(40, 10_000, 0)])!;

        CollectionAssert.AreEqual(new[] { 0.0, 0.25, 1.0 }, chart.Download.Select(point => point.X).ToArray());
        CollectionAssert.AreEqual(new[] { 0.0, 0.5, 1.0 }, chart.Download.Select(point => point.Y).ToArray());
    }

    [TestMethod]
    public void AnIdleSessionStillGetsAReadableScale()
    {
        var chart = SpeedChart.For([Sample(0, 0, 0), Sample(5, 0, 0)])!;

        Assert.AreEqual(SpeedChart.MinimumScale, chart.Scale);
        Assert.IsTrue(chart.Upload.All(point => point.Y == 0));
    }

    [TestMethod]
    public void StepsAreOneTwoOrFiveTimesAPowerOfTen()
    {
        Assert.AreEqual(1, SpeedChart.RoundStep(0.5));
        Assert.AreEqual(2_000, SpeedChart.RoundStep(1_500));
        Assert.AreEqual(5_000, SpeedChart.RoundStep(2_001));
        Assert.AreEqual(10_000, SpeedChart.RoundStep(5_001));
    }

    private static SpeedSample Sample(int seconds, long download, long upload) =>
        new(Start.AddSeconds(seconds), download, upload);
}

[TestClass]
public sealed class FileTreeRowsTests
{
    private static readonly IReadOnlyList<FileNode> Tree = FileTree.Build(
    [
        new FileEntry(0, "Show/Season 1/e01.mkv", 100),
        new FileEntry(1, "Show/Season 1/e02.mkv", 100),
        new FileEntry(2, "Show/Extras/making-of.mkv", 100),
        new FileEntry(3, "Show/readme.txt", 10),
    ]);

    [TestMethod]
    public void FoldersStartClosed() =>
        CollectionAssert.AreEqual(
            new[] { "Show/Extras/", "Show/Season 1/", "Show/readme.txt" },
            FileTreeRows.Visible(Tree, new HashSet<string>()).Select(row => row.Node.Id).ToArray());

    [TestMethod]
    public void AnOpenFolderShowsItsContentsOneLevelDeeper()
    {
        var rows = FileTreeRows.Visible(Tree, new HashSet<string> { "Show/Season 1/" });

        CollectionAssert.AreEqual(
            new[] { "Show/Extras/", "Show/Season 1/", "Show/Season 1/e01.mkv", "Show/Season 1/e02.mkv", "Show/readme.txt" },
            rows.Select(row => row.Node.Id).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 0, 1, 1, 0 }, rows.Select(row => row.Depth).ToArray());
        CollectionAssert.AreEqual(new[] { false, true, false, false, false }, rows.Select(row => row.IsExpanded).ToArray());
    }
}

[TestClass]
public sealed class PeerOrderTests
{
    private static readonly Peer[] Peers =
    [
        new() { Address = "10.0.0.10", Port = 1, ClientName = "qBittorrent", RateToClient = 500 },
        new() { Address = "10.0.0.2", Port = 2, ClientName = "Transmission", RateToClient = 900 },
        new() { Address = "10.0.0.3", Port = 3, ClientName = "Deluge", RateToClient = 500 },
    ];

    [TestMethod]
    public void SortsByTheColumnWithTiesByAddress()
    {
        CollectionAssert.AreEqual(
            new[] { "10.0.0.2", "10.0.0.3", "10.0.0.10" },
            PeerOrder.Sorted(Peers, PeerColumn.Down, ascending: false).Select(peer => peer.Address).ToArray());
        CollectionAssert.AreEqual(
            new[] { "10.0.0.3", "10.0.0.10", "10.0.0.2" },
            PeerOrder.Sorted(Peers, PeerColumn.Down, ascending: true).Select(peer => peer.Address).ToArray());
    }

    [TestMethod]
    public void AddressesSortAsNumbers() =>
        CollectionAssert.AreEqual(
            new[] { "10.0.0.2", "10.0.0.3", "10.0.0.10" },
            PeerOrder.Sorted(Peers, PeerColumn.Address, ascending: true).Select(peer => peer.Address).ToArray());
}

[TestClass]
public sealed class RowListSyncTests
{
    private sealed class Row(string key)
    {
        public string Key { get; } = key;
        public int Updates { get; set; }
    }

    [TestMethod]
    public void KeepsTheRowsThatStayAndInsertsAndRemovesTheRest()
    {
        var rows = new ObservableCollection<Row> { new("a"), new("b"), new("c") };
        var (a, c) = (rows[0], rows[2]);

        Sync(rows, ["a", "x", "c", "y"]);

        CollectionAssert.AreEqual(new[] { "a", "x", "c", "y" }, rows.Select(row => row.Key).ToArray());
        Assert.AreSame(a, rows[0]);
        Assert.AreSame(c, rows[2]);
        Assert.AreEqual(1, a.Updates);
    }

    [TestMethod]
    public void MovesRowsToANewOrder()
    {
        var rows = new ObservableCollection<Row> { new("a"), new("b"), new("c") };
        var b = rows[1];
        var changes = new List<string>();
        rows.CollectionChanged += (_, e) => changes.Add(e.Action.ToString());

        Sync(rows, ["b", "c", "a"]);

        CollectionAssert.AreEqual(new[] { "b", "c", "a" }, rows.Select(row => row.Key).ToArray());
        Assert.AreSame(b, rows[0]);
        Assert.IsFalse(changes.Contains("Reset") || changes.Contains("Remove"), "rows move, they aren't replaced");
    }

    private static void Sync(ObservableCollection<Row> rows, string[] keys) => RowListSync.Apply(
        rows, keys, key => key, row => row.Key, key => new Row(key), (row, _) => row.Updates++);
}
