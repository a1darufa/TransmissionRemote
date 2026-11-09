import Foundation

/// Notices that the daemon restarted between two refreshes. A restarted daemon numbers its
/// torrents afresh, so torrent IDs kept from before (the selection, the list that recent changes
/// are applied to) may now mean other torrents.
///
/// Transmission doesn't announce a restart, but its uptime shows it: the current session's
/// `secondsActive` starts again from zero. Comparing the uptime with the time that passed, not
/// just with the previous uptime, also catches a restart while the app wasn't refreshing for
/// longer than the new run has lasted, such as during a long outage or while the Mac slept
/// (`ContinuousClock` keeps counting during sleep). The cumulative session count isn't used:
/// Transmission only saves it when something was transferred, so an idle daemon reports 1 after
/// every restart.
public struct DaemonRestartDetector: Sendable {
    /// Transmission counts uptime in whole seconds, so it can grow by up to a second less than
    /// the time that passed; the rest allows for a small adjustment of the server's clock.
    static let tolerance: Duration = .seconds(2)

    private struct Observation: Sendable {
        let uptime: Duration
        let answeredAt: ContinuousClock.Instant
    }

    private var last: Observation?

    public init() {}

    /// Compares session stats with the previous ones, and keeps them for the next call.
    /// - Parameters:
    ///   - stats: A `session-stats` answer.
    ///   - requestedAt: When the request was sent.
    ///   - answeredAt: When its answer had arrived; later is fine.
    /// - Returns: Whether the daemon restarted since the previous stats.
    public mutating func observe(
        _ stats: SessionStats,
        requestedAt: ContinuousClock.Instant,
        answeredAt: ContinuousClock.Instant
    ) -> Bool {
        guard let current = stats.currentStats else { return false }
        let uptime = Duration.seconds(current.secondsActive)
        let restarted = last.map { hasRestarted(since: $0, uptime: uptime, requestedAt: requestedAt) } ?? false
        last = Observation(uptime: uptime, answeredAt: answeredAt)
        return restarted
    }

    private func hasRestarted(
        since previous: Observation,
        uptime: Duration,
        requestedAt: ContinuousClock.Instant
    ) -> Bool {
        // A daemon that doesn't report its uptime reads as 0 every time; there's nothing to compare then.
        guard previous.uptime > .zero else { return false }
        // The daemon read its clock for the previous answer before that answer arrived, and for
        // this one after the request was sent, so at least this much time passed between the two.
        let passed = max(previous.answeredAt.duration(to: requestedAt), .zero)
        return uptime < previous.uptime + passed - Self.tolerance
    }
}
