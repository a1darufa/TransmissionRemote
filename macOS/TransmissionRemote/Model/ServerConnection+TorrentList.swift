import Foundation
import TransmissionKit

extension ServerConnection {
    /// Large libraries make the full list slow to fetch and decode every few seconds, so most
    /// refreshes ask only for what changed in the last minute (transgui #708, #1163, #1422).
    func fetchTorrentList(using client: TransmissionClient) async throws -> [Torrent] {
        let started = ContinuousClock.now
        let isFullList = listPlan.needsFullList(at: started)
        let list = isFullList
            ? try await client.allTorrents()
            : try await client.recentlyChangedTorrents().applied(to: torrents)
        listPlan.recordFetch(startedAt: started, wasFullList: isFullList)
        return list
    }
}
