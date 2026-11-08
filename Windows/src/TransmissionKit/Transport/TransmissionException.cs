namespace TransmissionKit;

/// <summary>What went wrong talking to a daemon. The app picks its message and actions from this.</summary>
public enum TransmissionErrorKind
{
    /// <summary>The server's address can't be used; <see cref="TransmissionException.Detail"/> says why.</summary>
    InvalidEndpoint,

    /// <summary>The server wants a username and password, and the ones sent (if any) were rejected.</summary>
    Unauthorized,

    /// <summary>The proxy rejected its username and password.</summary>
    ProxyUnauthorized,

    /// <summary>The server's <c>rpc-whitelist</c> doesn't include this computer's IP address.</summary>
    IpNotAllowed,

    /// <summary>The server's <c>rpc-host-whitelist</c> doesn't include the host name used.</summary>
    HostNotAllowed,

    /// <summary>The TLS certificate isn't trusted and doesn't match the pinned fingerprint.</summary>
    UntrustedCertificate,

    /// <summary>
    /// The HTTPS handshake failed, most often because the server only speaks plain HTTP, as
    /// Transmission's built-in RPC server does.
    /// </summary>
    HttpsHandshakeFailed,

    /// <summary>The server tried to redirect; refused so credentials can't leak to another host.</summary>
    RedirectRefused,

    /// <summary>An unexpected HTTP status; see <see cref="TransmissionException.StatusCode"/>.</summary>
    HttpStatus,

    /// <summary>The call reached Transmission but failed there (bad path, unknown torrent and so on).</summary>
    RpcFailure,

    /// <summary>The daemon is older than Transmission 2.40.</summary>
    UnsupportedServer,

    /// <summary>The response couldn't be read.</summary>
    InvalidResponse,

    /// <summary>The address answered with a web page instead of Transmission's RPC: wrong port or path.</summary>
    NotTransmissionRpc,

    /// <summary>The request didn't get an answer; see <see cref="TransmissionException.NetworkProblem"/>.</summary>
    Network,
}

/// <summary>The kind of network failure behind <see cref="TransmissionErrorKind.Network"/>.</summary>
public enum NetworkProblem
{
    Other,
    Timeout,
    NameNotResolved,
    CannotConnect,
    ConnectionLost,
    ProxyFailed,
}

/// <summary>
/// Any failure of a call to a Transmission daemon. <see cref="Exception.Message"/> is English
/// and meant for logs; the app shows its own localized text based on <see cref="Kind"/>.
/// </summary>
public sealed class TransmissionException : Exception
{
    public TransmissionException()
        : this(TransmissionErrorKind.Network, "The request failed.")
    {
    }

    public TransmissionException(string message)
        : this(TransmissionErrorKind.Network, message)
    {
    }

    public TransmissionException(string message, Exception innerException)
        : this(TransmissionErrorKind.Network, message, innerException: innerException)
    {
    }

    private TransmissionException(
        TransmissionErrorKind kind,
        string message,
        string? detail = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Detail = detail;
    }

    public TransmissionErrorKind Kind { get; }

    /// <summary>
    /// The specific part of the message: the reason an address is invalid, the server's own error
    /// text, the unsupported version number or what couldn't be read.
    /// </summary>
    public string? Detail { get; }

    public int? StatusCode { get; private init; }

    /// <summary>
    /// For <see cref="TransmissionErrorKind.UntrustedCertificate"/>: the certificate's SHA-256, <c>AB:CD:…</c>.
    /// </summary>
    public string? CertificateFingerprint { get; private init; }

    public NetworkProblem? NetworkProblem { get; private init; }

    /// <summary>Errors that won't fix themselves by retrying, so polling should stop.</summary>
    public bool RequiresUserAction => Kind switch
    {
        TransmissionErrorKind.Unauthorized or TransmissionErrorKind.ProxyUnauthorized
            or TransmissionErrorKind.IpNotAllowed or TransmissionErrorKind.HostNotAllowed
            or TransmissionErrorKind.UntrustedCertificate or TransmissionErrorKind.RedirectRefused
            or TransmissionErrorKind.UnsupportedServer or TransmissionErrorKind.InvalidEndpoint
            or TransmissionErrorKind.NotTransmissionRpc => true,
        // A handshake failure in the middle of a working session is a network hiccup; a failure
        // on the first connection is shown either way.
        _ => false,
    };

