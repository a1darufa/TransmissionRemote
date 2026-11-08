using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>The status filters in the sidebar, matching Transmission Remote GUI's filter pane.</summary>
public enum StatusFilter
{
    All,
    Downloading,
    Seeding,
    Completed,
    Active,
    Inactive,
    Stopped,
    Waiting,
    Verifying,
    Error,
}

public static class StatusFilterExtensions
{
    public static bool Matches(this StatusFilter filter, Torrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return filter switch
        {
            StatusFilter.All => true,
            StatusFilter.Downloading => torrent.Status is TorrentStatus.Downloading or TorrentStatus.QueuedToDownload,
            StatusFilter.Seeding => torrent.Status is TorrentStatus.Seeding or TorrentStatus.QueuedToSeed,
            StatusFilter.Completed => torrent.IsComplete,
            StatusFilter.Active => torrent.IsActive,
            StatusFilter.Inactive => !torrent.IsActive && torrent.Status != TorrentStatus.Stopped,
            StatusFilter.Stopped => torrent.Status == TorrentStatus.Stopped,
            StatusFilter.Waiting => torrent.Status.IsQueued(),
            StatusFilter.Verifying => torrent.Status is TorrentStatus.Verifying or TorrentStatus.QueuedToVerify,
            StatusFilter.Error => torrent.HasError,
            _ => false,
        };
    }
}

/// <summary>Anything selectable in the sidebar.</summary>
public abstract record SidebarFilter
{
    public abstract bool Matches(Torrent torrent);
}

public sealed record StatusSidebarFilter(StatusFilter Status) : SidebarFilter
{
    public override bool Matches(Torrent torrent) => Status.Matches(torrent);
}

/// <summary>Torrents saved in one folder on the server.</summary>
public sealed record FolderSidebarFilter(string Path) : SidebarFilter
{
    public override bool Matches(Torrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return RemotePaths.TrimTrailingSeparator(torrent.DownloadDir) == Path;
    }
}

public sealed record TrackerSidebarFilter(string Host) : SidebarFilter
{
    public override bool Matches(Torrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return torrent.TrackerHosts.Contains(Host);
    }
}

public sealed record LabelSidebarFilter(string Label) : SidebarFilter
{
    public override bool Matches(Torrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return torrent.Labels.Contains(Label);
    }
}

/// <summary>One folder, tracker or label in the sidebar.</summary>
/// <param name="Name">The full value: the folder path, tracker host or label.</param>
/// <param name="DisplayName">What the sidebar shows; for folders, a short form of the path.</param>
public sealed record SidebarEntry(string Name, int Count, string DisplayName);

/// <summary>Counts for every sidebar entry, computed in one pass over the torrent list.</summary>
public sealed class SidebarSummary
{
    private readonly Dictionary<StatusFilter, int> _statusCounts = [];

    public SidebarSummary(IEnumerable<Torrent> torrents)
    {
        ArgumentNullException.ThrowIfNull(torrents);
        var folderCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var trackerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var labelCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var torrent in torrents)
        {
            foreach (var filter in Enum.GetValues<StatusFilter>())
            {
                if (filter.Matches(torrent))
                {
                    _statusCounts[filter] = _statusCounts.GetValueOrDefault(filter) + 1;
                }
            }
            Increment(folderCounts, RemotePaths.TrimTrailingSeparator(torrent.DownloadDir));
            foreach (var host in torrent.TrackerHosts)
            {
                Increment(trackerCounts, host);
            }
            foreach (var label in torrent.Labels)
            {
                Increment(labelCounts, label);
            }
        }
        Folders = FolderEntries(folderCounts);
        Trackers = SortedEntries(trackerCounts);
        Labels = SortedEntries(labelCounts);
    }

    public IReadOnlyList<SidebarEntry> Folders { get; }
    public IReadOnlyList<SidebarEntry> Trackers { get; }
    public IReadOnlyList<SidebarEntry> Labels { get; }

    public int CountFor(StatusFilter filter) => _statusCounts.GetValueOrDefault(filter);

    /// <summary>
    /// Folders by their last name, sorted the way they're shown. Two folders with the same last name get their
    /// parent folder added, so they can be told apart (transgui #979).
    /// </summary>
    private static List<SidebarEntry> FolderEntries(Dictionary<string, int> counts)
    {
        var paths = counts.Where(pair => pair.Key.Length > 0).ToList();
        var nameCounts = paths.GroupBy(pair => RemotePaths.LastComponent(pair.Key))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return paths
            .Select(pair =>
            {
                var name = RemotePaths.LastComponent(pair.Key);
                var parent = RemotePaths.ParentComponent(pair.Key);
                var displayName = nameCounts[name] > 1 && parent.Length > 0 ? $"{name} — {parent}" : name;
                return new SidebarEntry(pair.Key, pair.Value, displayName);
            })
            .OrderBy(entry => entry.DisplayName, NaturalStringComparer.Instance)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static List<SidebarEntry> SortedEntries(Dictionary<string, int> counts) => counts
        .Where(pair => pair.Key.Length > 0)
        .Select(pair => new SidebarEntry(pair.Key, pair.Value, pair.Key))
        .OrderBy(entry => entry.Name, NaturalStringComparer.Instance)
        .ToList();

    private static void Increment(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;
}

/// <summary>Showing remote paths compactly. Daemons on Windows use backslashes.</summary>
public static class RemotePaths
{
    private static readonly char[] Separators = ['/', '\\'];

    public static string TrimTrailingSeparator(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Length > 1 && path[^1] is '/' or '\\' ? path[..^1] : path;
    }

    /// <summary>The last path component, or the path itself when it has none.</summary>
    public static string LastComponent(string path)
    {
        var components = Components(path);
        return components.Length > 0 ? components[^1] : path;
    }

    /// <summary>The folder above the last one, or empty at the top.</summary>
    public static string ParentComponent(string path)
    {
        var components = Components(path);
        return components.Length > 1 ? components[^2] : "";
    }

    private static string[] Components(string path) =>
        TrimTrailingSeparator(path).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
}
