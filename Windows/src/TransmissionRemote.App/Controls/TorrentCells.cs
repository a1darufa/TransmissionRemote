using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using TransmissionKit;
using TransmissionRemote.App.Presentation;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Controls;

/// <summary>One cell of a torrent row: an element, and how to show a torrent in it.</summary>
internal abstract class TorrentCell
{
    /// <summary>Space on either side of a cell's content, so neighbouring columns don't touch.</summary>
    public static readonly Thickness Padding = new(8, 0, 8, 0);

    public abstract FrameworkElement Element { get; }

    public abstract void Update(Torrent torrent, TorrentCellText text);

    public static TorrentCell Create(TorrentColumn column) => column switch
    {
        TorrentColumn.Name => new NameCell(),
        TorrentColumn.Done => new ProgressCell(),
        TorrentColumn.Private => new PrivateCell(),
        _ => new TextCell(column),
    };

    protected static TextBlock CellText(TextAlignment alignment) => new()
    {
        TextAlignment = alignment,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// A brush of our own, or none: then the control's default from its style and theme, which a null brush would
    /// hide instead (no brush means nothing is drawn).
    /// </summary>
    protected static void SetBrush(DependencyObject element, DependencyProperty property, Brush? brush)
    {
        if (brush is null)
        {
            element.ClearValue(property);
        }
        else
        {
            element.SetValue(property, brush);
        }
    }

    /// <summary>Sets a tooltip only when it changed: refreshes come every few seconds.</summary>
    protected static void SetToolTip(FrameworkElement element, ref string? shown, string? text)
    {
        if (text != shown)
        {
            ToolTipService.SetToolTip(element, text);
            shown = text;
        }
    }
}

/// <summary>Plain text, aligned the column's way; numbers in tabular figures so they don't jitter.</summary>
internal sealed class TextCell : TorrentCell
{
    private readonly TorrentColumn _column;
    private readonly TextBlock _text;
    private string? _toolTip;

    public TextCell(TorrentColumn column)
    {
        _column = column;
        var alignment = TorrentColumns.Spec(column).Alignment;
        _text = CellText(alignment == ColumnAlignment.Trailing ? TextAlignment.Right : TextAlignment.Left);
        if (alignment == ColumnAlignment.Trailing)
        {
            Typography.SetNumeralAlignment(_text, FontNumeralAlignment.Tabular);
        }
    }

    public override FrameworkElement Element => _text;

    public override void Update(Torrent torrent, TorrentCellText text)
    {
        _text.Text = text.Text(torrent, _column);
        SetToolTip(_text, ref _toolTip, text.Tooltip(torrent, _column));
        if (_column == TorrentColumn.Status)
        {
            // The caution colour for an error, as the Mac's orange; the words say it too.
            SetBrush(_text, TextBlock.ForegroundProperty, torrent.HasError ? StatusVisuals.Brush(TorrentStatusTone.Warning) : null);
        }
    }
}

/// <summary>The status glyph in its colour, then the name; the full name or the error in the tooltip.</summary>
internal sealed class NameCell : TorrentCell
{
    private readonly Grid _grid = new() { ColumnSpacing = 8 };
    private readonly FontIcon _glyph = new() { FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _name = CellText(TextAlignment.Left);
    private string? _toolTip;

    public NameCell()
    {
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // The status is in the Status column and the row's name; the glyph only repeats it for the eye.
        AutomationProperties.SetAccessibilityView(_glyph, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Grid.SetColumn(_name, 1);
        _grid.Children.Add(_glyph);
        _grid.Children.Add(_name);
    }

    public override FrameworkElement Element => _grid;

    public override void Update(Torrent torrent, TorrentCellText text)
    {
        var status = TorrentDisplayStatus.Of(torrent);
        _glyph.Glyph = StatusVisuals.Glyph(status.Icon);
        _glyph.Foreground = StatusVisuals.Brush(status.Tone);
        _name.Text = torrent.Name;
        SetToolTip(_grid, ref _toolTip, text.Tooltip(torrent, TorrentColumn.Name));
    }
}

/// <summary>A progress bar with the percentage after it: green when complete, accent otherwise.</summary>
internal sealed class ProgressCell : TorrentCell
{
    private readonly Grid _grid = new() { ColumnSpacing = 6 };
    private readonly ProgressBar _bar = new() { Maximum = 1, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _percent = new()
    {
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        TextAlignment = TextAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        MinWidth = 40,
    };

    public ProgressCell()
    {
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Typography.SetNumeralAlignment(_percent, FontNumeralAlignment.Tabular);
        AutomationProperties.SetAccessibilityView(_percent, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Grid.SetColumn(_percent, 1);
        _grid.Children.Add(_bar);
        _grid.Children.Add(_percent);
    }

    public override FrameworkElement Element => _grid;

    public override void Update(Torrent torrent, TorrentCellText text)
    {
        var progress = torrent.DisplayedProgress;
        _bar.Value = progress;
        SetBrush(_bar, Control.ForegroundProperty, progress >= 1 ? StatusVisuals.Brush(TorrentStatusTone.Seeding) : null);
        _percent.Text = text.Text(torrent, TorrentColumn.Done);
        AutomationProperties.SetName(_bar, _percent.Text);
    }
}

/// <summary>A lock for private torrents; Narrator reads "Private".</summary>
internal sealed class PrivateCell : TorrentCell
{
    private readonly FontIcon _lock = new()
    {
        Glyph = "",
        FontSize = 16,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };

    public PrivateCell() => AutomationProperties.SetName(_lock, Strings.Private);

    public override FrameworkElement Element => _lock;

    public override void Update(Torrent torrent, TorrentCellText text) =>
        _lock.Visibility = torrent.IsPrivate ? Visibility.Visible : Visibility.Collapsed;
}
