import SwiftUI
import TransmissionKit

struct MainWindow: View {
    @Environment(AppModel.self) private var model
    @State private var sidebarSelection: SidebarItem? = .status(.all)
    @State private var searchText = ""
    @AppStorage(AppSettings.showDetailPane) private var showsDetailPane = true

    private var connection: ServerConnection { model.connection }

    var body: some View {
        NavigationSplitView {
            SidebarView(selection: $sidebarSelection)
                .navigationSplitViewColumnWidth(min: 180, ideal: 220, max: 340)
        } detail: {
            detail
        }
        .toolbar { MainToolbar() }
        .searchable(text: $searchText, placement: .toolbar, prompt: Text("Filter by name"))
        .navigationTitle(connection.profile?.displayName ?? String(localized: "Transmission Remote"))
        .navigationSubtitle(subtitle)
        .modifier(MainWindowPresentation())
        .onOpenURL { model.addFlow.open([$0]) }
        .dropDestination(for: URL.self) { urls, _ in
            guard connection.isConnected else { return false }
            model.addFlow.open(urls)
            return true
        }
    }

    @ViewBuilder
    private var detail: some View {
        switch connection.state {
        case .connected:
            connectedContent
        case .connecting:
            ProgressView("Connecting to \(connection.profile?.displayName ?? "")…")
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        case .reconnecting(let attempt):
            ReconnectingView(attempt: attempt)
        case .disconnected:
            DisconnectedView()
        case .failed(let failure):
            ConnectionFailureView(failure: failure)
        }
    }

    private var connectedContent: some View {
        VSplitView {
            TorrentTableView(torrents: visibleTorrents)
                .frame(minHeight: 160)
                .pasteDestination(for: String.self) { strings in
                    model.addFlow.openLinks(in: strings.joined(separator: "\n"))
                }
            if showsDetailPane {
                DetailPane()
                    .frame(minHeight: 180, idealHeight: 280)
            }
        }
        .safeAreaInset(edge: .bottom, spacing: 0) {
            StatusBarView()
        }
    }

    private var visibleTorrents: [Torrent] {
        let filter = sidebarSelection ?? .status(.all)
        let query = searchText.trimmingCharacters(in: .whitespaces)
        return connection.torrents.filter { torrent in
            filter.matches(torrent) && (query.isEmpty || torrent.name.localizedStandardContains(query))
        }
    }

    private var subtitle: String {
        guard let info = connection.serverInfo, let dialect = connection.dialect else { return "" }
        let protocolName = dialect == .jsonRPC ? "JSON-RPC" : String(localized: "legacy RPC")
        return "Transmission \(info.version) · \(protocolName)"
    }
}
