import Foundation

/// A named set of speed limits that torrents can share, so for example all torrents from one
/// tracker stay under 2 MB/s together. Transmission 4.0 added these; they can be created and
/// changed over RPC, but not deleted.
public struct BandwidthGroup: Identifiable, Hashable, Sendable {
    public var name: String
    /// Whether the server's global (and alternative) speed limits apply on top of the group's.
    public var honorsSessionLimits: Bool
    public var speedLimitDownEnabled: Bool
    /// KB/s.
    public var speedLimitDown: Int
    public var speedLimitUpEnabled: Bool
    /// KB/s.
    public var speedLimitUp: Int

    public var id: String { name }

    public init(
        name: String,
        honorsSessionLimits: Bool = true,
        speedLimitDownEnabled: Bool = false,
        speedLimitDown: Int = 0,
        speedLimitUpEnabled: Bool = false,
        speedLimitUp: Int = 0
    ) {
        self.name = name
        self.honorsSessionLimits = honorsSessionLimits
        self.speedLimitDownEnabled = speedLimitDownEnabled
        self.speedLimitDown = speedLimitDown
        self.speedLimitUpEnabled = speedLimitUpEnabled
        self.speedLimitUp = speedLimitUp
    }
}

extension BandwidthGroup: Codable {
    private enum CodingKeys: String, CodingKey {
        case name, honorsSessionLimits, speedLimitDownEnabled, speedLimitDown, speedLimitUpEnabled, speedLimitUp
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        name = container.lenient(.name, default: "")
        honorsSessionLimits = container.lenient(.honorsSessionLimits, default: true)
        speedLimitDownEnabled = container.lenient(.speedLimitDownEnabled, default: false)
        speedLimitDown = container.lenient(.speedLimitDown, default: 0)
        speedLimitUpEnabled = container.lenient(.speedLimitUpEnabled, default: false)
        speedLimitUp = container.lenient(.speedLimitUp, default: 0)
    }
}

struct GroupGetResponse: Decodable, Sendable {
    let group: [BandwidthGroup]

    private enum CodingKeys: String, CodingKey {
        case group
    }

    init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        group = container.lenientArray(.group)
    }
}
