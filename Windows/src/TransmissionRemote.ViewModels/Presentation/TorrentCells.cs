using System.Globalization;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>
/// The words the torrent list needs. The app loads them from its resources; tests use English stand-ins.
/// </summary>
public interface ITorrentListWords
{
    /// <summary>
    /// The Status column's word for a label. <see cref="TorrentStatusLabel.Error"/> ("Error: {0}") takes the
    /// torrent's error message and <see cref="TorrentStatusLabel.Verifying"/> ("Verifying {0}") the percentage.
    /// </summary>
    string Status(TorrentStatusLabel label);

    string Priority(Priority priority);

    /// <summary>"Working", "Updating…" or "Tracker error"; empty while no tracker was contacted.</summary>
    string TrackerStatus(TrackerStatus status);

    /// <summary>What Narrator reads for a row: "{0}, {1}, {2}" for the name, status and percentage.</summary>
    string RowName { get; }
}

/// <summary>
/// What each cell of the torrent list says, the Mac app's <c>TorrentTableView</c> column by column. Free of
/// resources itself, so every column is tested.
/// </summary>
public sealed class TorrentCellText(DisplayFormatter format, ITorrentListWords words)
{
    private readonly DisplayFormatter _format = format ?? throw new ArgumentNullException(nameof(format));
    private readonly ITorrentListWords _words = words ?? throw new ArgumentNullException(nameof(words));

    public string Text(Torrent torrent, TorrentColumn column)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return column switch
        {
            TorrentColumn.Name => torrent.Name,
            TorrentColumn.Size => _format.Bytes(torrent.SizeWhenDone),
            TorrentColumn.Done => _format.Percent(torrent.DisplayedProgress),
            TorrentColumn.Status => StatusText(torrent),
            TorrentColumn.Seeds => _format.SeedsText(torrent),
            TorrentColumn.Peers => _format.PeersText(torrent),
            TorrentColumn.Down => _format.SpeedOrBlank(torrent.RateDownload),
            TorrentColumn.Up => _format.SpeedOrBlank(torrent.RateUpload),
            TorrentColumn.Eta => _format.Eta(torrent.Eta),
            TorrentColumn.Ratio => _format.Ratio(torrent.UploadRatio),
            TorrentColumn.Added => _format.Date(torrent.AddedDate),
            TorrentColumn.Completed => _format.Date(torrent.DoneDate),
            TorrentColumn.Folder => torrent.DownloadDir,
            TorrentColumn.Labels => torrent.LabelsText,
            TorrentColumn.Tracker => torrent.PrimaryTracker,
            TorrentColumn.Queue => _format.Count(torrent.QueuePosition + 1),
            TorrentColumn.Downloaded => _format.Bytes(torrent.DownloadedEver),
            TorrentColumn.Uploaded => _format.Bytes(torrent.UploadedEver),
            TorrentColumn.Priority => _words.Priority(torrent.BandwidthPriority),
            TorrentColumn.TotalSize => _format.Bytes(torrent.TotalSize),
            TorrentColumn.SizeLeft => _format.Bytes(torrent.LeftUntilDone),
            TorrentColumn.LastActive => _format.Date(torrent.ActivityDate),
            TorrentColumn.SeedingTime => torrent.SecondsSeeding > 0 ? _format.Duration(torrent.SecondsSeeding) : "",
            TorrentColumn.TrackerStatus => TrackerStatusText(torrent),
            // A lock glyph, not text; the app gives it an accessible name.
            TorrentColumn.Private => "",
            TorrentColumn.BandwidthGroup => torrent.BandwidthGroup,
            // An identifier, not a quantity: no digit grouping.
            TorrentColumn.Id => torrent.Id.ToString(CultureInfo.InvariantCulture),
            _ => "",
        };
    }

    /// <summary>
    /// The cell's tooltip, for text that's often cut off: the error for a torrent with one (on the name), the
    /// full folder path and the tracker's message. Null for no tooltip.
    /// </summary>
    public string? Tooltip(Torrent torrent, TorrentColumn column)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        var text = column switch
        {
            TorrentColumn.Name => torrent.HasError && torrent.ErrorString.Length > 0 ? torrent.ErrorString : torrent.Name,
            TorrentColumn.Folder => torrent.DownloadDir,
            TorrentColumn.TrackerStatus => TrackerStatusText(torrent),
            _ => null,
        };
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>"Downloading", "Error: No data found", "Verifying 45.3%"…</summary>
    public string StatusText(Torrent torrent)
    {
        var status = TorrentDisplayStatus.Of(torrent);
        var word = _words.Status(status.Label);
        return status.Label switch
        {
            TorrentStatusLabel.Error => string.Format(_format.Culture, word, status.ErrorMessage),
            TorrentStatusLabel.Verifying => string.Format(_format.Culture, word, _format.Percent(status.VerifyProgress ?? 0)),
            _ => word,
        };
    }

    /// <summary>The tracker's own message when its last announce failed, as on the Mac.</summary>
    public string TrackerStatusText(Torrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return torrent.TrackerSummary switch
        {
            { Status: TrackerStatus.Failed, FailureMessage: { Length: > 0 } message } => message,
            { } summary => _words.TrackerStatus(summary.Status),
            null => "",
        };
    }

    /// <summary>What Narrator reads for the row: "ubuntu.iso, Downloading, 45.3%".</summary>
    public string RowName(Torrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return string.Format(_format.Culture, _words.RowName, torrent.Name, StatusText(torrent), _format.Percent(torrent.DisplayedProgress));
    }
}
