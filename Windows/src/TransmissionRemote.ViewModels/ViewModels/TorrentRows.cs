using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>One row of the torrent list. Rows are updated in place, so selection and scrolling survive refreshes.</summary>
public sealed partial class TorrentRow(Torrent torrent) : ObservableObject
{
    public int Id { get; } = torrent.Id;

    [ObservableProperty]
    public partial Torrent Torrent { get; private set; } = torrent;

    internal void Update(Torrent torrent)
    {
        if (torrent != Torrent)
        {
            Torrent = torrent;
        }
    }
}

/// <summary>Sort orders for every list column, matching the Mac app's.</summary>
public static class TorrentSorting
{
    public static IComparer<Torrent> Comparer(TorrentColumn column, bool ascending)
    {
        var primary = PrimaryComparison(column);
        // Ties fall back to the name and then the ID, so the order never jumps between refreshes.
        return Comparer<Torrent>.Create((left, right) =>
        {
            var order = primary(left, right);
            if (!ascending)
            {
                order = -order;
            }
            if (order == 0)
            {
                order = NaturalStringComparer.Instance.Compare(left.Name, right.Name);
            }
            return order != 0 ? order : left.Id.CompareTo(right.Id);
        });
    }

    private static Comparison<Torrent> PrimaryComparison(TorrentColumn column) => column switch
    {
        TorrentColumn.Name => (a, b) => NaturalStringComparer.Instance.Compare(a.Name, b.Name),
        TorrentColumn.Size => By(t => t.SizeWhenDone),
        TorrentColumn.Done => By(t => t.PercentDone),
        TorrentColumn.Status => By(t => ((int)t.Status, t.HasError)),
        // Connected peers first, then the swarm's size, so idle torrents still sort by popularity (transgui #1180).
        TorrentColumn.Seeds => By(t => (t.PeersSendingToUs, t.TrackerSummary?.Seeders ?? -1)),
        TorrentColumn.Peers => By(t => (t.PeersGettingFromUs, t.TrackerSummary?.Leechers ?? -1)),
        TorrentColumn.Down => By(t => t.RateDownload),
        TorrentColumn.Up => By(t => t.RateUpload),
        TorrentColumn.Eta => By(t => t.Eta >= 0 ? t.Eta : int.MaxValue),
        TorrentColumn.Ratio => By(t => t.UploadRatio),
        TorrentColumn.Added => By(t => t.AddedDate ?? DateTimeOffset.MinValue),
        TorrentColumn.Completed => By(t => t.DoneDate ?? DateTimeOffset.MinValue),
        TorrentColumn.Folder => (a, b) => NaturalStringComparer.Instance.Compare(a.DownloadDir, b.DownloadDir),
        TorrentColumn.Labels => (a, b) => NaturalStringComparer.Instance.Compare(string.Join(", ", a.Labels), string.Join(", ", b.Labels)),
        TorrentColumn.Tracker => (a, b) => string.CompareOrdinal(PrimaryTracker(a), PrimaryTracker(b)),
        TorrentColumn.Queue => By(t => t.QueuePosition),
        TorrentColumn.Downloaded => By(t => t.DownloadedEver),
        TorrentColumn.Uploaded => By(t => t.UploadedEver),
        TorrentColumn.Priority => By(t => (int)t.BandwidthPriority),
        TorrentColumn.TotalSize => By(t => t.TotalSize),
        TorrentColumn.SizeLeft => By(t => t.LeftUntilDone),
        TorrentColumn.LastActive => By(t => t.ActivityDate ?? DateTimeOffset.MinValue),
        TorrentColumn.SeedingTime => By(t => t.SecondsSeeding),
        TorrentColumn.TrackerStatus => By(t => (int)(t.TrackerSummary?.Status ?? TrackerStatus.NotContacted)),
        TorrentColumn.Private => By(t => t.IsPrivate),
        TorrentColumn.BandwidthGroup => (a, b) => NaturalStringComparer.Instance.Compare(a.BandwidthGroup, b.BandwidthGroup),
        _ => By(t => t.Id),
    };

    private static string? PrimaryTracker(Torrent torrent) => torrent.TrackerHosts.Count > 0 ? torrent.TrackerHosts[0] : null;

