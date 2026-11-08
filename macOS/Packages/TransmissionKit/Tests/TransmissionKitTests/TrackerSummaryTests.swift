import Foundation
import Testing
@testable import TransmissionKit

@Suite("Tracker summaries for the Seeds, Peers and Tracker Status columns")
struct TrackerSummaryTests {
    private typealias Entry = TrackerSummaryEntry

    @Test func decodesJSONRPCTrackerStats() throws {
        let response = #"""
        {
          "jsonrpc": "2.0", "id": 7,
          "result": {
            "torrents": [{
              "id": 3,
              "tracker_stats": [{
                "tier": 0, "is_backup": false, "seeder_count": 172, "leecher_count": 9,
                "announce_state": 1, "has_announced": true, "last_announce_succeeded": true,
                "last_announce_result": "Success"
              }]
            }]
          }
        }
        """#
        let decoder = RPCCoding.decoder(for: .jsonRPC)
        let decoded = try decoder.decode(
            JSONRPCResponse<TorrentGetResponse<TrackerSummary>>.self,
            from: Data(response.utf8)
        )
        let summary = try #require(decoded.result?.torrents.first)

        #expect(summary.torrentID == 3)
        #expect(summary.seeders == 172)
        #expect(summary.leechers == 9)
        #expect(summary.status == .working)
    }

    @Test func takesTheLargestSwarmAnyTrackerReports() {
        let summary = TrackerSummary(torrentID: 1, trackers: [
            Entry(tier: 0, seederCount: 40, leecherCount: 2),
            Entry(tier: 1, seederCount: 1_284, leecherCount: -1)
        ])
        #expect(summary.seeders == 1_284)
        #expect(summary.leechers == 2)
    }

    @Test func unscrapedTrackersReportNoSwarm() {
        let summary = TrackerSummary(torrentID: 1, trackers: [Entry(seederCount: -1, leecherCount: -1)])
        #expect(summary.seeders == nil)
        #expect(summary.leechers == nil)
    }

    @Test func statusComesFromTheFirstTierAndSkipsBackups() {
        let summary = TrackerSummary(torrentID: 1, trackers: [
            Entry(tier: 1, hasAnnounced: true, lastAnnounceSucceeded: true),
            Entry(tier: 0, isBackup: true, hasAnnounced: true, lastAnnounceResult: "backup down"),
            Entry(tier: 0, hasAnnounced: true, lastAnnounceResult: "Connection refused")
        ])
        #expect(summary.status == .failed("Connection refused"))
    }

    @Test func announcingTrackersShowAsUpdating() {
        let summary = TrackerSummary(torrentID: 1, trackers: [Entry(announceState: 3, hasAnnounced: true)])
        #expect(summary.status == .updating)
    }

    @Test func trackersNotYetContactedHaveNoStatus() {
        #expect(TrackerSummary(torrentID: 1, trackers: [Entry()]).status == .notContacted)
        #expect(TrackerSummary(torrentID: 1, trackers: []).status == .notContacted)
    }
}
