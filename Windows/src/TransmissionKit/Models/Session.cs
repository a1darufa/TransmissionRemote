using System.Globalization;
using System.Text.Json.Nodes;

namespace TransmissionKit;

/// <summary>The daemon's version, read when connecting.</summary>
public sealed record ServerInfo(string Version, int RpcVersion, string? RpcVersionSemver)
{
    /// <summary>Transmission 2.40, the oldest version this app supports.</summary>
    public const int MinimumRpcVersion = 14;

    internal static readonly string[] FieldNames = ["version", "rpcVersion", "rpcVersionSemver"];

    /// <summary>Transmission 4.1 (RPC semver 6) and newer speak JSON-RPC 2.0.</summary>
    public bool SupportsJsonRpc =>
        RpcVersionSemver?.Split('.')[0] is { } major
        && int.TryParse(major, NumberStyles.None, CultureInfo.InvariantCulture, out var majorVersion)
        && majorVersion >= 6;

    /// <summary>Renaming files and folders arrived in Transmission 2.80.</summary>
    public bool SupportsRenaming => RpcVersion >= 15;

    /// <summary>Labels arrived in Transmission 3.00.</summary>
    public bool SupportsLabels => RpcVersion >= 16;

    public bool SupportsLabelsOnAdd => RpcVersion >= 17;

    /// <summary>Editing trackers as one list arrived in Transmission 4.0.</summary>
    public bool SupportsTrackerList => RpcVersion >= 17;

    public bool SupportsSequentialDownload => RpcVersion >= 18;

    internal static ServerInfo FromJson(JsonObject json) => new(
        json.GetString("version") ?? "unknown",
        json.GetInt32("rpcVersion") ?? 0,
        json.GetString("rpcVersionSemver"));
}

public enum EncryptionMode
{
    Required,
    Preferred,
    Allowed,
}

internal static class EncryptionModeWire
{
    /// <summary>The legacy protocol calls "allowed" "tolerated".</summary>
    public static EncryptionMode? FromWire(string? value) => value switch
    {
        null => null,
        "required" => EncryptionMode.Required,
        "allowed" or "tolerated" => EncryptionMode.Allowed,
        _ => EncryptionMode.Preferred,
    };

    public static string ToWire(this EncryptionMode mode, RpcDialect dialect) => mode switch
    {
        EncryptionMode.Required => "required",
        EncryptionMode.Allowed => dialect == RpcDialect.Legacy ? "tolerated" : "allowed",
        _ => "preferred",
    };
}

/// <summary>
/// The daemon's settings. Every value is optional: reading fills what the server reports, and
/// writing sends only the values that are set, so <see cref="ChangesFrom"/> builds the update.
/// </summary>
public sealed record SessionSettings
{
    /// <summary>Reported by the server but not settable.</summary>
    private static readonly HashSet<string> ReadOnlyKeys = ["blocklistSize", "configDir", "version"];

    public int? AltSpeedDown { get; init; }
    public bool? AltSpeedEnabled { get; init; }
    public int? AltSpeedTimeBegin { get; init; }
    public int? AltSpeedTimeDay { get; init; }
    public bool? AltSpeedTimeEnabled { get; init; }
    public int? AltSpeedTimeEnd { get; init; }
    public int? AltSpeedUp { get; init; }
    public bool? BlocklistEnabled { get; init; }
    public int? BlocklistSize { get; init; }
    public string? BlocklistUrl { get; init; }
    public int? CacheSizeMb { get; init; }
    public string? ConfigDir { get; init; }
    public bool? DhtEnabled { get; init; }
    public string? DownloadDir { get; init; }
    public bool? DownloadQueueEnabled { get; init; }
    public int? DownloadQueueSize { get; init; }
    public EncryptionMode? Encryption { get; init; }
    public int? IdleSeedingLimit { get; init; }
    public bool? IdleSeedingLimitEnabled { get; init; }
    public string? IncompleteDir { get; init; }
    public bool? IncompleteDirEnabled { get; init; }
    public bool? LpdEnabled { get; init; }
    public int? PeerLimitGlobal { get; init; }
    public int? PeerLimitPerTorrent { get; init; }
    public int? PeerPort { get; init; }
    public bool? PeerPortRandomOnStart { get; init; }
    public bool? PexEnabled { get; init; }
    public bool? PortForwardingEnabled { get; init; }
    public bool? QueueStalledEnabled { get; init; }
    public int? QueueStalledMinutes { get; init; }
    public bool? RenamePartialFiles { get; init; }
    public bool? SeedQueueEnabled { get; init; }
    public int? SeedQueueSize { get; init; }
    public double? SeedRatioLimit { get; init; }
    public bool? SeedRatioLimited { get; init; }
    public bool? SequentialDownload { get; init; }
    public int? SpeedLimitDown { get; init; }
    public bool? SpeedLimitDownEnabled { get; init; }
    public int? SpeedLimitUp { get; init; }
    public bool? SpeedLimitUpEnabled { get; init; }
    public bool? StartAddedTorrents { get; init; }
    public bool? TrashOriginalTorrentFiles { get; init; }
    public bool? UtpEnabled { get; init; }
    public string? Version { get; init; }

