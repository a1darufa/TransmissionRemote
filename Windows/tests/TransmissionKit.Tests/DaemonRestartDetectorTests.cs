using System.Diagnostics;

namespace TransmissionKit.Tests;

[TestClass]
public sealed class DaemonRestartDetectorTests
{
    private static readonly long Start = Stopwatch.GetTimestamp();

    [TestMethod]
    public void TheFirstStatsAreNoRestart() =>
        Assert.IsFalse(new DaemonRestartDetector().Observe(Uptime(3), At(0), At(0)));

    [TestMethod]
    public void AnUptimeThatKeepsUpWithTheClockIsNoRestart()
    {
        var detector = new DaemonRestartDetector();
        detector.Observe(Uptime(1000), At(0), At(0));

        Assert.IsFalse(detector.Observe(Uptime(1005), At(5), At(5)));
    }

    [TestMethod]
    public void AnUptimeThatStartedOverIsARestart()
    {
        var detector = new DaemonRestartDetector();
        detector.Observe(Uptime(1000), At(0), At(0));

        Assert.IsTrue(detector.Observe(Uptime(3), At(5), At(5)));
    }

    [TestMethod]
    public void ARestartDuringALongGapIsCaughtEvenWhenTheNewRunLastedLonger()
    {
        var detector = new DaemonRestartDetector();
        detector.Observe(Uptime(60), At(0), At(0));

        // Ten minutes without refreshing (an outage, or the PC asleep); the daemon restarted five minutes ago.
        Assert.IsTrue(detector.Observe(Uptime(300), At(600), At(600)));
    }

    [TestMethod]
    public void WholeSecondsAndASmallClockAdjustmentAreNoRestart()
    {
        var detector = new DaemonRestartDetector();
        detector.Observe(Uptime(1000), At(0), At(0));

        Assert.IsFalse(detector.Observe(Uptime(1001), At(1.9), At(1.9)), "grew a second while 1.9 seconds passed");
        Assert.IsFalse(detector.Observe(Uptime(1000), At(1.9), At(1.9)), "the server's clock went back a second");
    }

    [TestMethod]
    public void ASlowAnswerIsNoRestart()
    {
        var detector = new DaemonRestartDetector();
        // The daemon read its clock right away, but the answer took 20 seconds to arrive.
        detector.Observe(Uptime(1000), At(0), At(20));

        Assert.IsFalse(detector.Observe(Uptime(1025), At(25), At(25)));
    }

    [TestMethod]
    public void ADaemonThatDoesNotReportItsUptimeNeverRestarts()
    {
        var detector = new DaemonRestartDetector();
        var noCurrentStats = new SessionStats(0, 0, 0, 0, 0, CumulativeStats: null, CurrentStats: null);

        detector.Observe(Uptime(0), At(0), At(0));
        Assert.IsFalse(detector.Observe(Uptime(0), At(60), At(60)));
        Assert.IsFalse(detector.Observe(noCurrentStats, At(120), At(120)));
    }

    [TestMethod]
    public void ResettingForgetsThePreviousStats()
    {
        var detector = new DaemonRestartDetector();
        detector.Observe(Uptime(1000), At(0), At(0));

        detector.Reset();

        Assert.IsFalse(detector.Observe(Uptime(3), At(5), At(5)));
    }

    private static SessionStats Uptime(int seconds) => new(
        0, 0, 0, 0, 0, CumulativeStats: null, CurrentStats: new SessionTotals(0, 0, 0, seconds, SessionCount: 1));

    private static long At(double seconds) => Start + (long)(seconds * Stopwatch.Frequency);
}
