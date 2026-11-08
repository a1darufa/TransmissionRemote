using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Time.Testing;

namespace TransmissionRemote.ViewModels.Tests;

/// <summary>Peer countries from a small gzipped CSV made here, and host names from a fake reverse DNS.</summary>
[TestClass]
public sealed class PeerInfoServiceTests
{
    private const string Csv = """
        1.0.0.0,1.0.0.255,AU
        1.0.8.0,1.0.15.255,DE
        2001:200::,2001:200:ffff:ffff:ffff:ffff:ffff:ffff,JP
        """;

    private const string NovemberUrl = "https://download.db-ip.com/free/dbip-country-lite-2026-11.csv.gz";
    private const string OctoberUrl = "https://download.db-ip.com/free/dbip-country-lite-2026-10.csv.gz";

    private static readonly byte[] Database = Gzipped(Csv);

    private string _directory = null!;
    private FakeTimeProvider _time = null!;
    private SettingsStore _settings = null!;

    [TestInitialize]
    public void CreateSettings()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PeerInfoServiceTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
        _settings = new SettingsStore(Environment());
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public async Task DownloadsThisMonthsDatabaseAndSavesIt()
    {
        var handler = Serving(new() { [NovemberUrl] = Database });
        var service = Service(handler);
        var states = RecordStates(service);

        await service.DownloadDatabaseAsync();

        CollectionAssert.AreEqual(
            new[] { CountryDatabaseState.Downloading, CountryDatabaseState.Ready }, states.ToArray());
        Assert.AreEqual("AU", service.CountryCodeFor("1.0.0.1"));
        Assert.AreEqual("JP", service.CountryCodeFor("2001:200::1"));
        Assert.AreEqual("2026-11", service.DatabaseMonth);
        Assert.AreEqual("2026-11", _settings.Current.CountryDatabaseMonth);
        CollectionAssert.AreEqual(Database, File.ReadAllBytes(SavedDatabasePath));
        Assert.StartsWith("TransmissionRemote/", handler.UserAgents.Single());
    }

    [TestMethod]
    public async Task FallsBackToLastMonthBeforeThisMonthsFileIsOut()
    {
        var handler = Serving(new() { [OctoberUrl] = Database });
        var service = Service(handler);

        await service.DownloadDatabaseAsync();

        Assert.AreEqual(CountryDatabaseState.Ready, service.DatabaseState);
        Assert.AreEqual("2026-10", service.DatabaseMonth);
        CollectionAssert.AreEqual(
            new[] { NovemberUrl, OctoberUrl }, handler.RequestedUrls.Select(url => url.AbsoluteUri).ToArray());
    }

    [TestMethod]
    public async Task AFailedFirstDownloadSaysWhy()
    {
        var notPublished = Service(Serving([]));
        var offline = Service(new StubHttpMessageHandler(_ => throw new HttpRequestException("No route")));
        var damaged = Service(Serving(new() { [NovemberUrl] = Database[..^10] }));
        var notADatabase = Service(Serving(new() { [NovemberUrl] = Gzipped("<html>Moved</html>") }));

        await notPublished.DownloadDatabaseAsync();
        await offline.DownloadDatabaseAsync();
        await damaged.DownloadDatabaseAsync();
        await notADatabase.DownloadDatabaseAsync();

        Assert.AreEqual(CountryDatabaseState.Failed, notPublished.DatabaseState);
        Assert.AreEqual(CountryDatabaseFailure.ServerError, notPublished.DatabaseFailure);
        Assert.AreEqual(CountryDatabaseFailure.Network, offline.DatabaseFailure);
        Assert.AreEqual(CountryDatabaseFailure.Unreadable, damaged.DatabaseFailure);
        Assert.AreEqual(CountryDatabaseFailure.Unreadable, notADatabase.DatabaseFailure);
        Assert.IsFalse(File.Exists(SavedDatabasePath));
        Assert.IsNull(_settings.Current.CountryDatabaseMonth);
    }

