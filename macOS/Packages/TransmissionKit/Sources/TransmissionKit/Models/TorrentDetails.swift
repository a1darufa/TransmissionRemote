import Foundation

/// `tr_ratiolimit` / `tr_idlelimit`: whether a torrent follows the session default,
/// its own limit, or no limit at all.
public enum LimitMode: Int, Sendable, Codable, CaseIterable {
    case global = 0
    case single = 1
    case unlimited = 2
}

/// Everything shown in the detail pane for a single torrent. Which properties are
/// filled depends on the fields requested, so collections default to empty.
public struct TorrentDetails: Identifiable, Sendable, Equatable {
    public var id: Int
    public var files: [FileEntry]
    public var peers: [Peer]
    public var trackerStats: [TrackerStat]
    public var piecesBitfield: Data?
    public var pieceCount: Int
    public var pieceSize: Int64
    public var comment: String
    public var creator: String
    public var dateCreated: Date?
    public var startDate: Date?
    public var magnetLink: String
    public var haveValid: Int64
    public var haveUnchecked: Int64
    public var corruptEver: Int64
    public var desiredAvailable: Int64
    public var secondsDownloading: Int
    public var secondsSeeding: Int
    public var downloadLimit: Int
    public var downloadLimited: Bool
    public var uploadLimit: Int
    public var uploadLimited: Bool
    public var seedRatioLimit: Double
    public var seedRatioMode: LimitMode
    public var seedIdleLimit: Int
    public var seedIdleMode: LimitMode
    public var honorsSessionLimits: Bool
    public var peerLimit: Int
    public var sequentialDownload: Bool?
    public var trackerList: String?
    public var webseedsSendingToUs: Int
    /// Transmission 4.0+; nil on older servers.
    public var bandwidthGroup: String?

    /// Always requested, cheap fields for the General tab.
    public static let generalFields: [TorrentField] = [
        .id, .pieces, .pieceCount, .pieceSize, .comment, .creator, .dateCreated, .startDate,
        .haveValid, .haveUnchecked, .corruptEver, .desiredAvailable, .secondsDownloading,
        .secondsSeeding, .webseedsSendingToUs
    ]
    public static let fileFields: [TorrentField] = [.id, .files, .fileStats]
    public static let peerFields: [TorrentField] = [.id, .peers]
    public static let trackerFields: [TorrentField] = [.id, .trackerStats, .trackerList]
    public static let optionFields: [TorrentField] = [
        .id, .downloadLimit, .downloadLimited, .uploadLimit, .uploadLimited, .seedRatioLimit,
        .seedRatioMode, .seedIdleLimit, .seedIdleMode, .honorsSessionLimits, .peerLimit,
        .bandwidthPriority, .sequentialDownload, .group
    ]
}

extension TorrentDetails: Decodable {
    private enum CodingKeys: String, CodingKey {
        case id, files, fileStats, peers, trackerStats, pieces, pieceCount, pieceSize, comment
        case creator, dateCreated, startDate, magnetLink, haveValid, haveUnchecked, corruptEver
        case desiredAvailable, secondsDownloading, secondsSeeding, downloadLimit, downloadLimited
        case uploadLimit, uploadLimited, seedRatioLimit, seedRatioMode, seedIdleLimit, seedIdleMode
        case honorsSessionLimits, peerLimit, sequentialDownload, trackerList, webseedsSendingToUs, group
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decode(Int.self, forKey: .id)

        let fileInfo = container.lenientArray(.files, of: FileInfo.self)
        let fileStats = container.lenientArray(.fileStats, of: FileStat.self)
        files = FileEntry.combine(fileInfo, fileStats)

