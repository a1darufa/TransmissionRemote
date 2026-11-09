using System.Net;
using Microsoft.Extensions.Time.Testing;
using TransmissionKit;

namespace TransmissionRemote.ViewModels.Tests;

[TestClass]
/// <remarks>MSTest disposes each test's instance after the test, which cleans up.</remarks>
public sealed class ConnectionStateViewModelTests : IDisposable
{
    private string _directory = null!;
    private FakeDaemon _daemon = null!;
    private FakeTimeProvider _time = null!;
    private ServerStore _servers = null!;
    private ServerConnection _connection = null!;
    private ConnectionStateViewModel _state = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "ConnectionStateViewModelTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var environment = new AppEnvironment(_directory, "1.0");
        _daemon = new FakeDaemon();
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
        _servers = new ServerStore(environment, new FakeCredentialStore());
        _connection = new ServerConnection(
            new SettingsStore(environment), new NoClientCertificates(), new FakeNotifications(), null, _time,
            _daemon.CreateClient);
        _state = new ConnectionStateViewModel(_connection, _servers, _time);
    }

    public void Dispose()
    {
        _state.Dispose();
        _connection.Dispose();
        _daemon.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public void WithoutServersItOffersToAddOneAndOnceThereIsOneToConnect()
    {
        Assert.AreEqual(ConnectionPage.NoServer, _state.Page);

        _servers.Save(Profile());

        Assert.AreEqual(ConnectionPage.NotConnected, _state.Page);
    }

    [TestMethod]
    public async Task ConnectingNamesTheServerAndEndsInTheList()
    {
        var pages = new List<ConnectionPage>();
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectionStateViewModel.Page))
            {
                pages.Add(_state.Page);
            }
        };

        _connection.Connect(Profile(), password: null);
        await WaitUntil(() => _state.Page == ConnectionPage.Connected);

        Assert.AreEqual(ConnectionPage.Connecting, pages[0]);
        Assert.AreEqual("Test", _state.ServerName);
    }

    [TestMethod]
    public async Task AnUnreachableServerCountsDownToTheNextAttempt()
    {
        _daemon.UnreachableRequests = 1;

        _connection.Connect(Profile(), password: null);
        await WaitUntil(() => _state.RetryCountdown > TimeSpan.Zero);

        Assert.AreEqual(ConnectionPage.Reconnecting, _state.Page);
        Assert.AreEqual(TimeSpan.FromSeconds(2), _state.RetryCountdown);
        Assert.IsTrue(_state.CanRetryNow);
        Assert.AreEqual(NetworkProblem.CannotConnect, (_state.Error as TransmissionException)?.NetworkProblem);

        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntil(() => _state.RetryCountdown == TimeSpan.FromSeconds(1));

        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntil(() => _state.Page == ConnectionPage.Connected);
        Assert.AreEqual(TimeSpan.Zero, _state.RetryCountdown);
        Assert.IsNull(_state.Error);
    }

    [TestMethod]
    public async Task APartSecondStillShowsAsAWholeOne()
    {
        _daemon.UnreachableRequests = 1;
        _connection.Connect(Profile(), password: null);
        await WaitUntil(() => _state.RetryCountdown > TimeSpan.Zero);

        _time.Advance(TimeSpan.FromMilliseconds(1500));
        await Task.Delay(50);

        Assert.AreEqual(TimeSpan.FromSeconds(1), _state.RetryCountdown, "0.5 s left reads 0:01, not 0:00");
    }

    [TestMethod]
    public async Task AWrongPasswordIsACouldNotConnectPageWithItsError()
    {
        _daemon.FailWith = HttpStatusCode.Unauthorized;

        _connection.Connect(Profile(), password: "wrong");
        await WaitUntil(() => _state.Page == ConnectionPage.CouldNotConnect);

        Assert.AreEqual(TransmissionErrorKind.Unauthorized, (_state.Error as TransmissionException)?.Kind);
        Assert.IsNull(_state.CertificateFingerprint);
        Assert.IsFalse(_state.CanRetryNow);
    }

    [TestMethod]
    public void PlainHttpOverTheInternetIsBlockedWithoutAnError()
    {
        _connection.Connect(Profile() with { Endpoint = new ServerEndpoint("203.0.113.10") }, password: null);

        Assert.AreEqual(ConnectionPage.UnencryptedConnectionBlocked, _state.Page);
        Assert.IsNull(_state.Error);
        Assert.IsEmpty(_daemon.Methods, "a documentation address counts as the internet, so nothing is sent");
    }

    [TestMethod]
    public void StoppingAfterAFailureShowsNotConnected()
    {
        _servers.Save(Profile());
        _connection.Connect(Profile() with { Endpoint = new ServerEndpoint("203.0.113.10") }, password: null);

        _connection.Disconnect();

        Assert.AreEqual(ConnectionPage.NotConnected, _state.Page);
        Assert.IsNull(_state.Error);
    }

    private static ServerProfile Profile() => new() { Name = "Test", Endpoint = new ServerEndpoint("127.0.0.1", 19091) };

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
        Assert.IsTrue(condition(), "timed out");
    }
}
