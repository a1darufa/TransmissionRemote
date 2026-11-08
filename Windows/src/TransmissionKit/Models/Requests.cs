using System.Text.Json.Nodes;

namespace TransmissionKit;

/// <summary>Changes for <c>torrent-set</c>. Only the properties that are set are sent.</summary>
public sealed record TorrentMutation(IReadOnlyList<int> Ids)
{
    public Priority? BandwidthPriority { get; init; }
    public int? DownloadLimit { get; init; }
    public bool? DownloadLimited { get; init; }
    public int? UploadLimit { get; init; }
    public bool? UploadLimited { get; init; }
    public bool? HonorsSessionLimits { get; init; }
    public IReadOnlyList<int>? FilesWanted { get; init; }
    public IReadOnlyList<int>? FilesUnwanted { get; init; }
    public IReadOnlyList<int>? PriorityHigh { get; init; }
    public IReadOnlyList<int>? PriorityNormal { get; init; }
    public IReadOnlyList<int>? PriorityLow { get; init; }
    public IReadOnlyList<string>? Labels { get; init; }
    public int? PeerLimit { get; init; }
    public double? SeedRatioLimit { get; init; }
    public LimitMode? SeedRatioMode { get; init; }
    public int? SeedIdleLimit { get; init; }
    public LimitMode? SeedIdleMode { get; init; }
    public bool? SequentialDownload { get; init; }

    /// <summary>Transmission 4.0 and newer: announce URLs, one per line, a blank line between tiers.</summary>
    public string? TrackerList { get; init; }

    /// <summary>Tracker editing before Transmission 4.0.</summary>
    public IReadOnlyList<string>? TrackerAdd { get; init; }

    /// <summary>Tracker editing before Transmission 4.0: tracker IDs.</summary>
    public IReadOnlyList<int>? TrackerRemove { get; init; }

    /// <summary>
    /// Transmission 4.0 and newer: the group's name; an empty name takes the torrent out of its group.
    /// </summary>
    public string? Group { get; init; }

    public static TorrentMutation Wanted(bool wanted, IReadOnlyList<int> files, int torrentId) =>
        wanted
            ? new TorrentMutation([torrentId]) { FilesWanted = files }
            : new TorrentMutation([torrentId]) { FilesUnwanted = files };

    public static TorrentMutation WithPriority(Priority priority, IReadOnlyList<int> files, int torrentId) =>
        priority switch
        {
            Priority.High => new TorrentMutation([torrentId]) { PriorityHigh = files },
            Priority.Low => new TorrentMutation([torrentId]) { PriorityLow = files },
            _ => new TorrentMutation([torrentId]) { PriorityNormal = files },
        };

    internal JsonObject ToJson()
    {
        var json = new JsonObject { ["ids"] = Ids.ToJsonArray() };
        json.PutIfSet("bandwidthPriority", (int?)BandwidthPriority);
        json.PutIfSet("downloadLimit", DownloadLimit);
        json.PutIfSet("downloadLimited", DownloadLimited);
        json.PutIfSet("uploadLimit", UploadLimit);
        json.PutIfSet("uploadLimited", UploadLimited);
        json.PutIfSet("honorsSessionLimits", HonorsSessionLimits);
        json.PutIfSet("filesWanted", FilesWanted);
        json.PutIfSet("filesUnwanted", FilesUnwanted);
        json.PutIfSet("priorityHigh", PriorityHigh);
        json.PutIfSet("priorityNormal", PriorityNormal);
        json.PutIfSet("priorityLow", PriorityLow);
        json.PutIfSet("labels", Labels);
        json.PutIfSet("peerLimit", PeerLimit);
        json.PutIfSet("seedRatioLimit", SeedRatioLimit);
        json.PutIfSet("seedRatioMode", (int?)SeedRatioMode);
        json.PutIfSet("seedIdleLimit", SeedIdleLimit);
        json.PutIfSet("seedIdleMode", (int?)SeedIdleMode);
        json.PutIfSet("sequentialDownload", SequentialDownload);
        json.PutIfSet("trackerList", TrackerList);
        json.PutIfSet("trackerAdd", TrackerAdd);
        json.PutIfSet("trackerRemove", TrackerRemove);
        json.PutIfSet("group", Group);
        return json;
    }
}

/// <summary>
/// Parameters for <c>torrent-add</c>. Exactly one of <see cref="Filename"/> and <see cref="Metainfo"/> is set.
/// </summary>
public sealed record AddTorrentRequest
{
    private AddTorrentRequest()
    {
    }

    /// <summary>A magnet link, or an http(s) URL the daemon downloads itself.</summary>
    public string? Filename { get; private init; }

    /// <summary>Base64-encoded .torrent contents.</summary>
    public string? Metainfo { get; private init; }

    public string? DownloadDir { get; init; }
    public bool? Paused { get; init; }
    public Priority? BandwidthPriority { get; init; }
    public IReadOnlyList<int>? FilesUnwanted { get; init; }
    public IReadOnlyList<string>? Labels { get; init; }
    public bool? SequentialDownload { get; init; }

    public static AddTorrentRequest MagnetOrUrl(string link) => new() { Filename = link };

    public static AddTorrentRequest TorrentFile(ReadOnlySpan<byte> contents) =>
        new() { Metainfo = Convert.ToBase64String(contents) };

    internal JsonObject ToJson()
    {
        var json = new JsonObject();
        json.PutIfSet("filename", Filename);
        json.PutIfSet("metainfo", Metainfo);
        json.PutIfSet("downloadDir", DownloadDir);
        json.PutIfSet("paused", Paused);
        json.PutIfSet("bandwidthPriority", (int?)BandwidthPriority);
        json.PutIfSet("filesUnwanted", FilesUnwanted);
        json.PutIfSet("labels", Labels);
        json.PutIfSet("sequentialDownload", SequentialDownload);
        return json;
    }

    /// <summary>Base64 .torrent files can be megabytes long, so only their length is shown.</summary>
    public override string ToString() => Metainfo is null
        ? $"AddTorrentRequest {{ Filename = {Filename} }}"
        : $"AddTorrentRequest {{ Metainfo = {Metainfo.Length} characters }}";
}

public sealed record AddedTorrent(int Id, string Name, string HashString)
{
    internal static AddedTorrent? FromJson(JsonObject? json) =>
        json?.GetInt32("id") is { } id
            ? new AddedTorrent(id, json.GetString("name") ?? "", json.GetString("hashString") ?? "")
            : null;
}

/// <param name="TorrentDuplicate">
/// Set instead of <paramref name="TorrentAdded"/> when the torrent already existed.
/// </param>
public sealed record AddTorrentResult(AddedTorrent? TorrentAdded, AddedTorrent? TorrentDuplicate)
{
    public AddedTorrent? Torrent => TorrentAdded ?? TorrentDuplicate;

    public bool IsDuplicate => TorrentDuplicate is not null;

    internal static AddTorrentResult FromJson(JsonObject json) => new(
        AddedTorrent.FromJson(json.GetObject("torrentAdded")),
        AddedTorrent.FromJson(json.GetObject("torrentDuplicate")));
}

public sealed record RenameResult(int Id, string Path, string Name)
{
    internal static RenameResult FromJson(JsonObject json) => new(
        json.GetInt32("id") ?? throw TransmissionException.InvalidResponse("the rename result has no id"),
        json.GetString("path") ?? "",
        json.GetString("name") ?? "");
}
