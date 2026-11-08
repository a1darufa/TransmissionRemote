import Foundation

/// `tr_torrent_activity` from libtransmission.
public enum TorrentStatus: Int, Sendable, Codable, CaseIterable {
    case stopped = 0
    case queuedToVerify = 1
    case verifying = 2
    case queuedToDownload = 3
    case downloading = 4
    case queuedToSeed = 5
    case seeding = 6

    public var isQueued: Bool {
        self == .queuedToVerify || self == .queuedToDownload || self == .queuedToSeed
    }
}

/// `tr_priority_t`: used for both bandwidth priority and file priority.
public enum Priority: Int, Sendable, Codable, CaseIterable, Comparable {
    case low = -1
    case normal = 0
    case high = 1

    public static func < (lhs: Self, rhs: Self) -> Bool { lhs.rawValue < rhs.rawValue }
}

/// Fields we can request from `torrent-get`. Raw values are canonical camelCase names;
/// the client converts them to the server's spelling.
public enum TorrentField: String, Sendable, CaseIterable {
    case activityDate, addedDate, bandwidthPriority, comment, corruptEver, creator, dateCreated
    case desiredAvailable, doneDate, downloadDir, downloadedEver, downloadLimit, downloadLimited
    case error, errorString, eta, fileStats, files, hashString, haveUnchecked, haveValid
    case honorsSessionLimits, id, isFinished, isPrivate, isStalled, labels, leftUntilDone
    case magnetLink, metadataPercentComplete, name, peerLimit, peers, peersConnected, peersFrom
    case peersGettingFromUs, peersSendingToUs, percentDone, pieceCount, pieceSize, pieces
    case queuePosition, rateDownload, rateUpload, recheckProgress, secondsDownloading
    case secondsSeeding, seedIdleLimit, seedIdleMode, seedRatioLimit, seedRatioMode
    case sequentialDownload, sizeWhenDone, startDate, status, totalSize, trackerList
    case trackerStats, trackers, uploadLimit, uploadLimited, uploadRatio, uploadedEver
    case webseedsSendingToUs

    /// The `rpc-version` that introduced this field. Older servers silently drop
    /// unknown fields on some releases and reject the request on others, so we
    /// only ask for what the server knows about.
    public var minimumRPCVersion: Int {
        switch self {
        case .labels: 16 // Transmission 3.00
        case .trackerList: 17 // Transmission 4.0
        case .sequentialDownload: 18 // Transmission 4.1
        default: ServerInfo.minimumRPCVersion
        }
    }
}

/// A torrent as shown in the main list. Detail-only data lives in `TorrentDetails`.
public struct Torrent: Identifiable, Hashable, Sendable {
    public var id: Int
    public var name: String
    public var hashString: String
    public var status: TorrentStatus
    /// 0 = none, 1 = tracker warning, 2 = tracker error, 3 = local error.
    public var errorCode: Int
    public var errorString: String
    public var totalSize: Int64
    public var sizeWhenDone: Int64
    public var leftUntilDone: Int64
    public var percentDone: Double
    public var metadataPercentComplete: Double
    public var recheckProgress: Double
    public var rateDownload: Int64
    public var rateUpload: Int64
    /// Seconds; negative values mean "not available" (-1) or "unknown" (-2).
    public var eta: Int
    /// Negative values mean "not available" (-1) or "infinite" (-2).
    public var uploadRatio: Double
    public var uploadedEver: Int64
    public var downloadedEver: Int64
    public var addedDate: Date?
    public var doneDate: Date?
    public var activityDate: Date?
    public var downloadDir: String
    public var labels: [String]
    public var queuePosition: Int
    public var peersConnected: Int
    public var peersSendingToUs: Int
    public var peersGettingFromUs: Int
    public var isFinished: Bool
    public var isStalled: Bool
    public var isPrivate: Bool
    public var bandwidthPriority: Priority
    /// Hosts of the torrent's trackers, in tier order.
    public var trackerHosts: [String]

