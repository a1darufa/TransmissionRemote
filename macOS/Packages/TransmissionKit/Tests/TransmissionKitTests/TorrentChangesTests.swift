import Foundation
import Testing
@testable import TransmissionKit

@Suite("Applying recently changed torrents to the list")
struct TorrentChangesTests {
    private func torrent(_ id: Int, _ name: String) throws -> Torrent {
        try RPCCoding.decoder(for: .legacy).decode(Torrent.self, from: Data(#"{"id": \#(id), "name": "\#(name)"}"#.utf8))
    }

    @Test func replacesChangedTorrentsInPlace() throws {
        let list = [try torrent(1, "a"), try torrent(2, "b"), try torrent(3, "c")]
        let changes = TorrentChanges(changed: [try torrent(2, "b, renamed")], removedIDs: [])

        #expect(changes.applied(to: list).map(\.name) == ["a", "b, renamed", "c"])
    }

    @Test func dropsRemovedTorrents() throws {
        let list = [try torrent(1, "a"), try torrent(2, "b")]
        let changes = TorrentChanges(changed: [], removedIDs: [1, 99])

        #expect(changes.applied(to: list).map(\.id) == [2])
    }

    @Test func addsNewTorrentsAtTheEndOnce() throws {
        let list = [try torrent(1, "a")]
        let changes = TorrentChanges(
            changed: [try torrent(5, "new"), try torrent(1, "a2"), try torrent(5, "new, later")],
            removedIDs: []
        )

        #expect(changes.applied(to: list).map(\.name) == ["a2", "new, later"])
    }

    @Test func aTorrentAddedAndRemovedInTheSameWindowStaysOut() throws {
        let changes = TorrentChanges(changed: [try torrent(7, "gone")], removedIDs: [7])

        #expect(changes.applied(to: []).isEmpty)
    }

    @Test func decodesTheRemovedList() throws {
        let body = #"{"jsonrpc": "2.0", "id": 3, "result": {"torrents": [{"id": 4}], "removed": [9, 10]}}"#
        let response = try RPCCoding.decoder(for: .jsonRPC).decode(
            JSONRPCResponse<RecentlyActiveResponse>.self, from: Data(body.utf8)
        )

        #expect(response.result?.torrents.map(\.id) == [4])
        #expect(response.result?.removed == [9, 10])
    }

    @Test func spellsTheIDsValuePerDialect() {
        #expect(RPCDialect.legacy.recentlyActiveIDs == "recently-active")
        #expect(RPCDialect.jsonRPC.recentlyActiveIDs == "recently_active")
    }
}

@Suite("When to fetch the full torrent list")
struct TorrentListRefreshPlanTests {
    private let start = ContinuousClock.now

    @Test func startsWithTheFullList() {
        #expect(TorrentListRefreshPlan().needsFullList(at: start))
    }

    @Test func asksForChangesBetweenFrequentRefreshes() {
        var plan = TorrentListRefreshPlan()
        plan.recordFetch(startedAt: start, wasFullList: true)
        plan.recordFetch(startedAt: start + .seconds(5), wasFullList: false)

        #expect(!plan.needsFullList(at: start + .seconds(10)))
    }

    @Test func fetchesEverythingAfterAGapLongerThanTheWindowAllows() {
        var plan = TorrentListRefreshPlan()
        plan.recordFetch(startedAt: start, wasFullList: true)

        // A slow background interval, or the Mac waking from sleep.
        #expect(plan.needsFullList(at: start + .seconds(45)))
    }

    @Test func fetchesEverythingOnceAMinuteAnyway() {
        var plan = TorrentListRefreshPlan()
        plan.recordFetch(startedAt: start, wasFullList: true)
        for seconds in stride(from: 5, through: 60, by: 5) {
            plan.recordFetch(startedAt: start + .seconds(seconds), wasFullList: false)
        }

        #expect(plan.needsFullList(at: start + .seconds(65)))
    }
}
