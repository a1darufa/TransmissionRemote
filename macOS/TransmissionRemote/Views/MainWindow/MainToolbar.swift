import SwiftUI
import TransmissionKit

struct MainToolbar: ToolbarContent {
    @Environment(AppModel.self) private var model

    private var connection: ServerConnection { model.connection }
    private var selection: Set<Torrent.ID> { connection.selection }

    var body: some ToolbarContent {
        ToolbarItemGroup(placement: .primaryAction) {
            Group {
                Button("Add Torrent File", systemImage: "doc.badge.plus") {
                    model.router.isImportingTorrentFiles = true
                }
                .help("Add a .torrent file")
                Button("Add Link", systemImage: "link.badge.plus") {
                    model.router.sheet = .addLink
                }
                .help("Add a magnet link or torrent URL")
            }
            .disabled(!connection.isConnected)
        }

        ToolbarSpacer(.fixed, placement: .primaryAction)

        ToolbarItemGroup(placement: .primaryAction) {
            Group {
                Button("Start", systemImage: "play.fill") { connection.perform(.start, on: selection) }
                    .help("Start the selected torrents")
                Button("Stop", systemImage: "pause.fill") { connection.perform(.stop, on: selection) }
                    .help("Stop the selected torrents")
                Button("Remove", systemImage: "trash") {
                    model.router.removal = RemovalRequest(ids: selection, deletingData: false)
                }
                .help("Remove the selected torrents")
            }
            .disabled(selection.isEmpty || !connection.isConnected)
        }

        ToolbarSpacer(.fixed, placement: .primaryAction)

        ToolbarItemGroup(placement: .primaryAction) {
            Toggle(isOn: Binding(
                get: { connection.session?.altSpeedEnabled ?? false },
                set: { connection.setAlternativeSpeedEnabled($0) }
            )) {
                Label("Alternative Speed Limits", systemImage: "tortoise")
            }
            .help("Alternative speed limits")
            .disabled(!connection.isConnected)

            // One Settings window holds both the app's options and the server's options.
            SettingsLink {
                Label("Settings", systemImage: "gearshape")
            }
            .help("Settings for this app and the connected Transmission server")

            serverMenu
        }
    }

    private var serverMenu: some View {
        Menu {
            ForEach(model.servers.servers) { server in
                Button {
                    model.switchToServer(server.id)
                } label: {
                    if server.id == connection.profile?.id {
                        Label(server.displayName, systemImage: "checkmark")
                    } else {
                        Text(server.displayName)
                    }
                }
            }
            Divider()
            Button("Reconnect") { model.connectToActiveServer() }
            ManageServersLink { Text("Manage Servers…") }
        } label: {
            Label("Server", systemImage: "server.rack")
        }
        .help("Switch server")
    }
}
