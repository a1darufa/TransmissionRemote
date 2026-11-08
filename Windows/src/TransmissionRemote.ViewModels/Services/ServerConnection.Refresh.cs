using TransmissionKit;

namespace TransmissionRemote.ViewModels;

public sealed partial class ServerConnection
{
    /// <summary>Tracker stats and free space change slowly, so they're fetched every sixth refresh.</summary>
    private const int SlowDataInterval = 6;

    /// <summary>Fetches everything the window shows. Overlapping calls are coalesced into one more pass.</summary>
    public async Task RefreshAsync()
    {
        if (Status != ConnectionStatus.Connected || _client is not { } client)
        {
            return;
        }
        if (_isRefreshing)
        {
            _isRefreshRequested = true;
            return;
        }
        _isRefreshing = true;
        try
        {
            do
            {
                _isRefreshRequested = false;
                await RefreshOnceAsync(client);
            }
            while (_isRefreshRequested && IsCurrent(client));
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task RefreshOnceAsync(TransmissionClient client)
    {
        try
        {
            var listTask = FetchTorrentListAsync(client);
            var statsTask = client.GetSessionStatsAsync();
            var sessionTask = client.GetSessionSettingsAsync();
            await Task.WhenAll(listTask, statsTask, sessionTask);
            if (!IsCurrent(client))
            {
                return;
            }

            var torrents = WithTrackerSummaries(await listTask);
            var stats = await statsTask;
            _completions.Compare(Torrents, torrents, _settings.Current.NotifyOnCompletion);
            Torrents = torrents;
            Stats = stats;
            Session = await sessionTask;
            SpeedHistory.Append(stats.DownloadSpeed, stats.UploadSpeed, _time.GetUtcNow());
            var remaining = _selection.Where(id => torrents.Any(torrent => torrent.Id == id)).ToHashSet();
            if (remaining.Count != _selection.Count)
            {
                Selection = remaining;
            }
            PollingProblem = null;
            LastUpdated = _time.GetUtcNow();

            await LoadDetailsAsync(client);
            if (_refreshCount % SlowDataInterval == 0)
            {
                await LoadFreeSpaceAsync(client);
                await LoadTrackerSummariesAsync(client);
            }
            _refreshCount++;
        }
        catch (TransmissionException error) when (IsCurrent(client))
        {
            if (error.RequiresUserAction)
            {
                Fail(error);
            }
            else
            {
                Log.RefreshFailed(_logger, error, error.Message);
                PollingProblem = error;
            }
        }
    }

    /// <summary>
    /// Large libraries make the full list slow to fetch and decode every few seconds, so most refreshes ask
    /// only for what changed in the last minute (transgui #708, #1163, #1422).
    /// </summary>
    private async Task<IReadOnlyList<Torrent>> FetchTorrentListAsync(TransmissionClient client)
    {
        var started = _time.GetTimestamp();
        var isFullList = _listPlan.NeedsFullList(started);
        IReadOnlyList<Torrent> list = isFullList
            ? await client.GetAllTorrentsAsync()
            : (await client.GetRecentlyChangedTorrentsAsync()).AppliedTo(Torrents);
        _listPlan.RecordFetch(started, isFullList);
        return list;
    }

    private async Task LoadDetailsAsync(TransmissionClient client)
    {
        if (SingleSelectedTorrent is not { } torrent)
        {
            Details = null;
            return;
        }
        var fields = DetailTab.Fields();
        if (fields.Count == 0)
        {
            return;
        }
        var details = await client.GetDetailsAsync(torrent.Id, fields);
        if (IsCurrent(client) && SingleSelectedTorrent?.Id == torrent.Id)
        {
            Details = details;
        }
    }

    private async Task ReloadDetailsAsync()
    {
        if (_client is not { } client || !IsConnected)
        {
            return;
        }
        Details = null;
        try
        {
            await LoadDetailsAsync(client);
        }
        catch (TransmissionException error)
        {
            // The next refresh tries again; a failure here is already logged by the client.
            PollingProblem = error;
        }
    }

    private async Task LoadFreeSpaceAsync(TransmissionClient client)
    {
        if (Session?.DownloadDir is not { Length: > 0 } path)
        {
            return;
        }
        try
        {
            var freeSpace = await client.GetFreeSpaceAsync(path);
            if (IsCurrent(client))
            {
                FreeSpace = freeSpace;
            }
        }
        catch (TransmissionException)
        {
            // Optional information; the client has logged why.
        }
    }

    /// <summary>Tracker stats are bulky and only change when a tracker is contacted.</summary>
    private async Task LoadTrackerSummariesAsync(TransmissionClient client)
    {
        try
        {
            var summaries = await client.GetTrackerSummariesAsync();
            if (!IsCurrent(client))
            {
                return;
            }
            _trackerSummaries = summaries.GroupBy(summary => summary.TorrentId)
                .ToDictionary(group => group.Key, group => group.First());
            Torrents = WithTrackerSummaries(Torrents);
        }
        catch (TransmissionException)
        {
            // Optional information; the client has logged why.
        }
    }

    private List<Torrent> WithTrackerSummaries(IEnumerable<Torrent> torrents) => torrents
        .Select(torrent => torrent with { TrackerSummary = _trackerSummaries.GetValueOrDefault(torrent.Id) })
        .ToList();
}
