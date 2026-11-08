import AppKit
import SwiftUI
import TransmissionKit

/// Actions for a set of torrents. Used both as the row context menu and as the Torrent menu,
/// where it also shows keyboard shortcuts.
struct TorrentActionsMenu: View {
    let model: AppModel
    let ids: Set<Torrent.ID>
    var showsShortcuts = false

    private var connection: ServerConnection { model.connection }

    private var singleTorrent: Torrent? {
        guard ids.count == 1, let id = ids.first else { return nil }
        return connection.torrents.first { $0.id == id }
    }

    private var selectedTorrents: [Torrent] {
        connection.torrents.filter { ids.contains($0.id) }
    }

    var body: some View {
        Group {
            // Need a path mapping in Settings → Paths; disabled until the folder is mapped.
            Button("Open", systemImage: "arrow.up.forward.app") {
                if let singleTorrent { model.openLocally(singleTorrent) }
            }
            .disabled(singleTorrent.flatMap(model.localFileLocation(for:)) == nil)
            Button("Show in Finder", systemImage: "magnifyingglass") { model.revealLocally(selectedTorrents) }
                .disabled(!selectedTorrents.contains { model.localFileLocation(for: $0) != nil })

            Divider()

            Button("Start", systemImage: "play.fill") { connection.perform(.start, on: ids) }
                .shortcut("/", enabled: showsShortcuts)
            Button("Start Now", systemImage: "forward.fill") { connection.perform(.startNow, on: ids) }
                .shortcut("/", modifiers: [.command, .shift], enabled: showsShortcuts)
            Button("Stop", systemImage: "pause.fill") { connection.perform(.stop, on: ids) }
                .shortcut(".", enabled: showsShortcuts)

            Divider()

            Button("Verify Local Data", systemImage: "checkmark.shield") { connection.perform(.verify, on: ids) }
            Button("Ask Trackers for More Peers", systemImage: "antenna.radiowaves.left.and.right") {
                connection.perform(.reannounce, on: ids)
            }
            .shortcut("r", enabled: showsShortcuts)

            Divider()

            Button("Move Data…", systemImage: "folder") { model.router.sheet = .move(ids) }
                .shortcut("m", modifiers: [.command, .shift], enabled: showsShortcuts)
            Button("Rename…", systemImage: "pencil") {
                if let singleTorrent { model.router.sheet = .rename(singleTorrent) }
            }
            .disabled(singleTorrent == nil || connection.serverInfo?.supportsRenaming == false)
            if connection.serverInfo?.supportsLabels ?? true {
                Button("Labels…", systemImage: "tag") { model.router.sheet = .labels(ids) }
                    .shortcut("l", enabled: showsShortcuts)
            }
            Button("Torrent Options…", systemImage: "slider.horizontal.3") { model.router.sheet = .options(ids) }
                .shortcut("i", modifiers: [.command, .option], enabled: showsShortcuts)
            SpeedLimitMenu(direction: .download) { connection.setSpeedLimit($0, direction: .download, on: ids) }
            SpeedLimitMenu(direction: .upload) { connection.setSpeedLimit($0, direction: .upload, on: ids) }
            Button("Edit Trackers…", systemImage: "list.bullet.rectangle") {
                if let singleTorrent { model.router.sheet = .editTrackers(singleTorrent.id) }
            }
            .disabled(singleTorrent == nil)

            priorityMenu
            queueMenu

            Divider()

            Button("Copy Magnet Link", systemImage: "link") { copyMagnetLinks() }
                .shortcut("c", modifiers: [.command, .shift], enabled: showsShortcuts)

            Divider()

            Button("Remove…", systemImage: "minus.circle") {
                model.router.removal = RemovalRequest(ids: ids, deletingData: false)
            }
            .shortcut(.delete, enabled: showsShortcuts)
            Button("Remove and Delete Data…", systemImage: "trash") {
                model.router.removal = RemovalRequest(ids: ids, deletingData: true)
            }
            .shortcut(.delete, modifiers: [.command, .option], enabled: showsShortcuts)
        }
        .disabled(ids.isEmpty || !connection.isConnected)
    }

    private var priorityMenu: some View {
        Menu("Bandwidth Priority", systemImage: "gauge.with.dots.needle.50percent") {
            ForEach(Priority.allCases.reversed(), id: \.self) { priority in
                Button(String(localized: priority.title)) { connection.setBandwidthPriority(priority, on: ids) }
            }
        }
    }

    private var queueMenu: some View {
        Menu("Queue", systemImage: "list.number") {
            Button("Move to Top") { connection.move(.top, ids) }
                .shortcut(.upArrow, modifiers: [.command, .option, .shift], enabled: showsShortcuts)
            Button("Move Up") { connection.move(.up, ids) }
                .shortcut(.upArrow, modifiers: [.command, .option], enabled: showsShortcuts)
            Button("Move Down") { connection.move(.down, ids) }
                .shortcut(.downArrow, modifiers: [.command, .option], enabled: showsShortcuts)
            Button("Move to Bottom") { connection.move(.bottom, ids) }
                .shortcut(.downArrow, modifiers: [.command, .option, .shift], enabled: showsShortcuts)
        }
    }

    private func copyMagnetLinks() {
        Task {
            do {
                let links = try await connection.magnetLinks(for: ids)
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(links.joined(separator: "\n"), forType: .string)
            } catch {
                connection.actionError = error.localizedDescription
            }
        }
    }
}

extension View {
    /// A keyboard shortcut that only exists in the menu bar, not in context menus.
    func shortcut(_ key: KeyEquivalent, modifiers: EventModifiers = .command, enabled: Bool) -> some View {
        keyboardShortcut(enabled ? KeyboardShortcut(key, modifiers: modifiers) : nil)
    }
}