        peers = container.lenientArray(.peers)
        trackerStats = container.lenientArray(.trackerStats)
        piecesBitfield = container.lenient(.pieces).flatMap { (encoded: String) in Data(base64Encoded: encoded) }
        pieceCount = container.lenient(.pieceCount, default: 0)
        pieceSize = container.lenient(.pieceSize, default: 0)
        comment = container.lenient(.comment, default: "")
        creator = container.lenient(.creator, default: "")
        dateCreated = container.lenientDate(.dateCreated)
        startDate = container.lenientDate(.startDate)
        magnetLink = container.lenient(.magnetLink, default: "")
        haveValid = container.lenient(.haveValid, default: 0)
        haveUnchecked = container.lenient(.haveUnchecked, default: 0)
        corruptEver = container.lenient(.corruptEver, default: 0)
        desiredAvailable = container.lenient(.desiredAvailable, default: 0)
        secondsDownloading = container.lenient(.secondsDownloading, default: 0)
        secondsSeeding = container.lenient(.secondsSeeding, default: 0)
        downloadLimit = container.lenient(.downloadLimit, default: 0)
        downloadLimited = container.lenient(.downloadLimited, default: false)
        uploadLimit = container.lenient(.uploadLimit, default: 0)
        uploadLimited = container.lenient(.uploadLimited, default: false)
        seedRatioLimit = container.lenient(.seedRatioLimit, default: 0)
        seedRatioMode = LimitMode(rawValue: container.lenient(.seedRatioMode, default: 0)) ?? .global
        seedIdleLimit = container.lenient(.seedIdleLimit, default: 0)
        seedIdleMode = LimitMode(rawValue: container.lenient(.seedIdleMode, default: 0)) ?? .global
        honorsSessionLimits = container.lenient(.honorsSessionLimits, default: true)
        peerLimit = container.lenient(.peerLimit, default: 0)
        sequentialDownload = container.lenient(.sequentialDownload)
        trackerList = container.lenient(.trackerList)
        webseedsSendingToUs = container.lenient(.webseedsSendingToUs, default: 0)
        bandwidthGroup = container.lenient(.group)
    }
}

// MARK: - Files

/// One file in a torrent, combining the static `files` entry with its `fileStats`.
public struct FileEntry: Identifiable, Sendable, Equatable {
    /// The file index Transmission uses in `files-wanted`, `priority-high` and friends.
    public var id: Int
    /// Path inside the torrent, `/`-separated, usually starting with the torrent's folder.
    public var path: String
    public var length: Int64
    public var bytesCompleted: Int64
    public var wanted: Bool
    public var priority: Priority
    /// A BEP 47 padding file, which only aligns pieces. Transmission (3.00 through 4.1) lists them
    /// like real files, so they keep their place in the numbering, but the app hides them.
    public var isPadding: Bool

    public init(
        id: Int,
        path: String,
        length: Int64,
        bytesCompleted: Int64 = 0,
        wanted: Bool = true,
        priority: Priority = .normal,
        isPadding: Bool? = nil
    ) {
        self.id = id
        self.path = path
        self.length = length
        self.bytesCompleted = bytesCompleted
        self.wanted = wanted
        self.priority = priority
        self.isPadding = isPadding ?? Self.looksLikePadding(path)
    }

    /// The daemon doesn't report the `attr` flag, so padding files are recognized by the names
    /// clients give them: libtorrent's `.pad/<size>` and BitComet's `_____padding_file_…`.
    static func looksLikePadding(_ path: String) -> Bool {
        let components = path.split(separator: "/")
        return components.dropLast().contains(".pad") || components.last?.hasPrefix("_____padding_file") == true
    }

    static func combine(_ files: [FileInfo], _ stats: [FileStat]) -> [Self] {
        files.enumerated().map { index, file in
            let stat = stats.indices.contains(index) ? stats[index] : nil
            return Self(
                id: index,
                path: file.name,
                length: file.length,
                bytesCompleted: stat?.bytesCompleted ?? file.bytesCompleted,
                wanted: stat?.wanted ?? true,
                priority: stat?.priority ?? .normal
            )
        }
    }
}

struct FileInfo: Decodable {
    let name: String
    let length: Int64
    let bytesCompleted: Int64

    private enum CodingKeys: String, CodingKey { case name, length, bytesCompleted }

