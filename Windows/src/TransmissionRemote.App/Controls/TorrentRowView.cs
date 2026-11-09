using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TransmissionRemote.ViewModels;
using Windows.Foundation;

namespace TransmissionRemote.App.Controls;

/// <summary>
/// One row of the torrent list: a cell per visible column, laid out side by side at the shared layout's widths.
/// The list recycles rows while scrolling, so <see cref="Bind"/> may give it another torrent at any time.
/// </summary>
public sealed partial class TorrentRowView : Panel
{
    public const double RowHeight = 32;

    private readonly List<TorrentCell> _cells = [];
    private TorrentColumnLayout? _layout;
    private TorrentCellText? _text;
    private TorrentRow? _row;
    private DependencyObject? _container;
    private IReadOnlyList<TorrentColumn> _builtColumns = [];

    /// <summary>Shows a torrent; <paramref name="container"/> is the list item, which gets the row's Narrator name.</summary>
    internal void Bind(TorrentRow row, TorrentColumnLayout layout, TorrentCellText text, DependencyObject container)
    {
        if (!ReferenceEquals(_layout, layout))
        {
            if (_layout is not null)
            {
                _layout.ColumnsChanged -= OnColumnsChanged;
                _layout.WidthsChanged -= OnWidthsChanged;
            }
            _layout = layout;
            layout.ColumnsChanged += OnColumnsChanged;
            layout.WidthsChanged += OnWidthsChanged;
        }
        if (!ReferenceEquals(_row, row))
        {
            if (_row is not null)
            {
                _row.PropertyChanged -= OnRowChanged;
            }
            _row = row;
            row.PropertyChanged += OnRowChanged;
        }
        _text = text;
        _container = container;
        if (!_builtColumns.SequenceEqual(layout.Columns))
        {
            BuildCells();
        }
        Refresh();
    }

    /// <summary>Lets go of the torrent when the list recycles the row.</summary>
    internal void Unbind()
    {
        if (_row is not null)
        {
            _row.PropertyChanged -= OnRowChanged;
            _row = null;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_layout is null)
        {
            return new Size(0, RowHeight);
        }
        for (var index = 0; index < Children.Count && index < _builtColumns.Count; index++)
        {
            var width = Math.Max(_layout.Width(_builtColumns[index]) - TorrentCell.Padding.Left - TorrentCell.Padding.Right, 0);
            Children[index].Measure(new Size(width, RowHeight));
        }
        return new Size(_layout.TotalWidth, RowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_layout is null)
        {
            return finalSize;
        }
        var x = 0.0;
        for (var index = 0; index < Children.Count && index < _builtColumns.Count; index++)
        {
            var width = _layout.Width(_builtColumns[index]);
            var content = Math.Max(width - TorrentCell.Padding.Left - TorrentCell.Padding.Right, 0);
            Children[index].Arrange(new Rect(x + TorrentCell.Padding.Left, 0, content, finalSize.Height));
            x += width;
        }
        return finalSize;
    }

    private void BuildCells()
    {
        _cells.Clear();
        Children.Clear();
        _builtColumns = _layout!.Columns;
        foreach (var column in _builtColumns)
        {
            var cell = TorrentCell.Create(column);
            _cells.Add(cell);
            Children.Add(cell.Element);
        }
    }

    private void Refresh()
    {
        if (_row is null || _text is null)
        {
            return;
        }
        foreach (var cell in _cells)
        {
            cell.Update(_row.Torrent, _text);
        }
        if (_container is not null)
        {
            AutomationProperties.SetName(_container, _text.RowName(_row.Torrent));
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void OnColumnsChanged(object? sender, EventArgs e)
    {
        BuildCells();
        Refresh();
        InvalidateMeasure();
    }

    private void OnWidthsChanged(object? sender, EventArgs e) => InvalidateMeasure();
}
