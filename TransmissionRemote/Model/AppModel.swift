import Foundation
import Observation
import TransmissionKit

/// Sheets the main window can show. Menu commands set these through the router.
enum MainSheet: Identifiable {
    case addLink
    case move(Set<Torrent.ID>)
    case rename(Torrent)
    /// A file or folder inside a torrent; `path` is relative to the torrent's download folder.
    case renameFile(torrentID: Torrent.ID, path: String)
    case labels(Set<Torrent.ID>)
    case options(Set<Torrent.ID>)
    case editTrackers(Torrent.ID)

    var id: String {
        switch self {
        case .addLink: "addLink"
        case .move(let ids): "move-\(ids.sorted())"
        case .rename(let torrent): "rename-\(torrent.id)"
        case .renameFile(let torrentID, let path): "rename-\(torrentID)-\(path)"
        case .labels(let ids): "labels-\(ids.sorted())"
        case .options(let ids): "options-\(ids.sorted())"
        case .editTrackers(let id): "trackers-\(id)"
        }
    }
}

struct RemovalRequest: Identifiable {
    let id = UUID()
    let ids: Set<Torrent.ID>
    let deletingData: Bool
}

/// UI presentation state shared by the main window, its toolbar and the menu bar commands.
@Observable
final class Router {
    var sheet: MainSheet?
    var removal: RemovalRequest?
    var isImportingTorrentFiles = false
}

/// The app's root object: saved servers, the live connection and the add-torrent queue.
@Observable
final class AppModel {
    let servers = ServerStore()
    let connection = ServerConnection()
    let router = Router()
    let updates = UpdateChecker()
    let addFlow: AddTorrentFlow

    init() {
        addFlow = AddTorrentFlow(connection: connection)
    }

    func start() {
        connectToActiveServer()
        updates.startAutomaticChecks()
    }

    func connectToActiveServer() {
        guard let server = servers.activeServer else {
            connection.disconnect()
            return
        }
        connection.connect(to: server, password: servers.password(for: server.id))
    }

    func switchToServer(_ id: UUID) {
        servers.activate(id)
        connectToActiveServer()
    }

    /// Pins the certificate the server presented, after the user confirmed its fingerprint.
    func trustCertificate(fingerprint: String) {
        guard var server = servers.activeServer else { return }
        server.pinnedCertificateFingerprint = fingerprint
        servers.save(server)
        connectToActiveServer()
    }

    func allowUnencryptedConnection() {
        guard var server = servers.activeServer else { return }
        server.allowsUnencryptedInternetConnection = true
        servers.save(server)
        connectToActiveServer()
    }

    func recordFolderUse(_ path: String) {
        guard let id = connection.profile?.id else { return }
        servers.recordFolderUse(path, onServer: id)
    }

    /// Download folders to suggest: the server's history, then its default folder.
    var suggestedFolders: [String] {
        let history = connection.profile.flatMap { servers.server(withID: $0.id) }?.suggestedFolders ?? []
        let defaultFolder = connection.session?.downloadDir.map { [$0] } ?? []
        var seen = Set<String>()
        return (history + defaultFolder).filter { seen.insert($0).inserted }
    }

    /// Every folder the app knows on the server, as a tree for the folder browser:
    /// where torrents are saved, plus the history and the default folder.
    var knownFolders: [RemoteFolder] {
        var torrentCounts: [String: Int] = [:]
        for torrent in connection.torrents {
            torrentCounts[torrent.downloadDir, default: 0] += 1
        }
        for folder in suggestedFolders where torrentCounts[folder] == nil {
            torrentCounts[folder] = 0
        }
        return RemoteFolderTree.build(torrentCounts: torrentCounts)
    }
}