    public bool IsEmpty => this == new SessionSettings();

    /// <summary>Only the settable values that differ from <paramref name="original"/>.</summary>
    public SessionSettings ChangesFrom(SessionSettings original)
    {
        ArgumentNullException.ThrowIfNull(original);
        // Both are written in the JSON-RPC spelling only to compare them; nothing is sent here.
        var current = ToJson(RpcDialect.JsonRpc);
        var previous = original.ToJson(RpcDialect.JsonRpc);
        var changed = new JsonObject();
        foreach (var (key, value) in current)
        {
            if (!ReadOnlyKeys.Contains(key) && !JsonNode.DeepEquals(value, previous[key]))
            {
                changed[key] = value?.DeepClone();
            }
        }
        return FromJson(changed);
    }

    internal static SessionSettings FromJson(JsonObject json) => new()
    {
        AltSpeedDown = json.GetInt32("altSpeedDown"),
        AltSpeedEnabled = json.GetBoolean("altSpeedEnabled"),
        AltSpeedTimeBegin = json.GetInt32("altSpeedTimeBegin"),
        AltSpeedTimeDay = json.GetInt32("altSpeedTimeDay"),
        AltSpeedTimeEnabled = json.GetBoolean("altSpeedTimeEnabled"),
        AltSpeedTimeEnd = json.GetInt32("altSpeedTimeEnd"),
        AltSpeedUp = json.GetInt32("altSpeedUp"),
        BlocklistEnabled = json.GetBoolean("blocklistEnabled"),
        BlocklistSize = json.GetInt32("blocklistSize"),
        BlocklistUrl = json.GetString("blocklistUrl"),
        CacheSizeMb = json.GetInt32("cacheSizeMb"),
        ConfigDir = json.GetString("configDir"),
        DhtEnabled = json.GetBoolean("dhtEnabled"),
        DownloadDir = json.GetString("downloadDir"),
        DownloadQueueEnabled = json.GetBoolean("downloadQueueEnabled"),
        DownloadQueueSize = json.GetInt32("downloadQueueSize"),
        Encryption = EncryptionModeWire.FromWire(json.GetString("encryption")),
        IdleSeedingLimit = json.GetInt32("idleSeedingLimit"),
        IdleSeedingLimitEnabled = json.GetBoolean("idleSeedingLimitEnabled"),
        IncompleteDir = json.GetString("incompleteDir"),
        IncompleteDirEnabled = json.GetBoolean("incompleteDirEnabled"),
        LpdEnabled = json.GetBoolean("lpdEnabled"),
        PeerLimitGlobal = json.GetInt32("peerLimitGlobal"),
        PeerLimitPerTorrent = json.GetInt32("peerLimitPerTorrent"),
        PeerPort = json.GetInt32("peerPort"),
        PeerPortRandomOnStart = json.GetBoolean("peerPortRandomOnStart"),
        PexEnabled = json.GetBoolean("pexEnabled"),
        PortForwardingEnabled = json.GetBoolean("portForwardingEnabled"),
        QueueStalledEnabled = json.GetBoolean("queueStalledEnabled"),
        QueueStalledMinutes = json.GetInt32("queueStalledMinutes"),
        RenamePartialFiles = json.GetBoolean("renamePartialFiles"),
        SeedQueueEnabled = json.GetBoolean("seedQueueEnabled"),
        SeedQueueSize = json.GetInt32("seedQueueSize"),
        SeedRatioLimit = json.GetDouble("seedRatioLimit"),
        SeedRatioLimited = json.GetBoolean("seedRatioLimited"),
        SequentialDownload = json.GetBoolean("sequentialDownload"),
        SpeedLimitDown = json.GetInt32("speedLimitDown"),
        SpeedLimitDownEnabled = json.GetBoolean("speedLimitDownEnabled"),
        SpeedLimitUp = json.GetInt32("speedLimitUp"),
        SpeedLimitUpEnabled = json.GetBoolean("speedLimitUpEnabled"),
        StartAddedTorrents = json.GetBoolean("startAddedTorrents"),
        TrashOriginalTorrentFiles = json.GetBoolean("trashOriginalTorrentFiles"),
        UtpEnabled = json.GetBoolean("utpEnabled"),
        Version = json.GetString("version"),
    };

