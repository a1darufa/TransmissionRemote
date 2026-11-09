using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>
/// The strip under the torrent list: connection security and the torrent count, a polling problem with how old the
/// data is, the speeds with their limits, free space and the alternative speed toggle. Ported from the Mac app's
/// <c>StatusBarView</c>.
/// </summary>
/// <remarks>Use it from the UI thread, like <see cref="ServerConnection"/>.</remarks>
public sealed partial class StatusBarViewModel : ObservableObject, IDisposable
{
    /// <summary>How often "Updated 2m ago" moves on, as on the Mac.</summary>
    public static readonly TimeSpan AgeRefreshInterval = TimeSpan.FromSeconds(10);

    private readonly ServerConnection _connection;
    private readonly TimeProvider _time;
    private CancellationTokenSource? _ageTicker;

    public StatusBarViewModel(ServerConnection connection, TimeProvider? time = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _time = time ?? TimeProvider.System;
        _connection.PropertyChanged += OnConnectionChanged;
    }

    public int TorrentCount => _connection.Torrents.Count;

    /// <summary>How well the connection protects the password, for the lock glyph; null before any server is set.</summary>
    public TransportSecurity? Security => _connection.Profile?.Endpoint.TransportSecurity;

    public string ServerVersion => _connection.ServerInfo?.Version ?? "";

    public RpcDialect? Dialect => _connection.Dialect;

    public TransmissionException? PollingProblem => _connection.PollingProblem;

    /// <summary>How long ago the list last refreshed, while there's a polling problem; zero otherwise.</summary>
    [ObservableProperty]
    public partial TimeSpan DataAge { get; private set; }

    public long DownloadSpeed => _connection.Stats?.DownloadSpeed ?? 0;

    public long UploadSpeed => _connection.Stats?.UploadSpeed ?? 0;

    /// <summary>The download limit in force, in kB/s, or null for none.</summary>
    public int? DownloadLimit => ActiveLimit(_connection.Session, TransferDirection.Download);

    public int? UploadLimit => ActiveLimit(_connection.Session, TransferDirection.Upload);

    /// <summary>Free space in the server's default download folder; null until it's known.</summary>
    public FreeSpace? FreeSpace => _connection.FreeSpace;

    public bool IsAlternativeSpeedEnabled => _connection.Session?.AltSpeedEnabled == true;

    /// <summary>
    /// The limit that applies: the alternative one while alternative speed limits are on, else the regular one when
    /// it's enabled, else none. The Mac app's <c>activeLimit</c>.
    /// </summary>
    public static int? ActiveLimit(SessionSettings? session, TransferDirection direction)
    {
        if (session is null)
        {
            return null;
        }
        var isDownload = direction == TransferDirection.Download;
        if (session.AltSpeedEnabled == true)
        {
            return isDownload ? session.AltSpeedDown : session.AltSpeedUp;
        }
        var enabled = isDownload ? session.SpeedLimitDownEnabled : session.SpeedLimitUpEnabled;
        return enabled == true ? (isDownload ? session.SpeedLimitDown : session.SpeedLimitUp) : null;
    }

    public void Dispose()
    {
        _connection.PropertyChanged -= OnConnectionChanged;
        StopAgeTicker();
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ServerConnection.Torrents):
                OnPropertyChanged(nameof(TorrentCount));
                break;
            case nameof(ServerConnection.Profile):
                OnPropertyChanged(nameof(Security));
                break;
            case nameof(ServerConnection.ServerInfo):
                OnPropertyChanged(nameof(ServerVersion));
                break;
            case nameof(ServerConnection.Dialect):
                OnPropertyChanged(nameof(Dialect));
                break;
            case nameof(ServerConnection.Stats):
                OnPropertyChanged(nameof(DownloadSpeed));
                OnPropertyChanged(nameof(UploadSpeed));
                break;
            case nameof(ServerConnection.Session):
                OnPropertyChanged(nameof(DownloadLimit));
                OnPropertyChanged(nameof(UploadLimit));
                OnPropertyChanged(nameof(IsAlternativeSpeedEnabled));
                break;
            case nameof(ServerConnection.FreeSpace):
                OnPropertyChanged(nameof(FreeSpace));
                break;
            case nameof(ServerConnection.PollingProblem):
                OnPropertyChanged(nameof(PollingProblem));
                RestartAgeTicker();
                break;
            case nameof(ServerConnection.LastUpdated):
                UpdateDataAge();
                break;
        }
    }

    private void RestartAgeTicker()
    {
        StopAgeTicker();
        UpdateDataAge();
        if (_connection.PollingProblem is not null)
        {
            _ageTicker = new CancellationTokenSource();
            _ = TickAsync(_ageTicker.Token);
        }
    }

    private void StopAgeTicker()
    {
        _ageTicker?.Cancel();
        _ageTicker?.Dispose();
        _ageTicker = null;
    }

    private async Task TickAsync(CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(AgeRefreshInterval, _time, cancellation);
                UpdateDataAge();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The problem cleared, or a new one restarted the ticker.
        }
    }

    private void UpdateDataAge() =>
        DataAge = _connection.PollingProblem is not null && _connection.LastUpdated is { } updated
            ? _time.GetUtcNow() - updated
            : TimeSpan.Zero;
}
