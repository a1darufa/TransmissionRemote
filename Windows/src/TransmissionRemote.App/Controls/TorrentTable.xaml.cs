using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TransmissionRemote.App.Presentation;
using TransmissionRemote.ViewModels;
using Windows.Foundation;

namespace TransmissionRemote.App.Controls;

/// <summary>
/// The torrent list: columns from the view model's layout, sorting, resizing, reordering, the column chooser, and
/// a selection by torrent ID that survives refreshes and re-sorting.
/// </summary>
public sealed partial class TorrentTable : UserControl
{
    private readonly TorrentColumnLayout _layout = new();
    private MainViewModel? _viewModel;
    private TorrentCellText? _text;
    private ScrollViewer? _scroller;

    /// <summary>
    /// True while the table itself changes the list's selection, or the view model reorders the rows: the
    /// ListView's own selection events then don't overwrite the torrents the user selected.
    /// </summary>
    private bool _isSyncingSelection;

    public TorrentTable()
    {
        InitializeComponent();
        Header.Bind(_layout);
        Header.SortRequested += (_, column) => ViewModel.SortBy(column);
        Header.ColumnMoved += (_, move) => ViewModel.MoveColumn(move.Column, move.Index);
        Header.ColumnResized += (_, resize) => ViewModel.SetColumnWidth(resize.Column, resize.Width);
        Header.ChooserRequested += (_, point) => ShowColumnChooser(point);
        _layout.ColumnsChanged += (_, _) => UpdateContentWidth();
        _layout.WidthsChanged += (_, _) => UpdateContentWidth();
    }

    /// <summary>The menu for the selected torrents (right-click, Shift+F10, the Menu key).</summary>
    public MenuFlyout? RowMenu { get; set; }

    /// <summary>The list itself: the scope of keys that only work in the list, and what gets the focus.</summary>
    internal ListView List => TorrentList;


    private MainViewModel ViewModel => _viewModel ?? throw new InvalidOperationException("Call Bind first.");

