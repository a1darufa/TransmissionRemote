using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>
/// The live connection to one Transmission daemon: polls it, holds what it reported, and runs the user's
/// actions against it. Ported from the Mac app's ServerConnection.
/// </summary>
/// <remarks>
/// Use it from the UI thread. Awaits resume on the UI thread's synchronization context, so properties
/// change there and bindings update safely.
/// </remarks>
public sealed partial class ServerConnection : ObservableObject, IDisposable
{
    private readonly SettingsStore _settings;
    private readonly IClientCertificateProvider _certificates;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Func<TransmissionClientOptions, TransmissionClient> _createClient;
    private readonly CompletionDetector _completions;
    private readonly TorrentListRefreshPlan _listPlan;

    private TransmissionClient? _client;
    private CancellationTokenSource? _pollCancellation;
    private CancellationTokenSource? _pendingWait;
    private IReadOnlySet<int> _selection = new HashSet<int>();
    private DetailTab _detailTab;
    private bool _isRefreshing;
    private bool _isRefreshRequested;
    private int _refreshCount;
    private Dictionary<int, TrackerSummary> _trackerSummaries = [];

    public ServerConnection(
        SettingsStore settings,
        IClientCertificateProvider certificates,
        INotificationService notifications,
        ILoggerFactory? loggerFactory = null,
        TimeProvider? time = null)
        : this(settings, certificates, notifications, loggerFactory, time, createClient: null)
    {
    }

