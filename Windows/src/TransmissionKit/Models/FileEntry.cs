namespace TransmissionKit;

/// <summary>A file's download priority, with Transmission's numbering.</summary>
public enum Priority
{
    Low = -1,
    Normal = 0,
    High = 1,
}

/// <summary>One file in a torrent, as the daemon numbers it.</summary>
/// <param name="Id">The file index Transmission uses in files-wanted, priority-high and friends.</param>
/// <param name="Path">The path inside the torrent, "/"-separated, usually starting with the torrent's folder.</param>
/// <param name="Length">The size in bytes.</param>
public sealed record FileEntry(int Id, string Path, long Length)
{
    public long BytesCompleted { get; init; }
    public bool Wanted { get; init; } = true;
    public Priority Priority { get; init; } = Priority.Normal;

    /// <summary>
    /// A BEP 47 padding file, which only aligns pieces. Transmission (3.00 through 4.1) lists them like real
    /// files, so they keep their place in the numbering, but the app hides them and never downloads them.
    /// </summary>
    public bool IsPadding { get; init; } = LooksLikePadding(Path);

    /// <summary>
    /// The daemon doesn't report the attr flag, so padding files are recognized by the names clients give them:
    /// libtorrent's ".pad/&lt;size&gt;" and BitComet's "_____padding_file_…".
    /// </summary>
    public static bool LooksLikePadding(string path)
    {
        var components = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 0)
        {
            return false;
        }
        var folders = components.AsSpan(0, components.Length - 1);
        return folders.Contains(".pad") || components[^1].StartsWith("_____padding_file", StringComparison.Ordinal);
    }
}
