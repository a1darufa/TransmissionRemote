using System.Collections.ObjectModel;
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

/// <summary>Brings an observable collection of rows in line with a new list, moving and updating rather than resetting.</summary>
internal static class TorrentRowSync
{
    public static void Apply(ObservableCollection<TorrentRow> rows, IReadOnlyList<Torrent> desired)
    {
        var existing = rows.ToDictionary(row => row.Id);
        for (var index = 0; index < desired.Count; index++)
        {
            var torrent = desired[index];
            if (index < rows.Count && rows[index].Id == torrent.Id)
            {
                rows[index].Update(torrent);
            }
            else if (existing.TryGetValue(torrent.Id, out var row))
            {
                rows.Move(rows.IndexOf(row), index);
                row.Update(torrent);
            }
            else
            {
                var newRow = new TorrentRow(torrent);
                existing[torrent.Id] = newRow;
                rows.Insert(index, newRow);
            }
        }
        while (rows.Count > desired.Count)
        {
            rows.RemoveAt(rows.Count - 1);
        }
    }
}
