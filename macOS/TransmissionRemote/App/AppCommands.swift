import SwiftUI
import TransmissionKit

struct AppCommands: Commands {
    let model: AppModel
    @AppStorage(AppSettings.showDetailPane) private var showsDetailPane = true

    var body: some Commands {
        SidebarCommands()
        ToolbarCommands()

        CommandGroup(after: .appInfo) {
            Button("Check for Updates…") {
                Task { await model.updates.check(userInitiated: true) }
            }
            .disabled(model.updates.isChecking)
        }

        CommandGroup(after: .help) {
            Button("Show Log Files") { AppDiagnostics.showLogFiles() }
        }

        CommandGroup(replacing: .newItem) {
            Group {
                Button("Add Torrent File…") { model.router.isImportingTorrentFiles = true }
                    .keyboardShortcut("o")
                Button("Add Torrent Link…") { model.router.sheet = .addLink }
                    .keyboardShortcut("u")
            }
            .disabled(!model.connection.isConnected)
        }

        CommandGroup(after: .sidebar) {
            Toggle("Show Info Pane", isOn: $showsDetailPane)
                .keyboardShortcut("i")
        }

        CommandMenu("Torrent") {
            TorrentActionsMenu(model: model, ids: model.connection.selection, showsShortcuts: true)
        }

        CommandMenu("Server") {
            ServerMenuItems(model: model)
        }
    }
}

/// Server-wide actions, shared by the Server menu and the menu bar extra.
struct ServerMenuItems: View {
    let model: AppModel

    private var connection: ServerConnection { model.connection }

    var body: some View {
        Button("Start All") { connection.performOnAll(.start) }
            .keyboardShortcut("/", modifiers: [.command, .option])
            .disabled(!connection.isConnected)
        Button("Stop All") { connection.performOnAll(.stop) }
            .keyboardShortcut(".", modifiers: [.command, .option])
            .disabled(!connection.isConnected)

        Divider()

        Toggle("Alternative Speed Limits", isOn: Binding(
            get: { connection.session?.altSpeedEnabled ?? false },
            set: { connection.setAlternativeSpeedEnabled($0) }
        ))
        .keyboardShortcut("t", modifiers: [.command, .shift])
        .disabled(!connection.isConnected)

        Group {
            SpeedLimitMenu(direction: .download, current: .some(connection.globalSpeedLimit(.download))) {
                connection.setGlobalSpeedLimit($0, direction: .download)
            }
            SpeedLimitMenu(direction: .upload, current: .some(connection.globalSpeedLimit(.upload))) {
                connection.setGlobalSpeedLimit($0, direction: .upload)
            }
        }
        .disabled(!connection.isConnected)

        SettingsPaneLink(pane: .download) { Text("Server Settings…") }
            .keyboardShortcut(",", modifiers: [.command, .option])
            .disabled(!connection.isConnected)

        Divider()

        if model.servers.servers.count > 1 {
            Picker("Server", selection: Binding(
                get: { model.servers.activeServer?.id },
                set: { if let id = $0 { model.switchToServer(id) } }
            )) {
                ForEach(model.servers.servers) { server in
                    Text(server.displayName).tag(Optional(server.id))
                }
            }
        }
        Button("Reconnect") { model.connectToActiveServer() }
            .keyboardShortcut("r", modifiers: [.command, .shift])
            .disabled(model.servers.activeServer == nil)
        Button("Disconnect") { connection.disconnect() }
            .disabled(connection.state == .disconnected)

        Divider()

        ManageServersLink { Text("Manage Servers…") }
    }
}