    /// <summary>
    /// Failures that usually clear up on their own, so trying again later is worthwhile: timeouts
    /// and dropped connections, or a reverse proxy whose daemon is restarting.
    /// </summary>
    public bool IsTransient => Kind switch
    {
        TransmissionErrorKind.Network => true,
        TransmissionErrorKind.HttpStatus => StatusCode is 502 or 503 or 504,
        _ => false,
    };

    internal static TransmissionException InvalidEndpoint(string reason) =>
        new(TransmissionErrorKind.InvalidEndpoint, $"The server address is invalid: {reason}", reason);

    internal static TransmissionException Unauthorized() =>
        new(TransmissionErrorKind.Unauthorized, "The server rejected the username or password.");

    internal static TransmissionException ProxyUnauthorized() =>
        new(TransmissionErrorKind.ProxyUnauthorized, "The proxy rejected the username or password.");

    internal static TransmissionException IpNotAllowed(string serverMessage) =>
        new(
            TransmissionErrorKind.IpNotAllowed,
            "The server refused this computer's IP address. Add it to \"rpc-whitelist\" in the daemon's "
                + "settings.json, or disable the whitelist.",
            serverMessage)
        { StatusCode = 403 };

    internal static TransmissionException HostNotAllowed() =>
        new(
            TransmissionErrorKind.HostNotAllowed,
            "The server doesn't accept this host name. Connect by IP address, or add the host name to "
                + "\"rpc-host-whitelist\" in the daemon's settings.json.")
        { StatusCode = 421 };

    internal static TransmissionException UntrustedCertificate(string fingerprint) =>
        new(
            TransmissionErrorKind.UntrustedCertificate,
            $"The server's TLS certificate isn't trusted (SHA-256 {fingerprint}).")
        { CertificateFingerprint = fingerprint };

    internal static TransmissionException HttpsHandshakeFailed(Exception innerException) =>
        new(
            TransmissionErrorKind.HttpsHandshakeFailed,
            "The server didn't accept a secure (HTTPS) connection. Transmission's built-in server only "
                + "speaks plain HTTP, so turn off HTTPS for this server unless a reverse proxy in front of "
                + "the daemon provides it.",
            innerException: innerException);

    internal static TransmissionException RedirectRefused(string? target) =>
        new(
            TransmissionErrorKind.RedirectRefused,
            "The server tried to redirect the connection to a different address. The request was blocked "
                + "to protect your credentials.",
            target);

    /// <summary>Where a redirect pointed, without its query string, which may carry tokens.</summary>
    internal static string? RedirectTarget(Uri? location) => location switch
    {
        null => null,
        { IsAbsoluteUri: true } => location.GetLeftPart(UriPartial.Path),
        _ => location.OriginalString.Split('?')[0],
    };

    internal static TransmissionException HttpStatusCode(int statusCode) =>
        new(
            TransmissionErrorKind.HttpStatus,
            $"The server responded with HTTP {statusCode}. Check the RPC path and port.")
        { StatusCode = statusCode };

    internal static TransmissionException RpcFailure(string serverMessage) =>
        new(TransmissionErrorKind.RpcFailure, serverMessage, serverMessage);

    internal static TransmissionException UnsupportedServer(string version) =>
        new(
            TransmissionErrorKind.UnsupportedServer,
            $"Transmission {version} is too old. Version 2.40 or newer is required.",
            version);

    internal static TransmissionException InvalidResponse(string details, Exception? innerException = null) =>
        new(
            TransmissionErrorKind.InvalidResponse,
            $"The server sent a response that couldn't be read ({details}).",
            details,
            innerException);

    internal static TransmissionException NotTransmissionRpc() =>
        new(
            TransmissionErrorKind.NotTransmissionRpc,
            "The server answered with a web page instead of Transmission. Check the port and the RPC path, "
                + "usually /transmission/rpc.");

    internal static TransmissionException Network(
        NetworkProblem problem,
        string message,
        Exception? innerException = null) =>
        new(TransmissionErrorKind.Network, message, innerException: innerException) { NetworkProblem = problem };
}