    /// <summary>Set once, before the table loads.</summary>
    internal void Bind(MainViewModel viewModel, TorrentCellText text)
    {
        _viewModel = viewModel;
        _text = text;
        TorrentList.ItemsSource = viewModel.Rows;
        viewModel.PropertyChanged += OnViewModelChanged;
        viewModel.Connection.PropertyChanged += OnConnectionChanged;
        viewModel.RowsUpdating += (_, _) => _isSyncingSelection = true;
        viewModel.RowsUpdated += (_, _) => RestoreSelection();
        ResetColumns();
        Header.ShowSort(viewModel.SortColumn, viewModel.SortAscending);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.VisibleColumns):
                ResetColumns();
                break;
            case nameof(MainViewModel.SortColumn) or nameof(MainViewModel.SortAscending):
                Header.ShowSort(ViewModel.SortColumn, ViewModel.SortAscending);
                break;
        }
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ServerConnection.Selection) && !_isSyncingSelection)
        {
            RestoreSelection();
        }
    }

    private void ResetColumns() => _layout.Reset(ViewModel.VisibleColumns, ViewModel.ColumnWidth);

    // Rows ----------------------------------------------------------------------------------------------------------

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer.ContentTemplateRoot is TorrentRowView row)
        {
            if (args.InRecycleQueue)
            {
                row.Unbind();
            }
            else if (args.Item is TorrentRow torrentRow && _text is not null)
            {
                row.Bind(torrentRow, _layout, _text, args.ItemContainer);
            }
        }
        args.Handled = true;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection)
        {
            return;
        }
        ViewModel.Connection.Selection = TorrentList.SelectedItems.OfType<TorrentRow>().Select(row => row.Id).ToHashSet();
    }

    /// <summary>Selects exactly the view model's selection again, after rows moved or the selection changed elsewhere.</summary>
    private void RestoreSelection()
    {
        _isSyncingSelection = true;
        try
        {
            var wanted = ViewModel.Connection.Selection;
            var selected = TorrentList.SelectedItems.OfType<TorrentRow>().Select(row => row.Id).ToHashSet();
            if (selected.SetEquals(wanted))
            {
                return;
            }
            // Whole runs of rows at once: one row at a time took half a second for a thousand selected torrents.
            foreach (var range in IndexRuns(row => selected.Contains(row.Id) && !wanted.Contains(row.Id)))
            {
                TorrentList.DeselectRange(range);
            }
            foreach (var range in IndexRuns(row => wanted.Contains(row.Id) && !selected.Contains(row.Id)))
            {
                TorrentList.SelectRange(range);
            }
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    /// <summary>The runs of consecutive rows that match, as index ranges for the list's range selection.</summary>
    private List<ItemIndexRange> IndexRuns(Func<TorrentRow, bool> matches)
    {
        var runs = new List<ItemIndexRange>();
        var rows = ViewModel.Rows;
        var start = -1;
        for (var index = 0; index <= rows.Count; index++)
        {
            var isMatch = index < rows.Count && matches(rows[index]);
            if (isMatch && start < 0)
            {
                start = index;
            }
            else if (!isMatch && start >= 0)
            {
                runs.Add(new ItemIndexRange(start, (uint)(index - start)));
                start = -1;
            }
        }
        return runs;
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: TorrentRow } || TorrentList.SelectedItems.Count > 0)
        {
            ViewModel.ShowInfoForSelection();
        }
    }

    /// <summary>
    /// Right-click, Shift+F10 or the Menu key: the torrent menu for the selection. A right-clicked row that isn't
    /// selected becomes the selection first, the Windows way.
    /// </summary>
    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (RowMenu is null)
        {
            return;
        }
        var row = (args.OriginalSource as DependencyObject)?.FindAscendantOrSelf<ListViewItem>();
        if (row is not null && !row.IsSelected)
        {
            TorrentList.SelectedItems.Clear();
            row.IsSelected = true;
        }
        if (args.TryGetPosition(TorrentList, out var point))
        {
            RowMenu.ShowAt(TorrentList, new FlyoutShowOptions { Position = point });
        }
        else
        {
            RowMenu.ShowAt(row ?? (FrameworkElement)TorrentList);
        }
        args.Handled = true;
    }

    // Columns -------------------------------------------------------------------------------------------------------

    /// <summary>The column chooser, as the Mac's table header menu: every column, the visible ones checked.</summary>
    private void ShowColumnChooser(Point point)
    {
        var menu = new MenuFlyout();
        foreach (var choice in ViewModel.ColumnChoices)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = ColumnTitles.For(choice.Column),
                IsChecked = choice.IsVisible,
                IsEnabled = choice.CanHide,
            };
            item.Click += (_, _) => ViewModel.SetColumnVisible(choice.Column, item.IsChecked);
            menu.Items.Add(item);
        }
        menu.ShowAt(Header, new FlyoutShowOptions { Position = point });
    }

    // Sideways scrolling --------------------------------------------------------------------------------------------

    private void OnListLoaded(object sender, RoutedEventArgs e)
    {
        _scroller = TorrentList.FindDescendant<ScrollViewer>();
        if (_scroller is not null)
        {
            _scroller.ViewChanged += (_, _) => Header.RenderTransform = new TranslateTransform { X = -_scroller.HorizontalOffset };
        }
        UpdateContentWidth();
    }

    /// <summary>The rows are as wide as the columns, so the list scrolls sideways when they don't fit.</summary>
    private void UpdateContentWidth()
    {
        if (TorrentList.ItemsPanelRoot is { } panel)
        {
            panel.MinWidth = _layout.TotalWidth;
        }
    }

    private void OnHeaderViewportSizeChanged(object sender, SizeChangedEventArgs e) =>
        HeaderViewport.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
}
