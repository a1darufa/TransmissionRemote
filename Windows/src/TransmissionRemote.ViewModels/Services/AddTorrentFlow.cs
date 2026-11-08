using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>Something the user wants to add, waiting for the Add Torrent dialog.</summary>
public sealed record PendingTorrent
{
    private PendingTorrent(string name, string? infoHash, IReadOnlyList<FileEntry> files)
    {
        Name = name;
        InfoHash = infoHash;
        Files = files;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; }
    public string? InfoHash { get; }

    /// <summary>Known only for .torrent files; magnet links have no file list until the daemon gets the metadata.</summary>
    public IReadOnlyList<FileEntry> Files { get; }

    /// <summary>The .torrent file's contents, or null for a link.</summary>
    public byte[]? TorrentFile { get; private init; }

    /// <summary>A magnet link or an http(s) URL the daemon fetches itself, or null for a file.</summary>
    public string? Link { get; private init; }

    /// <summary>The real files' size; null when the file list isn't known.</summary>
    public long? TotalSize => Files.Count == 0 ? null : Files.Where(file => !file.IsPadding).Sum(file => file.Length);

    public static PendingTorrent FromFile(byte[] contents, TorrentMetainfo metainfo)
    {
        ArgumentNullException.ThrowIfNull(metainfo);
        return new PendingTorrent(metainfo.Name, metainfo.InfoHash, metainfo.Files) { TorrentFile = contents };
    }

    public static PendingTorrent FromLink(string link, string name, string? infoHash) =>
        new(name, infoHash, []) { Link = link };
}

/// <summary>What the user chose in the Add Torrent dialog.</summary>
public sealed record AddTorrentOptions
{
    public string? DownloadDir { get; init; }

    /// <summary>Null leaves it to the server's "start added torrents" setting.</summary>
    public bool? StartWhenAdded { get; init; }

    public Priority Priority { get; init; } = Priority.Normal;
    public IReadOnlySet<int> UnwantedFiles { get; init; } = new HashSet<int>();
    public IReadOnlyList<string> Labels { get; init; } = [];
    public bool SequentialDownload { get; init; }

    /// <summary>The request for <paramref name="pending"/>, leaving out what the server is too old to accept.</summary>
    public AddTorrentRequest RequestFor(PendingTorrent pending, ServerInfo? serverInfo)
    {
        ArgumentNullException.ThrowIfNull(pending);
        var request = pending.TorrentFile is { } contents
            ? AddTorrentRequest.TorrentFile(contents)
            : AddTorrentRequest.MagnetOrUrl(pending.Link!);
        return request with
        {
            DownloadDir = DownloadDir,
            Paused = StartWhenAdded is { } start ? !start : null,
            BandwidthPriority = Priority == Priority.Normal ? null : Priority,
            FilesUnwanted = UnwantedFiles.Count == 0 ? null : UnwantedFiles.Order().ToList(),
            Labels = serverInfo?.SupportsLabelsOnAdd == true && Labels.Count > 0 ? Labels : null,
            SequentialDownload = serverInfo?.SupportsSequentialDownload == true && SequentialDownload ? true : null,
        };
    }
}

public enum AddProblemKind
{
    /// <summary>The file is larger than any .torrent file should be.</summary>
    FileTooLarge,

    /// <summary>The file couldn't be read as a .torrent file.</summary>
    NotATorrentFile,

    /// <summary>The text isn't a magnet link or a torrent URL.</summary>
    NotALink,

    /// <summary>The server refused the torrent; see <see cref="AddProblem.Error"/>.</summary>
    AddFailed,
}

/// <param name="Subject">The file name, link or torrent name the problem is about.</param>
public sealed record AddProblem(AddProblemKind Kind, string Subject, Exception? Error = null);

/// <summary>
/// Collects torrents from every entry point (Open dialog, drag and drop, File Explorer, browser magnet links,
/// paste) and queues them for the Add Torrent dialog, or adds them straight away when the dialog is turned off.
/// </summary>
public sealed partial class AddTorrentFlow : ObservableObject
{
    private readonly ServerConnection _connection;
    private readonly SettingsStore _settings;
    private readonly ILogger _logger;
    private readonly Queue<PendingTorrent> _quietQueue = new();
    private bool _isAddingQuietly;
    private Task _quietAdding = Task.CompletedTask;

