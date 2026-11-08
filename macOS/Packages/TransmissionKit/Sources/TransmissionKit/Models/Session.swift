import Foundation

/// Version information used to pick a protocol.
public struct ServerInfo: Sendable, Equatable, Decodable {
    /// Transmission 2.40 introduced the queue, which the UI relies on.
    public static let minimumRPCVersion = 14
    static let fieldNames = ["version", "rpcVersion", "rpcVersionSemver"]

    public let version: String
    public let rpcVersion: Int
    /// Only reported by Transmission 4.0 and newer.
    public let rpcVersionSemver: String?

    public init(version: String, rpcVersion: Int, rpcVersionSemver: String?) {
        self.version = version
        self.rpcVersion = rpcVersion
        self.rpcVersionSemver = rpcVersionSemver
    }

    private enum CodingKeys: String, CodingKey { case version, rpcVersion, rpcVersionSemver }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        version = container.lenient(.version, default: "unknown")
        rpcVersion = container.lenient(.rpcVersion, default: 0)
        rpcVersionSemver = container.lenient(.rpcVersionSemver)
    }

    /// JSON-RPC 2.0 arrived with RPC semver 6.0.0 (Transmission 4.1).
    public var supportsJSONRPC: Bool {
        guard let major = rpcVersionSemver?.split(separator: ".").first.flatMap({ Int($0) }) else {
            return false
        }
        return major >= 6
    }

    /// torrent-rename-path and free-space arrived in Transmission 2.80; 2.40–2.7x answer
    /// "method name not recognized" (transgui issue #1023).
    public var supportsRenaming: Bool { rpcVersion >= 15 }
    public var supportsLabels: Bool { rpcVersion >= 16 }
    public var supportsLabelsOnAdd: Bool { rpcVersion >= 17 }
    public var supportsTrackerList: Bool { rpcVersion >= 17 }
    public var supportsSequentialDownload: Bool { rpcVersion >= 18 }
}

public enum EncryptionMode: String, Sendable, CaseIterable {
    case required
    case preferred
    /// Called `tolerated` before Transmission 4.1.
    case allowed
}

extension EncryptionMode: Codable {
    public init(from decoder: any Decoder) throws {
        let value = try decoder.singleValueContainer().decode(String.self)
        self = value == "tolerated" ? .allowed : (EncryptionMode(rawValue: value) ?? .preferred)
    }

    public func encode(to encoder: any Encoder) throws {
        var container = encoder.singleValueContainer()
        let dialect = encoder.userInfo[.rpcDialect] as? RPCDialect
        if self == .allowed, dialect == .legacy {
            try container.encode("tolerated")
        } else {
            try container.encode(rawValue)
        }
    }
}

/// Daemon-wide settings from `session-get`.
///
/// Every property is optional: older servers don't know every key, and when this type is
/// used as a patch for `session-set`, `nil` means "leave unchanged".
public struct SessionSettings: Sendable, Equatable, Codable {
    public var altSpeedDown: Int?
    public var altSpeedEnabled: Bool?
    public var altSpeedTimeBegin: Int?
    /// Bitmask: Sunday = 1, Monday = 2, … Saturday = 64.
    public var altSpeedTimeDay: Int?
    public var altSpeedTimeEnabled: Bool?
    public var altSpeedTimeEnd: Int?
    public var altSpeedUp: Int?
    public var blocklistEnabled: Bool?
    public var blocklistSize: Int?
    public var blocklistUrl: String?
    /// The daemon's disk cache, in megabytes (`cache-size-mb`).
    public var cacheSizeMb: Int?
    public var configDir: String?
    public var dhtEnabled: Bool?
    public var downloadDir: String?
    public var downloadQueueEnabled: Bool?
    public var downloadQueueSize: Int?
    public var encryption: EncryptionMode?
    public var idleSeedingLimit: Int?
    public var idleSeedingLimitEnabled: Bool?
    public var incompleteDir: String?
    public var incompleteDirEnabled: Bool?
    public var lpdEnabled: Bool?
    public var peerLimitGlobal: Int?
    public var peerLimitPerTorrent: Int?
    public var peerPort: Int?
    public var peerPortRandomOnStart: Bool?
    public var pexEnabled: Bool?
    public var portForwardingEnabled: Bool?
    public var queueStalledEnabled: Bool?
    public var queueStalledMinutes: Int?
    public var renamePartialFiles: Bool?
    public var seedQueueEnabled: Bool?
    public var seedQueueSize: Int?
    public var seedRatioLimit: Double?
    public var seedRatioLimited: Bool?
    public var sequentialDownload: Bool?
    public var speedLimitDown: Int?
    public var speedLimitDownEnabled: Bool?
    public var speedLimitUp: Int?
    public var speedLimitUpEnabled: Bool?
    public var startAddedTorrents: Bool?
    public var trashOriginalTorrentFiles: Bool?
    public var utpEnabled: Bool?
    public var version: String?