    private static Comparison<Torrent> By<TKey>(Func<Torrent, TKey> key) =>
        (left, right) => Comparer<TKey>.Default.Compare(key(left), key(right));
}

/// <summary>
/// The torrent list's rows. Besides the usual one-at-a-time changes, it can replace everything with a single reset:
/// a list control handles each move or insert on its own, which took seconds for a thousand rows.
/// </summary>
public sealed class TorrentRowCollection : ObservableCollection<TorrentRow>
{
    internal void ReplaceAll(IEnumerable<TorrentRow> rows)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var row in rows)
        {
            Items.Add(row);
        }
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

/// <summary>
/// Brings the rows in line with a new list. Small changes (a refresh, a torrent that sorts elsewhere now) update rows
/// in place and move as few as possible, so the selection, focus and scroll position stay put. Big ones (another sort
/// order, the first load) replace the rows at once, which is far quicker than hundreds of moves.
/// </summary>
internal static class TorrentRowSync
{
    /// <summary>
    /// The most row changes made one at a time. Each costs the list control a few milliseconds with a thousand rows.
    /// </summary>
    internal const int MaxSingleChanges = 32;

    public static void Apply(TorrentRowCollection rows, IReadOnlyList<Torrent> desired)
    {
        var existing = rows.ToDictionary(row => row.Id);
        var staying = RowsAlreadyInOrder(rows, desired);
        // Each row out of place is taken out and put back; gone rows are taken out, new ones put in.
        var changes = (rows.Count - staying.Count) + (desired.Count - staying.Count);
        if (changes > MaxSingleChanges)
        {
            rows.ReplaceAll(desired.Select(torrent => RowFor(torrent, existing)));
            return;
        }

        // What's left is in the right order; everything else then goes in at its place.
        for (var index = rows.Count - 1; index >= 0; index--)
        {
            if (!staying.Contains(rows[index].Id))
            {
                rows.RemoveAt(index);
            }
        }
        for (var index = 0; index < desired.Count; index++)
        {
            var torrent = desired[index];
            if (index < rows.Count && rows[index].Id == torrent.Id)
            {
                rows[index].Update(torrent);
            }
            else
            {
                rows.Insert(index, RowFor(torrent, existing));
            }
        }
    }

    /// <summary>The existing row for a torrent, brought up to date, or a new one: rows keep their identity.</summary>
    private static TorrentRow RowFor(Torrent torrent, Dictionary<int, TorrentRow> existing)
    {
        if (existing.TryGetValue(torrent.Id, out var row))
        {
            row.Update(torrent);
            return row;
        }
        return new TorrentRow(torrent);
    }

    /// <summary>
    /// The IDs of the most rows that can keep their place: the longest run of current rows that the new list has in
    /// the same order (a longest increasing subsequence of their current positions, found in O(n log n)).
    /// </summary>
    internal static HashSet<int> RowsAlreadyInOrder(IReadOnlyList<TorrentRow> rows, IReadOnlyList<Torrent> desired)
    {
        var currentIndex = new Dictionary<int, int>(rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            currentIndex[rows[index].Id] = index;
        }
        var positions = desired
            .Where(torrent => currentIndex.ContainsKey(torrent.Id))
            .Select(torrent => currentIndex[torrent.Id])
            .ToList();

        // tails[k]: index into positions of the smallest last element of an increasing run of length k + 1.
        var tails = new List<int>();
        var previous = new int[positions.Count];
        for (var i = 0; i < positions.Count; i++)
        {
            var length = LowerBound(tails, positions, positions[i]);
            previous[i] = length > 0 ? tails[length - 1] : -1;
            if (length == tails.Count)
            {
                tails.Add(i);
            }
            else
            {
                tails[length] = i;
            }
        }

        var staying = new HashSet<int>();
        for (var i = tails.Count > 0 ? tails[^1] : -1; i >= 0; i = previous[i])
        {
            staying.Add(rows[positions[i]].Id);
        }
        return staying;
    }

    /// <summary>The first run length whose last element isn't smaller than <paramref name="value"/>.</summary>
    private static int LowerBound(List<int> tails, List<int> positions, int value)
    {
        var low = 0;
        var high = tails.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (positions[tails[middle]] < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low;
    }
}
