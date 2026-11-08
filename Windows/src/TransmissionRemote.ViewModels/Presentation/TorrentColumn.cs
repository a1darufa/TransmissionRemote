namespace TransmissionRemote.ViewModels;

/// <summary>
/// The torrent list's columns, the same as the Mac app's and Transmission Remote GUI's. The names are saved in
/// <see cref="AppSettings.TorrentColumns"/>, so don't rename them.
/// </summary>
public enum TorrentColumn
{
    Name,
    Size,
    Done,
    Status,
    Seeds,
    Peers,
    Down,
    Up,
    Eta,
    Ratio,
    Added,
    Completed,
    Folder,
    Labels,
    Tracker,
    Queue,
    Downloaded,
    Uploaded,
    Priority,
    TotalSize,
    SizeLeft,
    LastActive,
    SeedingTime,
    TrackerStatus,
    Private,
    BandwidthGroup,
    Id,
}

public static class TorrentColumns
{
    /// <summary>Shown until the user picks columns; the rest are in the column chooser.</summary>
    public static IReadOnlyList<TorrentColumn> Default { get; } =
    [
        TorrentColumn.Name, TorrentColumn.Size, TorrentColumn.Done, TorrentColumn.Status, TorrentColumn.Seeds,
        TorrentColumn.Peers, TorrentColumn.Down, TorrentColumn.Up, TorrentColumn.Eta, TorrentColumn.Ratio,
        TorrentColumn.Added,
    ];

    /// <summary>The saved column layout, skipping names this version doesn't know.</summary>
    public static IReadOnlyList<TorrentColumn> FromSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var columns = settings.TorrentColumns
            .Select(name => Enum.TryParse<TorrentColumn>(name, out var column) ? column : (TorrentColumn?)null)
            .OfType<TorrentColumn>()
            .Distinct()
            .ToList();
        return columns.Count > 0 ? columns : Default;
    }
}
