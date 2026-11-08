import Foundation

/// What changed on the server recently: the torrents Transmission counts as recently active
/// (changed in the last 60 seconds) and the IDs of torrents removed in that time.
///
/// Fetching only these instead of every torrent keeps refreshes small for large libraries
/// (Transmission Remote GUI issues #708, #1163 and #1422).
public struct TorrentChanges: Sendable, Equatable {
    /// How far back Transmission looks: `RecentlyActiveSeconds` in libtransmission 3.00 to 4.1.
    public static let window: Duration = .seconds(60)

    public var changed: [Torrent]
    public var removedIDs: [Torrent.ID]

    public init(changed: [Torrent], removedIDs: [Torrent.ID]) {
        self.changed = changed
        self.removedIDs = removedIDs
    }

    /// Applies the changes to the previous full list: removed torrents go, changed ones are replaced
    /// in place, and torrents that weren't listed yet are added at the end.
    public func applied(to torrents: [Torrent]) -> [Torrent] {
        let removed = Set(removedIDs)
        var updates = Dictionary(changed.map { ($0.id, $0) }, uniquingKeysWith: { _, latest in latest })
        var result = torrents.compactMap { torrent -> Torrent? in
            guard !removed.contains(torrent.id) else { return nil }
            return updates.removeValue(forKey: torrent.id) ?? torrent
        }
        // What's left in `updates` is new; keep the server's order and add each torrent once.
        for torrent in changed where !removed.contains(torrent.id) {
            if let latest = updates.removeValue(forKey: torrent.id) {
                result.append(latest)
            }
        }
        return result
    }
}

struct RecentlyActiveRequest: Encodable, Sendable {
    let fields: [String]
    /// The string `recently-active` (legacy) or `recently_active` (JSON-RPC) instead of a list.
    let ids: String
}

struct RecentlyActiveResponse: Decodable, Sendable {
    let torrents: [Torrent]
    let removed: [Torrent.ID]

    private enum CodingKeys: String, CodingKey {
        case torrents, removed
    }

    init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        torrents = try container.decode([Torrent].self, forKey: .torrents)
        removed = container.lenientArray(.removed)
    }
}

extension RPCDialect {
    /// The `ids` value that asks for recently active torrents. It's a value, not a key, so the
    /// usual key conversion doesn't reach it.
    var recentlyActiveIDs: String {
        switch self {
        case .jsonRPC: "recently_active"
        case .legacy: "recently-active"
        }
    }
}

/// Decides whether a refresh can ask only for recently changed torrents or needs the full list.
///
/// Transmission reports what changed in the last 60 seconds, so the previous fetch must have
/// started less than that ago, with a margin for slow answers and Transmission's whole-second
/// clock. The full list is also fetched at least once a minute, so anything a daemon version
/// doesn't count as a change still gets refreshed.
public struct TorrentListRefreshPlan: Sendable {
    static let maximumGap: Duration = .seconds(40)
    static let fullListInterval: Duration = .seconds(60)

    private var lastRequest: ContinuousClock.Instant?
    private var lastFullRequest: ContinuousClock.Instant?

    public init() {}

    public func needsFullList(at now: ContinuousClock.Instant) -> Bool {
        guard let lastRequest, let lastFullRequest else { return true }
        return lastRequest.duration(to: now) > Self.maximumGap
            || lastFullRequest.duration(to: now) > Self.fullListInterval
    }

    /// Call after a fetch succeeded, with the time it started.
    public mutating func recordFetch(startedAt start: ContinuousClock.Instant, wasFullList: Bool) {
        lastRequest = start
        if wasFullList {
            lastFullRequest = start
        }
    }
}