    public init() {}

    enum CodingKeys: String, CodingKey {
        case altSpeedDown, altSpeedEnabled, altSpeedTimeBegin, altSpeedTimeDay, altSpeedTimeEnabled
        case altSpeedTimeEnd, altSpeedUp, blocklistEnabled, blocklistSize, blocklistUrl, cacheSizeMb, configDir
        case dhtEnabled, downloadDir, downloadQueueEnabled, downloadQueueSize, encryption
        case idleSeedingLimit, idleSeedingLimitEnabled, incompleteDir, incompleteDirEnabled
        case lpdEnabled, peerLimitGlobal, peerLimitPerTorrent, peerPort, peerPortRandomOnStart
        case pexEnabled, portForwardingEnabled, queueStalledEnabled, queueStalledMinutes
        case renamePartialFiles, seedQueueEnabled, seedQueueSize, seedRatioLimit, seedRatioLimited
        case sequentialDownload, speedLimitDown, speedLimitDownEnabled, speedLimitUp
        case speedLimitUpEnabled, startAddedTorrents, trashOriginalTorrentFiles, utpEnabled, version
    }

    /// Keys the server reports but won't accept in `session-set`.
    private static let readOnlyKeys: Set<CodingKeys> = [.blocklistSize, .configDir, .version]

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        altSpeedDown = container.lenient(.altSpeedDown)
        altSpeedEnabled = container.lenient(.altSpeedEnabled)
        altSpeedTimeBegin = container.lenient(.altSpeedTimeBegin)
        altSpeedTimeDay = container.lenient(.altSpeedTimeDay)
        altSpeedTimeEnabled = container.lenient(.altSpeedTimeEnabled)
        altSpeedTimeEnd = container.lenient(.altSpeedTimeEnd)
        altSpeedUp = container.lenient(.altSpeedUp)
        blocklistEnabled = container.lenient(.blocklistEnabled)
        blocklistSize = container.lenient(.blocklistSize)
        blocklistUrl = container.lenient(.blocklistUrl)
        cacheSizeMb = container.lenient(.cacheSizeMb)
        configDir = container.lenient(.configDir)
        dhtEnabled = container.lenient(.dhtEnabled)
        downloadDir = container.lenient(.downloadDir)
        downloadQueueEnabled = container.lenient(.downloadQueueEnabled)
        downloadQueueSize = container.lenient(.downloadQueueSize)
        encryption = container.lenientValue(.encryption)
        idleSeedingLimit = container.lenient(.idleSeedingLimit)
        idleSeedingLimitEnabled = container.lenient(.idleSeedingLimitEnabled)
        incompleteDir = container.lenient(.incompleteDir)
        incompleteDirEnabled = container.lenient(.incompleteDirEnabled)
        lpdEnabled = container.lenient(.lpdEnabled)
        peerLimitGlobal = container.lenient(.peerLimitGlobal)
        peerLimitPerTorrent = container.lenient(.peerLimitPerTorrent)
        peerPort = container.lenient(.peerPort)
        peerPortRandomOnStart = container.lenient(.peerPortRandomOnStart)
        pexEnabled = container.lenient(.pexEnabled)
        portForwardingEnabled = container.lenient(.portForwardingEnabled)
        queueStalledEnabled = container.lenient(.queueStalledEnabled)
        queueStalledMinutes = container.lenient(.queueStalledMinutes)
        renamePartialFiles = container.lenient(.renamePartialFiles)
        seedQueueEnabled = container.lenient(.seedQueueEnabled)
        seedQueueSize = container.lenient(.seedQueueSize)
        seedRatioLimit = container.lenient(.seedRatioLimit)
        seedRatioLimited = container.lenient(.seedRatioLimited)
        sequentialDownload = container.lenient(.sequentialDownload)
        speedLimitDown = container.lenient(.speedLimitDown)
        speedLimitDownEnabled = container.lenient(.speedLimitDownEnabled)
        speedLimitUp = container.lenient(.speedLimitUp)
        speedLimitUpEnabled = container.lenient(.speedLimitUpEnabled)
        startAddedTorrents = container.lenient(.startAddedTorrents)
        trashOriginalTorrentFiles = container.lenient(.trashOriginalTorrentFiles)
        utpEnabled = container.lenient(.utpEnabled)
        version = container.lenient(.version)
    }

    /// A patch containing only the writable values that differ from `original`.
    ///
    /// Comparing the encoded forms keeps this correct as properties are added,
    /// without a hand-written comparison per field.
    public func changes(from original: Self) throws -> Self {
        let encoder = JSONEncoder()
        let current = try JSONDecoder().decode([String: JSONValue].self, from: encoder.encode(self))
        let previous = try JSONDecoder().decode([String: JSONValue].self, from: encoder.encode(original))

        let readOnly = Set(Self.readOnlyKeys.map(\.stringValue))
        let changed = current.filter { key, value in !readOnly.contains(key) && previous[key] != value }
        return try JSONDecoder().decode(Self.self, from: encoder.encode(changed))
    }

    public var isEmpty: Bool { self == Self() }
}