    /// <summary>The values that are set, with canonical camelCase keys.</summary>
    internal JsonObject ToJson(RpcDialect dialect)
    {
        var json = new JsonObject();
        json.PutIfSet("altSpeedDown", AltSpeedDown);
        json.PutIfSet("altSpeedEnabled", AltSpeedEnabled);
        json.PutIfSet("altSpeedTimeBegin", AltSpeedTimeBegin);
        json.PutIfSet("altSpeedTimeDay", AltSpeedTimeDay);
        json.PutIfSet("altSpeedTimeEnabled", AltSpeedTimeEnabled);
        json.PutIfSet("altSpeedTimeEnd", AltSpeedTimeEnd);
        json.PutIfSet("altSpeedUp", AltSpeedUp);
        json.PutIfSet("blocklistEnabled", BlocklistEnabled);
        json.PutIfSet("blocklistSize", BlocklistSize);
        json.PutIfSet("blocklistUrl", BlocklistUrl);
        json.PutIfSet("cacheSizeMb", CacheSizeMb);
        json.PutIfSet("configDir", ConfigDir);
        json.PutIfSet("dhtEnabled", DhtEnabled);
        json.PutIfSet("downloadDir", DownloadDir);
        json.PutIfSet("downloadQueueEnabled", DownloadQueueEnabled);
        json.PutIfSet("downloadQueueSize", DownloadQueueSize);
        json.PutIfSet("encryption", Encryption?.ToWire(dialect));
        json.PutIfSet("idleSeedingLimit", IdleSeedingLimit);
        json.PutIfSet("idleSeedingLimitEnabled", IdleSeedingLimitEnabled);
        json.PutIfSet("incompleteDir", IncompleteDir);
        json.PutIfSet("incompleteDirEnabled", IncompleteDirEnabled);
        json.PutIfSet("lpdEnabled", LpdEnabled);
        json.PutIfSet("peerLimitGlobal", PeerLimitGlobal);
        json.PutIfSet("peerLimitPerTorrent", PeerLimitPerTorrent);
        json.PutIfSet("peerPort", PeerPort);
        json.PutIfSet("peerPortRandomOnStart", PeerPortRandomOnStart);
        json.PutIfSet("pexEnabled", PexEnabled);
        json.PutIfSet("portForwardingEnabled", PortForwardingEnabled);
        json.PutIfSet("queueStalledEnabled", QueueStalledEnabled);
        json.PutIfSet("queueStalledMinutes", QueueStalledMinutes);
        json.PutIfSet("renamePartialFiles", RenamePartialFiles);
        json.PutIfSet("seedQueueEnabled", SeedQueueEnabled);
        json.PutIfSet("seedQueueSize", SeedQueueSize);
        json.PutIfSet("seedRatioLimit", SeedRatioLimit);
        json.PutIfSet("seedRatioLimited", SeedRatioLimited);
        json.PutIfSet("sequentialDownload", SequentialDownload);
        json.PutIfSet("speedLimitDown", SpeedLimitDown);
        json.PutIfSet("speedLimitDownEnabled", SpeedLimitDownEnabled);
        json.PutIfSet("speedLimitUp", SpeedLimitUp);
        json.PutIfSet("speedLimitUpEnabled", SpeedLimitUpEnabled);
        json.PutIfSet("startAddedTorrents", StartAddedTorrents);
        json.PutIfSet("trashOriginalTorrentFiles", TrashOriginalTorrentFiles);
        json.PutIfSet("utpEnabled", UtpEnabled);
        json.PutIfSet("version", Version);
        return json;
    }
}

/// <summary>Transfer totals, for the current session or since the daemon was installed.</summary>
public sealed record SessionTotals(
    long UploadedBytes,
    long DownloadedBytes,
    int FilesAdded,
    int SecondsActive,
    int SessionCount)
{
    internal static SessionTotals FromJson(JsonObject json) => new(
        json.GetInt64("uploadedBytes") ?? 0,
        json.GetInt64("downloadedBytes") ?? 0,
        json.GetInt32("filesAdded") ?? 0,
        json.GetInt32("secondsActive") ?? 0,
        json.GetInt32("sessionCount") ?? 0);
}

public sealed record SessionStats(
    int ActiveTorrentCount,
    int PausedTorrentCount,
    int TorrentCount,
    long DownloadSpeed,
    long UploadSpeed,
    SessionTotals? CumulativeStats,
    SessionTotals? CurrentStats)
{
    internal static SessionStats FromJson(JsonObject json) => new(
        json.GetInt32("activeTorrentCount") ?? 0,
        json.GetInt32("pausedTorrentCount") ?? 0,
        json.GetInt32("torrentCount") ?? 0,
        json.GetInt64("downloadSpeed") ?? 0,
        json.GetInt64("uploadSpeed") ?? 0,
        json.GetObject("cumulativeStats") is { } cumulative ? SessionTotals.FromJson(cumulative) : null,
        json.GetObject("currentStats") is { } current ? SessionTotals.FromJson(current) : null);
}

/// <summary>Free space in a folder on the server.</summary>
/// <param name="TotalSize">Only reported by Transmission 4.0 and newer.</param>
public sealed record FreeSpace(string Path, long SizeBytes, long? TotalSize)
{
    internal static FreeSpace FromJson(JsonObject json) => new(
        json.GetString("path") ?? "",
        json.GetInt64("sizeBytes") ?? throw TransmissionException.InvalidResponse("free-space has no size-bytes"),
        json.GetInt64("totalSize"));
}
