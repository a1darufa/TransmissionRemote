using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>The user's actions on torrents, and one-off requests that dialogs and settings make.</summary>
public sealed partial class ServerConnection
{
    public Task PerformAsync(TorrentAction action, IEnumerable<int> ids)
    {
        var sorted = Sorted(ids);
        return RunAsync($"{action} {Describe(sorted)}", client => client.PerformAsync(action, sorted));
    }

    public Task PerformOnAllAsync(TorrentAction action) => PerformAsync(action, Torrents.Select(torrent => torrent.Id));

    public Task MoveInQueueAsync(QueueMove move, IEnumerable<int> ids)
    {
        var sorted = Sorted(ids);
        return RunAsync($"Queue {move} {Describe(sorted)}", client => client.MoveInQueueAsync(move, sorted));
    }

    public Task RemoveAsync(IEnumerable<int> ids, bool deletingData)
    {
        var sorted = Sorted(ids);
        var what = deletingData ? "Remove with data" : "Remove";
        return RunAsync($"{what} {Describe(sorted)}", client => client.RemoveAsync(sorted, deletingData));
    }

    public Task SetLocationAsync(IEnumerable<int> ids, string location, bool movingData)
    {
        var sorted = Sorted(ids);
        var cleaned = ServerFolder.Cleaned(location);
        return RunAsync(
            $"Set location of {Describe(sorted)}", client => client.SetLocationAsync(sorted, cleaned, movingData));
    }

