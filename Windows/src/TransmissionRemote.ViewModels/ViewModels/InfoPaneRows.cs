using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>What the Trackers tab's "Last announce" column shows for a tracker.</summary>
public enum TrackerAnnounce
{
    /// <summary>The last announce worked: show the peers it returned.</summary>
    Succeeded,

    /// <summary>The last announce failed: show the tracker's message.</summary>
    Failed,

    /// <summary>Not announced to, because it's a backup for another tracker in its tier.</summary>
    Backup,

    NotYetAnnounced,
}

/// <summary>A row of the Trackers tab, updated in place on every refresh.</summary>
public sealed partial class TrackerRow : ObservableObject
{
    internal TrackerRow(TrackerStat stat, DateTimeOffset now) => Update(stat, now);

    [ObservableProperty]
    public partial TrackerStat Stat { get; private set; }

    /// <summary>How long until the next announce, while that's in the future.</summary>
    [ObservableProperty]
    public partial TimeSpan? NextAnnounceIn { get; private set; }

    public TrackerAnnounce Announce => Stat switch
    {
        { HasAnnounced: true, LastAnnounceSucceeded: true } => TrackerAnnounce.Succeeded,
        { HasAnnounced: true } => TrackerAnnounce.Failed,
        { IsBackup: true } => TrackerAnnounce.Backup,
        _ => TrackerAnnounce.NotYetAnnounced,
    };

    internal string Key => Stat.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [MemberNotNull(nameof(Stat))]
    internal void Update(TrackerStat stat, DateTimeOffset now)
    {
        Stat = stat;
        NextAnnounceIn = stat.NextAnnounceTime is { } next && next > now ? next - now : null;
    }

    partial void OnStatChanged(TrackerStat value) => OnPropertyChanged(nameof(Announce));
}

/// <summary>A row of the Peers tab, with the host name and country found for its address, if any.</summary>
public sealed partial class PeerRow : ObservableObject
{
    internal PeerRow(Peer peer, string? hostName, string? countryCode)
    {
        Peer = peer;
        HostName = hostName;
        CountryCode = countryCode;
    }

    [ObservableProperty]
    public partial Peer Peer { get; internal set; }

    /// <summary>The name reverse DNS found, while resolving host names is on.</summary>
    [ObservableProperty]
    public partial string? HostName { get; internal set; }

    /// <summary>The ISO country code ("DE"), while showing countries is on and the database knows the address.</summary>
    [ObservableProperty]
    public partial string? CountryCode { get; internal set; }

    internal string Key => Peer.Id;
}

/// <summary>A row of the Files tab: a file or folder, its depth, and whether the folder is open.</summary>
public sealed partial class FileRow : ObservableObject
{
    internal FileRow(VisibleFileNode visible) => Update(visible);

    [ObservableProperty]
    public partial FileNode Node { get; private set; }

    [ObservableProperty]
    public partial int Depth { get; private set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; private set; }

    internal string Key => Node.Id;

    [MemberNotNull(nameof(Node))]
    internal void Update(VisibleFileNode visible)
    {
        Node = visible.Node;
        Depth = visible.Depth;
        IsExpanded = visible.IsExpanded;
    }
}

/// <summary>
/// Updates a list of rows to match new items without replacing the rows that stay, so the list keeps its selection
/// and scroll position across refreshes. Keys must be unique within each list.
/// </summary>
internal static class RowListSync
{
    public static void Apply<TRow, TItem>(
        ObservableCollection<TRow> rows,
        IReadOnlyList<TItem> items,
        Func<TItem, string> keyOfItem,
        Func<TRow, string> keyOfRow,
        Func<TItem, TRow> create,
        Action<TRow, TItem> update)
        where TRow : class
    {
        var wanted = items.Select(keyOfItem).ToHashSet(StringComparer.Ordinal);
        for (var index = rows.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(keyOfRow(rows[index])))
            {
                rows.RemoveAt(index);
            }
        }

        var existing = rows.ToDictionary(keyOfRow, StringComparer.Ordinal);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (!existing.TryGetValue(keyOfItem(item), out var row))
            {
                rows.Insert(index, create(item));
                continue;
            }
            if (!ReferenceEquals(rows[index], row))
            {
                rows.Move(rows.IndexOf(row), index);
            }
            update(row, item);
        }
    }
}
