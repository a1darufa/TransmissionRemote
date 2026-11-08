import Testing
import TransmissionKit

@Suite("Reconnect backoff")
struct RetryTests {
    @Test(arguments: [
        (1, Duration.seconds(2)),
        (2, .seconds(5)),
        (3, .seconds(10)),
        (6, .seconds(60)),
        (500, .seconds(60))
    ])
    func backsOffToAMinute(failures: Int, expected: Duration) {
        #expect(RetryBackoff.delay(afterFailures: failures) == expected)
    }

    @Test func treatsZeroFailuresLikeTheFirst() {
        #expect(RetryBackoff.delay(afterFailures: 0) == .seconds(2))
    }

    @Test(arguments: [
        (TransmissionError.network(code: -1001, description: "timed out"), true),
        (.httpStatus(503), true),
        (.httpStatus(500), false),
        (.unauthorized, false),
        (.httpsHandshakeFailed, false),
        (.invalidResponse(details: "not JSON"), false)
    ])
    func retriesOnlyTransientFailures(error: TransmissionError, isTransient: Bool) {
        #expect(error.isTransient == isTransient)
    }
}