    /// <summary>Renames a torrent's top-level file or folder.</summary>
    public Task RenameAsync(Torrent torrent, string newName)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return RenameFileAsync(torrent.Id, torrent.Name, newName);
    }

    /// <summary>Renames a file or folder inside a torrent; <paramref name="path"/> is relative to its download folder.</summary>
    public Task RenameFileAsync(int torrentId, string path, string newName) =>
        RunAsync($"Rename in torrent {torrentId}", client => client.RenameAsync(torrentId, path, newName));

    public Task ApplyAsync(TorrentMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        return RunAsync($"Change {Describe(mutation.Ids)}", client => client.ApplyAsync(mutation));
    }

    public Task SetLabelsAsync(IReadOnlyList<string> labels, IEnumerable<int> ids) =>
        ApplyAsync(new TorrentMutation(Sorted(ids)) { Labels = labels });

    public Task SetBandwidthPriorityAsync(Priority priority, IEnumerable<int> ids) =>
        ApplyAsync(new TorrentMutation(Sorted(ids)) { BandwidthPriority = priority });

    /// <summary>Sets or clears (null) a speed limit on torrents, in kB/s.</summary>
    public Task SetSpeedLimitAsync(int? limit, TransferDirection direction, IEnumerable<int> ids)
    {
        var mutation = new TorrentMutation(Sorted(ids));
        mutation = direction == TransferDirection.Download
            ? mutation with { DownloadLimited = limit is not null, DownloadLimit = limit }
            : mutation with { UploadLimited = limit is not null, UploadLimit = limit };
        return ApplyAsync(mutation);
    }

    public Task ReplaceTrackersAsync(int torrentId, IReadOnlyList<TrackerStat> current, IReadOnlyList<IReadOnlyList<string>> tiers) =>
        RunAsync($"Edit trackers of torrent {torrentId}", client => client.ReplaceTrackersAsync(torrentId, current, tiers));

    /// <summary>Sets or clears (null) the daemon-wide speed limit, in kB/s.</summary>
    public Task SetGlobalSpeedLimitAsync(int? limit, TransferDirection direction)
    {
        var changes = direction == TransferDirection.Download
            ? new SessionSettings { SpeedLimitDownEnabled = limit is not null, SpeedLimitDown = limit }
            : new SessionSettings { SpeedLimitUpEnabled = limit is not null, SpeedLimitUp = limit };
        return RunAsync($"Set the {direction} speed limit", client => client.UpdateSessionAsync(changes));
    }

    /// <summary>The daemon-wide limit in kB/s, or null when it's off; for the menu's check mark.</summary>
    public int? GlobalSpeedLimit(TransferDirection direction) => direction == TransferDirection.Download
        ? Session?.SpeedLimitDownEnabled == true ? Session.SpeedLimitDown : null
        : Session?.SpeedLimitUpEnabled == true ? Session.SpeedLimitUp : null;

    public Task SetAlternativeSpeedEnabledAsync(bool enabled)
    {
        // Optimistic, so the toggle doesn't flicker until the next refresh.
        if (Session is { } session)
        {
            Session = session with { AltSpeedEnabled = enabled };
        }
        return RunAsync(
            "Switch alternative speed limits",
            client => client.UpdateSessionAsync(new SessionSettings { AltSpeedEnabled = enabled }));
    }

    // Requests that return data. They throw TransmissionException, for the dialog that asked to show.

    public async Task<AddTorrentResult> AddAsync(AddTorrentRequest request)
    {
        var result = await RequireClient().AddAsync(request);
        await RefreshAsync();
        return result;
    }

    public async Task UpdateSessionAsync(SessionSettings changes)
    {
        await RequireClient().UpdateSessionAsync(changes);
        await RefreshAsync();
    }

    public async Task<FreeSpace?> GetFreeSpaceAsync(string path)
    {
        try
        {
            return await RequireClient().GetFreeSpaceAsync(path);
        }
        catch (TransmissionException)
        {
            return null;
        }
    }

    public Task<TorrentDetails?> GetOptionsAsync(int torrentId) =>
        RequireClient().GetDetailsAsync(torrentId, TorrentDetails.OptionFields);

    public Task<TorrentDetails?> GetTrackersAsync(int torrentId) =>
        RequireClient().GetDetailsAsync(torrentId, TorrentDetails.TrackerFields);

    public async Task<IReadOnlyList<string>> GetMagnetLinksAsync(IEnumerable<int> ids)
    {
        var client = RequireClient();
        var links = new List<string>();
        foreach (var id in Sorted(ids))
        {
            if (await client.GetDetailsAsync(id, [TorrentField.Id, TorrentField.MagnetLink]) is { MagnetLink.Length: > 0 } details)
            {
                links.Add(details.MagnetLink);
            }
        }
        return links;
    }

    public Task<bool> TestPortAsync() => RequireClient().TestPortAsync();

    public Task<int> UpdateBlocklistAsync() => RequireClient().UpdateBlocklistAsync();

    public Task<List<BandwidthGroup>> GetBandwidthGroupsAsync() => RequireClient().GetBandwidthGroupsAsync();

    public async Task SaveBandwidthGroupAsync(BandwidthGroup group)
    {
        await RequireClient().SaveBandwidthGroupAsync(group);
        await RefreshAsync();
    }

    /// <summary>
    /// Runs an action against the daemon. Failures are logged with what was attempted and shown through
    /// <see cref="ActionError"/>; then the data refreshes so the window catches up either way.
    /// </summary>
    private async Task RunAsync(string description, Func<TransmissionClient, Task> operation)
    {
        if (_client is not { } client)
        {
            return;
        }
        try
        {
            await operation(client);
        }
        catch (TransmissionException error)
        {
            Log.ActionFailed(_logger, error, description, error.Message);
            ActionError = error;
        }
        await RefreshAsync();
    }

    /// <summary>The live client, or a "not connected" error for requests made while disconnected.</summary>
    private TransmissionClient RequireClient() =>
        _client ?? throw new TransmissionException("Not connected to a server.");

    private static int[] Sorted(IEnumerable<int> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.Distinct().Order().ToArray();
    }

    /// <summary>"torrent 5" or "3 torrents", for log messages.</summary>
    private static string Describe(IReadOnlyCollection<int> ids) =>
        ids.Count == 1 ? $"torrent {ids.First()}" : $"{ids.Count} torrents";
}
