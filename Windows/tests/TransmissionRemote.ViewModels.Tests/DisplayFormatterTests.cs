using System.Globalization;
using TransmissionKit;

namespace TransmissionRemote.ViewModels.Tests;

/// <summary>Sizes, speeds, durations and the torrent list's status, without words.</summary>
[TestClass]
public sealed class DisplayFormatterTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly DisplayFormatter Format = new(English, timeZone: TimeZoneInfo.Utc);

    [TestMethod]
    [DataRow(0L, "0 bytes")]
    [DataRow(-5L, "0 bytes")]
    [DataRow(532L, "532 bytes")]
    [DataRow(999L, "999 bytes")]
    [DataRow(1_000L, "0.97 KB")]
    [DataRow(1_340L, "1.30 KB")]
    [DataRow(23_506L, "22.9 KB")]
    [DataRow(1_047_552L, "0.99 MB")]
    [DataRow(1_048_576L, "1.00 MB")]
    [DataRow(2_400_016L, "2.28 MB")]
    [DataRow(2_400_000_000L, "2.23 GB")]
    [DataRow(5L << 40, "5.00 TB")]
    [DataRow(2_000L << 40, "2,000 TB")]
    public void WritesSizesLikeFileExplorer(long bytes, string expected)
    {
        Assert.AreEqual(expected, Format.Bytes(bytes));
    }

    [TestMethod]
    public void UsesTheCulturesDigitsAndTheAppsUnitLabels()
    {
        var german = new DisplayFormatter(
            CultureInfo.GetCultureInfo("de-DE"),
            new ByteUnitLabels { Bytes = "Bytes", Kilobytes = "KB", Megabytes = "MB", PerSecond = "/s" });

        Assert.AreEqual("1,30 KB", german.Bytes(1_340));
        Assert.AreEqual("532 Bytes", german.Bytes(532));
        Assert.AreEqual("1,00 MB/s", german.Speed(1_048_576));
        Assert.AreEqual("1,23", german.Ratio(1.234));
    }

    [TestMethod]
    public void WritesSpeedsAndLimits()
    {
        Assert.AreEqual("1.00 MB/s", Format.Speed(1_048_576));
        Assert.AreEqual("", Format.SpeedOrBlank(0));
        Assert.AreEqual("22.9 KB/s", Format.SpeedOrBlank(23_506));
        Assert.AreEqual("100 KB/s", Format.SpeedLimit(100), "a limit shows the number the user typed");
        Assert.AreEqual("10,000 KB/s", Format.SpeedLimit(10_000));
    }

    [TestMethod]
    [DataRow(0.453, "45.3%")]
    [DataRow(0.9999, "99.9%")]
    [DataRow(1.0, "100%")]
    [DataRow(1.2, "100%")]
    [DataRow(0.0, "0%")]
    [DataRow(double.NaN, "0%")]
    public void WritesPercentagesWithoutRoundingUpToDone(double fraction, string expected)
    {
        Assert.AreEqual(expected, Format.Percent(fraction));
    }

    [TestMethod]
    [DataRow(-1, "")]
    [DataRow(-2, "")]
    [DataRow(0, "0s")]
    [DataRow(45, "45s")]
    [DataRow(3_600, "1h")]
    [DataRow(3_661, "1h 1m")]
    [DataRow(3_599, "59m 59s")]
    [DataRow(90_061, "1d 1h")]
    [DataRow(259_259, "3d")]
    public void WritesEtasWithAtMostTwoUnits(int seconds, string expected)
    {
        Assert.AreEqual(expected, Format.Eta(seconds));
    }

    [TestMethod]
    public void PlacesDurationUnitsTheWayTheLanguageDoes()
    {
        var units = new DurationUnitLabels { Days = "{0} д", Hours = "{0} ч", Minutes = "{0} мин", Seconds = "{0} с" };
        var russian = new DisplayFormatter(CultureInfo.GetCultureInfo("ru-RU"), durationUnits: units);

        Assert.AreEqual("1 ч 1 мин", russian.Duration(3_661));
        Assert.AreEqual("0 с", russian.Duration(0));
    }

    [TestMethod]
    public void RefusesADurationFormatWithMoreThanOneNumber()
    {
        Assert.Throws<FormatException>(() =>
            new DisplayFormatter(English, durationUnits: new DurationUnitLabels { Hours = "{1}h" }));
        Assert.Throws<FormatException>(() =>
            new DisplayFormatter(English, durationUnits: new DurationUnitLabels { Minutes = "{0m" }));
    }

    [TestMethod]
    [DataRow(-1.0, "–")]
    [DataRow(-2.0, "∞")]
    [DataRow(double.NaN, "–")]
    [DataRow(0.0, "0.00")]
    [DataRow(1.234, "1.23")]
    [DataRow(1234.5, "1,234.50")]
    public void WritesRatios(double ratio, string expected)
    {
        Assert.AreEqual(expected, Format.Ratio(ratio));
    }

    [TestMethod]
    public void WritesDatesInTheGivenTimeZone()
    {
        var moscow = TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");
        var format = new DisplayFormatter(English, timeZone: moscow);

        var text = format.Date(new DateTimeOffset(2026, 11, 8, 12, 45, 0, TimeSpan.Zero));

        // The culture's own short date and time, whichever space ICU or Windows puts before PM.
        Assert.AreEqual(new DateTime(2026, 11, 8, 15, 45, 0).ToString("g", English), text);
        Assert.StartsWith("11/8/2026 3:45", text);
        Assert.AreEqual("", format.Date(null));
    }

    [TestMethod]
    public void WritesSeedsAndPeersWithTheSwarmWhenKnown()
    {
        var torrent = new Torrent { Id = 1, PeersSendingToUs = 3, PeersGettingFromUs = 0 };
        var withSwarm = torrent with { TrackerSummary = new TrackerSummary(1, 172, 1_234, TrackerStatus.Working) };

        Assert.AreEqual("3", Format.SeedsText(torrent));
        Assert.AreEqual("3 (172)", Format.SeedsText(withSwarm));
        Assert.AreEqual("0 (1,234)", Format.PeersText(withSwarm));
        Assert.AreEqual("1,234", Format.Count(1_234));
    }
}

