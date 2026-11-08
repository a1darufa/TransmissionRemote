using System.Diagnostics;
using System.Text.Json.Nodes;

namespace TransmissionKit;

/// <summary>
/// What changed on the server recently: the torrents Transmission counts as recently active
/// (changed in the last 60 seconds) and the IDs of torrents removed in that time.
/// </summary>
/// <remarks>
/// Fetching only these instead of every torrent keeps refreshes small for large libraries
/// (Transmission Remote GUI issues #708, #1163 and #1422).
/// </remarks>
public sealed record TorrentChanges(IReadOnlyList<Torrent> Changed, IReadOnlyList<int> RemovedIds)
{
    /// <summary>How far back Transmission looks: <c>RecentlyActiveSeconds</c> in libtransmission 3.00 to 4.1.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Applies the changes to the previous full list: removed torrents go, changed ones are replaced in
    /// place, and torrents that weren't listed yet are added at the end.
    /// </summary>
    public List<Torrent> AppliedTo(IEnumerable<Torrent> torrents)
    {
        ArgumentNullException.ThrowIfNull(torrents);
        var removed = RemovedIds.ToHashSet();
        var updates = new Dictionary<int, Torrent>();
        foreach (var torrent in Changed)
        {
            updates[torrent.Id] = torrent; // the latest entry wins
        }

        var result = new List<Torrent>();
        foreach (var torrent in torrents)
        {
            if (removed.Contains(torrent.Id))
            {
                continue;
            }
            result.Add(updates.Remove(torrent.Id, out var latest) ? latest : torrent);
        }
        // What's left in updates is new; keep the server's order and add each torrent once.
        foreach (var torrent in Changed)
        {
            if (!removed.Contains(torrent.Id) && updates.Remove(torrent.Id, out var latest))
            {
                result.Add(latest);
            }
        }
        return result;
    }

    internal static TorrentChanges FromJson(JsonObject json) => new(
        json.GetObjects("torrents").Select(Torrent.FromJson).ToList(),
        json["removed"] is JsonArray removed
            ? removed.OfType<JsonValue>()
                .Select(value => value.TryGetValue(out int id) ? (int?)id : null)
                .OfType<int>()
                .ToList()
            : []);
}

/// <summary>Decides whether a refresh can ask only for recently changed torrents or needs the full list.</summary>
/// <remarks>
/// Transmission reports what changed in the last 60 seconds, so the previous fetch must have started
/// less than that ago, with a margin for slow answers and Transmission's whole-second clock. The full
/// list is also fetched at least once a minute, so anything a daemon version doesn't count as a change
/// still gets refreshed. Times are <see cref="Stopwatch.GetTimestamp"/> values, which don't jump when
/// the clock is changed.
/// </remarks>
public sealed class TorrentListRefreshPlan
{
    internal static readonly TimeSpan MaximumGap = TimeSpan.FromSeconds(40);
    internal static readonly TimeSpan FullListInterval = TimeSpan.FromSeconds(60);

    private long? _lastFetch;
    private long? _lastFullFetch;

    public bool NeedsFullList(long now)
    {
        if (_lastFetch is not { } lastFetch || _lastFullFetch is not { } lastFullFetch)
        {
            return true;
        }
        return Stopwatch.GetElapsedTime(lastFetch, now) > MaximumGap
            || Stopwatch.GetElapsedTime(lastFullFetch, now) > FullListInterval;
    }

    /// <summary>Call after a fetch succeeded, with the time it started.</summary>
    public void RecordFetch(long startedAt, bool wasFullList)
    {
        _lastFetch = startedAt;
        if (wasFullList)
        {
            _lastFullFetch = startedAt;
        }
    }

    /// <summary>Forgets earlier fetches, for example after reconnecting.</summary>
    public void Reset()
    {
        _lastFetch = null;
        _lastFullFetch = null;
    }
}
