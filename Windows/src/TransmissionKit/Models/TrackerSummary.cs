using System.Text.Json.Nodes;

namespace TransmissionKit;

public enum TrackerStatus
{
    /// <summary>No tracker has been contacted yet, or the torrent has none.</summary>
    NotContacted,

    /// <summary>The last announce worked.</summary>
    Working,

    /// <summary>An announce is queued or in progress.</summary>
    Updating,

    /// <summary>The last announce failed; <see cref="TrackerSummary.FailureMessage"/> says why.</summary>
    Failed,
}

/// <summary>
/// What a torrent's trackers say about its swarm, for the main list's Seeds, Peers and Tracker
/// Status columns. Trackers only update every few minutes, so this is fetched for all torrents
/// less often than the list itself.
/// </summary>
/// <param name="Seeders">The largest swarm any tracker reported, or null when none has been scraped yet.</param>
/// <param name="Status">The first tracker's status, the one Transmission Remote GUI shows too.</param>
/// <param name="FailureMessage">
/// The tracker's or Transmission's message when <paramref name="Status"/> is Failed.
/// </param>
public sealed record TrackerSummary(
    int TorrentId,
    int? Seeders,
    int? Leechers,
    TrackerStatus Status,
    string? FailureMessage = null)
{
    internal static TrackerSummary FromJson(JsonObject json)
    {
        var torrentId = json.GetInt32("id") ?? throw TransmissionException.InvalidResponse("a torrent has no id");
        var trackers = json.GetObjects("trackerStats").Select(TrackerSummaryEntry.FromJson).ToList();
        return Summarize(torrentId, trackers);
    }

    internal static TrackerSummary Summarize(int torrentId, IReadOnlyList<TrackerSummaryEntry> trackers)
    {
        var first = trackers.Where(tracker => !tracker.IsBackup).MinBy(tracker => tracker.Tier)
            ?? (trackers.Count > 0 ? trackers[0] : null);
        var (status, message) = first is null ? (TrackerStatus.NotContacted, null) : StatusOf(first);
        return new TrackerSummary(
            torrentId,
            LargestReported(trackers.Select(tracker => tracker.SeederCount)),
            LargestReported(trackers.Select(tracker => tracker.LeecherCount)),
            status,
            message);
    }

    /// <summary>Counts below zero mean the tracker hasn't been scraped.</summary>
    private static int? LargestReported(IEnumerable<int> counts) =>
        counts.Where(count => count >= 0).Select(count => (int?)count).Max();

    private static (TrackerStatus Status, string? Message) StatusOf(TrackerSummaryEntry tracker)
    {
        if (tracker.AnnounceState is 2 or 3)
        {
            return (TrackerStatus.Updating, null);
        }
        if (!tracker.HasAnnounced)
        {
            return (TrackerStatus.NotContacted, null);
        }
        // Some trackers answer with the word "Success" in an otherwise failed-looking result.
        if (tracker.LastAnnounceSucceeded || tracker.LastAnnounceResult == "Success")
        {
            return (TrackerStatus.Working, null);
        }
        return (TrackerStatus.Failed, tracker.LastAnnounceResult);
    }
}

/// <summary>The parts of a tracker's stats the summary needs.</summary>
internal sealed record TrackerSummaryEntry
{
    public int Tier { get; init; }
    public bool IsBackup { get; init; }

    /// <summary>-1 until the tracker has been scraped.</summary>
    public int SeederCount { get; init; } = -1;

    public int LeecherCount { get; init; } = -1;

    /// <summary>0 inactive, 1 waiting, 2 queued, 3 announcing.</summary>
    public int AnnounceState { get; init; }

    public bool HasAnnounced { get; init; }
    public bool LastAnnounceSucceeded { get; init; }
    public string LastAnnounceResult { get; init; } = "";

    public static TrackerSummaryEntry FromJson(JsonObject json) => new()
    {
        Tier = json.GetInt32("tier") ?? 0,
        IsBackup = json.GetBoolean("isBackup") ?? false,
        SeederCount = json.GetInt32("seederCount") ?? -1,
        LeecherCount = json.GetInt32("leecherCount") ?? -1,
        AnnounceState = json.GetInt32("announceState") ?? 0,
        HasAnnounced = json.GetBoolean("hasAnnounced") ?? false,
        LastAnnounceSucceeded = json.GetBoolean("lastAnnounceSucceeded") ?? false,
        LastAnnounceResult = json.GetString("lastAnnounceResult") ?? "",
    };
}