[TestClass]
public sealed class TorrentDisplayTests
{
    [TestMethod]
    public void ShowsTheProgressThatMatters()
    {
        var torrent = new Torrent { Id = 1, PercentDone = 0.5, RecheckProgress = 0.2, MetadataPercentComplete = 1 };

        Assert.AreEqual(0.5, torrent.DisplayedProgress);
        Assert.AreEqual(0.2, (torrent with { Status = TorrentStatus.Verifying }).DisplayedProgress);
        Assert.AreEqual(0.3, (torrent with { MetadataPercentComplete = 0.3 }).DisplayedProgress);
    }

    [TestMethod]
    public void DescribesEachStatusLikeTheMacApp()
    {
        var done = new Torrent { Id = 1, LeftUntilDone = 0 };
        var unfinished = done with { LeftUntilDone = 10 };

        AssertStatus(done, TorrentStatusLabel.Finished, TorrentStatusIcon.Finished, TorrentStatusTone.Neutral);
        AssertStatus(unfinished, TorrentStatusLabel.Stopped, TorrentStatusIcon.Paused, TorrentStatusTone.Neutral);
        AssertStatus(
            unfinished with { Status = TorrentStatus.QueuedToVerify },
            TorrentStatusLabel.QueuedToVerify, TorrentStatusIcon.Queued, TorrentStatusTone.Neutral);
        AssertStatus(
            unfinished with { Status = TorrentStatus.QueuedToDownload },
            TorrentStatusLabel.Queued, TorrentStatusIcon.Queued, TorrentStatusTone.Neutral);
        AssertStatus(
            unfinished with { Status = TorrentStatus.Downloading },
            TorrentStatusLabel.Downloading, TorrentStatusIcon.Downloading, TorrentStatusTone.Downloading);
        AssertStatus(
            unfinished with { Status = TorrentStatus.Downloading, MetadataPercentComplete = 0.4 },
            TorrentStatusLabel.GettingMetadata, TorrentStatusIcon.Downloading, TorrentStatusTone.Downloading);
        AssertStatus(
            done with { Status = TorrentStatus.QueuedToSeed },
            TorrentStatusLabel.QueuedToSeed, TorrentStatusIcon.Queued, TorrentStatusTone.Neutral);
        AssertStatus(
            done with { Status = TorrentStatus.Seeding },
            TorrentStatusLabel.Seeding, TorrentStatusIcon.Seeding, TorrentStatusTone.Seeding);
    }