/// Minimal JSON value, used to diff encoded settings.
enum JSONValue: Codable, Equatable {
    case string(String)
    case number(Double)
    case bool(Bool)
    case array([Self])
    case object([String: Self])
    case null

    init(from decoder: any Decoder) throws {
        let container = try decoder.singleValueContainer()
        if container.decodeNil() {
            self = .null
        } else if let value = try? container.decode(Bool.self) {
            self = .bool(value)
        } else if let value = try? container.decode(Double.self) {
            self = .number(value)
        } else if let value = try? container.decode(String.self) {
            self = .string(value)
        } else if let value = try? container.decode([Self].self) {
            self = .array(value)
        } else {
            self = .object(try container.decode([String: Self].self))
        }
    }

    func encode(to encoder: any Encoder) throws {
        var container = encoder.singleValueContainer()
        switch self {
        case .string(let value): try container.encode(value)
        case .number(let value): try container.encode(value)
        case .bool(let value): try container.encode(value)
        case .array(let value): try container.encode(value)
        case .object(let value): try container.encode(value)
        case .null: try container.encodeNil()
        }
    }
}

/// Transfer totals for one period (since the daemon started, or all time), from `session-stats`.
public struct SessionTotals: Sendable, Equatable, Decodable {
    public var uploadedBytes: Int64
    public var downloadedBytes: Int64
    public var filesAdded: Int
    public var secondsActive: Int
    public var sessionCount: Int

    private enum CodingKeys: String, CodingKey {
        case uploadedBytes, downloadedBytes, filesAdded, secondsActive, sessionCount
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        uploadedBytes = container.lenient(.uploadedBytes, default: 0)
        downloadedBytes = container.lenient(.downloadedBytes, default: 0)
        filesAdded = container.lenient(.filesAdded, default: 0)
        secondsActive = container.lenient(.secondsActive, default: 0)
        sessionCount = container.lenient(.sessionCount, default: 0)
    }
}

/// Speeds, torrent counts and transfer totals from `session-stats`.
public struct SessionStats: Sendable, Equatable, Decodable {
    public var activeTorrentCount: Int
    public var pausedTorrentCount: Int
    public var torrentCount: Int
    public var downloadSpeed: Int64
    public var uploadSpeed: Int64
    public var cumulativeStats: SessionTotals?
    public var currentStats: SessionTotals?

    private enum CodingKeys: String, CodingKey {
        case activeTorrentCount, pausedTorrentCount, torrentCount, downloadSpeed, uploadSpeed
        case cumulativeStats, currentStats
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        activeTorrentCount = container.lenient(.activeTorrentCount, default: 0)
        pausedTorrentCount = container.lenient(.pausedTorrentCount, default: 0)
        torrentCount = container.lenient(.torrentCount, default: 0)
        downloadSpeed = container.lenient(.downloadSpeed, default: 0)
        uploadSpeed = container.lenient(.uploadSpeed, default: 0)
        cumulativeStats = container.lenientValue(.cumulativeStats)
        currentStats = container.lenientValue(.currentStats)
    }
}

/// Result of `free-space`.
public struct FreeSpace: Sendable, Equatable, Decodable {
    public var path: String
    public var sizeBytes: Int64
    /// Only reported by Transmission 4.0 and newer.
    public var totalSize: Int64?
}
