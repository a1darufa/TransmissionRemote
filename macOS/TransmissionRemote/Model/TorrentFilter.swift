import Foundation
import TransmissionKit

/// The status filters in the sidebar, matching Transmission Remote GUI's filter pane.
enum StatusFilter: String, CaseIterable, Identifiable {
    case all, downloading, seeding, completed, active, inactive, stopped, waiting, verifying, error

    var id: Self { self }

    var title: LocalizedStringResource {
        switch self {
        case .all: "All"
        case .downloading: "Downloading"
        case .seeding: "Seeding"
        case .completed: "Completed"
        case .active: "Active"
        case .inactive: "Inactive"
        case .stopped: "Stopped"
        case .waiting: "Waiting"
        case .verifying: "Verifying"
        case .error: "Error"
        }
    }

    var systemImage: String {
        switch self {
        case .all: "tray.full"
        case .downloading: "arrow.down.circle"
        case .seeding: "arrow.up.circle"
        case .completed: "checkmark.circle"
        case .active: "bolt.horizontal.circle"
        case .inactive: "moon.circle"
        case .stopped: "pause.circle"
        case .waiting: "clock"
        case .verifying: "arrow.triangle.2.circlepath"
        case .error: "exclamationmark.triangle"
        }
    }

    func matches(_ torrent: Torrent) -> Bool {
        switch self {
        case .all: true
        case .downloading: torrent.status == .downloading || torrent.status == .queuedToDownload
        case .seeding: torrent.status == .seeding || torrent.status == .queuedToSeed
        case .completed: torrent.isComplete
        case .active: torrent.isActive
        case .inactive: !torrent.isActive && torrent.status != .stopped
        case .stopped: torrent.status == .stopped
        case .waiting: torrent.status.isQueued
        case .verifying: torrent.status == .verifying || torrent.status == .queuedToVerify
        case .error: torrent.hasError
        }
    }
}

/// Anything selectable in the sidebar.
enum SidebarItem: Hashable {
    case status(StatusFilter)
    case folder(String)
    case tracker(String)
    case label(String)

    func matches(_ torrent: Torrent) -> Bool {
        switch self {
        case .status(let filter): filter.matches(torrent)
        case .folder(let path): torrent.downloadDir.trimmingTrailingSlash == path
        case .tracker(let host): torrent.trackerHosts.contains(host)
        case .label(let label): torrent.labels.contains(label)
        }
    }
}

/// Counts for every sidebar entry, computed in one pass over the torrent list.
struct SidebarSummary {
    struct Entry: Identifiable {
        let name: String
        let count: Int
        /// What the sidebar shows; for folders, a short form of `name`.
        var displayName: String
        var id: String { name }

        init(name: String, count: Int, displayName: String? = nil) {
            self.name = name
            self.count = count
            self.displayName = displayName ?? name
        }
    }

    private(set) var statusCounts: [StatusFilter: Int] = [:]
    private(set) var folders: [Entry] = []
    private(set) var trackers: [Entry] = []
    private(set) var labels: [Entry] = []

    init(torrents: [Torrent]) {
        var folderCounts: [String: Int] = [:]
        var trackerCounts: [String: Int] = [:]
        var labelCounts: [String: Int] = [:]

        for torrent in torrents {
            for filter in StatusFilter.allCases where filter.matches(torrent) {
                statusCounts[filter, default: 0] += 1
            }
            folderCounts[torrent.downloadDir.trimmingTrailingSlash, default: 0] += 1
            for host in torrent.trackerHosts { trackerCounts[host, default: 0] += 1 }
            for label in torrent.labels { labelCounts[label, default: 0] += 1 }
        }

        folders = Self.folderEntries(folderCounts)
        trackers = Self.sortedEntries(trackerCounts)
        labels = Self.sortedEntries(labelCounts)
    }

    /// Folders by their last name, sorted the way they're shown. Two folders with the same last
    /// name get their parent folder added, so they can be told apart (transgui issue #979).
    private static func folderEntries(_ counts: [String: Int]) -> [Entry] {
        let paths = counts.filter { !$0.key.isEmpty }
        let pathsByName = Dictionary(grouping: paths.keys, by: \.lastPathComponentForDisplay)
        return paths.map { path, count in
            let name = path.lastPathComponentForDisplay
            let isAmbiguous = (pathsByName[name]?.count ?? 0) > 1
            let parent = path.parentComponentForDisplay
            let displayName = isAmbiguous && !parent.isEmpty ? "\(name) — \(parent)" : name
            return Entry(name: path, count: count, displayName: displayName)
        }
        .sorted { lhs, rhs in
            let order = lhs.displayName.localizedStandardCompare(rhs.displayName)
            return order == .orderedSame ? lhs.name < rhs.name : order == .orderedAscending
        }
    }

    private static func sortedEntries(_ counts: [String: Int]) -> [Entry] {
        counts.filter { !$0.key.isEmpty }
            .map { Entry(name: $0.key, count: $0.value) }
            .sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
    }
}

extension String {
    var trimmingTrailingSlash: String {
        count > 1 && (hasSuffix("/") || hasSuffix("\\")) ? String(dropLast()) : self
    }

    /// The last path component, for showing long remote paths compactly. Windows daemons
    /// use backslashes.
    var lastPathComponentForDisplay: String {
        pathComponentsForDisplay.last ?? self
    }

    /// The folder above the last one, or empty at the top.
    var parentComponentForDisplay: String {
        let components = pathComponentsForDisplay
        return components.count > 1 ? components[components.count - 2] : ""
    }

    private var pathComponentsForDisplay: [String] {
        trimmingTrailingSlash.split { $0 == "/" || $0 == "\\" }.map(Self.init)
    }
}
