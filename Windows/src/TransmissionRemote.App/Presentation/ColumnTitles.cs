using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Presentation;

/// <summary>The torrent list's column titles, the Mac's in sentence case.</summary>
internal static class ColumnTitles
{
    public static string For(TorrentColumn column) => column switch
    {
        TorrentColumn.Name => Strings.Name,
        TorrentColumn.Size => Strings.Size,
        TorrentColumn.Done => Strings.Done,
        TorrentColumn.Status => Strings.Status,
        TorrentColumn.Seeds => Strings.Seeds,
        TorrentColumn.Peers => Strings.Peers,
        TorrentColumn.Down => Strings.Down,
        TorrentColumn.Up => Strings.Up,
        TorrentColumn.Eta => Strings.ETA,
        TorrentColumn.Ratio => Strings.Ratio,
        TorrentColumn.Added => Strings.Added,
        TorrentColumn.Completed => Strings.Completed,
        TorrentColumn.Folder => Strings.Folder,
        TorrentColumn.Labels => Strings.Labels,
        TorrentColumn.Tracker => Strings.Tracker,
        TorrentColumn.Queue => Strings.Queue,
        TorrentColumn.Downloaded => Strings.Downloaded,
        TorrentColumn.Uploaded => Strings.Uploaded,
        TorrentColumn.Priority => Strings.Priority_D60DBB,
        TorrentColumn.TotalSize => Strings.TotalSize_9AC7DE,
        TorrentColumn.SizeLeft => Strings.SizeLeft,
        TorrentColumn.LastActive => Strings.LastActive,
        TorrentColumn.SeedingTime => Strings.SeedingTime,
        TorrentColumn.TrackerStatus => Strings.TrackerStatus,
        TorrentColumn.Private => Strings.Private,
        TorrentColumn.BandwidthGroup => Strings.BandwidthGroup_319062,
        TorrentColumn.Id => Strings.ID,
        _ => column.ToString(),
    };
}
