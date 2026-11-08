using System.Text.Json.Nodes;

namespace TransmissionKit;

public enum TorrentAction
{
    Start,

    /// <summary>Start immediately, ignoring the download queue.</summary>
    StartNow,
    Stop,
    Verify,
    Reannounce,
}

public enum QueueMove
{
    Top,
    Up,
    Down,
    Bottom,
}

public sealed partial class TransmissionClient
{
    public Task<List<Torrent>> GetAllTorrentsAsync(CancellationToken cancellationToken = default) =>
        GetTorrentsAsync(Torrent.ListFields, Torrent.FromJson, ids: null, cancellationToken);

    /// <summary>
    /// The torrents changed in the last minute, with the list's fields, and the IDs of torrents removed in
    /// that time. Apply them to the previous list with <see cref="TorrentChanges.AppliedTo"/>.
    /// </summary>
    public Task<TorrentChanges> GetRecentlyChangedTorrentsAsync(CancellationToken cancellationToken = default)
    {
        var rpcVersion = RpcVersion;
        var supportedFields = Torrent.ListFields.Where(field => field.MinimumRpcVersion() <= rpcVersion)
            .Select(field => field.CamelName());
        var parameters = new JsonObject
        {
            ["fields"] = WireNames(supportedFields, KeyScope.TorrentFields).ToJsonArray(),
            // A value rather than a key, so the usual key conversion doesn't reach it.
            ["ids"] = Dialect == RpcDialect.JsonRpc ? "recently_active" : "recently-active",
        };
        return CallAsync(
            RpcMethod.TorrentGet, parameters, KeyScope.TorrentFields, TorrentChanges.FromJson, cancellationToken);
    }

    /// <summary>
    /// Swarm sizes and tracker status for every torrent. Tracker stats are large, so the app asks
    /// for these less often than for the list.
    /// </summary>
    public Task<List<TrackerSummary>> GetTrackerSummariesAsync(CancellationToken cancellationToken = default) =>
        GetTorrentsAsync(
            [TorrentField.Id, TorrentField.TrackerStats], TrackerSummary.FromJson, ids: null, cancellationToken);

    /// <summary>The requested details of one torrent, or null when it's no longer on the server.</summary>
    public async Task<TorrentDetails?> GetDetailsAsync(
        int torrentId,
        IEnumerable<TorrentField> fields,
        CancellationToken cancellationToken = default)
    {
        var torrents = await GetTorrentsAsync(fields, TorrentDetails.FromJson, [torrentId], cancellationToken)
            .ConfigureAwait(false);
        return torrents.FirstOrDefault();
    }

