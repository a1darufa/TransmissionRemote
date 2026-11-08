import SwiftUI

/// The panes of the Settings window, in two sidebar groups named after Transmission Remote GUI's
/// two dialogs: "Application options" (this app) and "Transmission options" (the connected daemon).
nonisolated enum SettingsPane: String, CaseIterable, Identifiable {
    case general, advanced, transmission, proxy, paths, misc
    case download, network, bandwidth, queue, about

    static let applicationPanes: [Self] = [.general, .advanced, .transmission, .proxy, .paths, .misc]
    static let transmissionPanes: [Self] = [.download, .network, .bandwidth, .queue, .about]

    var id: Self { self }

    var title: LocalizedStringResource {
        switch self {
        case .general: "General"
        case .advanced: "Advanced"
        case .transmission: "Transmission"
        case .proxy: "Proxy"
        case .paths: "Paths"
        case .misc: "Misc"
        case .download: "Download"
        case .network: "Network (WAN)"
        case .bandwidth: "Bandwidth"
        case .queue: "Queue"
        case .about: "About"
        }
    }

    var systemImage: String {
        switch self {
        case .general: "gearshape.fill"
        case .advanced: "gearshape.2.fill"
        case .transmission: "server.rack"
        case .proxy: "network.badge.shield.half.filled"
        case .paths: "folder.fill"
        case .misc: "ellipsis"
        case .download: "arrow.down"
        case .network: "network"
        case .bandwidth: "speedometer"
        case .queue: "list.number"
        case .about: "info"
        }
    }

    /// The colour behind the pane's icon, as in System Settings.
    var tint: Color {
        switch self {
        case .general, .advanced, .misc, .about: .gray
        case .download: .blue
        case .network: .indigo
        case .transmission: .teal
        case .proxy: .purple
        case .paths: .cyan
        case .bandwidth: .green
        case .queue: .orange
        }
    }
}
