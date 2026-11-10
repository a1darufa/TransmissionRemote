using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>What the info pane shows under its tabs.</summary>
public enum InfoPaneContent
{
    /// <summary>The Speed tab, which needs no selection: it charts the whole session.</summary>
    Speed,

    NoTorrentSelected,

    /// <summary>Several torrents are selected; <see cref="InfoPaneViewModel.SelectedCount"/> says how many.</summary>
    TorrentsSelected,

    /// <summary>The selected torrent's details for this tab are on their way.</summary>
    Loading,

    /// <summary>The selected tab, for the one selected torrent.</summary>
    Tab,
}

/// <summary>The Files tab's state for a torrent whose file list has loaded.</summary>
public enum FilesContent
{
    Tree,

    /// <summary>A magnet link whose metadata hasn't arrived: the files aren't known yet.</summary>
    WaitingForMetadata,

    NoFiles,
}

/// <summary>A column of the Peers tab, for sorting.</summary>
public enum PeerColumn
{
    Address,
    Port,
    Country,
    Client,
    Progress,
    Down,
    Up,
    Flags,
}

/// <summary>The Peers tab's banner about the country database.</summary>
public enum CountryBanner
{
    None,
    Downloading,

    /// <summary>Not downloaded yet, or the download failed: offer it, with the failure if there was one.</summary>
    OfferDownload,
}

/// <summary>
/// The info pane under the torrent list: General, Trackers, Peers, Files and Speed for the selected torrent. Ported
/// from the Mac app's <c>DetailPane</c> and its tabs; the app draws the tabs and writes the words.
/// </summary>
/// <remarks>
/// Only the visible tab's data is fetched (<see cref="ServerConnection.DetailTab"/>), so only its rows are kept up to
/// date. Rows are updated in place, which keeps each table's selection and scroll position across refreshes. Use it
/// from the UI thread, like <see cref="ServerConnection"/>.
/// </remarks>
public sealed partial class InfoPaneViewModel : ObservableObject, IDisposable
{
    private readonly ServerConnection _connection;
    private readonly SettingsStore _settings;
    private readonly PeerInfoService _peerInfo;
    private readonly LocalFiles _localFiles;
    private readonly TimeProvider _time;
    private readonly HashSet<string> _expandedFolders = new(StringComparer.Ordinal);
    private IReadOnlyList<FileNode> _fileTree = [];

    /// <summary>The torrent the rows belong to, so another torrent's rows are never mixed in.</summary>
    private int? _rowsTorrentId;

    public InfoPaneViewModel(
        ServerConnection connection,
        SettingsStore settings,
        PeerInfoService peerInfo,
        LocalFiles localFiles,
        TimeProvider? time = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _peerInfo = peerInfo ?? throw new ArgumentNullException(nameof(peerInfo));
        _localFiles = localFiles ?? throw new ArgumentNullException(nameof(localFiles));
        _time = time ?? TimeProvider.System;
        _connection.PropertyChanged += OnConnectionChanged;
        _settings.Changed += OnSettingsChanged;
        _peerInfo.PropertyChanged += OnPeerInfoChanged;
        _peerInfo.HostNameResolved += OnHostNameResolved;
    }

    public DetailTab SelectedTab
    {
        get => _connection.DetailTab;
        set => _connection.DetailTab = value;
    }

    public InfoPaneContent Content
    {
        get
        {
            if (SelectedTab == DetailTab.Speed)
            {
                return InfoPaneContent.Speed;
            }
            if (Torrent is null)
            {
                return SelectedCount == 0 ? InfoPaneContent.NoTorrentSelected : InfoPaneContent.TorrentsSelected;
            }
            // General shows what the list already knows at once; the other tabs need their details.
            return SelectedTab != DetailTab.General && Details is null ? InfoPaneContent.Loading : InfoPaneContent.Tab;
        }
    }

    public int SelectedCount => _connection.Selection.Count;

    /// <summary>The one selected torrent, or null with none or several.</summary>
    public Torrent? Torrent => _connection.SingleSelectedTorrent;

