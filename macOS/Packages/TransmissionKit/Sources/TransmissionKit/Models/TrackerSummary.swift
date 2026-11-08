import Foundation

/// What a torrent's trackers say about its swarm, for the main list's Seeds, Peers and Tracker
/// Status columns. Trackers only update every few minutes, so this is fetched for all torrents
/// less often than the list itself.
public struct TrackerSummary: Hashable, Sendable {
    public enum Status: Hashable, Sendable {
        /// The last announce worked.
        case working
        /// An announce is queued or in progress.
        case updating
        /// The last announce failed; carries the tracker's or Transmission's message.
        case failed(String)
        /// No tracker has been contacted yet, or the torrent has none.
        case notContacted
    }

    public var torrentID: Int
    /// The largest swarm any tracker reported, or nil when no tracker has been scraped yet.
    public var seeders: Int?
    public var leechers: Int?
    /// The first tracker's status, the one Transmission Remote GUI shows too.
    public var status: Status

    public init(torrentID: Int, seeders: Int?, leechers: Int?, status: Status) {
        self.torrentID = torrentID
        self.seeders = seeders
        self.leechers = leechers
        self.status = status
    }
}

extension TrackerSummary: Decodable {
    private enum CodingKeys: String, CodingKey {
        case id, trackerStats
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let torrentID = try container.decode(Int.self, forKey: .id)
        let trackers = container.lenientArray(.trackerStats, of: TrackerSummaryEntry.self)
        self.init(torrentID: torrentID, trackers: trackers)
    }

    init(torrentID: Int, trackers: [TrackerSummaryEntry]) {
        let reported = { (count: Int) in count >= 0 ? count : nil }
        let first = trackers.filter { !$0.isBackup }.min { $0.tier < $1.tier } ?? trackers.first
        self.init(
            torrentID: torrentID,
            seeders: trackers.compactMap { reported($0.seederCount) }.max(),
            leechers: trackers.compactMap { reported($0.leecherCount) }.max(),
            status: first.map(Self.status(of:)) ?? .notContacted
        )
    }

    private static func status(of tracker: TrackerSummaryEntry) -> Status {
        if tracker.announceState == 2 || tracker.announceState == 3 { return .updating }
        guard tracker.hasAnnounced else { return .notContacted }
        // Some trackers answer with the word "Success" in an otherwise failed-looking result.
        if tracker.lastAnnounceSucceeded || tracker.lastAnnounceResult == "Success" { return .working }
        return .failed(tracker.lastAnnounceResult)
    }
}

/// The parts of a tracker's stats the summary needs.
struct TrackerSummaryEntry: Decodable {
    var tier = 0
    var isBackup = false
    /// -1 until the tracker has been scraped.
    var seederCount = -1
    var leecherCount = -1
    /// 0 inactive, 1 waiting, 2 queued, 3 announcing.
    var announceState = 0
    var hasAnnounced = false
    var lastAnnounceSucceeded = false
    var lastAnnounceResult = ""

    private enum CodingKeys: String, CodingKey {
        case tier, isBackup, seederCount, leecherCount, announceState
        case hasAnnounced, lastAnnounceSucceeded, lastAnnounceResult
    }

    init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        tier = container.lenient(.tier, default: 0)
        isBackup = container.lenient(.isBackup, default: false)
        seederCount = container.lenient(.seederCount, default: -1)
        leecherCount = container.lenient(.leecherCount, default: -1)
        announceState = container.lenient(.announceState, default: 0)
        hasAnnounced = container.lenient(.hasAnnounced, default: false)
        lastAnnounceSucceeded = container.lenient(.lastAnnounceSucceeded, default: false)
        lastAnnounceResult = container.lenient(.lastAnnounceResult, default: "")
    }

    init(
        tier: Int = 0, isBackup: Bool = false, seederCount: Int = -1, leecherCount: Int = -1,
        announceState: Int = 0, hasAnnounced: Bool = false, lastAnnounceSucceeded: Bool = false,
        lastAnnounceResult: String = ""
    ) {
        self.tier = tier
        self.isBackup = isBackup
        self.seederCount = seederCount
        self.leecherCount = leecherCount
        self.announceState = announceState
        self.hasAnnounced = hasAnnounced
        self.lastAnnounceSucceeded = lastAnnounceSucceeded
        self.lastAnnounceResult = lastAnnounceResult
    }
}
