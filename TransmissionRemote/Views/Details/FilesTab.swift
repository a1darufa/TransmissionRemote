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
                    onSetPriority: setPriority
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
}
