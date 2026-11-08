using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TransmissionKit;

/// <summary>Everything needed to talk to one daemon.</summary>
public sealed record TransmissionClientOptions(ServerEndpoint Endpoint)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public Credentials? Credentials { get; init; }

    /// <summary>A self-signed reverse proxy's certificate the user chose to trust, as <c>AB:CD:…</c>.</summary>
    public string? PinnedCertificateFingerprint { get; init; }

    public ProtocolPreference ProtocolPreference { get; init; } = ProtocolPreference.Automatic;

    /// <summary>How long to wait for an ordinary call.</summary>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    public ProxyConfiguration? Proxy { get; init; }

    public ClientIdentity? ClientIdentity { get; init; }
}

/// <summary>Talks to one Transmission daemon.</summary>
/// <remarks>
/// Call <see cref="ConnectAsync"/> first: it detects the server version and picks the best
/// protocol. Every call then handles Transmission's CSRF session ID (HTTP 409) transparently.
/// Calls may run concurrently once connected.
/// </remarks>
public sealed partial class TransmissionClient : IDisposable
{
    /// <summary>
    /// How long to wait for calls the daemon answers only after finishing the work: moving data and
    /// deleting it. Large torrents on a NAS can take many minutes.
    /// </summary>
    internal static readonly TimeSpan SlowCallTimeout = TimeSpan.FromMinutes(30);

    private const string SessionIdHeader = "X-Transmission-Session-Id";

    /// <summary>A torrent list with tens of thousands of entries stays far below this.</summary>
    private const long MaxResponseBytes = 256L * 1024 * 1024;

    private const int MaxServerMessageLength = 512;

    private readonly TransmissionClientOptions _options;
    private readonly HttpClient _httpClient;
    private readonly Uri _uri;
    private readonly AuthenticationHeaderValue? _authorization;
    private readonly string? _pinnedFingerprint;
    private readonly ILogger _logger;
    private volatile string? _sessionId;
    private string? _rejectedFingerprint;
    private int _lastTag;

    /// <exception cref="TransmissionException">The endpoint is invalid.</exception>
    public TransmissionClient(TransmissionClientOptions options, ILoggerFactory? loggerFactory = null)
        : this(options, handler: null, loggerFactory?.CreateLogger<TransmissionClient>())
    {
    }

    /// <summary>Lets tests replace the network with a fake handler.</summary>
    internal TransmissionClient(TransmissionClientOptions options, HttpMessageHandler? handler, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _uri = options.Endpoint.ToUri();
        _logger = logger ?? NullLogger.Instance;
        _pinnedFingerprint = options.PinnedCertificateFingerprint is { } pinned
            ? CertificateFingerprint.Normalize(pinned)
            : null;
        _authorization = options.Credentials is { } credentials
            ? new AuthenticationHeaderValue("Basic", credentials.BasicAuthorizationParameter)
            : null;
        _httpClient = new HttpClient(handler ?? CreateHandler(options), disposeHandler: true)
        {
            // Each call sets its own deadline; see PostAsync.
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = MaxResponseBytes,
        };
    }

    public ServerEndpoint Endpoint => _options.Endpoint;

    public RpcDialect Dialect { get; private set; } = RpcDialect.Legacy;

    /// <summary>Set by <see cref="ConnectAsync"/>.</summary>
    public ServerInfo? ServerInfo { get; private set; }

    internal int RpcVersion => ServerInfo?.RpcVersion ?? ServerInfo.MinimumRpcVersion;

    /// <summary>Detects the server version and switches to JSON-RPC when the server supports it.</summary>
    /// <exception cref="TransmissionException">The server can't be reached, rejected us or is too old.</exception>
    public async Task<ServerInfo> ConnectAsync(CancellationToken cancellationToken = default)
    {
        Log.Connecting(_logger, _uri, Endpoint.TransportSecurity, _options.ProtocolPreference);

        // Every Transmission release understands a legacy session-get, so connecting always starts there.
        Dialect = RpcDialect.Legacy;
        var parameters = new JsonObject
        {
            ["fields"] = WireNames(ServerInfo.FieldNames, KeyScope.Session).ToJsonArray(),
        };
        var info = await CallAsync(
                RpcMethod.SessionGet, parameters, KeyScope.Plain, ServerInfo.FromJson, cancellationToken)
            .ConfigureAwait(false);

        if (info.RpcVersion < ServerInfo.MinimumRpcVersion)
        {
            Log.UnsupportedServer(_logger, info.Version, info.RpcVersion, ServerInfo.MinimumRpcVersion);
            throw TransmissionException.UnsupportedServer(info.Version);
        }
        if (_options.ProtocolPreference == ProtocolPreference.Automatic && info.SupportsJsonRpc)
        {
            Dialect = RpcDialect.JsonRpc;
        }
        ServerInfo = info;
        Log.Connected(_logger, info.Version, info.RpcVersion, info.RpcVersionSemver, Dialect.DisplayName());
        return info;
    }

