using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace TransmissionKit.Tests;

/// <summary>A request as the fake network saw it.</summary>
internal sealed record RecordedRequest(string Body, string? Authorization, string? SessionId);

/// <summary>Serves canned HTTP responses so the client can be tested without a daemon.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    public const string SessionIdHeader = "X-Transmission-Session-Id";

    private readonly Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> _respond;
    private readonly List<RecordedRequest> _requests = [];

    public FakeHttpHandler(Func<RecordedRequest, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public FakeHttpHandler(Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public static HttpResponseMessage Reply(HttpStatusCode status, string body = "", string? sessionId = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) };
        if (sessionId is not null)
        {
            response.Headers.Add(SessionIdHeader, sessionId);
        }
        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(
            body,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues(SessionIdHeader, out var values) ? values.First() : null);
        lock (_requests)
        {
            _requests.Add(recorded);
        }
        return await _respond(recorded, cancellationToken);
    }
}

/// <summary>Keeps every log entry so tests can check what was logged, and what wasn't.</summary>
internal sealed class CapturingLogger : ILogger
{
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add(new LogEntry(logLevel, eventId.Id, formatter(state, exception), exception));
        }
    }
}

internal sealed record LogEntry(LogLevel Level, int EventId, string Message, Exception? Exception);

internal static class Json
{
    /// <summary>Parses JSON and converts its keys to camelCase, as the client does with answers.</summary>
    public static JsonObject Normalized(string json) =>
        (JsonObject)RpcEnvelope.NormalizeKeys(JsonNode.Parse(json))!;

    /// <summary>Unwraps a whole response body the way the client does.</summary>
    public static JsonObject Result(string body, RpcDialect dialect) =>
        (JsonObject)RpcEnvelope.DecodeResult(Encoding.UTF8.GetBytes(body), dialect)!;

    /// <summary>Encodes a request and parses it back, to look at what goes over the wire.</summary>
    public static JsonObject Encoded(RpcDialect dialect, RpcMethod method, JsonObject parameters, KeyScope scope) =>
        JsonNode.Parse(RpcEnvelope.Encode(dialect, method, parameters, scope, tag: 7))!.AsObject();
}
