using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using TransmissionKit;

namespace TransmissionRemote.ViewModels.Tests;

/// <summary>
/// A tiny Transmission 3.00 stand-in behind HttpClient: CSRF handshake, session, stats and torrents, enough
/// to drive <see cref="ServerConnection"/> without a real daemon.
/// </summary>
internal sealed class FakeDaemon : HttpMessageHandler
{
    private const string SessionIdHeader = "X-Transmission-Session-Id";
    private const string SessionId = "fake-session";
    private readonly Lock _lock = new();
    private readonly List<string> _methods = [];

    /// <summary>Torrents in the legacy spelling, keyed by id.</summary>
    public Dictionary<int, JsonObject> Torrents { get; } = [];

    /// <summary>Which torrents count as recently changed; null means all of them.</summary>
    public HashSet<int>? RecentlyChanged { get; set; }

    public List<int> RecentlyRemoved { get; } = [];

    /// <summary>Answer every request with this status instead, such as 401.</summary>
    public HttpStatusCode? FailWith { get; set; }

    /// <summary>Fail this many requests with a connection error first.</summary>
    public int UnreachableRequests { get; set; }

    /// <summary>Make RPC calls other than reads fail with this message.</summary>
    public string? ActionFailure { get; set; }

    /// <summary>The arguments of the latest call to each method.</summary>
    public Dictionary<string, JsonObject> LastArguments { get; } = [];

    /// <summary>Whether the session's 100 kB/s download limit is on.</summary>
    public bool DownloadLimitEnabled { get; set; }

    /// <summary>The name torrent-add reports for a newly added torrent.</summary>
    public string AddedName { get; set; } = "added";

    public IReadOnlyList<string> Methods
    {
        get
        {
            lock (_lock)
            {
                return [.. _methods];
            }
        }
    }

    public TransmissionClient CreateClient(TransmissionClientOptions options) => new(options, this, logger: null);

    public void AddTorrent(int id, string name, double percentDone = 0.5, int status = 4) =>
        Torrents[id] = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["status"] = status,
            ["percentDone"] = percentDone,
            ["leftUntilDone"] = percentDone >= 1 ? 0 : 1000,
            ["downloadDir"] = "/data/downloads",
            ["rateDownload"] = 1000,
        };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (UnreachableRequests > 0)
        {
            UnreachableRequests--;
            throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused");
        }
        if (FailWith is { } status)
        {
            return new HttpResponseMessage(status);
        }
        if (!request.Headers.TryGetValues(SessionIdHeader, out var values) || values.First() != SessionId)
        {
            var conflict = new HttpResponseMessage(HttpStatusCode.Conflict);
            conflict.Headers.Add(SessionIdHeader, SessionId);
            return conflict;
        }

        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
        var method = (string)body["method"]!;
        var arguments = body["arguments"]?.AsObject() ?? [];
        lock (_lock)
        {
            _methods.Add(method);
            LastArguments[method] = arguments;
        }
        return Answer(method, arguments);
    }

    private HttpResponseMessage Answer(string method, JsonObject arguments)
    {
        JsonObject result;
        switch (method)
        {
            case "session-get":
                result = new JsonObject
                {
                    ["version"] = "3.00",
                    ["rpc-version"] = 16,
                    ["download-dir"] = "/data/downloads",
                    ["speed-limit-down"] = 100,
                    ["speed-limit-down-enabled"] = DownloadLimitEnabled,
                };
                break;
            case "session-stats":
                result = new JsonObject { ["torrentCount"] = Torrents.Count, ["downloadSpeed"] = 2000, ["uploadSpeed"] = 50 };
                break;
            case "free-space":
                result = new JsonObject { ["path"] = arguments["path"]?.DeepClone(), ["size-bytes"] = 123_456_789 };
                break;
            case "torrent-get":
                result = TorrentGet(arguments);
                break;
            case "torrent-add":
                result = new JsonObject
                {
                    ["torrent-added"] = new JsonObject { ["id"] = 100, ["name"] = AddedName, ["hashString"] = "abc" },
                };
                break;
            default:
                if (ActionFailure is { } failure)
                {
                    return Json(new JsonObject { ["result"] = failure });
                }
                result = [];
                break;
        }
        return Json(new JsonObject { ["result"] = "success", ["arguments"] = result });
    }

    private JsonObject TorrentGet(JsonObject arguments)
    {
        IEnumerable<JsonObject> torrents = Torrents.Values;
        var result = new JsonObject();
        switch (arguments["ids"])
        {
            case JsonValue value when (string?)value == "recently-active":
                torrents = torrents.Where(torrent => RecentlyChanged?.Contains((int)torrent["id"]!) ?? true);
                result["removed"] = new JsonArray(RecentlyRemoved.Select(id => (JsonNode?)id).ToArray());
                break;
            case JsonArray ids:
                var wanted = ids.Select(id => (int)id!).ToHashSet();
                torrents = torrents.Where(torrent => wanted.Contains((int)torrent["id"]!));
                break;
        }
        result["torrents"] = new JsonArray(torrents.Select(torrent => (JsonNode?)torrent.DeepClone()).ToArray());
        return result;
    }

    private static HttpResponseMessage Json(JsonObject body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
}

internal sealed class FakeNotifications : INotificationService
{
    public List<string> Completed { get; } = [];

    public void ShowDownloadComplete(Torrent torrent) => Completed.Add(torrent.Name);
}

internal sealed class NoClientCertificates : IClientCertificateProvider
{
    public ClientIdentity? IdentityFor(ServerProfile profile) => null;
}