    /// Fields needed to fill a `Torrent`.
    public static let listFields: [TorrentField] = [
        .id, .name, .hashString, .status, .error, .errorString, .totalSize, .sizeWhenDone,
        .leftUntilDone, .percentDone, .metadataPercentComplete, .recheckProgress, .rateDownload,
        .rateUpload, .eta, .uploadRatio, .uploadedEver, .downloadedEver, .addedDate, .doneDate,
        .activityDate, .downloadDir, .labels, .queuePosition, .peersConnected, .peersSendingToUs,
        .peersGettingFromUs, .isFinished, .isStalled, .isPrivate, .bandwidthPriority, .trackers
    ]

    public var hasError: Bool { errorCode != 0 }
    public var isComplete: Bool { leftUntilDone == 0 && metadataPercentComplete >= 1 }
    public var isActive: Bool { rateDownload > 0 || rateUpload > 0 }
}

extension Torrent: Decodable {
    private enum CodingKeys: String, CodingKey {
        case id, name, hashString, status, error, errorString, totalSize, sizeWhenDone
        case leftUntilDone, percentDone, metadataPercentComplete, recheckProgress, rateDownload
        case rateUpload, eta, uploadRatio, uploadedEver, downloadedEver, addedDate, doneDate
        case activityDate, downloadDir, labels, queuePosition, peersConnected, peersSendingToUs
        case peersGettingFromUs, isFinished, isStalled, isPrivate, bandwidthPriority, trackers
    }

    private struct TrackerInfo: Decodable {
        let announce: String
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decode(Int.self, forKey: .id)
        name = container.lenient(.name, default: "")
        hashString = container.lenient(.hashString, default: "")
        status = TorrentStatus(rawValue: container.lenient(.status, default: 0)) ?? .stopped
        errorCode = container.lenient(.error, default: 0)
        errorString = container.lenient(.errorString, default: "")
        totalSize = container.lenient(.totalSize, default: 0)
        sizeWhenDone = container.lenient(.sizeWhenDone, default: 0)
        leftUntilDone = container.lenient(.leftUntilDone, default: 0)
        percentDone = container.lenient(.percentDone, default: 0)
        metadataPercentComplete = container.lenient(.metadataPercentComplete, default: 1)
        recheckProgress = container.lenient(.recheckProgress, default: 0)
        rateDownload = container.lenient(.rateDownload, default: 0)
        rateUpload = container.lenient(.rateUpload, default: 0)
        eta = container.lenient(.eta, default: -1)
        uploadRatio = container.lenient(.uploadRatio, default: -1)
        uploadedEver = container.lenient(.uploadedEver, default: 0)
        downloadedEver = container.lenient(.downloadedEver, default: 0)
        addedDate = container.lenientDate(.addedDate)
        doneDate = container.lenientDate(.doneDate)
        activityDate = container.lenientDate(.activityDate)
        downloadDir = container.lenient(.downloadDir, default: "")
        labels = container.lenientArray(.labels)
        queuePosition = container.lenient(.queuePosition, default: 0)
        peersConnected = container.lenient(.peersConnected, default: 0)
        peersSendingToUs = container.lenient(.peersSendingToUs, default: 0)
        peersGettingFromUs = container.lenient(.peersGettingFromUs, default: 0)
        isFinished = container.lenient(.isFinished, default: false)
        isStalled = container.lenient(.isStalled, default: false)
        isPrivate = container.lenient(.isPrivate, default: false)
        bandwidthPriority = Priority(rawValue: container.lenient(.bandwidthPriority, default: 0)) ?? .normal

        let announceURLs = container.lenientArray(.trackers, of: TrackerInfo.self).map(\.announce)
        trackerHosts = Self.uniqueHosts(in: announceURLs)
    }

    static func uniqueHosts(in announceURLs: [String]) -> [String] {
        var seen = Set<String>()
        return announceURLs.compactMap { URL(string: $0)?.host()?.lowercased() }
            .filter { seen.insert($0).inserted }
    }
}

struct TorrentGetRequest: Encodable, Sendable {
    let fields: [String]
    let ids: [Int]?
}

struct TorrentGetResponse<Item: Decodable & Sendable>: Decodable, Sendable {
    let torrents: [Item]
}
