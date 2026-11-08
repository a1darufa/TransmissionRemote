/// Delays between attempts to reach a server that stopped answering.
///
/// Quick at first, because most drops (Wi-Fi roaming, a VPN reconnecting) last seconds,
/// then backing off to a minute so a server that's really down isn't hammered.
/// Transmission Remote GUI used a similar 5–60 second schedule.
public enum RetryBackoff {
    public static let delays: [Duration] = [
        .seconds(2), .seconds(5), .seconds(10), .seconds(20), .seconds(30), .seconds(60)
    ]

    /// How long to wait after `failures` failed attempts in a row.
    public static func delay(afterFailures failures: Int) -> Duration {
        let index = min(max(failures - 1, 0), delays.count - 1)
        return delays[index]
    }
}

extension TransmissionError {
    /// Failures that usually clear up on their own, so trying again later is worthwhile:
    /// timeouts and dropped connections, or a reverse proxy whose daemon is restarting.
    ///
    /// Everything else (wrong password, refused HTTPS, bad address) needs the user to change
    /// something, and retrying would only repeat the same error.
    public var isTransient: Bool {
        switch self {
        case .network:
            true
        case .httpStatus(let code):
            [502, 503, 504].contains(code)
        default:
            false
        }
    }
}
