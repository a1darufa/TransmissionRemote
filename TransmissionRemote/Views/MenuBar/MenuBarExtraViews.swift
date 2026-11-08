import AppKit
import SwiftUI

struct MenuBarLabel: View {
    let model: AppModel
    @AppStorage(AppSettings.showSpeedsInMenuBar) private var showsSpeeds = false

    var body: some View {
        if showsSpeeds, let stats = model.connection.stats {
            Text("↓\(stats.downloadSpeed.formattedSpeed) ↑\(stats.uploadSpeed.formattedSpeed)")
                .monospacedDigit()
        } else {
            let symbol = model.connection.isConnected ? "arrow.up.arrow.down.circle.fill" : "arrow.up.arrow.down.circle"
            Image(systemName: symbol)
        }
    }
}

struct MenuBarContent: View {
    let model: AppModel
    @Environment(\.openWindow) private var openWindow

    private var connection: ServerConnection { model.connection }

    var body: some View {
        if let stats = connection.stats, connection.isConnected {
            Text("↓ \(stats.downloadSpeed.formattedSpeed)   ↑ \(stats.uploadSpeed.formattedSpeed)")
            Text("Active: \(stats.activeTorrentCount) of \(stats.torrentCount)")
        } else {
            Text(connection.isConnected ? "Connected" : "Not connected")
        }

        Divider()
        ServerMenuItems(model: model)
        Divider()

        Button("Show Transmission Remote") {
            openWindow(id: SceneID.main)
            NSApplication.shared.activate()
        }
        SettingsLink { Text("Settings…") }

        Divider()
        Button("Quit Transmission Remote") { NSApplication.shared.terminate(nil) }
            .keyboardShortcut("q")
    }
}
