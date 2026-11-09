using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TransmissionRemote.ViewModels;

/// <summary>What the main window's content area shows. Every page but Connected replaces the torrent list.</summary>
public enum ConnectionPage
{
    /// <summary>The torrent list, the info pane and the status bar.</summary>
    Connected,
    Connecting,

    /// <summary>"Can't Reach the Server", counting down to the next automatic attempt.</summary>
    Reconnecting,

    /// <summary>The server's certificate isn't trusted; offer to trust its fingerprint.</summary>
    UntrustedCertificate,

    /// <summary>Plain HTTP over the internet, blocked before anything was sent; offer to allow it.</summary>
    UnencryptedConnectionBlocked,

    /// <summary>Any other failure that needs the user, such as a wrong password.</summary>
    CouldNotConnect,

    /// <summary>Disconnected, and no server is set up yet.</summary>
    NoServer,

    /// <summary>Disconnected from a saved server, for example after "Stop Trying".</summary>
    NotConnected,
}

/// <summary>
/// The page that replaces the torrent list while not connected: which one, the error to explain, the
/// certificate's fingerprint and the countdown to the next automatic attempt. Ported from the Mac app's
/// <c>ConnectionStateViews</c>; the buttons use <see cref="MainViewModel"/>'s commands.
/// </summary>
/// <remarks>Use it from the UI thread, like <see cref="ServerConnection"/>.</remarks>
public sealed partial class ConnectionStateViewModel : ObservableObject, IDisposable
{
    private readonly ServerConnection _connection;
    private readonly ServerStore _servers;
    private readonly TimeProvider _time;
    private CancellationTokenSource? _countdown;

    public ConnectionStateViewModel(ServerConnection connection, ServerStore servers, TimeProvider? time = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _servers = servers ?? throw new ArgumentNullException(nameof(servers));
        _time = time ?? TimeProvider.System;
        _connection.PropertyChanged += OnConnectionChanged;
        _servers.Changed += OnServersChanged;
        Page = CurrentPage();
        RestartCountdown();
    }

    [ObservableProperty]
    public partial ConnectionPage Page { get; private set; }

    /// <summary>
    /// Time left until the next automatic attempt in whole seconds, rounded up the way a countdown shows it.
    /// Zero once it's due or under way, which reads "Trying again…".
    /// </summary>
    /// <remarks>Zero rather than null because x:Bind doesn't call a function whose argument is null.</remarks>
    [ObservableProperty]
    public partial TimeSpan RetryCountdown { get; private set; }

    /// <summary>The server being connected to, for "Connecting to %@…".</summary>
    public string ServerName => _connection.Profile?.DisplayName ?? "";

    /// <summary>
    /// What went wrong, to pick the message from: the last failed attempt while reconnecting, or why the
    /// connection stopped. Null when it was blocked before trying.
    /// </summary>
    public Exception? Error => (Exception?)_connection.Reconnect?.Error ?? _connection.Failure?.Error;

    /// <summary>The SHA-256 fingerprint the server presented, for the Untrusted Certificate page.</summary>
    public string? CertificateFingerprint => _connection.Failure?.CertificateFingerprint;

    /// <summary>"Try Now" only makes sense while waiting, not while an attempt is under way.</summary>
    public bool CanRetryNow => _connection.Reconnect?.NextAttempt is not null;

    public void Dispose()
    {
        _connection.PropertyChanged -= OnConnectionChanged;
        _servers.Changed -= OnServersChanged;
        StopCountdown();
    }

    private ConnectionPage CurrentPage() => _connection.Status switch
    {
        ConnectionStatus.Connected => ConnectionPage.Connected,
        ConnectionStatus.Connecting => ConnectionPage.Connecting,
        ConnectionStatus.Reconnecting => ConnectionPage.Reconnecting,
        ConnectionStatus.Failed => _connection.Failure?.Remedy switch
        {
            FailureRemedy.TrustCertificate => ConnectionPage.UntrustedCertificate,
            FailureRemedy.AllowUnencryptedConnection => ConnectionPage.UnencryptedConnectionBlocked,
            _ => ConnectionPage.CouldNotConnect,
        },
        _ => _servers.Servers.Count == 0 ? ConnectionPage.NoServer : ConnectionPage.NotConnected,
    };

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ServerConnection.Status):
                Page = CurrentPage();
                break;
            case nameof(ServerConnection.Failure):
                Page = CurrentPage();
                OnPropertyChanged(nameof(Error));
                OnPropertyChanged(nameof(CertificateFingerprint));
                break;
            case nameof(ServerConnection.Reconnect):
                OnPropertyChanged(nameof(Error));
                OnPropertyChanged(nameof(CanRetryNow));
                RestartCountdown();
                break;
            case nameof(ServerConnection.Profile):
                OnPropertyChanged(nameof(ServerName));
                break;
        }
    }

    private void OnServersChanged(object? sender, EventArgs e) => Page = CurrentPage();

    private void RestartCountdown()
    {
        StopCountdown();
        if (_connection.Reconnect?.NextAttempt is { } nextAttempt)
        {
            _countdown = new CancellationTokenSource();
            _ = CountDownAsync(nextAttempt, _countdown.Token);
        }
        else
        {
            RetryCountdown = TimeSpan.Zero;
        }
    }

    private void StopCountdown()
    {
        _countdown?.Cancel();
        _countdown?.Dispose();
        _countdown = null;
    }

    /// <summary>Updates <see cref="RetryCountdown"/> each time the shown second changes, until the attempt is due.</summary>
    private async Task CountDownAsync(DateTimeOffset nextAttempt, CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var remaining = nextAttempt - _time.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    RetryCountdown = TimeSpan.Zero;
                    return;
                }
                var shownSeconds = Math.Ceiling(remaining.TotalSeconds);
                RetryCountdown = TimeSpan.FromSeconds(shownSeconds);
                var untilNextSecond = remaining - TimeSpan.FromSeconds(shownSeconds - 1);
                await Task.Delay(untilNextSecond, _time, cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The attempt changed: a new countdown took over.
        }
    }
}