    /// <summary>
    /// The selected torrent's details, never those of a torrent selected before while the new ones load (Mac:
    /// <c>details?.id == torrent.id</c>).
    /// </summary>
    public TorrentDetails? Details =>
        _connection.Details is { } details && details.Id == Torrent?.Id ? details : null;

    public IReadOnlyList<GeneralField> TransferFields =>
        Torrent is { } torrent ? GeneralFields.Transfer(torrent, Details) : [];

    public IReadOnlyList<GeneralField> AboutFields =>
        Torrent is { } torrent ? GeneralFields.About(torrent, Details) : [];

    public ObservableCollection<TrackerRow> Trackers { get; } = [];

    public ObservableCollection<PeerRow> Peers { get; } = [];

    public bool HasPeers => Details?.Peers.Count > 0;

    [ObservableProperty]
    public partial PeerColumn PeerSortColumn { get; private set; } = PeerColumn.Down;

    [ObservableProperty]
    public partial bool PeerSortAscending { get; private set; }

    /// <summary>The Peers tab's "Resolve host names" switch, remembered.</summary>
    public bool ResolvePeerHostNames
    {
        get => _settings.Current.ResolvePeerHostNames;
        set => _settings.Update(settings => settings with { ResolvePeerHostNames = value });
    }

    /// <summary>The Peers tab's "Show countries" switch, remembered.</summary>
    public bool ShowPeerCountries
    {
        get => _settings.Current.ShowPeerCountries;
        set => _settings.Update(settings => settings with { ShowPeerCountries = value });
    }

    public CountryBanner CountryBanner => !ShowPeerCountries
        ? CountryBanner.None
        : _peerInfo.DatabaseState switch
        {
            CountryDatabaseState.Downloading => CountryBanner.Downloading,
            CountryDatabaseState.NotDownloaded or CountryDatabaseState.Failed => CountryBanner.OfferDownload,
            _ => CountryBanner.None,
        };

    /// <summary>Why the last download of the country database failed, for the banner.</summary>
    public CountryDatabaseFailure? CountryDatabaseFailure =>
        _peerInfo.DatabaseState == CountryDatabaseState.Failed ? _peerInfo.DatabaseFailure : null;

    public ObservableCollection<FileRow> Files { get; } = [];

    public FilesContent FilesContent => Details?.Files.Count > 0
        ? FilesContent.Tree
        : Torrent?.MetadataPercentComplete < 1 ? FilesContent.WaitingForMetadata : FilesContent.NoFiles;

    /// <summary>Renaming files arrived in Transmission 2.80.</summary>
    public bool CanRenameFiles => _connection.ServerInfo?.SupportsRenaming == true;

    /// <summary>The Speed tab's chart, or null until there are two samples.</summary>
    [ObservableProperty]
    public partial SpeedChart? SpeedChart { get; private set; }

    /// <summary>Sorts the peers by a column; the same column again reverses the order. Country doesn't sort.</summary>
    public void SortPeersBy(PeerColumn column)
    {
        if (column == PeerColumn.Country)
        {
            return;
        }
        if (column == PeerSortColumn)
        {
            PeerSortAscending = !PeerSortAscending;
        }
        else
        {
            // A new sort column starts ascending, as in the torrent list.
            PeerSortAscending = true;
            PeerSortColumn = column;
        }
        SyncPeers();
    }

