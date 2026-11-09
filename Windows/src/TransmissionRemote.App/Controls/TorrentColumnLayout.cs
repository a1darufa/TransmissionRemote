using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Controls;

/// <summary>
/// The visible columns and their widths, shared by the header row and every list row so they line up. Widths
/// change live while a header is dragged; the view model saves them when the drag ends.
/// </summary>
internal sealed class TorrentColumnLayout
{
    private readonly Dictionary<TorrentColumn, double> _widths = [];

    public IReadOnlyList<TorrentColumn> Columns { get; private set; } = [];

    /// <summary>Raised when columns are added, removed or moved: rows rebuild their cells.</summary>
    public event EventHandler? ColumnsChanged;

    /// <summary>Raised when a width changes: rows only lay their cells out again.</summary>
    public event EventHandler? WidthsChanged;

    public double TotalWidth => Columns.Sum(Width);

    public double Width(TorrentColumn column) => _widths.GetValueOrDefault(column, TorrentColumns.Spec(column).IdealWidth);

    public void Reset(IReadOnlyList<TorrentColumn> columns, Func<TorrentColumn, double> width)
    {
        _widths.Clear();
        foreach (var column in columns)
        {
            _widths[column] = width(column);
        }
        Columns = columns;
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetWidth(TorrentColumn column, double width)
    {
        _widths[column] = Math.Max(width, TorrentColumns.Spec(column).MinimumWidth);
        WidthsChanged?.Invoke(this, EventArgs.Empty);
    }
}
