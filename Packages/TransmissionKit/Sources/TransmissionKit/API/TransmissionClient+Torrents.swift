import Foundation

public enum TorrentAction: Sendable {
    case start
    /// Start immediately, ignoring the download queue.
    case startNow
    case stop
    case verify
    case reannounce

    var method: RPCMethod {
        switch self {
        case .start: .torrentStart
        case .startNow: .torrentStartNow
        case .stop: .torrentStop
        case .verify: .torrentVerify
        case .reannounce: .torrentReannounce
        }
    }
}

public enum QueueMove: Sendable, CaseIterable {
    case top, up, down, bottom

    var method: RPCMethod {
        switch self {
        case .top: .queueMoveTop
        case .up: .queueMoveUp
        case .down: .queueMoveDown
        case .bottom: .queueMoveBottom
        }
    }
}

extension TransmissionClient {
    /// Fetches torrents with the given fields, skipping fields the server is too old to know.
    /// Pass `ids: nil` for all torrents.
    public func torrents<Item: Decodable & Sendable>(
        _ itemType: Item.Type,
        fields: [TorrentField],
        ids: [Int]? = nil
    ) async throws(TransmissionError) -> [Item] {
        let supportedFields = fields.filter { $0.minimumRPCVersion <= rpcVersion }
        let request = TorrentGetRequest(
            fields: wireNames(supportedFields.map(\.rawValue), scope: .torrentFields),
            ids: ids
        )
        let response = try await call(
            .torrentGet,
            params: request,
            scope: .torrentFields,
            returning: TorrentGetResponse<Item>.self
        )
        return response.torrents
    }

    public func allTorrents() async throws(TransmissionError) -> [Torrent] {
        try await torrents(Torrent.self, fields: Torrent.listFields)
    }

    /// Swarm sizes and tracker status for every torrent. Tracker stats are large, so the app
    /// asks for these less often than for the list.
    public func trackerSummaries() async throws(TransmissionError) -> [TrackerSummary] {
        try await torrents(TrackerSummary.self, fields: [.id, .trackerStats])
    }

    public func details(
        of torrentID: Int,
        fields: [TorrentField]
    ) async throws(TransmissionError) -> TorrentDetails? {
        try await torrents(TorrentDetails.self, fields: fields, ids: [torrentID]).first
    }

    public func perform(_ action: TorrentAction, ids: [Int]) async throws(TransmissionError) {
        try await call(action.method, params: IDsRequest(ids: ids))
    }

    public func move(_ move: QueueMove, ids: [Int]) async throws(TransmissionError) {
        try await call(move.method, params: IDsRequest(ids: ids))
    }

    public func remove(ids: [Int], deletingData: Bool) async throws(TransmissionError) {
        try await call(
            .torrentRemove,
            params: RemoveRequest(ids: ids, deleteLocalData: deletingData),
            scope: .torrentArguments
        )
    }

    /// Points torrents at a new folder. With `movingData`, the daemon moves the files too;
    /// otherwise it looks for existing files there.
    public func setLocation(
        ids: [Int],
        to location: String,
        movingData: Bool
    ) async throws(TransmissionError) {
        try await call(
            .torrentSetLocation,
            params: SetLocationRequest(ids: ids, location: location, move: movingData),
            scope: .torrentArguments
        )
    }

    @discardableResult
    public func rename(
        torrentID: Int,
        path: String,
        to newName: String
    ) async throws(TransmissionError) -> RenameResult {
        try await call(
            .torrentRenamePath,
            params: RenamePathRequest(ids: [torrentID], path: path, name: newName),
            returning: RenameResult.self
        )
    }

    public func apply(_ mutation: TorrentMutation) async throws(TransmissionError) {
        try await call(.torrentSet, params: mutation, scope: .torrentArguments)
    }

    public func add(_ request: AddTorrentRequest) async throws(TransmissionError) -> AddTorrentResult {
        try await call(.torrentAdd, params: request, scope: .torrentArguments, returning: AddTorrentResult.self)
    }

    /// Replaces a torrent's trackers. Uses `trackerList` on Transmission 4.0+, and the older
    /// add/remove arguments on earlier versions.
    public func replaceTrackers(
        of torrentID: Int,
        current: [TrackerStat],
        with announceURLs: [[String]]
    ) async throws(TransmissionError) {
        var mutation = TorrentMutation(ids: [torrentID])
        if rpcVersion >= 17 {
            mutation.trackerList = announceURLs.map { $0.joined(separator: "\n") }.joined(separator: "\n\n")
        } else {
            let wanted = Set(announceURLs.joined())
            let existing = Set(current.map(\.announce))
            mutation.trackerRemove = current.filter { !wanted.contains($0.announce) }.map(\.id)
            mutation.trackerAdd = announceURLs.joined().filter { !existing.contains($0) }
        }
        try await apply(mutation)
    }
}
