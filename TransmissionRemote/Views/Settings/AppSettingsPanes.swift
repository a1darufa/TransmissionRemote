import SwiftUI

/// "Application options" → General, named like Transmission Remote GUI's tab.
struct GeneralSettings: View {
    @AppStorage(AppSettings.refreshInterval) private var refreshInterval = 5
    @AppStorage(AppSettings.backgroundRefreshInterval) private var backgroundRefreshInterval = 20
    @AppStorage(AppSettings.showAddTorrentDialog) private var showsAddTorrentDialog = true
    @AppStorage(AppSettings.checkForUpdates) private var checksForUpdates = true
    @Environment(AppModel.self) private var model

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
            Section {
                Toggle("Check for updates once a day", isOn: $checksForUpdates)
                LabeledContent("Version \(UpdateChecker.currentVersionText)") {
                    Button("Check Now") {
                        Task { await model.updates.check(userInitiated: true) }
                    }
                    .disabled(model.updates.isChecking)
                }
            } header: {
                Text("Updates")
            } footer: {
                Text("Asks GitHub for the newest release. Nothing is downloaded or installed automatically.")
            }
        }
        .formStyle(.grouped)
    }
}

/// "Application options" → Advanced. Transmission Remote GUI puts its tray icon options here;
/// the Mac equivalent is the menu bar icon.
struct AdvancedSettings: View {
    @Environment(AppModel.self) private var model
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
            PeerCountrySettings(resolver: model.peerInfo)
        }
        .formStyle(.grouped)
    }
}

/// The country database behind the Peers tab's Country column. The on/off switches live in the
/// peer list's menu, like in Transmission Remote GUI.
private struct PeerCountrySettings: View {
    let resolver: PeerInfoResolver

    var body: some View {
        Section {
            LabeledContent("Country database") {
                HStack {
                    Text(status).foregroundStyle(.secondary)
                    Button(resolver.databaseMonth == nil ? "Download Now" : "Update Now") {
                        Task { await resolver.downloadDatabase() }
                    }
                    .disabled(resolver.databaseState == .downloading)
                }
            }
        } header: {
            Text("Peers")
        } footer: {
            Text("""
                Right-click the peer list to turn host names and countries on or off. Country data is \
                [IP to Country Lite by DB-IP](https://db-ip.com), licensed under CC BY 4.0, and updates monthly.
                """)
        }
    }

    private var status: String {
        switch resolver.databaseState {
        case .downloading: return String(localized: "Downloading…")
        case .failed: return String(localized: "Download failed")
        case .notDownloaded, .loading, .ready:
            guard let month = resolver.databaseMonth, let date = Self.date(fromMonth: month) else {
                return String(localized: "Not downloaded")
            }
            return date.formatted(.dateTime.month(.wide).year())
        }
    }

    /// "2026-11" → the middle of that month, so no time zone moves it into another month.
    private static func date(fromMonth month: String) -> Date? {
        let parts = month.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 2 else { return nil }
        return Calendar(identifier: .gregorian).date(from: DateComponents(year: parts[0], month: parts[1], day: 15))
    }
}
