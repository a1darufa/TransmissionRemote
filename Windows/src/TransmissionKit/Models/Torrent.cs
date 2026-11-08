using System.Collections.Frozen;
using System.Text.Json.Nodes;

namespace TransmissionKit;

/// <summary><c>tr_torrent_activity</c> from libtransmission.</summary>
public enum TorrentStatus
{
    Stopped = 0,
    QueuedToVerify = 1,
    Verifying = 2,
    QueuedToDownload = 3,
    Downloading = 4,
    QueuedToSeed = 5,
    Seeding = 6,
}

public static class TorrentStatusExtensions
{
    public static bool IsQueued(this TorrentStatus status) =>
        status is TorrentStatus.QueuedToVerify or TorrentStatus.QueuedToDownload or TorrentStatus.QueuedToSeed;
}

/// <summary>Fields that can be requested from <c>torrent-get</c>.</summary>
public enum TorrentField
{
    ActivityDate, AddedDate, BandwidthPriority, Comment, CorruptEver, Creator, DateCreated,
    DesiredAvailable, DoneDate, DownloadDir, DownloadedEver, DownloadLimit, DownloadLimited,
    Error, ErrorString, Eta, FileStats, Files, Group, HashString, HaveUnchecked, HaveValid,
    HonorsSessionLimits, Id, IsFinished, IsPrivate, IsStalled, Labels, LeftUntilDone,
    MagnetLink, MetadataPercentComplete, Name, PeerLimit, Peers, PeersConnected, PeersFrom,
    PeersGettingFromUs, PeersSendingToUs, PercentDone, PieceCount, PieceSize, Pieces,
    QueuePosition, RateDownload, RateUpload, RecheckProgress, SecondsDownloading,
    SecondsSeeding, SeedIdleLimit, SeedIdleMode, SeedRatioLimit, SeedRatioMode,
    SequentialDownload, SizeWhenDone, StartDate, Status, TotalSize, TrackerList,
    TrackerStats, Trackers, UploadLimit, UploadLimited, UploadRatio, UploadedEver,
    WebseedsSendingToUs,
}

public static class TorrentFieldExtensions
{
    /// <summary>The canonical camelCase names: the enum names with a lower-case first letter.</summary>
    private static readonly FrozenDictionary<TorrentField, string> CamelNames =
        Enum.GetValues<TorrentField>().ToFrozenDictionary(
            field => field,
            field => string.Concat(field.ToString()[..1].ToLowerInvariant(), field.ToString()[1..]));

    public static string CamelName(this TorrentField field) => CamelNames[field];

    /// <summary>
    /// The <c>rpc-version</c> that introduced this field. Older servers silently drop unknown fields
    /// on some releases and reject the request on others, so only known fields are requested.
    /// </summary>
    public static int MinimumRpcVersion(this TorrentField field) => field switch
    {
        TorrentField.Labels => 16, // Transmission 3.00
        TorrentField.TrackerList or TorrentField.Group => 17, // Transmission 4.0
        TorrentField.SequentialDownload => 18, // Transmission 4.1
        _ => ServerInfo.MinimumRpcVersion,
    };
}

/// <summary>A torrent as shown in the main list. Detail-only data lives in <see cref="TorrentDetails"/>.</summary>
public sealed record Torrent
{
    /// <summary>Fields needed to fill a <see cref="Torrent"/>.</summary>
    public static IReadOnlyList<TorrentField> ListFields { get; } =
    [
        TorrentField.Id, TorrentField.Name, TorrentField.HashString, TorrentField.Status, TorrentField.Error,
        TorrentField.ErrorString, TorrentField.TotalSize, TorrentField.SizeWhenDone, TorrentField.LeftUntilDone,
        TorrentField.PercentDone, TorrentField.MetadataPercentComplete, TorrentField.RecheckProgress,
        TorrentField.RateDownload, TorrentField.RateUpload, TorrentField.Eta, TorrentField.UploadRatio,
        TorrentField.UploadedEver, TorrentField.DownloadedEver, TorrentField.AddedDate, TorrentField.DoneDate,
        TorrentField.ActivityDate, TorrentField.SecondsSeeding, TorrentField.DownloadDir, TorrentField.Labels,
        TorrentField.QueuePosition, TorrentField.PeersConnected, TorrentField.PeersSendingToUs,
        TorrentField.PeersGettingFromUs, TorrentField.IsFinished, TorrentField.IsStalled, TorrentField.IsPrivate,
        TorrentField.BandwidthPriority, TorrentField.Trackers, TorrentField.Group,
    ];

    public required int Id { get; init; }
    public string Name { get; init; } = "";
    public string HashString { get; init; } = "";
    public TorrentStatus Status { get; init; }

    /// <summary>0 = none, 1 = tracker warning, 2 = tracker error, 3 = local error.</summary>
    public int ErrorCode { get; init; }

    public string ErrorString { get; init; } = "";
    public long TotalSize { get; init; }
    public long SizeWhenDone { get; init; }
    public long LeftUntilDone { get; init; }
    public double PercentDone { get; init; }
    public double MetadataPercentComplete { get; init; } = 1;
    public double RecheckProgress { get; init; }
    public long RateDownload { get; init; }
    public long RateUpload { get; init; }

