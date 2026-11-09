using System.Globalization;
using TransmissionKit;

namespace TransmissionRemote.ViewModels.Tests;

[TestClass]
public sealed class SidebarTests
{
    [TestMethod]
    public void CountsEveryStatusFolderTrackerAndLabel()
    {
        Torrent[] torrents =
        [
            new() { Id = 1, Status = TorrentStatus.Downloading, RateDownload = 5, LeftUntilDone = 100, DownloadDir = "/data/tv/", Labels = ["hd"], TrackerHosts = ["a.example"] },
            new() { Id = 2, Status = TorrentStatus.Stopped, LeftUntilDone = 0, DownloadDir = "/data/tv", TrackerHosts = ["a.example", "b.example"] },
            new() { Id = 3, Status = TorrentStatus.Seeding, ErrorCode = 2, DownloadDir = "/data/movies" },
        ];

        var summary = new SidebarSummary(torrents);

        Assert.AreEqual(3, summary.CountFor(StatusFilter.All));
        Assert.AreEqual(1, summary.CountFor(StatusFilter.Downloading));
        Assert.AreEqual(2, summary.CountFor(StatusFilter.Completed));
        Assert.AreEqual(1, summary.CountFor(StatusFilter.Active));
        Assert.AreEqual(1, summary.CountFor(StatusFilter.Error));
        CollectionAssert.AreEqual(new[] { "movies", "tv" }, summary.Folders.Select(folder => folder.DisplayName).ToArray());
        Assert.AreEqual(2, summary.Folders.Single(folder => folder.Name == "/data/tv").Count, "trailing slashes don't split a folder");
        Assert.AreEqual(2, summary.Trackers.Single(tracker => tracker.Name == "a.example").Count);
        Assert.AreEqual("hd", summary.Labels.Single().Name);
    }

    [TestMethod]
    public void FoldersWithTheSameNameShowTheirParent()
    {
        Torrent[] torrents =
        [
            new() { Id = 1, DownloadDir = "/data/tv/Series" },
            new() { Id = 2, DownloadDir = @"D:\anime\Series" },
        ];

        var names = new SidebarSummary(torrents).Folders.Select(folder => folder.DisplayName).ToArray();

        CollectionAssert.AreEquivalent(new[] { "Series — tv", "Series — anime" }, names);
    }

    [TestMethod]
    public void FiltersMatchLikeTheMacApp()
    {
        var torrent = new Torrent { Id = 1, Status = TorrentStatus.QueuedToDownload, DownloadDir = "/d/", Labels = ["x"], TrackerHosts = ["t"] };

        Assert.IsTrue(new StatusSidebarFilter(StatusFilter.Downloading).Matches(torrent));
        Assert.IsTrue(new StatusSidebarFilter(StatusFilter.Waiting).Matches(torrent));
        Assert.IsTrue(new FolderSidebarFilter("/d").Matches(torrent));
        Assert.IsTrue(new LabelSidebarFilter("x").Matches(torrent));
        Assert.IsTrue(new TrackerSidebarFilter("t").Matches(torrent));
        Assert.IsFalse(new StatusSidebarFilter(StatusFilter.Stopped).Matches(torrent));
    }
}

[TestClass]
public sealed class NameSearchTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    [TestMethod]
    public void IgnoresCaseAndAccents()
    {
        Assert.IsTrue(NameSearch.Matches("Café Society 1080p", "cafe soc", French));
        Assert.IsTrue(NameSearch.Matches("cafe society", "CAFÉ", French));
        Assert.IsFalse(NameSearch.Matches("Café Society", "tea", French));
    }

    [TestMethod]
    public void ABlankQueryMatchesEverythingAndSpacesAroundItDoNotCount()
    {
        Assert.IsTrue(NameSearch.Matches("anything", "   ", French));
        Assert.IsTrue(NameSearch.Matches("Season 10", " 10 ", French));
    }
}

[TestClass]
public sealed class FileTreeTests
{
    [TestMethod]
    public void SkipsTheTorrentsOwnFolderAndPaddingFiles()
    {
        FileEntry[] files =
        [
            new(0, "Show/Season 10/e1.mkv", 100) { BytesCompleted = 100 },
            new(1, "Show/.pad/1024", 1024),
            new(2, "Show/Season 2/e1.mkv", 200) { Wanted = false },
            new(3, "Show/readme.txt", 1) { Priority = Priority.High },
        ];

        var tree = FileTree.Build(files);

        CollectionAssert.AreEqual(new[] { "Season 2", "Season 10", "readme.txt" }, tree.Select(node => node.Name).ToArray());
        Assert.AreEqual("Show/Season 2/", tree[0].Id);
        Assert.AreEqual("Show/Season 2", tree[0].RelativePath);
        Assert.AreEqual(WantedState.None, tree[0].WantedState);
        Assert.AreEqual(1.0, tree[1].Progress);
        Assert.IsNull(tree[2].Children);
    }

    [TestMethod]
    public void FoldersSummarizeTheirFiles()
    {
        FileEntry[] files =
        [
            new(0, "A/x/1.bin", 10) { Priority = Priority.High },
            new(1, "A/x/2.bin", 30) { Wanted = false },
            new(2, "B.bin", 5),
        ];

        var folder = FileTree.Build(files).Single(node => node.IsFolder);

        Assert.AreEqual(40, folder.Size);
        Assert.AreEqual(WantedState.Mixed, folder.WantedState);
        Assert.IsNull(folder.Priority, "mixed priorities");
        CollectionAssert.AreEqual(new[] { 0, 1 }, folder.FileIds.ToArray());
    }
}

[TestClass]
public sealed class NaturalStringComparerTests
{
    [TestMethod]
    [DataRow("Season 2", "Season 10")]
    [DataRow("file9", "File10")]
    [DataRow("a", "B")]
    [DataRow("x 7", "x 008")]
    [DataRow("abc", "abcd")]
    public void SortsLikeFileExplorer(string first, string second)
    {
        Assert.IsLessThan(0, NaturalStringComparer.Instance.Compare(first, second));
        Assert.IsGreaterThan(0, NaturalStringComparer.Instance.Compare(second, first));
    }

    [TestMethod]
    public void LeadingZerosDoNotMatter() => Assert.AreEqual(0, NaturalStringComparer.Instance.Compare("x007", "x7"));
}

[TestClass]
public sealed class TorrentColumnTests
{
    [TestMethod]
    public void UnknownSavedColumnsAreSkipped()
    {
        var settings = new AppSettings { TorrentColumns = ["Name", "Removed", "Eta", "Name"] };

        CollectionAssert.AreEqual(
            new[] { TorrentColumn.Name, TorrentColumn.Eta }, TorrentColumns.FromSettings(settings).ToArray());
    }

    [TestMethod]
    public void NoSavedColumnsMeansTheDefaults() =>
        Assert.AreSame(TorrentColumns.Default, TorrentColumns.FromSettings(new AppSettings()));
}