    public void Dispose() => _httpClient.Dispose();

    /// <summary>Converts canonical camelCase names to the server's spelling.</summary>
    internal List<string> WireNames(IEnumerable<string> camelNames, KeyScope scope) =>
        camelNames.Select(name => Dialect.WireName(name, scope)).ToList();

    /// <summary>Sends a request and reads its result with <paramref name="parse"/>.</summary>
    internal async Task<T> CallAsync<T>(
        RpcMethod method,
        JsonObject? parameters,
        KeyScope scope,
        Func<JsonObject, T> parse,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var dialect = Dialect;
        var started = Stopwatch.GetTimestamp();
        var result = await SendAsync(method, parameters, scope, timeout, cancellationToken).ConfigureAwait(false);
        try
        {
            if (result is not JsonObject resultObject)
            {
                throw TransmissionException.InvalidResponse(
                    $"the response to {dialect.WireName(method)} had no result");
            }
            return parse(resultObject);
        }
        catch (TransmissionException error)
        {
            LogFailure(error, dialect.WireName(method), dialect, started);
            throw;
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException or JsonException)
        {
            var wrapped = TransmissionException.InvalidResponse(error.Message, error);
            LogFailure(wrapped, dialect.WireName(method), dialect, started);
            throw wrapped;
        }
    }

    /// <summary>Sends a request whose result isn't needed.</summary>
    internal async Task CallAsync(
        RpcMethod method,
        JsonObject? parameters,
        KeyScope scope,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null) =>
        await SendAsync(method, parameters, scope, timeout, cancellationToken).ConfigureAwait(false);