    [TestMethod]
    public void CarriesTheVerifyProgressAndTheErrorMessage()
    {
        var verifying = new Torrent { Id = 1, Status = TorrentStatus.Verifying, RecheckProgress = 0.453 };
        var failed = new Torrent { Id = 1, ErrorCode = 3, ErrorString = "No data found" };

        Assert.AreEqual(
            new TorrentDisplayStatus(
                TorrentStatusLabel.Verifying, TorrentStatusIcon.Verifying, TorrentStatusTone.Verifying,
                VerifyProgress: 0.453),
            verifying.DisplayStatus);
        Assert.AreEqual(
            new TorrentDisplayStatus(
                TorrentStatusLabel.Error, TorrentStatusIcon.Warning, TorrentStatusTone.Warning, "No data found"),
            failed.DisplayStatus);
    }

    [TestMethod]
    public void AnErrorWithoutAMessageKeepsItsLabelButShowsTheWarning()
    {
        var torrent = new Torrent { Id = 1, ErrorCode = 1, Status = TorrentStatus.Downloading, LeftUntilDone = 5 };

        AssertStatus(torrent, TorrentStatusLabel.Downloading, TorrentStatusIcon.Warning, TorrentStatusTone.Warning);
        Assert.IsNull(torrent.DisplayStatus.ErrorMessage);
    }

    [TestMethod]
    public void SortsMissingValuesPredictably()
    {
        var torrent = new Torrent { Id = 1, Eta = -1, IsPrivate = true };

        Assert.AreEqual(int.MaxValue, torrent.EtaSortKey);
        Assert.AreEqual(90, (torrent with { Eta = 90 }).EtaSortKey);
        Assert.AreEqual(DateTimeOffset.MinValue, torrent.AddedSortKey);
        Assert.AreEqual(DateTimeOffset.MinValue, torrent.DoneSortKey);
        Assert.AreEqual(DateTimeOffset.MinValue, torrent.ActivitySortKey);
        Assert.AreEqual(1, torrent.PrivateSortKey);
    }

    [TestMethod]
    public void SortsByConnectedPeersThenBySwarmSize()
    {
        PeerCountSortKey[] keys = [new(4, null), new(3, 10), new(3, null), new(3, 0), new(0, 500)];

        var sorted = keys.Order().ToArray();

        CollectionAssert.AreEqual(
            new PeerCountSortKey[] { new(0, 500), new(3, null), new(3, 0), new(3, 10), new(4, null) }, sorted);
        Assert.IsTrue(new PeerCountSortKey(3, null) < new PeerCountSortKey(3, 0));

        var torrent = new Torrent
        {
            Id = 1,
            PeersSendingToUs = 2,
            PeersGettingFromUs = 5,
            TrackerSummary = new TrackerSummary(1, 40, 7, TrackerStatus.Working),
        };
        Assert.AreEqual(new PeerCountSortKey(2, 40), torrent.SeedsSortKey);
        Assert.AreEqual(new PeerCountSortKey(5, 7), torrent.PeersSortKey);
    }

    [TestMethod]
    public void JoinsLabelsAndPicksTheFirstTracker()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Labels = ["linux", "iso"],
            TrackerHosts = ["a.example.org", "b.example.org"],
        };

        Assert.AreEqual("linux, iso", torrent.LabelsText);
        Assert.AreEqual("a.example.org", torrent.PrimaryTracker);
        Assert.AreEqual("", new Torrent { Id = 2 }.PrimaryTracker);
    }

    private static void AssertStatus(
        Torrent torrent, TorrentStatusLabel label, TorrentStatusIcon icon, TorrentStatusTone tone)
    {
        var status = torrent.DisplayStatus;
        Assert.AreEqual(label, status.Label, $"label for {torrent.Status}");
        Assert.AreEqual(icon, status.Icon, $"icon for {torrent.Status}");
        Assert.AreEqual(tone, status.Tone, $"tone for {torrent.Status}");
    }
}
