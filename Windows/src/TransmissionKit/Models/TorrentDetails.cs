using System.Text.Json.Nodes;

namespace TransmissionKit;

/// <summary>
/// <c>tr_ratiolimit</c> and <c>tr_idlelimit</c>: whether a torrent follows the session default,
/// its own limit, or no limit at all.
/// </summary>
public enum LimitMode
{
    Global = 0,

    /// <summary>The torrent's own limit; <c>TR_RATIOLIMIT_SINGLE</c> in libtransmission.</summary>
    PerTorrent = 1,
    Unlimited = 2,
}

/// <summary>
/// Everything shown in the detail pane for one torrent. Which properties are filled depends on
/// the fields requested, so collections default to empty.
/// </summary>
public sealed record TorrentDetails
{
    /// <summary>Always requested, cheap fields for the General tab.</summary>
    public static IReadOnlyList<TorrentField> GeneralFields { get; } =
    [
        TorrentField.Id, TorrentField.Pieces, TorrentField.PieceCount, TorrentField.PieceSize, TorrentField.Comment,
        TorrentField.Creator, TorrentField.DateCreated, TorrentField.StartDate, TorrentField.HaveValid,
        TorrentField.HaveUnchecked, TorrentField.CorruptEver, TorrentField.DesiredAvailable,
        TorrentField.SecondsDownloading, TorrentField.SecondsSeeding, TorrentField.WebseedsSendingToUs,
    ];

    public static IReadOnlyList<TorrentField> FileFields { get; } =
        [TorrentField.Id, TorrentField.Files, TorrentField.FileStats];

    public static IReadOnlyList<TorrentField> PeerFields { get; } = [TorrentField.Id, TorrentField.Peers];

    public static IReadOnlyList<TorrentField> TrackerFields { get; } =
        [TorrentField.Id, TorrentField.TrackerStats, TorrentField.TrackerList];

    public static IReadOnlyList<TorrentField> OptionFields { get; } =
    [
        TorrentField.Id, TorrentField.DownloadLimit, TorrentField.DownloadLimited, TorrentField.UploadLimit,
        TorrentField.UploadLimited, TorrentField.SeedRatioLimit, TorrentField.SeedRatioMode,
        TorrentField.SeedIdleLimit, TorrentField.SeedIdleMode, TorrentField.HonorsSessionLimits,
        TorrentField.PeerLimit, TorrentField.BandwidthPriority, TorrentField.SequentialDownload, TorrentField.Group,
    ];

    public required int Id { get; init; }
    public IReadOnlyList<FileEntry> Files { get; init; } = [];
    public IReadOnlyList<Peer> Peers { get; init; } = [];
    public IReadOnlyList<TrackerStat> TrackerStats { get; init; } = [];

    /// <summary>One bit per piece, most significant bit first.</summary>
    public byte[]? PiecesBitfield { get; init; }

    public int PieceCount { get; init; }
    public long PieceSize { get; init; }
    public string Comment { get; init; } = "";
    public string Creator { get; init; } = "";
    public DateTimeOffset? DateCreated { get; init; }
    public DateTimeOffset? StartDate { get; init; }
    public string MagnetLink { get; init; } = "";
    public long HaveValid { get; init; }
    public long HaveUnchecked { get; init; }
    public long CorruptEver { get; init; }
    public long DesiredAvailable { get; init; }
    public int SecondsDownloading { get; init; }
    public int SecondsSeeding { get; init; }
    public int DownloadLimit { get; init; }
    public bool DownloadLimited { get; init; }
    public int UploadLimit { get; init; }
    public bool UploadLimited { get; init; }
    public double SeedRatioLimit { get; init; }
    public LimitMode SeedRatioMode { get; init; }
    public int SeedIdleLimit { get; init; }
    public LimitMode SeedIdleMode { get; init; }
    public bool HonorsSessionLimits { get; init; } = true;
    public int PeerLimit { get; init; }
    public bool? SequentialDownload { get; init; }
    public string? TrackerList { get; init; }
    public int WebseedsSendingToUs { get; init; }

    /// <summary>Transmission 4.0 and newer; null on older servers.</summary>
    public string? BandwidthGroup { get; init; }

    internal static TorrentDetails FromJson(JsonObject json) => new()
    {
        Id = json.GetInt32("id") ?? throw TransmissionException.InvalidResponse("a torrent has no id"),
        Files = CombineFiles(json.GetObjects("files").ToList(), json.GetObjects("fileStats").ToList()),
        Peers = json.GetObjects("peers").Select(Peer.FromJson).ToList(),
        TrackerStats = json.GetObjects("trackerStats").Select(TrackerStat.FromJson).ToList(),
        PiecesBitfield = DecodeBase64(json.GetString("pieces")),
        PieceCount = json.GetInt32("pieceCount") ?? 0,
        PieceSize = json.GetInt64("pieceSize") ?? 0,
        Comment = json.GetString("comment") ?? "",
        Creator = json.GetString("creator") ?? "",
        DateCreated = json.GetDate("dateCreated"),
        StartDate = json.GetDate("startDate"),
        MagnetLink = json.GetString("magnetLink") ?? "",
        HaveValid = json.GetInt64("haveValid") ?? 0,
        HaveUnchecked = json.GetInt64("haveUnchecked") ?? 0,
        CorruptEver = json.GetInt64("corruptEver") ?? 0,
        DesiredAvailable = json.GetInt64("desiredAvailable") ?? 0,
        SecondsDownloading = json.GetInt32("secondsDownloading") ?? 0,
        SecondsSeeding = json.GetInt32("secondsSeeding") ?? 0,
        DownloadLimit = json.GetInt32("downloadLimit") ?? 0,
        DownloadLimited = json.GetBoolean("downloadLimited") ?? false,
        UploadLimit = json.GetInt32("uploadLimit") ?? 0,
        UploadLimited = json.GetBoolean("uploadLimited") ?? false,
        SeedRatioLimit = json.GetDouble("seedRatioLimit") ?? 0,
        SeedRatioMode = json.GetEnum("seedRatioMode", LimitMode.Global),
        SeedIdleLimit = json.GetInt32("seedIdleLimit") ?? 0,
        SeedIdleMode = json.GetEnum("seedIdleMode", LimitMode.Global),
        HonorsSessionLimits = json.GetBoolean("honorsSessionLimits") ?? true,
        PeerLimit = json.GetInt32("peerLimit") ?? 0,
        SequentialDownload = json.GetBoolean("sequentialDownload"),
        TrackerList = json.GetString("trackerList"),
        WebseedsSendingToUs = json.GetInt32("webseedsSendingToUs") ?? 0,
        BandwidthGroup = json.GetString("group"),
    };

