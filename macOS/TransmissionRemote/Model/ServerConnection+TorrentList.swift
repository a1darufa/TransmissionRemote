import Foundation
import TransmissionKit

extension ServerConnection {
    /// The torrent list and session stats for a refresh, fetched together because the stats show
    /// whether the daemon restarted since the last refresh.
    ///
    /// A restarted daemon numbers its torrents afresh, so a torrent kept by ID from its earlier run
    /// may now be another one: the user could remove the wrong torrent. Everything kept by ID is
    /// then dropped as when disconnecting, and the whole list is fetched again, since recent
    /// changes can't be applied to the old one.
    func fetchTorrentsAndStats(using client: TransmissionClient) async throws -> ([Torrent], SessionStats) {
        let requestedAt = ContinuousClock.now
        async let torrentList = fetchTorrentList(using: client)
        async let sessionStats = client.sessionStats()
        let (fetchedTorrents, newStats) = try await (torrentList, sessionStats)
        guard isCurrent(client) else { return (fetchedTorrents, newStats) }

        let restarted = restartDetector.observe(newStats, requestedAt: requestedAt, answeredAt: ContinuousClock.now)
        guard restarted else { return (fetchedTorrents, newStats) }
        let uptime = newStats.currentStats?.secondsActive ?? 0
        Self.log.info("The daemon restarted \(uptime) s ago; reloading its torrents and clearing the selection")
        forgetTorrentIDs()
        let reloadedTorrents = try await fetchTorrentList(using: client)
        return (reloadedTorrents, newStats)
    }

    /// Drops everything kept by torrent ID: when disconnecting, and when the daemon restarted.
    func forgetTorrentIDs() {
        notifier.reset() // The old list is no baseline for finished downloads.
        listPlan = TorrentListRefreshPlan()
        trackerSummaries = [:]
        refreshCount = 0 // So the tracker summaries are fetched again right away.
        selection = []
    }

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
