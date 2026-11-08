import SwiftUI

/// "Application options" → General, named like Transmission Remote GUI's tab.
struct GeneralSettings: View {
    @AppStorage(AppSettings.refreshInterval) private var refreshInterval = 5
    @AppStorage(AppSettings.backgroundRefreshInterval) private var backgroundRefreshInterval = 20
    @AppStorage(AppSettings.showAddTorrentDialog) private var showsAddTorrentDialog = true

    // Includes 20 s, the default background interval, so that picker never shows blank.
    private let intervals = [1, 2, 3, 5, 10, 15, 20, 30, 60]

    var body: some View {
        Form {
            Section("Data display") {
                Picker("Data refresh interval", selection: $refreshInterval) {
                    ForEach(intervals, id: \.self) { Text("Every \($0) s").tag($0) }
                }
                Picker("Data refresh interval in the background", selection: $backgroundRefreshInterval) {
                    ForEach(intervals, id: \.self) { Text("Every \($0) s").tag($0) }
                }
            }
            Section("Add torrent") {
                Toggle("Prompt for download options when adding a new torrent", isOn: $showsAddTorrentDialog)
            }
        }
        .formStyle(.grouped)
    }
}

/// "Application options" → Advanced. Transmission Remote GUI puts its tray icon options here;
/// the Mac equivalent is the menu bar icon.
struct AdvancedSettings: View {
    @AppStorage(AppSettings.notifyOnCompletion) private var notifiesOnCompletion = true
    @AppStorage(AppSettings.showMenuBarExtra) private var showsMenuBarExtra = true
    @AppStorage(AppSettings.showSpeedsInMenuBar) private var showsSpeedsInMenuBar = false

    var body: some View {
        Form {
            Section("Menu bar icon") {
                Toggle("Show in menu bar", isOn: $showsMenuBarExtra)
                Toggle("Show transfer speeds in the menu bar", isOn: $showsSpeedsInMenuBar)
                    .disabled(!showsMenuBarExtra)
                Toggle("Notify when a download completes", isOn: $notifiesOnCompletion)
            }
        }
        .formStyle(.grouped)
    }
}
