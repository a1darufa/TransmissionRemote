using System.Collections.ObjectModel;
using System.ComponentModel;
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
    public MainViewModel(ServerStore servers, SettingsStore settings, ServerConnection connection, AddTorrentFlow addQueue)
    {
        Servers = servers ?? throw new ArgumentNullException(nameof(servers));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        AddQueue = addQueue ?? throw new ArgumentNullException(nameof(addQueue));
        Sidebar = new SidebarSummary([]);
        var current = settings.Current;
        // Until the user picks a column, the newest torrents come first, as in the Mac app.
        SortColumn = Enum.TryParse<TorrentColumn>(current.SortColumn, out var column) ? column : TorrentColumn.Added;
        SortAscending = current.SortAscending;
        Connection.PropertyChanged += OnConnectionChanged;
    }

    public ServerStore Servers { get; }
    public SettingsStore Settings { get; }
    public ServerConnection Connection { get; }
    public AddTorrentFlow AddQueue { get; }

    /// <summary>The torrents the list shows: filtered, searched and sorted, updated in place.</summary>
    public ObservableCollection<TorrentRow> Rows { get; } = [];

    [ObservableProperty]
    public partial SidebarSummary Sidebar { get; private set; }

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

    public void SetVisibleColumns(IEnumerable<TorrentColumn> columns)
    {
        var names = columns.Distinct().Select(column => column.ToString()).ToList();
        Settings.Update(settings => settings with { TorrentColumns = names });
        OnPropertyChanged(nameof(VisibleColumns));
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

    [RelayCommand]
    private void Reconnect() => ConnectToActiveServer();

    [RelayCommand]
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

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task StartAsync() => Connection.PerformAsync(TorrentAction.Start, Connection.Selection);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task StartNowAsync() => Connection.PerformAsync(TorrentAction.StartNow, Connection.Selection);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task StopAsync() => Connection.PerformAsync(TorrentAction.Stop, Connection.Selection);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task VerifyAsync() => Connection.PerformAsync(TorrentAction.Verify, Connection.Selection);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task ReannounceAsync() => Connection.PerformAsync(TorrentAction.Reannounce, Connection.Selection);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task MoveInQueueAsync(QueueMove move) => Connection.MoveInQueueAsync(move, Connection.Selection);

    /// <summary>Removes the selection; ask the user first, as the Mac app does.</summary>
    public Task RemoveSelectionAsync(bool deletingData) => Connection.RemoveAsync(Connection.Selection, deletingData);

    [RelayCommand]
    private Task StartAllAsync() => Connection.PerformOnAllAsync(TorrentAction.Start);

    [RelayCommand]
    private Task StopAllAsync() => Connection.PerformOnAllAsync(TorrentAction.Stop);

    [RelayCommand]
    private Task ToggleAlternativeSpeedAsync() =>
        Connection.SetAlternativeSpeedEnabledAsync(Connection.Session?.AltSpeedEnabled != true);

    public void Dispose() => Connection.PropertyChanged -= OnConnectionChanged;

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
                UpdateRows();
                break;
            case nameof(ServerConnection.Selection):
                OnPropertyChanged(nameof(HasSelection));
                NotifySelectionCommands();
                break;
            case nameof(ServerConnection.ServerInfo) or nameof(ServerConnection.Dialect):
                OnPropertyChanged(nameof(ServerSummary));
                break;
        }
    }

    private void UpdateRows()
    {
        var query = SearchText.Trim();
        var visible = Connection.Torrents
            .Where(SelectedFilter.Matches)
            .Where(torrent => query.Length == 0 || torrent.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
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