    /// <summary>Lets tests supply clients that talk to a fake daemon.</summary>
    internal ServerConnection(
        SettingsStore settings,
        IClientCertificateProvider certificates,
        INotificationService notifications,
        ILoggerFactory? loggerFactory,
        TimeProvider? time,
        Func<TransmissionClientOptions, TransmissionClient>? createClient)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _certificates = certificates ?? throw new ArgumentNullException(nameof(certificates));
        _completions = new CompletionDetector(notifications ?? throw new ArgumentNullException(nameof(notifications)));
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<ServerConnection>();
        _time = time ?? TimeProvider.System;
        _listPlan = new TorrentListRefreshPlan(_time);
        _createClient = createClient ?? (options => new TransmissionClient(options, _loggerFactory));
    }

    [ObservableProperty]
    public partial ConnectionStatus Status { get; private set; }

    /// <summary>Set while <see cref="Status"/> is Failed.</summary>
    [ObservableProperty]
    public partial ConnectionFailure? Failure { get; private set; }

    /// <summary>Set while <see cref="Status"/> is Reconnecting.</summary>
    [ObservableProperty]
    public partial ReconnectAttempt? Reconnect { get; private set; }

    [ObservableProperty]
    public partial ServerProfile? Profile { get; private set; }

    [ObservableProperty]
    public partial ServerInfo? ServerInfo { get; private set; }

    [ObservableProperty]
    public partial RpcDialect? Dialect { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<Torrent> Torrents { get; private set; } = [];

    [ObservableProperty]
    public partial SessionSettings? Session { get; private set; }

    [ObservableProperty]
    public partial SessionStats? Stats { get; private set; }

    /// <summary>Free space in the server's default download folder.</summary>
    [ObservableProperty]
    public partial FreeSpace? FreeSpace { get; private set; }

    /// <summary>The selected torrent's data for the visible <see cref="DetailTab"/>.</summary>
    [ObservableProperty]
    public partial TorrentDetails? Details { get; private set; }

    /// <summary>A temporary problem while polling, such as a network blip. The data stays visible.</summary>
    [ObservableProperty]
    public partial TransmissionException? PollingProblem { get; private set; }

    /// <summary>When the list last refreshed, to show how stale it is during a problem.</summary>
    [ObservableProperty]
    public partial DateTimeOffset? LastUpdated { get; private set; }

    /// <summary>The last failed user action, for an error dialog. The app clears it once shown.</summary>
    [ObservableProperty]
    public partial Exception? ActionError { get; set; }

    /// <summary>Whether the window is in front; background polling is slower.</summary>
    public bool IsInForeground { get; set; } = true;

    public SpeedHistory SpeedHistory { get; } = new();

    public bool IsConnected => Status == ConnectionStatus.Connected;

    public IReadOnlySet<int> Selection
    {
        get => _selection;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_selection.SetEquals(value))
            {
                return;
            }
            _selection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SingleSelectedTorrent));
            _ = ReloadDetailsAsync();
        }
    }

    public DetailTab DetailTab
    {
        get => _detailTab;
        set
        {
            if (SetProperty(ref _detailTab, value))
            {
                _ = ReloadDetailsAsync();
            }
        }
    }

    public IReadOnlyList<Torrent> SelectedTorrents => Torrents.Where(torrent => _selection.Contains(torrent.Id)).ToList();

    public Torrent? SingleSelectedTorrent =>
        _selection.Count == 1 ? Torrents.FirstOrDefault(torrent => _selection.Contains(torrent.Id)) : null;

    /// <summary>Bandwidth groups arrived in Transmission 4.0.</summary>
    public bool SupportsBandwidthGroups => ServerInfo?.RpcVersion >= 17;

    /// <summary>Connects and starts polling, replacing any earlier connection.</summary>
    /// <param name="password">The server's password, from <see cref="ServerStore.Password"/>.</param>
    /// <param name="proxyPassword">The proxy's password, from <see cref="ServerStore.ProxyPassword"/>.</param>
    public void Connect(ServerProfile profile, string? password, string? proxyPassword = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Disconnect();
        Profile = profile;
        Log.ConnectingToServer(_logger, profile.DisplayName);
        if (profile.IsBlockedByTransportPolicy)
        {
            Log.BlockedByTransportPolicy(_logger, profile.DisplayName);
            SetFailed(new ConnectionFailure(FailureRemedy.AllowUnencryptedConnection, Error: null));
            return;
        }

        TransmissionClient client;
        try
        {
            client = _createClient(OptionsFor(profile, password, proxyPassword));
        }
        catch (Exception error) when (error is TransmissionException or ClientIdentityImportException)
        {
            Fail(error);
            return;
        }
        _client = client;
        Status = ConnectionStatus.Connecting;
        _pollCancellation = new CancellationTokenSource();
        _ = RunAsync(client, _pollCancellation.Token);
    }

    public void Disconnect()
    {
        _pollCancellation?.Cancel();
        _pollCancellation?.Dispose();
        _pollCancellation = null;
        _client?.Dispose();
        _client = null;
        _completions.Reset();
        _listPlan.Reset();
        _trackerSummaries = [];
        _refreshCount = 0;
        Status = ConnectionStatus.Disconnected;
        Failure = null;
        Reconnect = null;
        ServerInfo = null;
        Dialect = null;
        Torrents = [];
        Session = null;
        Stats = null;
        FreeSpace = null;
        Details = null;
        Selection = new HashSet<int>();
        SpeedHistory.Clear();
        PollingProblem = null;
        LastUpdated = null;
    }

    /// <summary>
    /// Skips the rest of the current wait: for "Try now", and when the PC wakes up or the network changes,
    /// since the data on screen may be stale.
    /// </summary>
    public void RetryNow() => _pendingWait?.Cancel();

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }

    private TransmissionClientOptions OptionsFor(ServerProfile profile, string? password, string? proxyPassword) =>
        new(profile.Endpoint)
        {
            Credentials = profile.Username.Length > 0 ? new Credentials(profile.Username, password ?? "") : null,
            PinnedCertificateFingerprint = profile.PinnedCertificateFingerprint,
            ProtocolPreference = profile.ProtocolPreference,
            Proxy = profile.Proxy.ToConfiguration(proxyPassword),
            ClientIdentity = _certificates.IdentityFor(profile),
        };

    private async Task RunAsync(TransmissionClient client, CancellationToken cancellation)
    {
        try
        {
            if (!await EstablishConnectionAsync(client, cancellation))
            {
                return;
            }
            while (!cancellation.IsCancellationRequested && IsCurrent(client))
            {
                await RefreshAsync();
                await PauseAsync(_settings.Current.RefreshInterval(IsInForeground), cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Disconnected.
        }
        catch (Exception error) when (IsCurrent(client))
        {
            // A background loop must never fail silently: an unexpected error stops the connection visibly.
            Fail(error);
        }
    }

    /// <summary>Connects, retrying with growing delays while the server can't be reached.</summary>
    private async Task<bool> EstablishConnectionAsync(TransmissionClient client, CancellationToken cancellation)
    {
        var failures = 0;
        while (!cancellation.IsCancellationRequested && IsCurrent(client))
        {
            try
            {
                var info = await client.ConnectAsync(cancellation);
                if (!IsCurrent(client))
                {
                    return false;
                }
                ServerInfo = info;
                Dialect = client.Dialect;
                Reconnect = null;
                Status = ConnectionStatus.Connected;
                return true;
            }
            catch (TransmissionException error) when (IsCurrent(client))
            {
                if (!error.IsTransient)
                {
                    Fail(error);
                    return false;
                }
                failures++;
                var delay = RetryBackoff.DelayAfterFailures(failures);
                Log.ServerUnreachable(_logger, error.Kind, error.Message, delay);
                Reconnect = new ReconnectAttempt(error, _time.GetUtcNow() + delay);
                Status = ConnectionStatus.Reconnecting;
                await PauseAsync(delay, cancellation);
                Reconnect = new ReconnectAttempt(error, NextAttempt: null);
            }
        }
        return false;
    }

    /// <summary>Waits, unless <see cref="RetryNow"/> or disconnecting ends the wait early.</summary>
    private async Task PauseAsync(TimeSpan delay, CancellationToken cancellation)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        _pendingWait = wait;
        try
        {
            await Task.Delay(delay, _time, wait.Token);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            // RetryNow: carry on at once.
        }
        finally
        {
            if (_pendingWait == wait)
            {
                _pendingWait = null;
            }
        }
    }

    /// <summary>Guards against results from a connection that was replaced while a request was in flight.</summary>
    private bool IsCurrent(TransmissionClient client) => ReferenceEquals(_client, client);

    private void Fail(Exception error)
    {
        Log.ConnectionFailed(_logger, error, Profile?.DisplayName ?? "?", error.Message);
        SetFailed(ConnectionFailure.From(error));
    }

    private void SetFailed(ConnectionFailure failure)
    {
        _pollCancellation?.Cancel();
        _client?.Dispose();
        _client = null;
        Reconnect = null;
        Failure = failure;
        Status = ConnectionStatus.Failed;
    }

    partial void OnStatusChanged(ConnectionStatus value) => OnPropertyChanged(nameof(IsConnected));

    partial void OnTorrentsChanged(IReadOnlyList<Torrent> value)
    {
        OnPropertyChanged(nameof(SelectedTorrents));
        OnPropertyChanged(nameof(SingleSelectedTorrent));
    }

    partial void OnServerInfoChanged(ServerInfo? value) => OnPropertyChanged(nameof(SupportsBandwidthGroups));
}
