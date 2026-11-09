using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>
/// The main window: the server, the sidebar filters, the torrent list and its actions, and the status bar.
/// Ported from the Mac app's AppModel and MainWindow.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public MainViewModel(
        ServerStore servers,
        SettingsStore settings,
        ServerConnection connection,
        AddTorrentFlow addQueue,
        TimeProvider? time = null)
    {
        Servers = servers ?? throw new ArgumentNullException(nameof(servers));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        AddQueue = addQueue ?? throw new ArgumentNullException(nameof(addQueue));
        ConnectionState = new ConnectionStateViewModel(connection, servers, time);
        Sidebar = new SidebarSummary([]);
        var current = settings.Current;
        // Until the user picks a column, the newest torrents come first, as in the Mac app.
        SortColumn = Enum.TryParse<TorrentColumn>(current.SortColumn, out var column) ? column : TorrentColumn.Added;
        SortAscending = current.SortAscending;
        StatusItems = [.. Enum.GetValues<StatusFilter>().Select(filter => new StatusSidebarItem(filter))];
        Connection.PropertyChanged += OnConnectionChanged;
        Servers.Changed += OnServersChanged;
    }

    public ServerStore Servers { get; }
    public SettingsStore Settings { get; }
    public ServerConnection Connection { get; }
    public AddTorrentFlow AddQueue { get; }

    /// <summary>The page shown instead of the list while not connected.</summary>
    public ConnectionStateViewModel ConnectionState { get; }

    /// <summary>The torrents the list shows: filtered, searched and sorted, updated in place.</summary>
    public ObservableCollection<TorrentRow> Rows { get; } = [];

    [ObservableProperty]
    public partial SidebarSummary Sidebar { get; private set; }

    /// <summary>
    /// The sidebar's Status section, always all ten. The items stay the same objects and only their counts
    /// change, so the selection and Narrator's place don't jump on every refresh.
    /// </summary>
    public IReadOnlyList<StatusSidebarItem> StatusItems { get; }

    [ObservableProperty]
    public partial SidebarFilter SelectedFilter { get; set; } = new StatusSidebarFilter(StatusFilter.All);

    /// <summary>"Filter by name", like the Mac app's search field.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial TorrentColumn SortColumn { get; set; }

    [ObservableProperty]
    public partial bool SortAscending { get; set; }

    public IReadOnlyList<TorrentColumn> VisibleColumns => TorrentColumns.FromSettings(Settings.Current);

    public bool HasSelection => Connection.Selection.Count > 0;

    public bool IsConnected => Connection.IsConnected;

    /// <summary>The torrent actions need torrents to act on and a server to send them to.</summary>
    public bool CanActOnSelection => HasSelection && IsConnected;

    public bool CanDisconnect => Connection.Status != ConnectionStatus.Disconnected;

    public bool HasActiveServer => Servers.ActiveServer is not null;

    public bool IsAlternativeSpeedEnabled => Connection.Session?.AltSpeedEnabled == true;

    /// <summary>The server's name for the title bar, or empty before the first connection.</summary>
    public string ServerName => Connection.Profile?.DisplayName ?? "";

    /// <summary>View → Show Sidebar, remembered.</summary>
    public bool ShowSidebar
    {
        get => Settings.Current.ShowSidebar;
        set
        {
            if (value != ShowSidebar)
            {
                Settings.Update(settings => settings with { ShowSidebar = value });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>View → Show Info Pane, remembered.</summary>
    public bool ShowInfoPane
    {
        get => Settings.Current.ShowDetailPane;
        set
        {
            if (value != ShowInfoPane)
            {
                Settings.Update(settings => settings with { ShowDetailPane = value });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>"Transmission 4.1.1 · JSON-RPC 2.0" for the window's subtitle; empty until connected.</summary>
    public string ServerSummary => Connection.ServerInfo is { } info && Connection.Dialect is { } dialect
        ? $"Transmission {info.Version} · {dialect.DisplayName()}"
        : "";

    /// <summary>Connects to the active server, or stays disconnected when there's none yet.</summary>
    public void Start() => ConnectToActiveServer();

    public void ConnectToActiveServer()
    {
        if (Servers.ActiveServer is not { } server)
        {
            Connection.Disconnect();
            return;
        }
        Connection.Connect(server, Servers.Password(server.Id), Servers.ProxyPassword(server.Id));
    }

    /// <summary>
    /// The Server menu's "Download Speed Limit" or "Upload Speed Limit": Unlimited, then the speeds from the
    /// settings, with the daemon's current limit checked.
    /// </summary>
    public IReadOnlyList<SpeedLimitChoice> GlobalSpeedLimitChoices(TransferDirection direction)
    {
        var current = Connection.GlobalSpeedLimit(direction);
        var presets = direction == TransferDirection.Download
            ? Settings.Current.DownloadSpeedPresets
            : Settings.Current.UploadSpeedPresets;
        return
        [
            new SpeedLimitChoice(Limit: null, IsCurrent: current is null),
            .. SpeedLimitChoice.Speeds(presets).Select(speed => new SpeedLimitChoice(speed, IsCurrent: speed == current)),
        ];
    }

    public void SetVisibleColumns(IEnumerable<TorrentColumn> columns)
    {
        var names = columns.Distinct().Select(column => column.ToString()).ToList();
        Settings.Update(settings => settings with { TorrentColumns = names });
        OnPropertyChanged(nameof(VisibleColumns));
    }

    /// <summary>The column chooser: the visible columns in their order, then the hidden ones.</summary>
    public IReadOnlyList<ColumnChoice> ColumnChoices => TorrentColumns.Choices(VisibleColumns);

    /// <summary>Dragging a header to another place.</summary>
    public void MoveColumn(TorrentColumn column, int index) => SetVisibleColumns(TorrentColumns.Moved(VisibleColumns, column, index));

    /// <summary>The column chooser's check box. Name can't be hidden.</summary>
    public void SetColumnVisible(TorrentColumn column, bool isVisible) =>
        SetVisibleColumns(TorrentColumns.WithVisibility(VisibleColumns, column, isVisible));

    public double ColumnWidth(TorrentColumn column) => TorrentColumns.Width(Settings.Current, column);

    /// <summary>Remembers a width the user dragged a column to, never below the column's minimum.</summary>
    public void SetColumnWidth(TorrentColumn column, double width)
    {
        var kept = Math.Round(Math.Max(width, TorrentColumns.Spec(column).MinimumWidth));
        Settings.Update(settings =>
        {
            var widths = new Dictionary<string, double>(settings.TorrentColumnWidths) { [column.ToString()] = kept };
            return settings with { TorrentColumnWidths = widths };
        });
    }

    /// <summary>Clicking a column header: the same column flips the order, another sorts by it.</summary>
    public void SortBy(TorrentColumn column)
    {
        if (column == SortColumn)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = column;
        }
    }

    [RelayCommand]
    private void SwitchToServer(Guid id)
    {
        Servers.Activate(id);
        ConnectToActiveServer();
    }

    [RelayCommand(CanExecute = nameof(HasActiveServer))]
    private void Reconnect() => ConnectToActiveServer();

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect() => Connection.Disconnect();

    [RelayCommand]
    private void RetryNow() => Connection.RetryNow();

    /// <summary>Pins the certificate the server presented, after the user confirmed its fingerprint.</summary>
    [RelayCommand]
    private void TrustCertificate()
    {
        if (Connection.Failure?.CertificateFingerprint is { } fingerprint && Servers.ActiveServer is { } server)
        {
            Servers.Save(server with { PinnedCertificateFingerprint = fingerprint });
            ConnectToActiveServer();
        }
    }

    [RelayCommand]
    private void AllowUnencryptedConnection()
    {
        if (Servers.ActiveServer is { } server)
        {
            Servers.Save(server with { AllowsUnencryptedInternetConnection = true });
            ConnectToActiveServer();
        }
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task StartAsync() => Connection.PerformAsync(TorrentAction.Start, Connection.Selection);

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task StartNowAsync() => Connection.PerformAsync(TorrentAction.StartNow, Connection.Selection);

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task StopAsync() => Connection.PerformAsync(TorrentAction.Stop, Connection.Selection);

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task VerifyAsync() => Connection.PerformAsync(TorrentAction.Verify, Connection.Selection);

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task ReannounceAsync() => Connection.PerformAsync(TorrentAction.Reannounce, Connection.Selection);

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task MoveInQueueAsync(QueueMove move) => Connection.MoveInQueueAsync(move, Connection.Selection);

    /// <summary>Removes the selection; ask the user first, as the Mac app does.</summary>
    public Task RemoveSelectionAsync(bool deletingData) => Connection.RemoveAsync(Connection.Selection, deletingData);

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task StartAllAsync() => Connection.PerformOnAllAsync(TorrentAction.Start);

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task StopAllAsync() => Connection.PerformOnAllAsync(TorrentAction.Stop);

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task ToggleAlternativeSpeedAsync() =>
        Connection.SetAlternativeSpeedEnabledAsync(Connection.Session?.AltSpeedEnabled != true);

    public void Dispose()
    {
        Connection.PropertyChanged -= OnConnectionChanged;
        Servers.Changed -= OnServersChanged;
        ConnectionState.Dispose();
    }

    partial void OnSelectedFilterChanged(SidebarFilter value) => UpdateRows();

    partial void OnSearchTextChanged(string value) => UpdateRows();

    partial void OnSortColumnChanged(TorrentColumn value) => SortChanged();

    partial void OnSortAscendingChanged(bool value) => SortChanged();

    private void SortChanged()
    {
        Settings.Update(settings => settings with { SortColumn = SortColumn.ToString(), SortAscending = SortAscending });
        UpdateRows();
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ServerConnection.Torrents):
                Sidebar = new SidebarSummary(Connection.Torrents);
                UpdateStatusCounts();
                UpdateRows();
                break;
            case nameof(ServerConnection.Selection):
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(CanActOnSelection));
                NotifySelectionCommands();
                break;
            case nameof(ServerConnection.Status):
                OnStatusChanged();
                break;
            case nameof(ServerConnection.Session):
                OnPropertyChanged(nameof(IsAlternativeSpeedEnabled));
                break;
            case nameof(ServerConnection.Profile):
                OnPropertyChanged(nameof(ServerName));
                break;
            case nameof(ServerConnection.ServerInfo) or nameof(ServerConnection.Dialect):
                OnPropertyChanged(nameof(ServerSummary));
                break;
        }
    }

    private void OnStatusChanged()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanActOnSelection));
        OnPropertyChanged(nameof(CanDisconnect));
        NotifySelectionCommands();
        StartAllCommand.NotifyCanExecuteChanged();
        StopAllCommand.NotifyCanExecuteChanged();
        ToggleAlternativeSpeedCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    private void OnServersChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasActiveServer));
        ReconnectCommand.NotifyCanExecuteChanged();
    }

    private void UpdateStatusCounts()
    {
        foreach (var item in StatusItems)
        {
            item.Count = Sidebar.CountFor(item.Filter);
        }
    }

    private void UpdateRows()
    {
        var visible = Connection.Torrents
            .Where(SelectedFilter.Matches)
            .Where(torrent => NameSearch.Matches(torrent.Name, SearchText, CultureInfo.CurrentCulture))
            .Order(TorrentSorting.Comparer(SortColumn, SortAscending))
            .ToList();
        TorrentRowSync.Apply(Rows, visible);
    }

    private void NotifySelectionCommands()
    {
        StartCommand.NotifyCanExecuteChanged();
        StartNowCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        VerifyCommand.NotifyCanExecuteChanged();
        ReannounceCommand.NotifyCanExecuteChanged();
        MoveInQueueCommand.NotifyCanExecuteChanged();
    }
}
