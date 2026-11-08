using System.Net.Security;
using Microsoft.Extensions.Logging;

namespace TransmissionKit;

/// <summary>
/// Every log message the core writes, generated at compile time by <see cref="LoggerMessageAttribute"/>.
/// </summary>
/// <remarks>
/// Event IDs are stable so a log can be searched for one kind of event: 1000s for connecting,
/// 1100s for RPC calls, 1200s for TLS. Never log passwords, the Authorization header or request
/// bodies (they can hold whole .torrent files); log the method, the outcome and how long it took.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(1000, LogLevel.Information,
        "Connecting to {Endpoint} (security: {Security}, protocol preference: {Preference})")]
    public static partial void Connecting(
        ILogger logger, Uri endpoint, TransportSecurity security, ProtocolPreference preference);

    [LoggerMessage(1001, LogLevel.Information,
        "Connected to Transmission {Version} (RPC version {RpcVersion}, semver {RpcSemver}) using {Dialect}")]
    public static partial void Connected(
        ILogger logger, string version, int rpcVersion, string? rpcSemver, string dialect);

    [LoggerMessage(1002, LogLevel.Warning,
        "Transmission {Version} (RPC version {RpcVersion}) is older than the supported minimum, RPC version {Minimum}")]
    public static partial void UnsupportedServer(ILogger logger, string version, int rpcVersion, int minimum);

    [LoggerMessage(1100, LogLevel.Debug,
        "{Method} ({Dialect}) succeeded: HTTP {StatusCode}, {Bytes} bytes in {ElapsedMs} ms")]
    public static partial void CallSucceeded(
        ILogger logger, string method, string dialect, int statusCode, int bytes, long elapsedMs);

    [LoggerMessage(1101, LogLevel.Warning,
        "{Method} ({Dialect}) failed after {ElapsedMs} ms: {Kind} {NetworkProblem} {StatusCode} {Detail}")]
    public static partial void CallFailed(
        ILogger logger,
        Exception exception,
        string method,
        string dialect,
        long elapsedMs,
        TransmissionErrorKind kind,
        NetworkProblem? networkProblem,
        int? statusCode,
        string? detail);

    [LoggerMessage(1102, LogLevel.Debug, "The server issued a new session ID (HTTP 409); repeating {Method}")]
    public static partial void SessionIdRenewed(ILogger logger, string method);

    [LoggerMessage(1103, LogLevel.Information,
        "The connection was closed while waiting for {Method}; retrying once on a new connection")]
    public static partial void RetryingDroppedConnection(ILogger logger, Exception exception, string method);

    [LoggerMessage(1104, LogLevel.Warning, "The server tried to redirect {Method} to {Location}; refused")]
    public static partial void RedirectRefused(ILogger logger, string method, string? location);

    [LoggerMessage(1200, LogLevel.Warning,
        "Rejected the server's TLS certificate {Subject} (SHA-256 {Fingerprint}): {PolicyErrors}")]
    public static partial void CertificateRejected(
        ILogger logger, string subject, string fingerprint, SslPolicyErrors policyErrors);

    [LoggerMessage(1201, LogLevel.Debug,
        "Accepted the server's untrusted TLS certificate because it matches the pinned fingerprint {Fingerprint}")]
    public static partial void PinnedCertificateAccepted(ILogger logger, string fingerprint);
}
