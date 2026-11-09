using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TransmissionRemote.App.Presentation;
using TransmissionRemote.ViewModels;
using Windows.Foundation;

namespace TransmissionRemote.App.Controls;

/// <summary>
/// The torrent list's column headers. A click sorts (again reverses), dragging a header moves the column, dragging
/// its right edge resizes it, and a right-click asks for the column chooser. The view model does the sorting and
/// remembers the layout; this only reports what the user did.
/// </summary>
public sealed partial class TorrentHeaderRow : Panel
{
    public const double HeaderHeight = 32;

    /// <summary>How far the pointer moves before a press becomes a drag rather than a click.</summary>
    private const double DragThreshold = 6;

    private readonly List<HeaderCell> _cells = [];
    private TorrentColumnLayout? _layout;
    private (TorrentColumn Column, bool Ascending)? _sort;
    private HeaderCell? _pressed;
    private double _pressX;
    private bool _isDragging;

    public TorrentHeaderRow()
    {
        Height = HeaderHeight;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        RightTapped += (_, e) => ChooserRequested?.Invoke(this, e.GetPosition(this));
    }

    internal event EventHandler<TorrentColumn>? SortRequested;

    /// <summary>A header was dragged to a new place: the column and its new index among the visible columns.</summary>
    internal event EventHandler<(TorrentColumn Column, int Index)>? ColumnMoved;

    /// <summary>A column's edge was let go of at a new width.</summary>
    internal event EventHandler<(TorrentColumn Column, double Width)>? ColumnResized;

    /// <summary>Right-click on the headers, at this point: show the column chooser.</summary>
    internal event EventHandler<Point>? ChooserRequested;

    internal void Bind(TorrentColumnLayout layout)
    {
        _layout = layout;
        layout.ColumnsChanged += (_, _) => Rebuild();
        layout.WidthsChanged += (_, _) => InvalidateMeasure();
        Rebuild();
    }

