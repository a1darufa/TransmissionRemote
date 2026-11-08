import Foundation
import TransmissionKit

/// The user's actions on torrents, and one-off requests that sheets and settings make.
extension ServerConnection {
    // MARK: - Torrent actions

    func perform(_ action: TorrentAction, on ids: Set<Torrent.ID>) {
        let ids = ids.sorted()
        run { try await $0.perform(action, ids: ids) }
    }

    func performOnAll(_ action: TorrentAction) {
        perform(action, on: Set(torrents.map(\.id)))
    }

    func move(_ move: QueueMove, _ ids: Set<Torrent.ID>) {
        let ids = ids.sorted()
        run { try await $0.move(move, ids: ids) }
    }

    func remove(_ ids: Set<Torrent.ID>, deletingData: Bool) {
        let ids = ids.sorted()
        run { try await $0.remove(ids: ids, deletingData: deletingData) }
    }

    func setLocation(_ ids: Set<Torrent.ID>, to location: String, movingData: Bool) {
        let ids = ids.sorted()
        run { try await $0.setLocation(ids: ids, to: location, movingData: movingData) }
    }

    func rename(_ torrent: Torrent, to newName: String) {
        renameTorrent(id: torrent.id, currentName: torrent.name, to: newName)
    }

    /// Renames a torrent's top-level file or folder, also right after adding it.
    func renameTorrent(id: Torrent.ID, currentName: String, to newName: String) {
        run { try await $0.rename(torrentID: id, path: currentName, to: newName) }
    }

    func apply(_ mutation: TorrentMutation) {
        run { try await $0.apply(mutation) }
    }

    func setLabels(_ labels: [String], on ids: Set<Torrent.ID>) {
        var mutation = TorrentMutation(ids: ids.sorted())
        mutation.labels = labels
        apply(mutation)
    }

    func setBandwidthPriority(_ priority: Priority, on ids: Set<Torrent.ID>) {
        var mutation = TorrentMutation(ids: ids.sorted())
        mutation.bandwidthPriority = priority
        apply(mutation)
    }

    func replaceTrackers(of torrentID: Torrent.ID, current: [TrackerStat], with tiers: [[String]]) {
        run { try await $0.replaceTrackers(of: torrentID, current: current, with: tiers) }
    }

    // MARK: - Requests that return data

    func add(_ request: AddTorrentRequest) async throws -> AddTorrentResult {
        let result = try await requireClient().add(request)
        await refresh()
        return result
    }

    func updateSession(_ changes: SessionSettings) async throws {
        try await requireClient().updateSession(changes)
        await refresh()
    }

    func freeSpace(at path: String) async -> FreeSpace? {
        try? await requireClient().freeSpace(at: path)
    }

    func options(of torrentID: Torrent.ID) async throws -> TorrentDetails? {
        try await requireClient().details(of: torrentID, fields: TorrentDetails.optionFields)
    }

    func trackers(of torrentID: Torrent.ID) async throws -> TorrentDetails? {
        try await requireClient().details(of: torrentID, fields: TorrentDetails.trackerFields)
    }

    func magnetLinks(for ids: Set<Torrent.ID>) async throws -> [String] {
        let fields: [TorrentField] = [.id, .magnetLink]
        return try await requireClient().torrents(TorrentDetails.self, fields: fields, ids: ids.sorted())
            .map(\.magnetLink)
            .filter { !$0.isEmpty }
    }

    func testPort() async throws -> Bool {
        try await requireClient().testPort()
    }

    func updateBlocklist() async throws -> Int {
        try await requireClient().updateBlocklist()
    }
}
