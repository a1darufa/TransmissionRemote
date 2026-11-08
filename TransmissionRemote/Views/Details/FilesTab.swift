import SwiftUI
import TransmissionKit

struct FilesTab: View {
    @Environment(AppModel.self) private var model
    let torrent: Torrent
    let details: TorrentDetails?

    var body: some View {
        if let files = details?.files {
            if files.isEmpty {
                ContentUnavailableView(
                    torrent.metadataPercentComplete < 1 ? "Waiting for Metadata" : "No Files",
                    systemImage: "doc.questionmark"
                )
            } else {
                FileTreeTable(
                    nodes: FileTree.build(from: files),
                    showsProgress: true,
                    onToggleWanted: toggle,
                    onSetPriority: setPriority,
                    fileActions: fileActions
                )
            }
        } else {
            ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }

    private func toggle(_ node: FileNode) {
        let wanted = node.wantedState != .all
        model.connection.apply(.wanted(wanted, files: node.fileIDs, in: torrent.id))
    }

    private func setPriority(_ priority: Priority, files: [Int]) {
        model.connection.apply(.priority(priority, files: files, in: torrent.id))
    }

    private var fileActions: FileActions {
        let torrent = torrent
        let model = model
        let folder = model.currentFolder(of: torrent)
        let serverPath = { (node: FileNode) in (folder as NSString).appendingPathComponent(node.relativePath) }
        return FileActions(
            isLocallyAvailable: { model.localFileLocation(forRemotePath: serverPath($0)) != nil },
            open: { model.openLocally(remotePath: serverPath($0)) },
            reveal: { model.revealLocally(remotePaths: $0.map(serverPath)) },
            rename: { model.router.sheet = .renameFile(torrentID: torrent.id, path: $0.relativePath) },
            copyPaths: { model.copyPaths($0.map(serverPath)) }
        )
    }
}