    private async Task<JsonNode?> SendAsync(
        RpcMethod method,
        JsonObject? parameters,
        KeyScope scope,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var dialect = Dialect;
        var methodName = dialect.WireName(method);
        var body = RpcEnvelope.Encode(dialect, method, parameters, scope, Interlocked.Increment(ref _lastTag));
        var started = Stopwatch.GetTimestamp();
        try
        {
            // Transmission answers the first request, and any request after the session ID expires,
            // with 409 and a fresh ID; the request is repeated once with it.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var response = await PostAsync(body, methodName, timeout ?? _options.Timeout, cancellationToken)
                    .ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    _sessionId = SessionIdFrom(response)
                        ?? throw TransmissionException.InvalidResponse("HTTP 409 without a session ID");
                    Log.SessionIdRenewed(_logger, methodName);
                    continue;
                }

                var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                ThrowIfFailed(response, content, methodName);
                var result = response.StatusCode == HttpStatusCode.NoContent
                    ? null
                    : RpcEnvelope.DecodeResult(content, dialect);
                Log.CallSucceeded(
                    _logger, methodName, dialect.DisplayName(), (int)response.StatusCode, content.Length,
                    ElapsedMilliseconds(started));
                return result;
            }
            throw TransmissionException.InvalidResponse("the server rejected its own session ID");
        }
        catch (TransmissionException error)
        {
            LogFailure(error, methodName, dialect, started);
            throw;
        }
    }

    private async Task<HttpResponseMessage> PostAsync(
        byte[] body,
        string methodName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            try
            {
                return await SendOnceAsync(body, deadline.Token).ConfigureAwait(false);
            }
            catch (HttpRequestException error) when (error.HttpRequestError == HttpRequestError.ResponseEnded)
            {
                // Proxies and NAS web servers close idle keep-alive connections, often after 5 seconds,
                // the same as the refresh interval, and a request can cross paths with the close.
                // Transmission's calls are safe to repeat (adding a torrent twice reports a duplicate),
                // so one retry on a fresh connection is fine.
                Log.RetryingDroppedConnection(_logger, error, methodName);
                return await SendOnceAsync(body, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw TransmissionException.Network(
                NetworkProblem.Timeout,
                $"The server didn't answer within {timeout.TotalSeconds:0} seconds.",
                error);
        }
        catch (HttpRequestException error)
        {
            throw TranslateNetworkError(error);
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(byte[] body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _uri);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization = _authorization;
        if (_sessionId is { } sessionId)
        {
            request.Headers.TryAddWithoutValidation(SessionIdHeader, sessionId);
        }
        return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private void ThrowIfFailed(HttpResponseMessage response, byte[] content, string methodName)
    {
        var statusCode = (int)response.StatusCode;
        switch (statusCode)
        {
            case >= 200 and < 300:
                return;
            case >= 300 and < 400:
                var target = TransmissionException.RedirectTarget(response.Headers.Location);
                Log.RedirectRefused(_logger, methodName, target);
                throw TransmissionException.RedirectRefused(target);
            case 401:
                throw TransmissionException.Unauthorized();
            case 403:
                var length = Math.Min(content.Length, MaxServerMessageLength);
                throw TransmissionException.IpNotAllowed(Encoding.UTF8.GetString(content, 0, length));
            case 407:
                throw TransmissionException.ProxyUnauthorized();
            case 421:
                throw TransmissionException.HostNotAllowed();
            default:
                throw TransmissionException.HttpStatusCode(statusCode);
        }
    }

    private TransmissionException TranslateNetworkError(HttpRequestException error)
    {
        if (Interlocked.Exchange(ref _rejectedFingerprint, null) is { } fingerprint)
        {
            return TransmissionException.UntrustedCertificate(fingerprint);
        }
        // .NET only says the SSL connection couldn't be established; name the usual cause instead.
        if (Endpoint.UsesTls && error.HttpRequestError == HttpRequestError.SecureConnectionError)
        {
            return TransmissionException.HttpsHandshakeFailed(error);
        }

        // The inner exception carries the operating system's reason, such as "Connection refused".
        var reason = error.InnerException?.Message is { Length: > 0 } inner ? $" ({inner})" : "";
        return error.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => TransmissionException.Network(
                NetworkProblem.NameNotResolved,
                $"The server name \"{Endpoint.NormalizedHost}\" couldn't be found.",
                error),
            HttpRequestError.ConnectionError => TransmissionException.Network(
                NetworkProblem.CannotConnect, $"Couldn't connect to the server{reason}.", error),
            HttpRequestError.ProxyTunnelError => TransmissionException.Network(
                NetworkProblem.ProxyFailed, $"The proxy couldn't connect to the server{reason}.", error),
            HttpRequestError.ResponseEnded => TransmissionException.Network(
                NetworkProblem.ConnectionLost, "The connection closed before the server answered.", error),
            _ => TransmissionException.Network(NetworkProblem.Other, error.Message, error),
        };
    }

    private SocketsHttpHandler CreateHandler(TransmissionClientOptions options) => new()
    {
        // Every request carries the Basic auth header; following a redirect could hand it to another host.
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectTimeout = options.Timeout,
        AutomaticDecompression = DecompressionMethods.All,
        // Without a proxy of its own, the server is reached like any other app would: through the
        // system's proxy settings, which normally leave local addresses alone.
        UseProxy = true,
        Proxy = options.Proxy?.CreateWebProxy(),
        SslOptions = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            RemoteCertificateValidationCallback = ValidateServerCertificate,
            ClientCertificateContext = options.ClientIdentity?.CertificateContext,
        },
    };

    /// <summary>
    /// Server certificates must be trusted by Windows or match the fingerprint the user pinned: the
    /// safe alternative to "ignore certificate errors" for self-signed reverse proxies.
    /// </summary>
    private bool ValidateServerCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors policyErrors)
    {
        if (policyErrors == SslPolicyErrors.None)
        {
            return true;
        }
        if (certificate is null)
        {
            return false;
        }

        var fingerprint = CertificateFingerprint.Sha256(certificate);
        if (fingerprint == _pinnedFingerprint)
        {
            Log.PinnedCertificateAccepted(_logger, fingerprint);
            return true;
        }
        // Kept so the error can offer to trust this certificate.
        Volatile.Write(ref _rejectedFingerprint, fingerprint);
        Log.CertificateRejected(_logger, certificate.Subject, fingerprint, policyErrors);
        return false;
    }

    private void LogFailure(TransmissionException error, string methodName, RpcDialect dialect, long started) =>
        Log.CallFailed(
            _logger, error, methodName, dialect.DisplayName(), ElapsedMilliseconds(started),
            error.Kind, error.NetworkProblem, error.StatusCode, error.Detail);

    private static string? SessionIdFrom(HttpResponseMessage response) =>
        response.Headers.TryGetValues(SessionIdHeader, out var values) ? values.FirstOrDefault() : null;

    private static long ElapsedMilliseconds(long started) =>
        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
