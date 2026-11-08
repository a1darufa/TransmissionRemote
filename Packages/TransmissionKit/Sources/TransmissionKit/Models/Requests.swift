import Foundation

/// Changes for `torrent-set`. Only non-nil properties are sent.
public struct TorrentMutation: Encodable, Sendable, Equatable {
    public var ids: [Int]
    public var bandwidthPriority: Priority?
    public var downloadLimit: Int?
    public var downloadLimited: Bool?
    public var uploadLimit: Int?
    public var uploadLimited: Bool?
    public var honorsSessionLimits: Bool?
    public var filesWanted: [Int]?
    public var filesUnwanted: [Int]?
    public var priorityHigh: [Int]?
    public var priorityNormal: [Int]?
    public var priorityLow: [Int]?
    public var labels: [String]?
    public var peerLimit: Int?
    public var seedRatioLimit: Double?
    public var seedRatioMode: LimitMode?
    public var seedIdleLimit: Int?
    public var seedIdleMode: LimitMode?
    public var sequentialDownload: Bool?
    /// Transmission 4.0+: announce URLs, one per line, blank line between tiers.
    public var trackerList: String?
    /// Pre-4.0 tracker editing.
    public var trackerAdd: [String]?
    public var trackerRemove: [Int]?

    public init(ids: [Int]) {
        self.ids = ids
    }

    public static func wanted(_ wanted: Bool, files: [Int], in torrentID: Int) -> Self {
        var mutation = Self(ids: [torrentID])
        if wanted { mutation.filesWanted = files } else { mutation.filesUnwanted = files }
        return mutation
    }

    public static func priority(_ priority: Priority, files: [Int], in torrentID: Int) -> Self {
        var mutation = Self(ids: [torrentID])
        switch priority {
        case .high: mutation.priorityHigh = files
        case .normal: mutation.priorityNormal = files
        case .low: mutation.priorityLow = files
        }
        return mutation
    }
}

/// Parameters for `torrent-add`. Exactly one of `filename` and `metainfo` must be set.
public struct AddTorrentRequest: Encodable, Sendable, Equatable {
    /// A magnet link or an http(s) URL the daemon downloads itself.
    public var filename: String?
    /// Base64-encoded `.torrent` contents.
    public var metainfo: String?
    public var downloadDir: String?
    public var paused: Bool?
    public var bandwidthPriority: Priority?
    public var filesUnwanted: [Int]?
    public var labels: [String]?
    public var sequentialDownload: Bool?

    public static func magnetOrURL(_ link: String) -> Self {
        Self(filename: link)
    }

    public static func torrentFile(_ data: Data) -> Self {
        Self(metainfo: data.base64EncodedString())
    }
}

public struct AddedTorrent: Sendable, Equatable, Decodable {
    public var id: Int
    public var name: String
    public var hashString: String
}

public struct AddTorrentResult: Sendable, Equatable, Decodable {
    public var torrentAdded: AddedTorrent?
    /// Set instead of `torrentAdded` when the torrent already existed.
    public var torrentDuplicate: AddedTorrent?

    public var torrent: AddedTorrent? { torrentAdded ?? torrentDuplicate }
    public var isDuplicate: Bool { torrentDuplicate != nil }
}

public struct RenameResult: Sendable, Equatable, Decodable {
    public var id: Int
    public var path: String
    public var name: String
}

// MARK: - Internal request bodies

struct FieldsRequest: Encodable, Sendable {
    let fields: [String]
}

struct IDsRequest: Encodable, Sendable {
    let ids: [Int]
}

struct RemoveRequest: Encodable, Sendable {
    let ids: [Int]
    let deleteLocalData: Bool
}

struct SetLocationRequest: Encodable, Sendable {
    let ids: [Int]
    let location: String
    let move: Bool
}

struct RenamePathRequest: Encodable, Sendable {
    let ids: [Int]
    let path: String
    let name: String
}

struct PathRequest: Encodable, Sendable {
    let path: String
}

struct PortTestResult: Decodable, Sendable {
    let portIsOpen: Bool
}

struct BlocklistUpdateResult: Decodable, Sendable {
    let blocklistSize: Int
}
