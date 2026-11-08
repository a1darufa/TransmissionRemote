using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>The Add Torrent dialog: name, destination folder, start, priority, labels and which files to get.</summary>
public sealed partial class AddTorrentViewModel : ObservableObject
{
    private readonly ServerConnection _connection;
    private readonly ServerStore _servers;
    private readonly SettingsStore _settings;
    private readonly HashSet<int> _unwanted = [];
    private bool _hasServerDefaults;

    public AddTorrentViewModel(
        PendingTorrent pending,
        ServerConnection connection,
        ServerStore servers,
        SettingsStore settings)
    {
        Pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _servers = servers ?? throw new ArgumentNullException(nameof(servers));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Name = pending.Name;
        FileTree = ViewModels.FileTree.Build(pending.Files);
        ApplyServerDefaults();
    }

    public PendingTorrent Pending { get; }

    /// <summary>The torrent's top-level name; changing it renames the item right after adding.</summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Folder { get; set; } = "";

    [ObservableProperty]
    public partial bool StartWhenAdded { get; set; } = true;

    [ObservableProperty]
    public partial Priority Priority { get; set; } = Priority.Normal;

    /// <summary>Comma-separated, like the Mac app.</summary>
    [ObservableProperty]
    public partial string LabelsText { get; set; } = "";

    [ObservableProperty]
    public partial bool SequentialDownload { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<FileNode> FileTree { get; private set; }

    /// <summary>Free space in <see cref="Folder"/>, after <see cref="RefreshFreeSpaceAsync"/>.</summary>
    [ObservableProperty]
    public partial FreeSpace? FreeSpace { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool IsAdding { get; private set; }

    /// <summary>Why adding failed, shown in the dialog.</summary>
    [ObservableProperty]
    public partial TransmissionException? Error { get; private set; }

    /// <summary>Set once the torrent was added; the dialog closes.</summary>
    [ObservableProperty]
    public partial AddTorrentResult? Result { get; private set; }

    public bool HasFileList => Pending.Files.Count > 0;
    public bool SupportsLabels => _connection.ServerInfo?.SupportsLabels == true;
    public bool SupportsSequentialDownload => _connection.ServerInfo?.SupportsSequentialDownload == true;

    /// <summary>The size of the files chosen, for "x of y selected".</summary>
    public long SelectedSize => Pending.Files.Where(file => !file.IsPadding && !_unwanted.Contains(file.Id)).Sum(file => file.Length);

    /// <summary>Folders to suggest: this server's history (most used first), then its default folder.</summary>
    public IReadOnlyList<string> SuggestedFolders
    {
        get
        {
            var history = CurrentProfile?.SuggestedFolders ?? [];
            IEnumerable<string> defaultFolder = _connection.Session?.DownloadDir is { Length: > 0 } folder ? [folder] : [];
            return history.Concat(defaultFolder).Distinct(StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>Every folder the app knows on the server, as a tree for the folder browser.</summary>
    public IReadOnlyList<RemoteFolder> KnownFolders
    {
        get
        {
            var counts = _connection.Torrents.GroupBy(torrent => torrent.DownloadDir)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            foreach (var folder in SuggestedFolders)
            {
                counts.TryAdd(folder, 0);
            }
            return RemoteFolderTree.Build(counts);
        }
    }

    private ServerProfile? CurrentProfile => _connection.Profile is { } profile ? _servers.Find(profile.Id) : null;

    /// <summary>Checks or unchecks a file or a whole folder.</summary>
    public void ToggleWanted(FileNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.WantedState == WantedState.All)
        {
            _unwanted.UnionWith(node.FileIds);
        }
        else
        {
            _unwanted.ExceptWith(node.FileIds);
        }
        var files = Pending.Files.Select(file => file with { Wanted = !_unwanted.Contains(file.Id) });
        FileTree = ViewModels.FileTree.Build(files);
        OnPropertyChanged(nameof(SelectedSize));
    }

    /// <summary>
    /// Opening a .torrent before the server's settings arrived shows the dialog without them (transgui #790);
    /// call this when the connection's session changes to fill in what was missing.
    /// </summary>
    public void ApplyServerDefaults()
    {
        if (_hasServerDefaults || _connection.Session is not { } session)
        {
            if (Folder.Length == 0)
            {
                Folder = CurrentProfile?.LastUsedFolder ?? "";
            }
            return;
        }
        if (Folder.Length == 0)
        {
            Folder = CurrentProfile?.LastUsedFolder ?? session.DownloadDir ?? "";
        }
        StartWhenAdded = session.StartAddedTorrents ?? StartWhenAdded;
        _hasServerDefaults = true;
    }

    public async Task RefreshFreeSpaceAsync()
    {
        var folder = ServerFolder.Cleaned(Folder);
        FreeSpace = folder.Length > 0 ? await _connection.GetFreeSpaceAsync(folder) : null;
    }

    public void ClearFolderHistory()
    {
        if (CurrentProfile is { } profile)
        {
            _servers.ClearFolderHistory(profile.Id);
            OnPropertyChanged(nameof(SuggestedFolders));
        }
    }

    private bool CanAdd() => !IsAdding;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        var labels = LabelsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var folder = ServerFolder.Cleaned(Folder);
        var options = new AddTorrentOptions
        {
            DownloadDir = folder.Length > 0 ? folder : null,
            StartWhenAdded = StartWhenAdded,
            Priority = Priority,
            // Padding files are never needed on disk, so they're always left out.
            UnwantedFiles = _unwanted.Union(Pending.Files.Where(file => file.IsPadding).Select(file => file.Id)).ToHashSet(),
            Labels = labels,
            SequentialDownload = SequentialDownload,
        };
        var serverInfo = _connection.ServerInfo;
        IsAdding = true;
        Error = null;
        try
        {
            var result = await _connection.AddAsync(options.RequestFor(Pending, serverInfo));
            if (CurrentProfile is { } profile && folder.Length > 0)
            {
                _servers.RecordFolderUse(folder, profile.Id, _settings.Current.EffectiveFolderHistoryLimit);
            }
            await ApplyLabelsIfNeededAsync(result, labels, serverInfo);
            await RenameIfNeededAsync(result);
            Result = result;
        }
        catch (TransmissionException error)
        {
            Error = error;
        }
        finally
        {
            IsAdding = false;
        }
    }

    /// <summary>Transmission 3.00 can label torrents but not while adding them, so label right after.</summary>
    private async Task ApplyLabelsIfNeededAsync(AddTorrentResult result, string[] labels, ServerInfo? serverInfo)
    {
        if (serverInfo is { SupportsLabels: true, SupportsLabelsOnAdd: false } && labels.Length > 0
            && result.TorrentAdded is { } torrent)
        {
            await _connection.SetLabelsAsync(labels, [torrent.Id]);
        }
    }

    /// <summary>
    /// Transmission can't name a torrent while adding it, so the top-level item is renamed right after.
    /// A duplicate keeps its name: it's the torrent already on the server.
    /// </summary>
    private async Task RenameIfNeededAsync(AddTorrentResult result)
    {
        if (TorrentName.Validated(Name) is { } newName && newName != Pending.Name
            && result.TorrentAdded is { } torrent && torrent.Name != newName)
        {
            await _connection.RenameFileAsync(torrent.Id, torrent.Name, newName);
        }
    }
}
