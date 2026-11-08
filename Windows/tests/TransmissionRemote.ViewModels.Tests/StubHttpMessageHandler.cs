using System.Net;

namespace TransmissionRemote.ViewModels.Tests;

/// <summary>Answers HTTP requests in memory, so tests never touch the network.</summary>
internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private readonly List<Uri> _requestedUrls = [];
    private readonly List<string> _userAgents = [];

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public IReadOnlyList<Uri> RequestedUrls
    {
        get
        {
            lock (_requestedUrls)
            {
                return [.. _requestedUrls];
            }
        }
    }

    public IReadOnlyList<string> UserAgents
    {
        get
        {
            lock (_requestedUrls)
            {
                return [.. _userAgents];
            }
        }
    }

    public static HttpResponseMessage Answer(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body) };

    public static HttpResponseMessage Answer(byte[] body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new ByteArrayContent(body) };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_requestedUrls)
        {
            _requestedUrls.Add(request.RequestUri!);
            _userAgents.Add(request.Headers.UserAgent.ToString());
        }
        return respond(request, cancellationToken);
    }
}

internal static class Eventually
{
    /// <summary>Waits for work started on another thread, such as a timer's, to reach a state.</summary>
    public static async Task AssertAsync(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail(message);
            }
            await Task.Delay(10);
        }
    }
}