    /// <summary>Seconds; negative values mean "not available" (-1) or "unknown" (-2).</summary>
    public int Eta { get; init; } = -1;

    /// <summary>Negative values mean "not available" (-1) or "infinite" (-2).</summary>
    public double UploadRatio { get; init; } = -1;

    public long UploadedEver { get; init; }
    public long DownloadedEver { get; init; }
    public DateTimeOffset? AddedDate { get; init; }
    public DateTimeOffset? DoneDate { get; init; }
    public DateTimeOffset? ActivityDate { get; init; }

    /// <summary>How long the torrent has been seeding, in seconds, across restarts.</summary>
    public int SecondsSeeding { get; init; }

    public string DownloadDir { get; init; } = "";
    public IReadOnlyList<string> Labels { get; init; } = [];
    public int QueuePosition { get; init; }
    public int PeersConnected { get; init; }
    public int PeersSendingToUs { get; init; }
    public int PeersGettingFromUs { get; init; }
    public bool IsFinished { get; init; }
    public bool IsStalled { get; init; }
    public bool IsPrivate { get; init; }
    public Priority BandwidthPriority { get; init; } = Priority.Normal;

    /// <summary>
    /// The bandwidth group's name, or empty when the torrent isn't in one or the server predates groups.
    /// </summary>
    public string BandwidthGroup { get; init; } = "";

    /// <summary>Hosts of the torrent's trackers, in tier order.</summary>
    public IReadOnlyList<string> TrackerHosts { get; init; } = [];

    /// <summary>
    /// Swarm sizes and tracker status. Not part of the list request: the app fetches these less
    /// often with <see cref="TransmissionClient.GetTrackerSummariesAsync"/> and attaches them.
    /// </summary>
    public TrackerSummary? TrackerSummary { get; init; }

    public bool HasError => ErrorCode != 0;
    public bool IsComplete => LeftUntilDone == 0 && MetadataPercentComplete >= 1;
    public bool IsActive => RateDownload > 0 || RateUpload > 0;

    internal static Torrent FromJson(JsonObject json) => new()
    {
        Id = json.GetInt32("id") ?? throw TransmissionException.InvalidResponse("a torrent has no id"),
        Name = json.GetString("name") ?? "",
        HashString = json.GetString("hashString") ?? "",
        Status = json.GetEnum("status", TorrentStatus.Stopped),
        ErrorCode = json.GetInt32("error") ?? 0,
        ErrorString = json.GetString("errorString") ?? "",
        TotalSize = json.GetInt64("totalSize") ?? 0,
        SizeWhenDone = json.GetInt64("sizeWhenDone") ?? 0,
        LeftUntilDone = json.GetInt64("leftUntilDone") ?? 0,
        PercentDone = json.GetDouble("percentDone") ?? 0,
        MetadataPercentComplete = json.GetDouble("metadataPercentComplete") ?? 1,
        RecheckProgress = json.GetDouble("recheckProgress") ?? 0,
        RateDownload = json.GetInt64("rateDownload") ?? 0,
        RateUpload = json.GetInt64("rateUpload") ?? 0,
        Eta = json.GetInt32("eta") ?? -1,
        UploadRatio = json.GetDouble("uploadRatio") ?? -1,
        UploadedEver = json.GetInt64("uploadedEver") ?? 0,
        DownloadedEver = json.GetInt64("downloadedEver") ?? 0,
        AddedDate = json.GetDate("addedDate"),
        DoneDate = json.GetDate("doneDate"),
        ActivityDate = json.GetDate("activityDate"),
        SecondsSeeding = json.GetInt32("secondsSeeding") ?? 0,
        DownloadDir = json.GetString("downloadDir") ?? "",
        Labels = json.GetStrings("labels"),
        QueuePosition = json.GetInt32("queuePosition") ?? 0,
        PeersConnected = json.GetInt32("peersConnected") ?? 0,
        PeersSendingToUs = json.GetInt32("peersSendingToUs") ?? 0,
        PeersGettingFromUs = json.GetInt32("peersGettingFromUs") ?? 0,
        IsFinished = json.GetBoolean("isFinished") ?? false,
        IsStalled = json.GetBoolean("isStalled") ?? false,
        IsPrivate = json.GetBoolean("isPrivate") ?? false,
        BandwidthPriority = json.GetEnum("bandwidthPriority", Priority.Normal),
        BandwidthGroup = json.GetString("group") ?? "",
        TrackerHosts = UniqueHosts(json.GetObjects("trackers").Select(tracker => tracker.GetString("announce"))),
    };

    internal static List<string> UniqueHosts(IEnumerable<string?> announceUrls)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hosts = new List<string>();
        foreach (var announceUrl in announceUrls)
        {
            if (Uri.TryCreate(announceUrl, UriKind.Absolute, out var uri) && uri.Host.Length > 0)
            {
                var host = uri.Host.ToLowerInvariant();
                if (seen.Add(host))
                {
                    hosts.Add(host);
                }
            }
        }
        return hosts;
    }
}