    public void SetExpanded(FileRow row, bool expanded)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.Node.IsFolder || (expanded ? !_expandedFolders.Add(row.Key) : !_expandedFolders.Remove(row.Key)))
        {
            return;
        }
        SyncFileRows();
    }

    /// <summary>A file or folder's checkbox: everything wanted becomes unwanted, anything else becomes wanted.</summary>
    public Task ToggleWantedAsync(FileNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return SetWantedAsync([node], node.WantedState != WantedState.All);
    }

    /// <summary>Download Selected and Don't Download Selected, as one request.</summary>
    public Task SetWantedAsync(IEnumerable<FileNode> nodes, bool wanted) =>
        FileIds(nodes) is { Count: > 0 } ids && Torrent is { } torrent
            ? _connection.ApplyAsync(TorrentMutation.Wanted(wanted, ids, torrent.Id))
            : Task.CompletedTask;

    public Task SetPriorityAsync(IEnumerable<FileNode> nodes, Priority priority) =>
        FileIds(nodes) is { Count: > 0 } ids && Torrent is { } torrent
            ? _connection.ApplyAsync(TorrentMutation.WithPriority(priority, ids, torrent.Id))
            : Task.CompletedTask;

    /// <summary>Copies each node's path, on this PC when a path mapping covers it, else on the server.</summary>
    public void CopyPaths(IEnumerable<FileNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (Torrent is not { } torrent || _connection.Profile is not { } profile)
        {
            return;
        }
        var paths = nodes.Select(node =>
            _localFiles.CurrentRemotePath(profile.Id, torrent, _connection.Session, node.RelativePath)).ToList();
        if (paths.Count > 0)
        {
            _localFiles.CopyPaths(profile.Id, paths);
        }
    }

    [RelayCommand]
    private Task AskForMorePeersAsync() =>
        Torrent is { } torrent ? _connection.PerformAsync(TorrentAction.Reannounce, [torrent.Id]) : Task.CompletedTask;

    [RelayCommand]
    private Task DownloadCountryDatabaseAsync() => _peerInfo.DownloadDatabaseAsync();

    [RelayCommand]
    private void HideCountries() => ShowPeerCountries = false;

    public void Dispose()
    {
        _connection.PropertyChanged -= OnConnectionChanged;
        _settings.Changed -= OnSettingsChanged;
        _peerInfo.PropertyChanged -= OnPeerInfoChanged;
        _peerInfo.HostNameResolved -= OnHostNameResolved;
    }

    private static List<int> FileIds(IEnumerable<FileNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return nodes.SelectMany(node => node.FileIds).Distinct().Order().ToList();
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ServerConnection.DetailTab):
                OnPropertyChanged(nameof(SelectedTab));
                ShowSelection();
                if (SelectedTab == DetailTab.Peers)
                {
                    PrepareCountries();
                }
                break;
            case nameof(ServerConnection.Selection):
            case nameof(ServerConnection.SingleSelectedTorrent):
            case nameof(ServerConnection.Details):
                ShowSelection();
                break;
            case nameof(ServerConnection.LastUpdated):
                // Set after each refresh's speed sample, unlike Stats.
                SpeedChart = SpeedChart.For(_connection.SpeedHistory.Samples);
                break;
            case nameof(ServerConnection.ServerInfo):
                OnPropertyChanged(nameof(CanRenameFiles));
                break;
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ResolvePeerHostNames));
        OnPropertyChanged(nameof(ShowPeerCountries));
        OnPropertyChanged(nameof(CountryBanner));
        if (SelectedTab == DetailTab.Peers && ShowPeerCountries)
        {
            PrepareCountries();
        }
        SyncPeers();
    }

    private void OnPeerInfoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PeerInfoService.DatabaseState) or nameof(PeerInfoService.DatabaseFailure))
        {
            OnPropertyChanged(nameof(CountryBanner));
            OnPropertyChanged(nameof(CountryDatabaseFailure));
            SyncPeers();
        }
    }

    private void OnHostNameResolved(object? sender, HostNameResolvedEventArgs e)
    {
        if (!ResolvePeerHostNames)
        {
            return;
        }
        foreach (var row in Peers.Where(row => row.Peer.Address == e.Address))
        {
            row.HostName = e.HostName;
        }
    }

    /// <summary>Loads a database downloaded earlier; the service reports its own failures.</summary>
    private void PrepareCountries()
    {
        if (ShowPeerCountries)
        {
            _ = _peerInfo.PrepareCountriesAsync();
        }
    }

    private void ShowSelection()
    {
        if (_rowsTorrentId != Torrent?.Id)
        {
            _rowsTorrentId = Torrent?.Id;
            Trackers.Clear();
            Peers.Clear();
            Files.Clear();
            _expandedFolders.Clear();
            _fileTree = [];
        }
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(Torrent));
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(TransferFields));
        OnPropertyChanged(nameof(AboutFields));
        OnPropertyChanged(nameof(HasPeers));
        OnPropertyChanged(nameof(FilesContent));
        switch (SelectedTab)
        {
            case DetailTab.Trackers:
                SyncTrackers();
                break;
            case DetailTab.Peers:
                SyncPeers();
                break;
            case DetailTab.Files:
                SyncFiles();
                break;
        }
    }

    private void SyncTrackers()
    {
        if (Details is not { } details)
        {
            return;
        }
        var now = _time.GetUtcNow();
        RowListSync.Apply(
            Trackers,
            details.TrackerStats,
            stat => stat.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            row => row.Key,
            stat => new TrackerRow(stat, now),
            (row, stat) => row.Update(stat, now));
    }

    private void SyncPeers()
    {
        if (SelectedTab != DetailTab.Peers || Details is not { } details)
        {
            return;
        }
        var resolve = ResolvePeerHostNames;
        var countries = ShowPeerCountries;
        string? HostName(Peer peer) => resolve ? _peerInfo.HostNameFor(peer.Address) : null;
        string? Country(Peer peer) => countries ? _peerInfo.CountryCodeFor(peer.Address) : null;
        RowListSync.Apply(
            Peers,
            PeerOrder.Sorted(details.Peers, PeerSortColumn, PeerSortAscending),
            peer => peer.Id,
            row => row.Key,
            peer => new PeerRow(peer, HostName(peer), Country(peer)),
            (row, peer) =>
            {
                row.Peer = peer;
                row.HostName = HostName(peer);
                row.CountryCode = Country(peer);
            });
        if (resolve)
        {
            _peerInfo.ResolveHostNames(details.Peers.Select(peer => peer.Address));
        }
    }

    private void SyncFiles()
    {
        if (Details is not { } details)
        {
            return;
        }
        _fileTree = FileTree.Build(details.Files);
        SyncFileRows();
    }

    private void SyncFileRows() => RowListSync.Apply(
        Files,
        FileTreeRows.Visible(_fileTree, _expandedFolders),
        visible => visible.Node.Id,
        row => row.Key,
        visible => new FileRow(visible),
        (row, visible) => row.Update(visible));
}

