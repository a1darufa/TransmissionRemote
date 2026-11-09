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

/// <summary>Where a column's content sits: numbers right, text left.</summary>
public enum ColumnAlignment
{
    Leading,
    Trailing,
    Center,
}

/// <summary>A column's fixed traits, the Mac app's: its width range in effective pixels and its alignment.</summary>
public sealed record TorrentColumnSpec(TorrentColumn Column, double MinimumWidth, double IdealWidth, ColumnAlignment Alignment)
{
    /// <summary>Every column but Name can be hidden; a list without names would be useless.</summary>
    public bool CanHide => Column != TorrentColumn.Name;
}

/// <summary>An entry of the column chooser: the column and whether it's shown.</summary>
public sealed record ColumnChoice(TorrentColumn Column, bool IsVisible, bool CanHide);

public static class TorrentColumns
{
    private const ColumnAlignment Leading = ColumnAlignment.Leading;
    private const ColumnAlignment Trailing = ColumnAlignment.Trailing;

    private static readonly Dictionary<TorrentColumn, TorrentColumnSpec> Specs = new TorrentColumnSpec[]
    {
        new(TorrentColumn.Name, 180, 340, Leading),
        new(TorrentColumn.Size, 60, 80, Trailing),
        new(TorrentColumn.Done, 80, 120, Leading),
        new(TorrentColumn.Status, 70, 110, Leading),
        new(TorrentColumn.Seeds, 40, 75, Trailing),
        new(TorrentColumn.Peers, 40, 75, Trailing),
        new(TorrentColumn.Down, 60, 85, Trailing),
        new(TorrentColumn.Up, 60, 85, Trailing),
        new(TorrentColumn.Eta, 50, 70, Trailing),
        new(TorrentColumn.Ratio, 40, 55, Trailing),
        new(TorrentColumn.Added, 90, 140, Leading),
        new(TorrentColumn.Completed, 90, 140, Leading),
        new(TorrentColumn.Folder, 80, 180, Leading),
        new(TorrentColumn.Labels, 60, 100, Leading),
        new(TorrentColumn.Tracker, 80, 140, Leading),
        new(TorrentColumn.Queue, 40, 50, Trailing),
        new(TorrentColumn.Downloaded, 60, 85, Trailing),
        new(TorrentColumn.Uploaded, 60, 85, Trailing),
        new(TorrentColumn.Priority, 50, 65, Leading),
        new(TorrentColumn.TotalSize, 60, 80, Trailing),
        new(TorrentColumn.SizeLeft, 60, 80, Trailing),
        new(TorrentColumn.LastActive, 90, 140, Leading),
        new(TorrentColumn.SeedingTime, 60, 85, Trailing),
        new(TorrentColumn.TrackerStatus, 80, 140, Leading),
        new(TorrentColumn.Private, 40, 50, ColumnAlignment.Center),
        new(TorrentColumn.BandwidthGroup, 60, 110, Leading),
        new(TorrentColumn.Id, 30, 45, Trailing),
    }.ToDictionary(spec => spec.Column);

    public static TorrentColumnSpec Spec(TorrentColumn column) => Specs[column];

    /// <summary>The saved width, or the ideal one, never below the column's minimum.</summary>
    public static double Width(AppSettings settings, TorrentColumn column)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var spec = Spec(column);
        var saved = settings.TorrentColumnWidths.TryGetValue(column.ToString(), out var width) && double.IsFinite(width)
            ? width
            : spec.IdealWidth;
        return Math.Max(saved, spec.MinimumWidth);
    }

    /// <summary>The visible columns with one moved to a new place, as dragging its header does.</summary>
    public static IReadOnlyList<TorrentColumn> Moved(IReadOnlyList<TorrentColumn> visible, TorrentColumn column, int index)
    {
        ArgumentNullException.ThrowIfNull(visible);
        if (!visible.Contains(column))
        {
            return visible;
        }
        var columns = visible.Where(other => other != column).ToList();
        columns.Insert(Math.Clamp(index, 0, columns.Count), column);
        return columns;
    }

    /// <summary>Shows a column at the end, or hides it; Name always stays.</summary>
    public static IReadOnlyList<TorrentColumn> WithVisibility(IReadOnlyList<TorrentColumn> visible, TorrentColumn column, bool isVisible)
    {
        ArgumentNullException.ThrowIfNull(visible);
        if (isVisible)
        {
            return visible.Contains(column) ? visible : [.. visible, column];
        }
        return Spec(column).CanHide ? [.. visible.Where(other => other != column)] : visible;
    }

    /// <summary>The column chooser's entries: the visible columns in their order, then the hidden ones.</summary>
    public static IReadOnlyList<ColumnChoice> Choices(IReadOnlyList<TorrentColumn> visible)
    {
        ArgumentNullException.ThrowIfNull(visible);
        var hidden = Enum.GetValues<TorrentColumn>().Where(column => !visible.Contains(column));
        return
        [
            .. visible.Select(column => new ColumnChoice(column, IsVisible: true, Spec(column).CanHide)),
            .. hidden.Select(column => new ColumnChoice(column, IsVisible: false, Spec(column).CanHide)),
        ];
    }

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
