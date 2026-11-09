using TransmissionKit;

namespace TransmissionRemote.ViewModels.Tests;

[TestClass]
public sealed class StatusBarViewModelTests
{
    [TestMethod]
    public void TheAlternativeLimitWinsWhileItIsOn()
    {
        var session = new SessionSettings
        {
            SpeedLimitDown = 500,
            SpeedLimitDownEnabled = true,
            SpeedLimitUp = 100,
            SpeedLimitUpEnabled = false,
            AltSpeedDown = 50,
            AltSpeedUp = 20,
            AltSpeedEnabled = false,
        };

        Assert.AreEqual(500, StatusBarViewModel.ActiveLimit(session, TransferDirection.Download));
        Assert.IsNull(StatusBarViewModel.ActiveLimit(session, TransferDirection.Upload), "turned off");
        Assert.AreEqual(50, StatusBarViewModel.ActiveLimit(session with { AltSpeedEnabled = true }, TransferDirection.Download));
        Assert.AreEqual(20, StatusBarViewModel.ActiveLimit(session with { AltSpeedEnabled = true }, TransferDirection.Upload));
        Assert.IsNull(StatusBarViewModel.ActiveLimit(null, TransferDirection.Download));
    }

    [TestMethod]
    public async Task ItShowsWhatTheServerReported()
    {
        using var app = new TestApp(daemon =>
        {
            daemon.AddTorrent(1, "a");
            daemon.DownloadLimitEnabled = true;
        });

        await app.ConnectAsync(expectedTorrents: 1);
        var bar = app.Main.StatusBar;

        Assert.AreEqual(1, bar.TorrentCount);
        Assert.AreEqual(TransportSecurity.Loopback, bar.Security);
        Assert.AreEqual(2000, bar.DownloadSpeed);
        Assert.AreEqual(50, bar.UploadSpeed);
        Assert.AreEqual(100, bar.DownloadLimit);
        Assert.IsNull(bar.UploadLimit);
        Assert.AreEqual(123_456_789, bar.FreeSpace?.SizeBytes);
        Assert.AreEqual("3.00", bar.ServerVersion);
        Assert.AreEqual(RpcDialect.Legacy, bar.Dialect);
    }

    [TestMethod]
    public async Task APollingProblemSaysHowOldTheDataIsEveryTenSeconds()
    {
        using var app = new TestApp(daemon => daemon.AddTorrent(1, "a"));
        await app.ConnectAsync(expectedTorrents: 1);
        var bar = app.Main.StatusBar;
        Assert.AreEqual(TimeSpan.Zero, bar.DataAge);

        app.Daemon.UnreachableRequests = 100;
        app.Time.Advance(TimeSpan.FromSeconds(5));
        await WaitUntil(() => bar.PollingProblem is not null);
        Assert.AreEqual(TimeSpan.FromSeconds(5), bar.DataAge);

        app.Time.Advance(StatusBarViewModel.AgeRefreshInterval);
        await WaitUntil(() => bar.DataAge == TimeSpan.FromSeconds(15));

        app.Daemon.UnreachableRequests = 0;
        app.Connection.RetryNow();
        await WaitUntil(() => bar.PollingProblem is null);
        Assert.AreEqual(TimeSpan.Zero, bar.DataAge);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
        Assert.IsTrue(condition(), "timed out");
    }
}