/// <summary>The Peers tab's order. Ties keep a stable order by address and port, so rows don't swap on refresh.</summary>
public static class PeerOrder
{
    public static IReadOnlyList<Peer> Sorted(IEnumerable<Peer> peers, PeerColumn column, bool ascending)
    {
        ArgumentNullException.ThrowIfNull(peers);
        Comparison<Peer> byColumn = column switch
        {
            PeerColumn.Port => (a, b) => a.Port.CompareTo(b.Port),
            PeerColumn.Client => (a, b) => NaturalStringComparer.Instance.Compare(a.ClientName, b.ClientName),
            PeerColumn.Progress => (a, b) => a.Progress.CompareTo(b.Progress),
            PeerColumn.Down => (a, b) => a.RateToClient.CompareTo(b.RateToClient),
            PeerColumn.Up => (a, b) => a.RateToPeer.CompareTo(b.RateToPeer),
            PeerColumn.Flags => (a, b) => string.CompareOrdinal(a.FlagStr, b.FlagStr),
            _ => (a, b) => NaturalStringComparer.Instance.Compare(a.Address, b.Address),
        };
        var sorted = peers.ToList();
        sorted.Sort((a, b) =>
        {
            var order = byColumn(a, b);
            if (order != 0)
            {
                return ascending ? order : -order;
            }
            var byAddress = NaturalStringComparer.Instance.Compare(a.Address, b.Address);
            return byAddress != 0 ? byAddress : a.Port.CompareTo(b.Port);
        });
        return sorted;
    }
}
