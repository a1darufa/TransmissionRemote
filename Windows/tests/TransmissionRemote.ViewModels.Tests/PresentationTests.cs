using System.Collections.ObjectModel;
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
public sealed class SidebarItemsSyncTests
{
    private static readonly SidebarFilter All = new StatusSidebarFilter(StatusFilter.All);

    [TestMethod]
    public void SectionsAppearInOrderOnlyWhileTheyHaveEntries()
    {
        var items = StatusOnly();

        SidebarItemsSync.Apply(items, Summary(
            new Torrent { Id = 1, DownloadDir = "/data/tv", TrackerHosts = ["b.example"] },
            new Torrent { Id = 2, DownloadDir = "/data/movies", TrackerHosts = ["a.example"] }), All);

        CollectionAssert.AreEqual(
            new object[] { "Status", "Folders", "movies", "tv", "Trackers", "a.example", "b.example" },
            Describe(items));

        SidebarItemsSync.Apply(items, Summary(new Torrent { Id = 3, Labels = ["hd"] }), All);

        CollectionAssert.AreEqual(new object[] { "Status", "Labels", "hd" }, Describe(items));
    }

    [TestMethod]
    public void EntriesKeepTheirIdentityAndOnlyTheirCountsChange()
    {
        var items = StatusOnly();
        SidebarItemsSync.Apply(items, Summary(new Torrent { Id = 1, Labels = ["hd"] }), All);
        var hd = items.OfType<EntrySidebarItem>().Single();

        SidebarItemsSync.Apply(items, Summary(new Torrent { Id = 1, Labels = ["hd"] }, new Torrent { Id = 2, Labels = ["hd", "4k"] }), All);

        Assert.AreSame(hd, items.OfType<EntrySidebarItem>().Single(item => item.Name == "hd"));
        Assert.AreEqual(2, hd.Count);
        CollectionAssert.AreEqual(new object[] { "Status", "Labels", "4k", "hd" }, Describe(items), "natural order");
        Assert.AreEqual(new LabelSidebarFilter("hd"), hd.SidebarFilter);
    }

    [TestMethod]
    public void ASelectedFolderStaysWithNoTorrentsUntilSomethingElseIsSelected()
    {
        var items = StatusOnly();
        var tv = new FolderSidebarFilter("/data/tv");

        SidebarItemsSync.Apply(items, Summary(new Torrent { Id = 1, DownloadDir = "/data/movies" }), tv);

        var kept = items.OfType<EntrySidebarItem>().Single(item => item.Name == "/data/tv");
        Assert.AreEqual(0, kept.Count);
        Assert.AreEqual("tv", kept.DisplayName);

        SidebarItemsSync.Apply(items, Summary(new Torrent { Id = 1, DownloadDir = "/data/movies" }), All);

        Assert.IsFalse(items.OfType<EntrySidebarItem>().Any(item => item.Name == "/data/tv"));
    }

    [TestMethod]
    public async Task TheMainViewModelKeepsTheSidebarInStep()
    {
        using var app = new TestApp(daemon => daemon.AddTorrent(1, "a"));

        await app.ConnectAsync(expectedTorrents: 1);

        Assert.IsInstanceOfType<SidebarHeaderItem>(app.Main.SidebarItems[0]);
        Assert.AreSame(app.Main.StatusItems[0], app.Main.SidebarItems[1]);
        Assert.IsTrue(app.Main.SidebarItems.OfType<SidebarHeaderItem>().Any(header => header.Section == SidebarSection.Folders));
    }

    private static ObservableCollection<object> StatusOnly() =>
        [new SidebarHeaderItem(SidebarSection.Status), .. Enum.GetValues<StatusFilter>().Select(filter => new StatusSidebarItem(filter))];

    private static SidebarSummary Summary(params Torrent[] torrents) => new(torrents);

    /// <summary>Section names for headers and display names for entries; the status items are left out.</summary>
    private static object[] Describe(IEnumerable<object> items) => items
        .Where(item => item is not StatusSidebarItem)
        .Select(item => item switch
        {
            SidebarHeaderItem header => (object)header.Section.ToString(),
            EntrySidebarItem entry => entry.DisplayName,
            _ => item,
        })
        .ToArray();
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

