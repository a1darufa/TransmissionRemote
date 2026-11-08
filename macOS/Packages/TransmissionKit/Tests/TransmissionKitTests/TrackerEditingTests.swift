import Foundation
import Testing
@testable import TransmissionKit

/// Transmission before 4.0 answers an empty `trackerAdd` or `trackerRemove` list with
/// "invalid argument" (and skips the remove after a failed add), so only real changes may be sent.
@Suite("Editing trackers on old and new servers")
struct TrackerEditingTests {
    private let current: [TrackerStat]
    private let transmission300 = 16
    private let transmission40 = 17

    init() throws {
        current = try JSONDecoder().decode([TrackerStat].self, from: Data("""
            [{"id": 0, "tier": 0, "announce": "http://one.example/announce"},
             {"id": 1, "tier": 1, "announce": "http://two.example/announce"}]
            """.utf8))
    }

    @Test func addingOnlySendsNoRemoveList() throws {
        let mutation = try #require(TransmissionClient.trackerMutation(
            for: 7, current: current,
            wanted: [["http://one.example/announce"], ["http://two.example/announce"], ["http://new.example/announce"]],
            rpcVersion: transmission300
        ))
        #expect(mutation.trackerAdd == ["http://new.example/announce"])
        #expect(mutation.trackerRemove == nil)
    }

    @Test func removingOnlySendsNoAddList() throws {
        let mutation = try #require(TransmissionClient.trackerMutation(
            for: 7, current: current, wanted: [["http://one.example/announce"]], rpcVersion: transmission300
        ))
        #expect(mutation.trackerRemove == [1])
        #expect(mutation.trackerAdd == nil)
    }

    @Test func unchangedListsSendNothing() {
        let unchanged = TransmissionClient.trackerMutation(
            for: 7, current: current,
            wanted: [["http://one.example/announce"], ["http://two.example/announce"]],
            rpcVersion: transmission300
        )
        #expect(unchanged == nil)
    }

    @Test func emptyListsAreLeftOutOfTheRequest() throws {
        let mutation = try #require(TransmissionClient.trackerMutation(
            for: 7, current: current, wanted: [["http://one.example/announce"]], rpcVersion: transmission300
        ))
        let encoded = try #require(String(data: try JSONEncoder().encode(mutation), encoding: .utf8))
        #expect(!encoded.contains("trackerAdd"))
    }

    @Test func transmission40ReplacesTheWholeList() throws {
        let mutation = try #require(TransmissionClient.trackerMutation(
            for: 7, current: current,
            wanted: [
                ["http://one.example/announce", "http://backup.example/announce"], ["http://two.example/announce"]
            ],
            rpcVersion: transmission40
        ))
        #expect(mutation.trackerList == """
            http://one.example/announce
            http://backup.example/announce

            http://two.example/announce
            """)
        #expect(mutation.trackerAdd == nil && mutation.trackerRemove == nil)
    }
}