    init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        name = container.lenient(.name, default: "")
        length = container.lenient(.length, default: 0)
        bytesCompleted = container.lenient(.bytesCompleted, default: 0)
    }
}

struct FileStat: Decodable {
    let bytesCompleted: Int64
    let wanted: Bool
    let priority: Priority

    private enum CodingKeys: String, CodingKey { case bytesCompleted, wanted, priority }

    init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        bytesCompleted = container.lenient(.bytesCompleted, default: 0)
        wanted = container.lenient(.wanted, default: true) // `0`/`1` on legacy servers
        priority = Priority(rawValue: container.lenient(.priority, default: 0)) ?? .normal
    }
}

// MARK: - Peers

public struct Peer: Identifiable, Sendable, Equatable, Decodable {
    public var address: String
    public var port: Int
    public var clientName: String
    /// Transmission's compact flag string, e.g. `"TDEI"`. See the `peer-status` docs.
    public var flagStr: String
    public var progress: Double
    public var rateToClient: Int64
    public var rateToPeer: Int64
    public var isEncrypted: Bool
    public var isUtp: Bool
    public var isIncoming: Bool

    public var id: String { "\(address):\(port)" }

    private enum CodingKeys: String, CodingKey {
        case address, port, clientName, flagStr, progress, rateToClient, rateToPeer
        case isEncrypted, isUtp, isIncoming
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        address = container.lenient(.address, default: "")
        port = container.lenient(.port, default: 0)
        clientName = container.lenient(.clientName, default: "")
        flagStr = container.lenient(.flagStr, default: "")
        progress = container.lenient(.progress, default: 0)
        rateToClient = container.lenient(.rateToClient, default: 0)
        rateToPeer = container.lenient(.rateToPeer, default: 0)
        isEncrypted = container.lenient(.isEncrypted, default: false)
        isUtp = container.lenient(.isUtp, default: false)
        isIncoming = container.lenient(.isIncoming, default: false)
    }
}

// MARK: - Trackers

public struct TrackerStat: Identifiable, Sendable, Equatable, Decodable {
    public var id: Int
    public var tier: Int
    public var announce: String
    public var host: String
    public var lastAnnounceResult: String
    public var lastAnnounceSucceeded: Bool
    public var lastAnnounceTime: Date?
    public var nextAnnounceTime: Date?
    public var lastAnnouncePeerCount: Int
    public var seederCount: Int
    public var leecherCount: Int
    public var downloadCount: Int
    public var isBackup: Bool
    public var hasAnnounced: Bool

    private enum CodingKeys: String, CodingKey {
        case id, tier, announce, host, lastAnnounceResult, lastAnnounceSucceeded, lastAnnounceTime
        case nextAnnounceTime, lastAnnouncePeerCount, seederCount, leecherCount, downloadCount
        case isBackup, hasAnnounced
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = container.lenient(.id, default: 0)
        tier = container.lenient(.tier, default: 0)
        announce = container.lenient(.announce, default: "")
        host = container.lenient(.host, default: URL(string: announce)?.host() ?? announce)
        lastAnnounceResult = container.lenient(.lastAnnounceResult, default: "")
        lastAnnounceSucceeded = container.lenient(.lastAnnounceSucceeded, default: false)
        lastAnnounceTime = container.lenientDate(.lastAnnounceTime)
        nextAnnounceTime = container.lenientDate(.nextAnnounceTime)
        lastAnnouncePeerCount = container.lenient(.lastAnnouncePeerCount, default: 0)
        // Trackers report -1 when they haven't been scraped yet.
        seederCount = max(container.lenient(.seederCount, default: 0), 0)
        leecherCount = max(container.lenient(.leecherCount, default: 0), 0)
        downloadCount = max(container.lenient(.downloadCount, default: 0), 0)
        isBackup = container.lenient(.isBackup, default: false)
        hasAnnounced = container.lenient(.hasAnnounced, default: false)
    }
}