    /// <summary>The sort indicator: an arrow on the sorted column.</summary>
    internal void ShowSort(TorrentColumn column, bool ascending)
    {
        _sort = (column, ascending);
        foreach (var cell in _cells)
        {
            cell.ShowSort(cell.Column == column ? ascending : null);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var cell in _cells)
        {
            cell.Measure(new Size(_layout!.Width(cell.Column), HeaderHeight));
        }
        return new Size(_layout?.TotalWidth ?? 0, HeaderHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        foreach (var cell in _cells)
        {
            var width = _layout!.Width(cell.Column);
            cell.Arrange(new Rect(x, 0, width, HeaderHeight));
            x += width;
        }
        return finalSize;
    }

    private void Rebuild()
    {
        _cells.Clear();
        Children.Clear();
        foreach (var column in _layout!.Columns)
        {
            var cell = new HeaderCell(column);
            cell.PointerPressed += OnCellPressed;
            cell.PointerMoved += OnCellMoved;
            cell.PointerReleased += OnCellReleased;
            cell.Grip.Resized += (_, width) => _layout.SetWidth(column, width);
            cell.Grip.ResizeEnded += (_, width) => ColumnResized?.Invoke(this, (column, width));
            cell.Grip.StartWidth = () => _layout.Width(column);
            _cells.Add(cell);
            Children.Add(cell);
        }
        // New cells after a move or a shown column: the arrow goes back on the sorted one.
        if (_sort is var (sortColumn, ascending))
        {
            ShowSort(sortColumn, ascending);
        }
        InvalidateMeasure();
    }

    private void OnCellPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not HeaderCell cell || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.OriginalSource is ResizeGrip)
        {
            return;
        }
        _pressed = cell;
        _pressX = e.GetCurrentPoint(this).Position.X;
        _isDragging = false;
        cell.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnCellMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pressed is null || !ReferenceEquals(sender, _pressed))
        {
            return;
        }
        var dx = e.GetCurrentPoint(this).Position.X - _pressX;
        if (!_isDragging && Math.Abs(dx) > DragThreshold)
        {
            _isDragging = true;
            _pressed.Opacity = 0.7;
            Canvas.SetZIndex(_pressed, 1);
        }
        if (_isDragging)
        {
            _pressed.RenderTransform = new TranslateTransform { X = dx };
        }
    }

    private void OnCellReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pressed is null || !ReferenceEquals(sender, _pressed))
        {
            return;
        }
        var cell = _pressed;
        _pressed = null;
        cell.ReleasePointerCapture(e.Pointer);
        if (_isDragging)
        {
            cell.RenderTransform = null;
            cell.Opacity = 1;
            Canvas.SetZIndex(cell, 0);
            ColumnMoved?.Invoke(this, (cell.Column, DropIndex(cell.Column, e.GetCurrentPoint(this).Position.X)));
        }
        else
        {
            SortRequested?.Invoke(this, cell.Column);
        }
        _isDragging = false;
        e.Handled = true;
    }

    /// <summary>Where a dragged column lands: after every other column whose middle is left of the pointer.</summary>
    private int DropIndex(TorrentColumn dragged, double pointerX)
    {
        var index = 0;
        var left = 0.0;
        foreach (var column in _layout!.Columns)
        {
            var width = _layout.Width(column);
            if (column != dragged && left + (width / 2) < pointerX)
            {
                index++;
            }
            left += width;
        }
        return index;
    }

    /// <summary>One header: its title, the sort arrow, a divider, and the edge to drag.</summary>
    private sealed partial class HeaderCell : Grid
    {
        private readonly FontIcon _sortArrow = new()
        {
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        public HeaderCell(TorrentColumn column)
        {
            Column = column;
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            var trailing = TorrentColumns.Spec(column).Alignment == ColumnAlignment.Trailing;
            var title = new TextBlock
            {
                Text = ColumnTitles.For(column),
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Padding = TorrentCell.Padding,
                HorizontalAlignment = trailing ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };
            // The arrow sits on the side away from the text's alignment edge, as in File Explorer.
            if (trailing)
            {
                content.Children.Add(_sortArrow);
                content.Children.Add(title);
            }
            else
            {
                content.Children.Add(title);
                content.Children.Add(_sortArrow);
            }
            Children.Add(content);
            Children.Add(new Border
            {
                Width = 1,
                Margin = new Thickness(0, 8, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
            });
            Grip = new ResizeGrip { HorizontalAlignment = HorizontalAlignment.Right };
            Children.Add(Grip);
            AutomationProperties.SetName(this, title.Text);
        }

        public TorrentColumn Column { get; }

        public ResizeGrip Grip { get; }

        public void ShowSort(bool? ascending)
        {
            _sortArrow.Visibility = ascending is null ? Visibility.Collapsed : Visibility.Visible;
            _sortArrow.Glyph = ascending == true ? "" : "";
        }
    }
}

/// <summary>The draggable right edge of a column header, with the resize pointer.</summary>
internal sealed partial class ResizeGrip : Grid
{
    private double _startX;
    private double _startWidth;
    private double _width;
    private bool _isResizing;

    public ResizeGrip()
    {
        Width = 8;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerCaptureLost += (_, _) => _isResizing = false;
    }

    /// <summary>The column's width when the drag starts.</summary>
    public Func<double> StartWidth { get; set; } = () => 0;

    /// <summary>The width while dragging, for the live layout.</summary>
    public event EventHandler<double>? Resized;

    /// <summary>The width when the drag ends, to remember.</summary>
    public event EventHandler<double>? ResizeEnded;

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        _startX = e.GetCurrentPoint(null).Position.X;
        _startWidth = StartWidth();
        _width = _startWidth;
        _isResizing = CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isResizing)
        {
            _width = _startWidth + e.GetCurrentPoint(null).Position.X - _startX;
            Resized?.Invoke(this, _width);
            e.Handled = true;
        }
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isResizing)
        {
            _isResizing = false;
            ReleasePointerCapture(e.Pointer);
            ResizeEnded?.Invoke(this, _width);
            e.Handled = true;
        }
    }
}
