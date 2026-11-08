using TransmissionKit;

namespace TransmissionRemote.ViewModels;

public enum ConnectionStatus
{
    /// <summary>No server is configured, or the user disconnected.</summary>
    Disconnected,
    Connecting,

    /// <summary>The server stopped answering; the connection keeps trying on its own. See <see cref="ReconnectAttempt"/>.</summary>
    Reconnecting,
    Connected,

    /// <summary>Stopped for good until the user acts. See <see cref="ConnectionFailure"/>.</summary>
    Failed,
}

/// <summary>What the user can do about a failed connection, for the button next to the message.</summary>
public enum FailureRemedy
{
    None,

    /// <summary>Show the certificate's fingerprint and offer to trust it.</summary>
    TrustCertificate,

    /// <summary>The server uses plain HTTP over the internet; offer to allow it for this server.</summary>
    AllowUnencryptedConnection,

    /// <summary>The address, login or HTTPS setting is wrong; offer to edit the server.</summary>
    EditServer,
}

/// <summary>Why the connection stopped.</summary>
/// <param name="Error">
/// The cause: usually a <see cref="TransmissionException"/>, whose <c>Kind</c> picks the message. Null when the
/// connection was blocked before trying (<see cref="FailureRemedy.AllowUnencryptedConnection"/>).
/// </param>
public sealed record ConnectionFailure(FailureRemedy Remedy, Exception? Error)
{
    public string? CertificateFingerprint => (Error as TransmissionException)?.CertificateFingerprint;

    internal static ConnectionFailure From(Exception error) => new(RemedyFor(error), error);

    private static FailureRemedy RemedyFor(Exception error) => (error as TransmissionException)?.Kind switch
    {
        TransmissionErrorKind.UntrustedCertificate => FailureRemedy.TrustCertificate,
        TransmissionErrorKind.Unauthorized or TransmissionErrorKind.InvalidEndpoint
            or TransmissionErrorKind.HostNotAllowed or TransmissionErrorKind.HttpsHandshakeFailed
            or TransmissionErrorKind.ProxyUnauthorized => FailureRemedy.EditServer,
        _ => FailureRemedy.None,
    };
}

/// <summary>The server couldn't be reached; the connection keeps trying.</summary>
/// <param name="Error">Why the last attempt failed.</param>
/// <param name="NextAttempt">When the next attempt starts, or null while one is in flight.</param>
public sealed record ReconnectAttempt(TransmissionException Error, DateTimeOffset? NextAttempt);

/// <summary>The info pane's tabs. Only the visible tab's data is fetched.</summary>
public enum DetailTab
{
    General,
    Trackers,
    Peers,
    Files,
    Speed,
}

public static class DetailTabExtensions
{
    /// <summary>
    /// The fields to fetch, so a torrent with thousands of files doesn't slow down every refresh. The Speed
    /// tab draws the session's history and fetches nothing.
    /// </summary>
    public static IReadOnlyList<TorrentField> Fields(this DetailTab tab) => tab switch
    {
        DetailTab.General => TorrentDetails.GeneralFields,
        DetailTab.Trackers => TorrentDetails.TrackerFields,
        DetailTab.Peers => TorrentDetails.PeerFields,
        DetailTab.Files => TorrentDetails.FileFields,
        _ => [],
    };
}

public enum TransferDirection
{
    Download,
    Upload,
}

/// <summary>A point in the Speed tab's chart.</summary>
public sealed record SpeedSample(DateTimeOffset Time, long Download, long Upload);

/// <summary>Recent session-wide transfer speeds, the last 10 minutes.</summary>
public sealed class SpeedHistory
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly List<SpeedSample> _samples = [];

    public IReadOnlyList<SpeedSample> Samples => _samples;

    public void Append(long download, long upload, DateTimeOffset time)
    {
        _samples.Add(new SpeedSample(time, download, upload));
        _samples.RemoveAll(sample => sample.Time < time - Window);
    }

    public void Clear() => _samples.Clear();
}

/// <summary>Posts a notification when a torrent finishes downloading.</summary>
internal sealed class CompletionDetector(INotificationService notifications)
{
    /// <summary>The first list after connecting is only a baseline: everything in it finished before we watched.</summary>
    private bool _hasBaseline;

    public void Reset() => _hasBaseline = false;

    public void Compare(IReadOnlyList<Torrent> previous, IReadOnlyList<Torrent> current, bool notify)
    {
        var hadBaseline = _hasBaseline;
        _hasBaseline = true;
        if (!hadBaseline || !notify)
        {
            return;
        }
        var previouslyIncomplete = previous.Where(torrent => !torrent.IsComplete).Select(torrent => torrent.Id).ToHashSet();
        foreach (var torrent in current.Where(torrent => torrent.IsComplete && previouslyIncomplete.Contains(torrent.Id)))
        {
            notifications.ShowDownloadComplete(torrent);
        }
    }
}
