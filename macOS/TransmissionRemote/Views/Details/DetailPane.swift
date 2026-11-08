import SwiftUI
import TransmissionKit

/// The bottom pane with General / Trackers / Peers / Files / Speed tabs.
struct DetailPane: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        @Bindable var connection = model.connection

        VStack(spacing: 0) {
            Picker("Detail", selection: $connection.detailTab) {
                ForEach(DetailTab.allCases) { tab in
                    Text(tab.title).tag(tab)
                }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .fixedSize()
            .padding(.vertical, 6)

            Divider()

            content
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }

    @ViewBuilder
    private var content: some View {
        let connection = model.connection
        if connection.detailTab == .speed {
            SpeedTab(history: connection.speedHistory)
        } else if let torrent = connection.singleSelectedTorrent {
            // Details for a previously selected torrent may still be loaded; never show those.
            let details = connection.details?.id == torrent.id ? connection.details : nil
            switch connection.detailTab {
            case .general: GeneralTab(torrent: torrent, details: details)
            case .trackers: TrackersTab(torrent: torrent, details: details)
            case .peers: PeersTab(peers: details?.peers)
            case .files: FilesTab(torrent: torrent, details: details)
            case .speed: EmptyView()
            }
        } else {
            ContentUnavailableView(
                connection.selection.isEmpty
                    ? String(localized: "No Torrent Selected")
                    : String(localized: "\(connection.selection.count) Torrents Selected"),
                systemImage: "square.stack.3d.up"
            )
        }
    }
}
