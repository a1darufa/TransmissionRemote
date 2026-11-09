using System.Globalization;
using TransmissionKit;

namespace TransmissionRemote.ViewModels.Tests;

[TestClass]
public sealed class TorrentCellTextTests
{
    private static readonly TorrentCellText Cells = new(
        new DisplayFormatter(CultureInfo.GetCultureInfo("en-US"), timeZone: TimeZoneInfo.Utc), new EnglishWords());

    private static readonly Torrent Downloading = new()
    {
        Id = 1234,
        Name = "ubuntu-26.04-desktop-amd64.iso",
        Status = TorrentStatus.Downloading,
        SizeWhenDone = 5_700_000_000,
        TotalSize = 5_800_000_000,
        LeftUntilDone = 3_100_000_000,
        PercentDone = 0.4567,
        RateDownload = 1_250_000,
        Eta = 4_380,
        UploadRatio = 0.123,
        AddedDate = new DateTimeOffset(2026, 11, 8, 14, 5, 0, TimeSpan.Zero),
        DownloadDir = "/data/iso",
        Labels = ["linux", "iso"],
        TrackerHosts = ["torrent.ubuntu.com"],
        QueuePosition = 2,
        PeersSendingToUs = 3,
        TrackerSummary = new TrackerSummary(1234, 172, 5, TrackerStatus.Working),
        BandwidthPriority = Priority.High,
        BandwidthGroup = "default",
    };

    [TestMethod]
    [DataRow(TorrentColumn.Name, "ubuntu-26.04-desktop-amd64.iso")]
    [DataRow(TorrentColumn.Size, "5.70 GB")]
    [DataRow(TorrentColumn.Done, "45.6%")]
    [DataRow(TorrentColumn.Status, "Downloading")]
    [DataRow(TorrentColumn.Seeds, "3 (172)")]
    [DataRow(TorrentColumn.Peers, "0 (5)")]
    [DataRow(TorrentColumn.Down, "1.25 MB/s")]
    [DataRow(TorrentColumn.Up, "")]
    [DataRow(TorrentColumn.Eta, "1h 13m")]
    [DataRow(TorrentColumn.Ratio, "0.12")]
    [DataRow(TorrentColumn.Added, "11/8/2026 2:05 PM")]
    [DataRow(TorrentColumn.Completed, "")]
    [DataRow(TorrentColumn.Folder, "/data/iso")]
    [DataRow(TorrentColumn.Labels, "linux, iso")]
    [DataRow(TorrentColumn.Tracker, "torrent.ubuntu.com")]
    [DataRow(TorrentColumn.Queue, "3")]
    [DataRow(TorrentColumn.Priority, "High")]
    [DataRow(TorrentColumn.TotalSize, "5.80 GB")]
    [DataRow(TorrentColumn.SizeLeft, "3.10 GB")]
    [DataRow(TorrentColumn.SeedingTime, "")]
    [DataRow(TorrentColumn.TrackerStatus, "Working")]
    [DataRow(TorrentColumn.Private, "")]
    [DataRow(TorrentColumn.BandwidthGroup, "default")]
    [DataRow(TorrentColumn.Id, "1234")]
    public void EachColumnShowsWhatTheMacAppShows(TorrentColumn column, string expected)
    {
        Assert.AreEqual(expected, Cells.Text(Downloading, column));
    }

    [TestMethod]
    public void EveryColumnHasAText()
    {
        foreach (var column in Enum.GetValues<TorrentColumn>())
        {
            Assert.IsNotNull(Cells.Text(Downloading, column), $"{column}");
        }
    }

    [TestMethod]
    public void AnErrorShowsInTheStatusAndTheNamesTooltip()
    {
        var broken = Downloading with { ErrorCode = 3, ErrorString = "No data found" };

        Assert.AreEqual("Error: No data found", Cells.Text(broken, TorrentColumn.Status));
        Assert.AreEqual("No data found", Cells.Tooltip(broken, TorrentColumn.Name));
        Assert.AreEqual(Downloading.Name, Cells.Tooltip(Downloading, TorrentColumn.Name));
    }

    [TestMethod]
    public void VerifyingShowsItsProgress()
    {
        var verifying = Downloading with { Status = TorrentStatus.Verifying, RecheckProgress = 0.25 };

        Assert.AreEqual("Verifying 25.0%", Cells.Text(verifying, TorrentColumn.Status));
        Assert.AreEqual("25.0%", Cells.Text(verifying, TorrentColumn.Done), "the bar follows the check, too");
    }

    [TestMethod]
    public void AFailedTrackerShowsItsOwnMessage()
    {
        var failing = Downloading with { TrackerSummary = new TrackerSummary(1, null, null, TrackerStatus.Failed, "Connection refused") };
        var silent = Downloading with { TrackerSummary = new TrackerSummary(1, null, null, TrackerStatus.Failed) };

        Assert.AreEqual("Connection refused", Cells.Text(failing, TorrentColumn.TrackerStatus));
        Assert.AreEqual("Tracker error", Cells.Text(silent, TorrentColumn.TrackerStatus));
        Assert.AreEqual("", Cells.Text(Downloading with { TrackerSummary = null }, TorrentColumn.TrackerStatus));
        Assert.IsNull(Cells.Tooltip(Downloading with { TrackerSummary = null }, TorrentColumn.TrackerStatus));
    }

    [TestMethod]
    public void SeedingTimeShowsOnceThereIsSome()
    {
        Assert.AreEqual("2d 3h", Cells.Text(Downloading with { SecondsSeeding = 183_600 }, TorrentColumn.SeedingTime));
    }

    [TestMethod]
    public void NarratorReadsTheNameStatusAndProgress()
    {
        Assert.AreEqual("ubuntu-26.04-desktop-amd64.iso, Downloading, 45.6%", Cells.RowName(Downloading));
    }

    private sealed class EnglishWords : ITorrentListWords
    {
        public string RowName => "{0}, {1}, {2}";

        public string Status(TorrentStatusLabel label) => label switch
        {
            TorrentStatusLabel.Error => "Error: {0}",
            TorrentStatusLabel.Verifying => "Verifying {0}",
            TorrentStatusLabel.QueuedToVerify => "Queued to verify",
            TorrentStatusLabel.QueuedToSeed => "Queued to seed",
            TorrentStatusLabel.GettingMetadata => "Getting metadata",
            _ => label.ToString(),
        };

        public string Priority(Priority priority) => priority.ToString();

        public string TrackerStatus(TrackerStatus status) => status switch
        {
            TransmissionKit.TrackerStatus.Working => "Working",
            TransmissionKit.TrackerStatus.Updating => "Updating…",
            TransmissionKit.TrackerStatus.Failed => "Tracker error",
            _ => "",
        };
    }
}