    public Task PerformAsync(
        TorrentAction action,
        IReadOnlyList<int> ids,
        CancellationToken cancellationToken = default)
    {
        var method = action switch
        {
            TorrentAction.Start => RpcMethod.TorrentStart,
            TorrentAction.StartNow => RpcMethod.TorrentStartNow,
            TorrentAction.Stop => RpcMethod.TorrentStop,
            TorrentAction.Verify => RpcMethod.TorrentVerify,
            TorrentAction.Reannounce => RpcMethod.TorrentReannounce,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
        return CallAsync(method, IdsParameters(ids), KeyScope.Plain, cancellationToken);
    }

    public Task MoveInQueueAsync(QueueMove move, IReadOnlyList<int> ids, CancellationToken cancellationToken = default)
    {
        var method = move switch
        {
            QueueMove.Top => RpcMethod.QueueMoveTop,
            QueueMove.Up => RpcMethod.QueueMoveUp,
            QueueMove.Down => RpcMethod.QueueMoveDown,
            QueueMove.Bottom => RpcMethod.QueueMoveBottom,
            _ => throw new ArgumentOutOfRangeException(nameof(move), move, null),
        };
        return CallAsync(method, IdsParameters(ids), KeyScope.Plain, cancellationToken);
    }

    public Task RemoveAsync(IReadOnlyList<int> ids, bool deletingData, CancellationToken cancellationToken = default)
    {
        var parameters = IdsParameters(ids);
        parameters["deleteLocalData"] = deletingData;
        // Deleting large torrents on a slow disk can take minutes, and the daemon answers after.
        return CallAsync(
            RpcMethod.TorrentRemove, parameters, KeyScope.TorrentArguments, cancellationToken,
            deletingData ? SlowCallTimeout : null);
    }

    /// <summary>
    /// Points torrents at a new folder. With <paramref name="movingData"/>, the daemon moves the files
    /// too; otherwise it looks for existing files there.
    /// </summary>
    public Task SetLocationAsync(
        IReadOnlyList<int> ids,
        string location,
        bool movingData,
        CancellationToken cancellationToken = default)
    {
        var parameters = IdsParameters(ids);
        parameters["location"] = location;
        parameters["move"] = movingData;
        // The daemon answers only after copying the data, which can take many minutes.
        return CallAsync(
            RpcMethod.TorrentSetLocation, parameters, KeyScope.TorrentArguments, cancellationToken,
            movingData ? SlowCallTimeout : null);
    }

    public Task<RenameResult> RenameAsync(
        int torrentId,
        string path,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var parameters = IdsParameters([torrentId]);
        parameters["path"] = path;
        parameters["name"] = newName;
        return CallAsync(
            RpcMethod.TorrentRenamePath, parameters, KeyScope.Plain, RenameResult.FromJson, cancellationToken);
    }

    public Task ApplyAsync(TorrentMutation mutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        return CallAsync(RpcMethod.TorrentSet, mutation.ToJson(), KeyScope.TorrentArguments, cancellationToken);
    }

    public Task<AddTorrentResult> AddAsync(AddTorrentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CallAsync(
            RpcMethod.TorrentAdd, request.ToJson(), KeyScope.TorrentArguments, AddTorrentResult.FromJson,
            cancellationToken);
    }

    /// <summary>
    /// Replaces a torrent's trackers: with <c>trackerList</c> on Transmission 4.0 and newer, and the
    /// older add and remove arguments before that.
    /// </summary>
    /// <param name="announceUrls">The wanted trackers, one list per tier.</param>
    public Task ReplaceTrackersAsync(
        int torrentId,
        IReadOnlyList<TrackerStat> current,
        IReadOnlyList<IReadOnlyList<string>> announceUrls,
        CancellationToken cancellationToken = default)
    {
        var mutation = TrackerMutation(torrentId, current, announceUrls, RpcVersion);
        return mutation is null ? Task.CompletedTask : ApplyAsync(mutation, cancellationToken);
    }

    /// <summary>
    /// The change that turns <paramref name="current"/> into the wanted trackers, or null when nothing changes.
    /// </summary>
    /// <remarks>
    /// Before Transmission 4.0, <c>trackerAdd</c> and <c>trackerRemove</c> answer an empty list with
    /// "invalid argument", and a failed add skips the remove, so only non-empty lists are sent.
    /// </remarks>
    internal static TorrentMutation? TrackerMutation(
        int torrentId,
        IReadOnlyList<TrackerStat> current,
        IReadOnlyList<IReadOnlyList<string>> announceUrls,
        int rpcVersion)
    {
        if (rpcVersion >= 17)
        {
            var tiers = announceUrls.Select(tier => string.Join('\n', tier));
            return new TorrentMutation([torrentId]) { TrackerList = string.Join("\n\n", tiers) };
        }

        var wanted = announceUrls.SelectMany(tier => tier).ToList();
        var wantedSet = wanted.ToHashSet(StringComparer.Ordinal);
        var existing = current.Select(tracker => tracker.Announce).ToHashSet(StringComparer.Ordinal);
        var removed = current.Where(tracker => !wantedSet.Contains(tracker.Announce))
            .Select(tracker => tracker.Id)
            .ToList();
        var added = wanted.Where(url => !existing.Contains(url)).ToList();
        if (removed.Count == 0 && added.Count == 0)
        {
            return null;
        }
        return new TorrentMutation([torrentId])
        {
            TrackerRemove = removed.Count > 0 ? removed : null,
            TrackerAdd = added.Count > 0 ? added : null,
        };
    }

    /// <summary>Fetches torrents, skipping fields the server is too old to know. Null ids means all torrents.</summary>
    private async Task<List<T>> GetTorrentsAsync<T>(
        IEnumerable<TorrentField> fields,
        Func<JsonObject, T> parse,
        IReadOnlyList<int>? ids,
        CancellationToken cancellationToken)
    {
        var rpcVersion = RpcVersion;
        var supportedFields = fields.Where(field => field.MinimumRpcVersion() <= rpcVersion)
            .Select(field => field.CamelName());
        var parameters = new JsonObject
        {
            ["fields"] = WireNames(supportedFields, KeyScope.TorrentFields).ToJsonArray(),
        };
        if (ids is not null)
        {
            parameters["ids"] = ids.ToJsonArray();
        }
        return await CallAsync(
                RpcMethod.TorrentGet,
                parameters,
                KeyScope.TorrentFields,
                result => result.GetObjects("torrents").Select(parse).ToList(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static JsonObject IdsParameters(IReadOnlyList<int> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return new JsonObject { ["ids"] = ids.ToJsonArray() };
    }
}