    /// <summary>Joins the static <c>files</c> entries with their <c>fileStats</c>, by position.</summary>
    internal static List<FileEntry> CombineFiles(IReadOnlyList<JsonObject> files, IReadOnlyList<JsonObject> stats) =>
        files.Select((file, index) =>
        {
            var stat = index < stats.Count ? stats[index] : null;
            return new FileEntry(index, file.GetString("name") ?? "", file.GetInt64("length") ?? 0)
            {
                BytesCompleted = stat?.GetInt64("bytesCompleted") ?? file.GetInt64("bytesCompleted") ?? 0,
                // 0 or 1 on legacy servers.
                Wanted = stat?.GetBoolean("wanted") ?? true,
                Priority = stat?.GetEnum("priority", Priority.Normal) ?? Priority.Normal,
            };
        }).ToList();

    private static byte[]? DecodeBase64(string? encoded)
    {
        if (encoded is null)
        {
            return null;
        }
        var buffer = new byte[encoded.Length * 3 / 4 + 3];
        return Convert.TryFromBase64String(encoded, buffer, out var length) ? buffer[..length] : null;
    }
}

public sealed record Peer
{
    public string Address { get; init; } = "";
    public int Port { get; init; }
    public string ClientName { get; init; } = "";

    /// <summary>Transmission's compact flag string, such as <c>"TDEI"</c>. See the peer-status docs.</summary>
    public string FlagStr { get; init; } = "";

    public double Progress { get; init; }
    public long RateToClient { get; init; }
    public long RateToPeer { get; init; }
    public bool IsEncrypted { get; init; }
    public bool IsUtp { get; init; }
    public bool IsIncoming { get; init; }

    public string Id => $"{Address}:{Port}";

    internal static Peer FromJson(JsonObject json) => new()
    {
        Address = json.GetString("address") ?? "",
        Port = json.GetInt32("port") ?? 0,
        ClientName = json.GetString("clientName") ?? "",
        FlagStr = json.GetString("flagStr") ?? "",
        Progress = json.GetDouble("progress") ?? 0,
        RateToClient = json.GetInt64("rateToClient") ?? 0,
        RateToPeer = json.GetInt64("rateToPeer") ?? 0,
        IsEncrypted = json.GetBoolean("isEncrypted") ?? false,
        IsUtp = json.GetBoolean("isUtp") ?? false,
        IsIncoming = json.GetBoolean("isIncoming") ?? false,
    };
}

public sealed record TrackerStat
{
    public int Id { get; init; }
    public int Tier { get; init; }
    public string Announce { get; init; } = "";
    public string Host { get; init; } = "";
    public string LastAnnounceResult { get; init; } = "";
    public bool LastAnnounceSucceeded { get; init; }
    public DateTimeOffset? LastAnnounceTime { get; init; }
    public DateTimeOffset? NextAnnounceTime { get; init; }
    public int LastAnnouncePeerCount { get; init; }
    public int SeederCount { get; init; }
    public int LeecherCount { get; init; }
    public int DownloadCount { get; init; }
    public bool IsBackup { get; init; }
    public bool HasAnnounced { get; init; }

    internal static TrackerStat FromJson(JsonObject json)
    {
        var announce = json.GetString("announce") ?? "";
        var announceHost = Uri.TryCreate(announce, UriKind.Absolute, out var uri) ? uri.Host : announce;
        return new TrackerStat
        {
            Id = json.GetInt32("id") ?? 0,
            Tier = json.GetInt32("tier") ?? 0,
            Announce = announce,
            Host = json.GetString("host") ?? announceHost,
            LastAnnounceResult = json.GetString("lastAnnounceResult") ?? "",
            LastAnnounceSucceeded = json.GetBoolean("lastAnnounceSucceeded") ?? false,
            LastAnnounceTime = json.GetDate("lastAnnounceTime"),
            NextAnnounceTime = json.GetDate("nextAnnounceTime"),
            LastAnnouncePeerCount = json.GetInt32("lastAnnouncePeerCount") ?? 0,
            // Trackers report -1 until they've been scraped.
            SeederCount = Math.Max(json.GetInt32("seederCount") ?? 0, 0),
            LeecherCount = Math.Max(json.GetInt32("leecherCount") ?? 0, 0),
            DownloadCount = Math.Max(json.GetInt32("downloadCount") ?? 0, 0),
            IsBackup = json.GetBoolean("isBackup") ?? false,
            HasAnnounced = json.GetBoolean("hasAnnounced") ?? false,
        };
    }
}
