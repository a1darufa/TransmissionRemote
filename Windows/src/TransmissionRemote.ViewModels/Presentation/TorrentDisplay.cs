using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>What the Status column says, without the words: the app maps each to a localized string.</summary>
public enum TorrentStatusLabel
{
    /// <summary>"Error: …" with <see cref="TorrentDisplayStatus.ErrorMessage"/>.</summary>
    Error,
    Finished,
    Stopped,
    QueuedToVerify,

    /// <summary>"Verifying 45.3%" with <see cref="TorrentDisplayStatus.VerifyProgress"/>.</summary>
    Verifying,
    Queued,
    GettingMetadata,
    Downloading,
    QueuedToSeed,
    Seeding,
}

/// <summary>The status glyph, which the app maps to a Segoe Fluent Icons symbol.</summary>
public enum TorrentStatusIcon
{
    Warning,
    Finished,
    Paused,
    Queued,
    Verifying,
    Downloading,
    Seeding,
}

/// <summary>The status color, which the app maps to theme colors.</summary>
public enum TorrentStatusTone
{
    /// <summary>Orange on the Mac.</summary>
    Warning,

    /// <summary>Blue on the Mac.</summary>
    Downloading,

    /// <summary>Green on the Mac.</summary>
    Seeding,

    /// <summary>Purple on the Mac.</summary>
    Verifying,

    /// <summary>The secondary text color.</summary>
    Neutral,
}

/// <summary>
/// A torrent's status for the list: the Mac app's <c>statusText</c>, <c>statusSymbol</c> and <c>statusColor</c>.
/// </summary>
/// <remarks>
/// A torrent with an error but no message shows its usual label next to the warning icon, as on the Mac.
/// </remarks>
public readonly record struct TorrentDisplayStatus(
    TorrentStatusLabel Label,
    TorrentStatusIcon Icon,
    TorrentStatusTone Tone,
    string? ErrorMessage = null,
    double? VerifyProgress = null)
{
    public static TorrentDisplayStatus Of(Torrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        var label = LabelOf(torrent);
        return new TorrentDisplayStatus(
            label,
            IconOf(torrent),
            ToneOf(torrent),
            label == TorrentStatusLabel.Error ? torrent.ErrorString : null,
            label == TorrentStatusLabel.Verifying ? torrent.RecheckProgress : null);
    }

    private static TorrentStatusLabel LabelOf(Torrent torrent)
    {
        if (torrent.HasError && torrent.ErrorString.Length > 0)
        {
            return TorrentStatusLabel.Error;
        }
        return torrent.Status switch
        {
            TorrentStatus.Stopped => torrent.IsComplete ? TorrentStatusLabel.Finished : TorrentStatusLabel.Stopped,
            TorrentStatus.QueuedToVerify => TorrentStatusLabel.QueuedToVerify,
            TorrentStatus.Verifying => TorrentStatusLabel.Verifying,
            TorrentStatus.QueuedToDownload => TorrentStatusLabel.Queued,
            TorrentStatus.Downloading => torrent.MetadataPercentComplete < 1
                ? TorrentStatusLabel.GettingMetadata
                : TorrentStatusLabel.Downloading,
            TorrentStatus.QueuedToSeed => TorrentStatusLabel.QueuedToSeed,
            _ => TorrentStatusLabel.Seeding,
        };
    }

    private static TorrentStatusIcon IconOf(Torrent torrent) => torrent.Status switch
    {
        _ when torrent.HasError => TorrentStatusIcon.Warning,
        TorrentStatus.Stopped => torrent.IsComplete ? TorrentStatusIcon.Finished : TorrentStatusIcon.Paused,
        TorrentStatus.Verifying => TorrentStatusIcon.Verifying,
        TorrentStatus.Downloading => TorrentStatusIcon.Downloading,
        TorrentStatus.Seeding => TorrentStatusIcon.Seeding,
        _ => TorrentStatusIcon.Queued,
    };

    private static TorrentStatusTone ToneOf(Torrent torrent) => torrent.Status switch
    {
        _ when torrent.HasError => TorrentStatusTone.Warning,
        TorrentStatus.Downloading => TorrentStatusTone.Downloading,
        TorrentStatus.Seeding => TorrentStatusTone.Seeding,
        TorrentStatus.Verifying => TorrentStatusTone.Verifying,
        _ => TorrentStatusTone.Neutral,
    };
}

/// <summary>
/// Sorts by connected peers first, then the swarm's size, so idle torrents still sort by popularity instead of all
/// tying at zero (transgui issue #1180). An unknown swarm sorts below a known one.
/// </summary>
public readonly record struct PeerCountSortKey(int Connected, int? Swarm) : IComparable<PeerCountSortKey>, IComparable
{
    public int CompareTo(PeerCountSortKey other)
    {
        var byConnected = Connected.CompareTo(other.Connected);
        return byConnected != 0 ? byConnected : (Swarm ?? -1).CompareTo(other.Swarm ?? -1);
    }

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        PeerCountSortKey other => CompareTo(other),
        _ => throw new ArgumentException($"Can only compare with another {nameof(PeerCountSortKey)}.", nameof(obj)),
    };

    public static bool operator <(PeerCountSortKey left, PeerCountSortKey right) => left.CompareTo(right) < 0;

    public static bool operator <=(PeerCountSortKey left, PeerCountSortKey right) => left.CompareTo(right) <= 0;

    public static bool operator >(PeerCountSortKey left, PeerCountSortKey right) => left.CompareTo(right) > 0;

    public static bool operator >=(PeerCountSortKey left, PeerCountSortKey right) => left.CompareTo(right) >= 0;
}

/// <summary>The torrent list's values that need no words or culture: progress, sort keys and joined text.</summary>
public static class TorrentDisplay
{
    extension(Torrent torrent)
    {
        /// <summary>The progress worth showing: verification, metadata download, or data.</summary>
        public double DisplayedProgress => torrent.Status == TorrentStatus.Verifying ? torrent.RecheckProgress
            : torrent.MetadataPercentComplete < 1 ? torrent.MetadataPercentComplete
            : torrent.PercentDone;

        public TorrentDisplayStatus DisplayStatus => TorrentDisplayStatus.Of(torrent);

        // Comparable stand-ins for values that may be missing, so every column can sort.

        /// <summary>Unknown ETAs sort last.</summary>
        public int EtaSortKey => torrent.Eta >= 0 ? torrent.Eta : int.MaxValue;

        public DateTimeOffset AddedSortKey => torrent.AddedDate ?? DateTimeOffset.MinValue;
        public DateTimeOffset DoneSortKey => torrent.DoneDate ?? DateTimeOffset.MinValue;
        public DateTimeOffset ActivitySortKey => torrent.ActivityDate ?? DateTimeOffset.MinValue;
        public PeerCountSortKey SeedsSortKey => new(torrent.PeersSendingToUs, torrent.TrackerSummary?.Seeders);
        public PeerCountSortKey PeersSortKey => new(torrent.PeersGettingFromUs, torrent.TrackerSummary?.Leechers);
        public int PrivateSortKey => torrent.IsPrivate ? 1 : 0;

        public string LabelsText => string.Join(", ", torrent.Labels);

        /// <summary>The first tracker's host, for the Tracker column.</summary>
        public string PrimaryTracker => torrent.TrackerHosts.Count > 0 ? torrent.TrackerHosts[0] : "";
    }
}
