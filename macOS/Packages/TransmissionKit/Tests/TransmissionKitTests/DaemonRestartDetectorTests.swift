import Foundation
import Testing
@testable import TransmissionKit

@Suite("Noticing that the daemon restarted")
struct DaemonRestartDetectorTests {
    private let start = ContinuousClock.now

    @Test func theFirstStatsAreNoRestart() throws {
        var detector = DaemonRestartDetector()

        let restarted = detector.observe(try uptime(3), requestedAt: after(0), answeredAt: after(0))

        #expect(!restarted)
    }

    @Test func anUptimeThatKeepsUpWithTheClockIsNoRestart() throws {
        var detector = DaemonRestartDetector()
        _ = detector.observe(try uptime(1000), requestedAt: after(0), answeredAt: after(0))

        let restarted = detector.observe(try uptime(1005), requestedAt: after(5), answeredAt: after(5))

        #expect(!restarted)
    }

    @Test func anUptimeThatStartedOverIsARestart() throws {
        var detector = DaemonRestartDetector()
        _ = detector.observe(try uptime(1000), requestedAt: after(0), answeredAt: after(0))

        let restarted = detector.observe(try uptime(3), requestedAt: after(5), answeredAt: after(5))

        #expect(restarted)
    }

    @Test func aRestartDuringALongGapIsCaughtEvenWhenTheNewRunLastedLonger() throws {
        var detector = DaemonRestartDetector()
        _ = detector.observe(try uptime(60), requestedAt: after(0), answeredAt: after(0))

        // Ten minutes without refreshing (an outage, or the Mac asleep); the daemon restarted five minutes ago.
        let restarted = detector.observe(try uptime(300), requestedAt: after(600), answeredAt: after(600))

        #expect(restarted)
    }

    @Test func wholeSecondsAndASmallClockAdjustmentAreNoRestart() throws {
        var detector = DaemonRestartDetector()
        _ = detector.observe(try uptime(1000), requestedAt: after(0), answeredAt: after(0))

        let grewLess = detector.observe(try uptime(1001), requestedAt: after(1.9), answeredAt: after(1.9))
        let wentBack = detector.observe(try uptime(1000), requestedAt: after(1.9), answeredAt: after(1.9))

        #expect(!grewLess, "grew a second while 1.9 seconds passed")
        #expect(!wentBack, "the server's clock went back a second")
    }

    @Test func aSlowAnswerIsNoRestart() throws {
        var detector = DaemonRestartDetector()
        // The daemon read its clock right away, but the answer took 20 seconds to arrive.
        _ = detector.observe(try uptime(1000), requestedAt: after(0), answeredAt: after(20))

        let restarted = detector.observe(try uptime(1025), requestedAt: after(25), answeredAt: after(25))

        #expect(!restarted)
    }

    @Test func aDaemonThatDoesNotReportItsUptimeNeverRestarts() throws {
        var detector = DaemonRestartDetector()
        let noCurrentStats = try RPCCoding.decoder(for: .legacy).decode(SessionStats.self, from: Data("{}".utf8))
        _ = detector.observe(try uptime(0), requestedAt: after(0), answeredAt: after(0))

        let afterAMinute = detector.observe(try uptime(0), requestedAt: after(60), answeredAt: after(60))
        let withoutStats = detector.observe(noCurrentStats, requestedAt: after(120), answeredAt: after(120))

        #expect(!afterAMinute)
        #expect(!withoutStats)
    }

    @Test func aNewDetectorForgetsThePreviousStats() throws {
        var detector = DaemonRestartDetector()
        _ = detector.observe(try uptime(1000), requestedAt: after(0), answeredAt: after(0))

        detector = DaemonRestartDetector()
        let restarted = detector.observe(try uptime(3), requestedAt: after(5), answeredAt: after(5))

        #expect(!restarted)
    }

    private func uptime(_ seconds: Int) throws -> SessionStats {
        try RPCCoding.decoder(for: .legacy).decode(
            SessionStats.self, from: Data(#"{"current-stats": {"secondsActive": \#(seconds)}}"#.utf8)
        )
    }

    private func after(_ seconds: Double) -> ContinuousClock.Instant {
        start + .seconds(seconds)
    }
}