    [TestMethod]
    public async Task LoadsTheSavedDatabaseWithoutDownloading()
    {
        SaveDatabase(Database, month: "2026-11");
        var handler = Serving([]);
        var service = Service(handler);

        await service.PrepareCountriesAsync();

        Assert.AreEqual(CountryDatabaseState.Ready, service.DatabaseState);
        Assert.AreEqual("DE", service.CountryCodeFor("1.0.9.9"));
        Assert.IsEmpty(handler.RequestedUrls);
    }

    [TestMethod]
    public async Task ADamagedSavedDatabaseIsReported()
    {
        SaveDatabase([1, 2, 3], month: "2026-11");
        var service = Service(Serving([]));

        await service.PrepareCountriesAsync();

        Assert.AreEqual(CountryDatabaseState.Failed, service.DatabaseState);
        Assert.AreEqual(CountryDatabaseFailure.Unreadable, service.DatabaseFailure);
    }

    [TestMethod]
    public async Task WithoutADatabasePreparingDownloadsNothing()
    {
        var handler = Serving(new() { [NovemberUrl] = Database });
        var service = Service(handler);

        await service.PrepareCountriesAsync();

        Assert.AreEqual(CountryDatabaseState.NotDownloaded, service.DatabaseState, "the user asks for it first");
        Assert.IsEmpty(handler.RequestedUrls);
    }

    [TestMethod]
    public async Task RefreshesAnOutdatedDatabaseAndKeepsItWhenThatFails()
    {
        SaveDatabase(Database, month: "2026-10");
        var handler = Serving([]);
        var service = Service(handler);

        await service.PrepareCountriesAsync();

        Assert.HasCount(2, handler.RequestedUrls, "tried November, then October");
        Assert.AreEqual(CountryDatabaseState.Ready, service.DatabaseState);
        Assert.IsNull(service.DatabaseFailure);
        Assert.AreEqual("AU", service.CountryCodeFor("1.0.0.1"));
        Assert.AreEqual("2026-10", service.DatabaseMonth);
    }

    [TestMethod]
    public async Task RetriesAFailedRefreshOnlyOnceADay()
    {
        SaveDatabase(Database, month: "2026-10");
        var handler = Serving([]);
        var service = Service(handler);
        await service.PrepareCountriesAsync();

        _time.Advance(TimeSpan.FromHours(23));
        await service.PrepareCountriesAsync();
        Assert.HasCount(2, handler.RequestedUrls);

        _time.Advance(TimeSpan.FromHours(2));
        await service.PrepareCountriesAsync();
        Assert.HasCount(4, handler.RequestedUrls);
    }

    [TestMethod]
    public void ResolvesHostNamesOnceAndAnnouncesThem()
    {
        var lookups = new List<IPAddress>();
        var service = Service(Serving([]), address =>
        {
            lookups.Add(address);
            return Task.FromResult<string?>(address.ToString() == "192.0.2.1" ? "peer.example.net" : null);
        });
        var announced = new List<HostNameResolvedEventArgs>();
        service.HostNameResolved += (_, change) => announced.Add(change);

        service.ResolveHostNames(["192.0.2.1", "192.0.2.2"]);
        service.ResolveHostNames(["192.0.2.1", "192.0.2.2"]);

        Assert.AreEqual("peer.example.net", service.HostNameFor("192.0.2.1"));
        Assert.IsNull(service.HostNameFor("192.0.2.2"));
        Assert.HasCount(2, lookups, "addresses already looked up, with or without a name, aren't looked up again");
        Assert.AreEqual("192.0.2.1", announced.Single().Address);
        Assert.AreEqual("peer.example.net", announced.Single().HostName);
    }

