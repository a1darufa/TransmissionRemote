using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>A row of the info pane's General tab. The app writes its label and value.</summary>
public enum GeneralField
{
    Status,
    Progress,
    Downloaded,
    Uploaded,
    Ratio,
    Speed,
    Peers,
    Remaining,
    Corrupt,
    DownloadingFor,
    SeedingFor,
    Name,
    Location,
    TotalSize,
    Hash,
    Added,
    Completed,
    Private,
    Pieces,
    CreatedBy,
    CreatedOn,
    Comment,
}

/// <summary>
/// Which rows the General tab shows, in the Mac app's order (<c>GeneralTab</c>). Rows that depend on the torrent's
/// details appear once they've loaded; the others show at once.
/// </summary>
public static class GeneralFields
{
    /// <summary>The left grid: how the transfer is going.</summary>
    public static IReadOnlyList<GeneralField> Transfer(Torrent torrent, TorrentDetails? details)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        List<GeneralField> fields =
        [
            GeneralField.Status, GeneralField.Progress, GeneralField.Downloaded, GeneralField.Uploaded,
            GeneralField.Ratio, GeneralField.Speed, GeneralField.Peers,
        ];
        // Transmission reports -1 when there's no estimate and -2 when it can't tell.
        if (torrent.Eta >= 0)
        {
            fields.Add(GeneralField.Remaining);
        }
        if (details is not null)
        {
            if (details.CorruptEver > 0)
            {
                fields.Add(GeneralField.Corrupt);
            }
            fields.Add(GeneralField.DownloadingFor);
            fields.Add(GeneralField.SeedingFor);
        }
        return fields;
    }

    /// <summary>The right grid: what the torrent is.</summary>
    public static IReadOnlyList<GeneralField> About(Torrent torrent, TorrentDetails? details)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        List<GeneralField> fields =
        [
            GeneralField.Name, GeneralField.Location, GeneralField.TotalSize, GeneralField.Hash, GeneralField.Added,
        ];
        if (torrent.DoneDate is not null)
        {
            fields.Add(GeneralField.Completed);
        }
        if (torrent.IsPrivate)
        {
            fields.Add(GeneralField.Private);
        }
        if (details is null)
        {
            return fields;
        }
        if (details.PieceCount > 0)
        {
            fields.Add(GeneralField.Pieces);
        }
        if (details.Creator.Length > 0)
        {
            fields.Add(GeneralField.CreatedBy);
        }
        if (details.DateCreated is not null)
        {
            fields.Add(GeneralField.CreatedOn);
        }
        if (details.Comment.Length > 0)
        {
            fields.Add(GeneralField.Comment);
        }
        return fields;
    }
}