    public AddTorrentFlow(ServerConnection connection, SettingsStore settings, ILogger<AddTorrentFlow>? logger = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? NullLogger<AddTorrentFlow>.Instance;
        Pending.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Current));
    }

    /// <summary>Torrents waiting for the dialog; it shows <see cref="Current"/> until <see cref="FinishCurrent"/>.</summary>
    public ObservableCollection<PendingTorrent> Pending { get; } = [];

    public PendingTorrent? Current => Pending.FirstOrDefault();

    /// <summary>Something that couldn't be opened or added, for an error dialog. The app clears it once shown.</summary>
    [ObservableProperty]
    public partial AddProblem? Problem { get; set; }

    /// <summary>The task adding torrents without the dialog, so tests can wait for it.</summary>
    internal Task QuietAdding => _quietAdding;

    public void FinishCurrent()
    {
        if (Pending.Count > 0)
        {
            Pending.RemoveAt(0);
        }
    }

    /// <summary>Opens files and links, for example from the command line or a drop.</summary>
    public void Open(IEnumerable<string> pathsOrLinks)
    {
        ArgumentNullException.ThrowIfNull(pathsOrLinks);
        foreach (var item in pathsOrLinks)
        {
            if (File.Exists(item))
            {
                OpenFile(item);
            }
            else
            {
                OpenLink(item);
            }
        }
    }

    /// <summary>Handles pasted text: magnet links and http(s) torrent URLs, one per line. False when there were none.</summary>
    public bool OpenLinks(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var links = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => MagnetLink.TryParse(line, out _) || line.IsTorrentDownloadUrl)
            .ToList();
        links.ForEach(OpenLink);
        return links.Count > 0;
    }

    public void OpenLink(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var link = text.Trim();
        if (MagnetLink.TryParse(link, out var magnet))
        {
            Enqueue(PendingTorrent.FromLink(magnet.Url.AbsoluteUri, magnet.DisplayName ?? "", magnet.InfoHash));
        }
        else if (link.IsTorrentDownloadUrl && Uri.TryCreate(link, UriKind.Absolute, out var url))
        {
            Enqueue(PendingTorrent.FromLink(link, Path.GetFileName(url.LocalPath), infoHash: null));
        }
        else
        {
            Problem = new AddProblem(AddProblemKind.NotALink, link);
        }
    }

    public void OpenFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var fileName = Path.GetFileName(path);
        try
        {
            if (new FileInfo(path).Length > TorrentMetainfo.MaximumFileSize)
            {
                Problem = new AddProblem(AddProblemKind.FileTooLarge, fileName);
                return;
            }
            var contents = File.ReadAllBytes(path);
            Enqueue(PendingTorrent.FromFile(contents, TorrentMetainfo.Parse(contents)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException)
        {
            Log.TorrentFileUnreadable(_logger, error, fileName);
            Problem = new AddProblem(AddProblemKind.NotATorrentFile, fileName, error);
        }
    }

    private void Enqueue(PendingTorrent pending)
    {
        if (_settings.Current.ShowAddTorrentDialog)
        {
            Pending.Add(pending);
        }
        else
        {
            AddWithDefaults(pending);
        }
    }

    /// <summary>
    /// Skips the dialog: adds to the server's default folder, starting per the server's setting. Torrents go one
    /// at a time: sending hundreds at once made the later ones time out (transgui #804).
    /// </summary>
    private void AddWithDefaults(PendingTorrent pending)
    {
        _quietQueue.Enqueue(pending);
        if (_isAddingQuietly)
        {
            return;
        }
        // Set before starting: an add can finish synchronously, and the loop must be able to say it's done.
        _isAddingQuietly = true;
        _quietAdding = AddQuietlyAsync();
    }

    private async Task AddQuietlyAsync()
    {
        try
        {
            while (_quietQueue.TryDequeue(out var next))
            {
                try
                {
                    await _connection.AddAsync(new AddTorrentOptions().RequestFor(next, _connection.ServerInfo));
                }
                catch (TransmissionException error)
                {
                    Log.AddFailed(_logger, error, next.Name);
                    // Keep the first failure on screen rather than replacing it with each later one.
                    Problem ??= new AddProblem(AddProblemKind.AddFailed, next.Name, error);
                }
            }
        }
        finally
        {
            _isAddingQuietly = false;
        }
    }
}