    [TestMethod]
    public void EveryColumnHasTheMacAppsWidthsAndOnlyNameCannotBeHidden()
    {
        foreach (var column in Enum.GetValues<TorrentColumn>())
        {
            var spec = TorrentColumns.Spec(column);
            Assert.IsGreaterThanOrEqualTo(spec.MinimumWidth, spec.IdealWidth, $"{column}");
            Assert.AreEqual(column != TorrentColumn.Name, spec.CanHide, $"{column}");
        }
        Assert.AreEqual(ColumnAlignment.Trailing, TorrentColumns.Spec(TorrentColumn.Size).Alignment);
        Assert.AreEqual(340, TorrentColumns.Spec(TorrentColumn.Name).IdealWidth);
    }

    [TestMethod]
    public void AWidthIsTheSavedOneButNeverBelowTheMinimum()
    {
        var settings = new AppSettings
        {
            TorrentColumnWidths = new Dictionary<string, double> { ["Size"] = 120, ["Eta"] = 5, ["Ratio"] = double.NaN },
        };

        Assert.AreEqual(120, TorrentColumns.Width(settings, TorrentColumn.Size));
        Assert.AreEqual(50, TorrentColumns.Width(settings, TorrentColumn.Eta), "the minimum");
        Assert.AreEqual(55, TorrentColumns.Width(settings, TorrentColumn.Ratio), "the ideal, for a broken value");
        Assert.AreEqual(340, TorrentColumns.Width(settings, TorrentColumn.Name), "the ideal, when nothing is saved");
    }

    [TestMethod]
    public void MovingAColumnPutsItAtTheNewPlace()
    {
        TorrentColumn[] visible = [TorrentColumn.Name, TorrentColumn.Size, TorrentColumn.Done, TorrentColumn.Status];

        CollectionAssert.AreEqual(
            new[] { TorrentColumn.Done, TorrentColumn.Name, TorrentColumn.Size, TorrentColumn.Status },
            TorrentColumns.Moved(visible, TorrentColumn.Done, 0).ToArray());
        CollectionAssert.AreEqual(
            new[] { TorrentColumn.Size, TorrentColumn.Done, TorrentColumn.Status, TorrentColumn.Name },
            TorrentColumns.Moved(visible, TorrentColumn.Name, 99).ToArray(), "past the end means last");
        Assert.AreSame(visible, TorrentColumns.Moved(visible, TorrentColumn.Eta, 0), "a hidden column doesn't move");
    }

    [TestMethod]
    public void ShownColumnsGoLastAndNameStays()
    {
        TorrentColumn[] visible = [TorrentColumn.Name, TorrentColumn.Size];

        CollectionAssert.AreEqual(
            new[] { TorrentColumn.Name, TorrentColumn.Size, TorrentColumn.Id },
            TorrentColumns.WithVisibility(visible, TorrentColumn.Id, isVisible: true).ToArray());
        CollectionAssert.AreEqual(
            new[] { TorrentColumn.Name },
            TorrentColumns.WithVisibility(visible, TorrentColumn.Size, isVisible: false).ToArray());
        Assert.AreSame(visible, TorrentColumns.WithVisibility(visible, TorrentColumn.Name, isVisible: false));
    }

    [TestMethod]
    public void TheChooserListsVisibleColumnsInOrderThenTheHiddenOnes()
    {
        var choices = TorrentColumns.Choices([TorrentColumn.Size, TorrentColumn.Name]);

        Assert.HasCount(27, choices);
        Assert.AreEqual(new ColumnChoice(TorrentColumn.Size, IsVisible: true, CanHide: true), choices[0]);
        Assert.AreEqual(new ColumnChoice(TorrentColumn.Name, IsVisible: true, CanHide: false), choices[1]);
        Assert.AreEqual(new ColumnChoice(TorrentColumn.Done, IsVisible: false, CanHide: true), choices[2]);
    }
}
