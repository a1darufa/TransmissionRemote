import SwiftUI

/// The one Settings window, laid out like System Settings: a sidebar with this app's options
/// and the connected server's options, each pane with a coloured icon.
struct SettingsView: View {
    @Environment(AppModel.self) private var model
    @AppStorage(AppSettings.settingsPane) private var selection = SettingsPane.general
    @State private var serverDraft = ServerSettingsDraft()
    /// The server the per-server panes (Transmission, Proxy, Paths) edit.
    @State private var selectedServerID: UUID?

    var body: some View {
        NavigationSplitView {
            List(selection: sidebarSelection) {
                Section("Application Options") {
                    ForEach(SettingsPane.applicationPanes) { SettingsSidebarRow(pane: $0) }
                }
                Section("Transmission Options") {
                    ForEach(SettingsPane.transmissionPanes) { SettingsSidebarRow(pane: $0) }
                }
            }
            // The column width alone is ignored; the minimum keeps "Network (WAN)" from truncating.
            .frame(minWidth: 220)
            .navigationSplitViewColumnWidth(min: 220, ideal: 220, max: 220)
            .toolbar(removing: .sidebarToggle)
        } detail: {
            detail
                .navigationTitle(Text(selection.title))
        }
        .frame(width: 800, height: 600)
        .onAppear {
            serverDraft.sync(with: model.connection)
            selectedServerID = selectedServerID ?? model.servers.activeServer?.id
        }
        .onChange(of: model.connection.session) { serverDraft.sync(with: model.connection) }
        .onChange(of: model.connection.profile?.id) { serverDraft.sync(with: model.connection) }
    }

    @ViewBuilder private var detail: some View {
        switch selection {
        case .general:
            GeneralSettings()
        case .advanced:
            AdvancedSettings()
        case .transmission:
            ServerConnectionsPane(selectedID: $selectedServerID)
        case .proxy:
            ProxySettingsPane(serverID: $selectedServerID)
        case .paths:
            PathsSettings(serverID: $selectedServerID)
        case .misc:
            MiscSettings()
        case .download:
            ServerSettingsDetail(draft: serverDraft) { DownloadSettings(settings: $0) }
        case .network:
            ServerSettingsDetail(draft: serverDraft) { NetworkSettings(settings: $0) }
        case .bandwidth:
            ServerSettingsDetail(draft: serverDraft) { BandwidthSettings(settings: $0) }
        case .queue:
            ServerSettingsDetail(draft: serverDraft) { QueueSettings(settings: $0) }
        case .about:
            ServerSettingsDetail(draft: serverDraft, showsApplyBar: false) { _ in ServerAboutView() }
        }
    }

    /// Ignores deselection, so a pane is always shown.
    private var sidebarSelection: Binding<SettingsPane?> {
        Binding {
            selection
        } set: { newSelection in
            if let newSelection { selection = newSelection }
        }
    }
}

private struct SettingsSidebarRow: View {
    let pane: SettingsPane

    var body: some View {
        Label {
            Text(pane.title)
        } icon: {
            SettingsIcon(systemImage: pane.systemImage, tint: pane.tint)
        }
    }
}

/// A white symbol on a coloured rounded square, like the icons in System Settings.
struct SettingsIcon: View {
    let systemImage: String
    let tint: Color

    var body: some View {
        Image(systemName: systemImage)
            .font(.system(size: 11, weight: .semibold))
            .foregroundStyle(.white)
            .frame(width: 20, height: 20)
            .background(tint.gradient, in: RoundedRectangle(cornerRadius: 5, style: .continuous))
    }
}

/// Opens Settings on a given pane; the Server menu's "Server Settings…" opens Download.
struct SettingsPaneLink<Label: View>: View {
    @Environment(\.openSettings) private var openSettings
    @AppStorage(AppSettings.settingsPane) private var selection = SettingsPane.general
    let pane: SettingsPane
    @ViewBuilder let label: () -> Label

    var body: some View {
        Button {
            selection = pane
            openSettings()
            // From the menu bar extra the app may still be in the background.
            NSApplication.shared.activate()
        } label: {
            label()
        }
    }
}