    [TestMethod]
    public void NeverLooksUpAHostNameOrKeepsAnAnswerThatIsJustTheAddress()
    {
        var lookups = 0;
        var service = Service(Serving([]), address =>
        {
            lookups++;
            return Task.FromResult<string?>(address.ToString());
        });

        service.ResolveHostNames(["tracker.example.com", "198.51.100.7"]);

        Assert.AreEqual(1, lookups, "a host name would start a forward lookup");
        Assert.IsNull(service.HostNameFor("198.51.100.7"));
    }

    [TestMethod]
    public void TreatsDnsErrorsAsNoName()
    {
        var service = Service(Serving([]), _ => throw new SocketException((int)SocketError.HostNotFound));

        service.ResolveHostNames(["203.0.113.5"]);

        Assert.IsNull(service.HostNameFor("203.0.113.5"));
    }

    [TestMethod]
    public async Task RunsAtMostFourLookupsAtOnce()
    {
        var pending = new List<TaskCompletionSource<string?>>();
        var service = Service(Serving([]), _ =>
        {
            var lookup = new TaskCompletionSource<string?>();
            lock (pending)
            {
                pending.Add(lookup);
            }
            return lookup.Task;
        });
        int Started()
        {
            lock (pending)
            {
                return pending.Count;
            }
        }

        service.ResolveHostNames(Enumerable.Range(1, 10).Select(index => $"192.0.2.{index}"));
        Assert.AreEqual(4, Started());

        pending[0].SetResult("one.example.net");
        await Eventually.AssertAsync(() => Started() == 5, "the next lookup starts when one finishes");
        Assert.AreEqual("one.example.net", service.HostNameFor("192.0.2.1"));

        // Finishing a lookup can start the next one right away, so finish a copy of the list each time.
        await Eventually.AssertAsync(
            () =>
            {
                List<TaskCompletionSource<string?>> started;
                lock (pending)
                {
                    started = [.. pending];
                }
                started.ForEach(lookup => lookup.TrySetResult(null));
                return Started() == 10;
            },
            "every queued address is looked up");
    }

    [TestMethod]
    public void ForgetsWhatItLearnedPastTheCacheLimit()
    {
        var lookups = 0;
        var service = Service(Serving([]), address =>
        {
            lookups++;
            return Task.FromResult<string?>($"host-{address}.example.net");
        });
        var addresses = Enumerable.Range(0, 5_001).Select(index => $"10.0.{index / 256}.{index % 256}").ToList();

        service.ResolveHostNames(addresses);
        Assert.AreEqual("host-10.0.0.0.example.net", service.HostNameFor("10.0.0.0"));

        service.ResolveHostNames(["10.0.0.0"]);

        Assert.AreEqual(5_002, lookups, "the cache was cleared, so the address is looked up again");
    }

    private string SavedDatabasePath => Path.Combine(_directory, "GeoIP", "dbip-country-lite.csv.gz");

    private void SaveDatabase(byte[] contents, string month)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SavedDatabasePath)!);
        File.WriteAllBytes(SavedDatabasePath, contents);
        _settings.Update(settings => settings with { CountryDatabaseMonth = month });
    }

    private PeerInfoService Service(
        StubHttpMessageHandler handler, Func<IPAddress, Task<string?>>? reverseLookup = null) =>
        new(_settings, Environment(), new HttpClient(handler),
            reverseLookup ?? (_ => Task.FromResult<string?>(null)), time: _time);

    private AppEnvironment Environment() => new(_directory, "1.0");

    private static StubHttpMessageHandler Serving(Dictionary<string, byte[]> files) => new(request =>
        files.TryGetValue(request.RequestUri!.AbsoluteUri, out var file)
            ? StubHttpMessageHandler.Answer(file)
            : StubHttpMessageHandler.Answer("Not Found", HttpStatusCode.NotFound));

    private static List<CountryDatabaseState> RecordStates(PeerInfoService service)
    {
        var states = new List<CountryDatabaseState>();
        service.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(PeerInfoService.DatabaseState))
            {
                states.Add(service.DatabaseState);
            }
        };
        return states;
    }

    private static byte[] Gzipped(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }
        return output.ToArray();
    }
}
