using System.Security.Cryptography;

namespace TransmissionKit;

/// <summary>Why data isn't a usable .torrent file.</summary>
public enum TorrentParseError
{
    TooLarge,
    Malformed,
    MissingInfoDictionary,
}

/// <summary>
/// Thrown when data isn't a usable .torrent file. For malformed bencode the inner exception says where.
/// </summary>
public sealed class TorrentParseException : FormatException
{
    public TorrentParseException(TorrentParseError error, Exception? innerException = null)
        : base(Describe(error), innerException)
    {
        Error = error;
    }

    public TorrentParseError Error { get; }

    private static string Describe(TorrentParseError error) => error switch
    {
        TorrentParseError.TooLarge => "The file is too large to be a torrent.",
        TorrentParseError.Malformed => "The file isn't a valid torrent.",
        TorrentParseError.MissingInfoDictionary => "The torrent has no info dictionary.",
        _ => error.ToString(),
    };
}

/// <summary>The parts of a .torrent file shown before adding it: name, files and info hash.</summary>
public sealed class TorrentMetainfo
{
    /// <summary>Larger files are almost certainly not torrents, so they're refused rather than parsed.</summary>
    public const int MaximumFileSize = 32 * 1024 * 1024;

    private TorrentMetainfo(string name, IReadOnlyList<FileEntry> files, string? infoHash, string? comment,
        bool isPrivate)
    {
        Name = name;
        Files = files;
        InfoHash = infoHash;
        Comment = comment;
        IsPrivate = isPrivate;
    }

    public string Name { get; }

    /// <summary>The files in the order, and with the numbers, Transmission will use.</summary>
    public IReadOnlyList<FileEntry> Files { get; }

    /// <summary>Lowercase hex SHA-1 of the info dictionary (the v1 info hash).</summary>
    public string? InfoHash { get; }

    public string? Comment { get; }

    public bool IsPrivate { get; }

    /// <summary>The size of the real files; padding only aligns pieces and isn't kept.</summary>
    public long TotalSize => Files.Where(file => !file.IsPadding).Sum(file => file.Length);

    /// <exception cref="TorrentParseException">
    /// The data is too large, isn't bencode, or has no info dictionary.
    /// </exception>
    public static TorrentMetainfo Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaximumFileSize)
        {
            throw new TorrentParseException(TorrentParseError.TooLarge);
        }

        BencodeDocument document;
        try
        {
            document = BencodeParser.Parse(data);
        }
        catch (BencodeException error)
        {
            throw new TorrentParseException(TorrentParseError.Malformed, error);
        }
        var info = document.Root["info"]?.AsDictionary
            ?? throw new TorrentParseException(TorrentParseError.MissingInfoDictionary);

        var name = Text(info, "name") ?? "Untitled";
        return new TorrentMetainfo(
            name,
            FileEntries(info, name),
            document.InfoRange is { } infoRange ? Sha1Hex(data[infoRange]) : null,
            document.Root.AsDictionary is { } root ? Text(root, "comment") : null,
            info.GetValueOrDefault("private")?.AsInteger == 1);
    }

    /// <summary>Prefers the ".utf-8" variant some clients write next to legacy-encoded keys.</summary>
    private static string? Text(IReadOnlyDictionary<string, BencodeValue> dictionary, string key) =>
        (dictionary.GetValueOrDefault($"{key}.utf-8") ?? dictionary.GetValueOrDefault(key))?.AsString;

    /// <summary>
    /// The files in the daemon's order, so their numbers match files-wanted and friends.
    /// Like libtransmission, a "files" list wins over a single-file "length".
    /// </summary>
    private static List<FileEntry> FileEntries(IReadOnlyDictionary<string, BencodeValue> info, string torrentName)
    {
        if (info.GetValueOrDefault("files")?.AsList is not { } fileList)
        {
            return info.GetValueOrDefault("length")?.AsInteger is { } singleLength
                ? [new FileEntry(0, torrentName, singleLength)]
                : [];
        }

        var entries = new List<FileEntry>();
        foreach (var file in fileList)
        {
            if (file.AsDictionary is not { } fields || fields.GetValueOrDefault("length")?.AsInteger is not { } length)
            {
                continue;
            }
            var components = (fields.GetValueOrDefault("path.utf-8") ?? fields.GetValueOrDefault("path"))?.AsList?
                .Select(component => component.AsString)
                .OfType<string>() ?? [];
            var path = string.Join('/', components.Prepend(torrentName));
            // Padding files stay in the list: Transmission counts them, so skipping them would
            // shift every later file's number and unselect the wrong files.
            var isPadding = fields.GetValueOrDefault("attr")?.AsString?.Contains('p') == true
                || FileEntry.LooksLikePadding(path);
            entries.Add(new FileEntry(entries.Count, path, length) { IsPadding = isPadding });
        }
        return entries;
    }

    private static string Sha1Hex(ReadOnlySpan<byte> infoDictionary)
    {
        // BEP 3 defines the v1 info hash as SHA-1. It names the torrent; it doesn't protect anything.
#pragma warning disable CA5350 // Weak cryptographic algorithm: the protocol mandates SHA-1 here.
        var hash = SHA1.HashData(infoDictionary);
#pragma warning restore CA5350
        return Convert.ToHexStringLower(hash);
    }
}
