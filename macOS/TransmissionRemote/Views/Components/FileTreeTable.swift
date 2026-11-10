import SwiftUI
import TransmissionKit

/// What the Files tab can do with files besides choosing what to download. The Add Torrent
/// sheet has none of these, because nothing is on the server yet.
struct FileActions {
    /// Whether a path mapping covers the node, so it can be opened or shown in Finder.
    let isLocallyAvailable: (FileNode) -> Bool
    let open: (FileNode) -> Void
    let reveal: ([FileNode]) -> Void
    let rename: (FileNode) -> Void
    /// False on servers too old to rename (before Transmission 2.80).
    var canRename = true
    let copyPaths: ([FileNode]) -> Void
}

/// A hierarchical file list with download checkboxes. Shared by the Files tab and the
/// Add Torrent sheet; progress and priority columns are optional.
struct FileTreeTable: View {
    let nodes: [FileNode]
    var showsProgress = false
    /// The checkbox: flips one file or folder, wanting all of a folder that's only partly wanted.
    let onToggleWanted: (FileNode) -> Void
    /// Download Selected and Don't Download Selected: every file of the given nodes becomes wanted
    /// or unwanted, whatever its state. A toggle can't do this: on a partly wanted folder it would
    /// download everything even for "Don't Download".
    let onSetWanted: (Bool, [FileNode]) -> Void
    var onSetPriority: ((Priority, [Int]) -> Void)?
    var fileActions: FileActions?

    @State private var selection: Set<FileNode.ID> = []

    var body: some View {
        Table(nodes, children: \.children, selection: $selection) {
            TableColumn("Name") { node in
                HStack(spacing: 6) {
                    WantedCheckbox(state: node.wantedState) { onToggleWanted(node) }
                    Image(systemName: node.isFolder ? "folder.fill" : "doc")
                        .foregroundStyle(node.isFolder ? Color.accentColor : Color.secondary)
                    Text(node.name)
                        .lineLimit(1)
                        .truncationMode(.middle)
                }
                .help(node.id)
            }
            .width(min: 200, ideal: 380)

            TableColumn("Size") { node in
                Text(node.size.formattedBytes).monospacedDigit()
            }
            .width(min: 60, ideal: 80)
            .alignment(.trailing)

            if showsProgress {
                TableColumn("Done") { node in
                    HStack(spacing: 6) {
                        ProgressView(value: node.progress).controlSize(.small)
                        Text(Format.percent(node.progress)).font(.caption).monospacedDigit()
                            .frame(minWidth: 40, alignment: .trailing)
                    }
                }
                .width(min: 90, ideal: 130)
            }

            if let onSetPriority {
                TableColumn("Priority") { node in
                    PriorityMenu(priority: node.priority) { onSetPriority($0, node.fileIDs) }
                }
                .width(min: 70, ideal: 90)
            }
        }
        .contextMenu(forSelectionType: FileNode.ID.self) { ids in
            selectionMenu(for: ids)
        } primaryAction: { ids in
            openOnDoubleClick(ids)
        }
    }

    @ViewBuilder
    private func selectionMenu(for ids: Set<FileNode.ID>) -> some View {
        let selected = Self.flatten(nodes).filter { ids.contains($0.id) }
        if let fileActions {
            fileMenu(for: selected, actions: fileActions)
            Divider()
        }
        // Not just "Download": that word is also the Download pane's name, a noun in many languages.
        Button("Download Selected") { onSetWanted(true, selected) }
            .disabled(selected.allSatisfy { $0.wantedState == .all })
        Button("Don't Download Selected") { onSetWanted(false, selected) }
            .disabled(selected.allSatisfy { $0.wantedState == .none })
        if let onSetPriority {
            Divider()
            ForEach(Priority.allCases.reversed(), id: \.self) { priority in
                Button(String(localized: "\(String(localized: priority.title)) Priority")) {
                    onSetPriority(priority, selected.flatMap(\.fileIDs))
                }
            }
        }
    }

    /// Open, Show in Finder, Rename and Copy Path, in the order transgui lists them.
    @ViewBuilder
    private func fileMenu(for selected: [FileNode], actions: FileActions) -> some View {
        let single = selected.count == 1 ? selected.first : nil
        Button("Open") { single.map(actions.open) }
            .disabled(single.map(actions.isLocallyAvailable) != true)
        Button("Show in Finder") { actions.reveal(selected) }
            .disabled(!selected.contains(where: actions.isLocallyAvailable))
        Divider()
        Button("Rename…") { single.map(actions.rename) }
            .disabled(single == nil || !actions.canRename)
        Button("Copy Path") { actions.copyPaths(selected) }
            .disabled(selected.isEmpty)
    }

    /// Double-clicking a file opens it, like in Finder. Folders are left alone.
    private func openOnDoubleClick(_ ids: Set<FileNode.ID>) {
        guard let fileActions, ids.count == 1,
              let node = Self.flatten(nodes).first(where: { ids.contains($0.id) }),
              !node.isFolder, fileActions.isLocallyAvailable(node)
        else { return }
        fileActions.open(node)
    }

    private static func flatten(_ nodes: [FileNode]) -> [FileNode] {
        nodes.flatMap { [$0] + flatten($0.children ?? []) }
    }
}

/// A checkbox that can also show a mixed state for partially selected folders.
private struct WantedCheckbox: View {
    let state: FileNode.WantedState
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: symbol)
                .foregroundStyle(state == .none ? Color.secondary : Color.accentColor)
        }
        .buttonStyle(.borderless)
        .accessibilityLabel(state == .none ? "Not downloading" : "Downloading")
    }

    private var symbol: String {
        switch state {
        case .all: "checkmark.square.fill"
        case .mixed: "minus.square.fill"
        case .none: "square"
        }
    }
}

private struct PriorityMenu: View {
    let priority: Priority?
    let onSelect: (Priority) -> Void

    var body: some View {
        Menu {
            ForEach(Priority.allCases.reversed(), id: \.self) { option in
                Button(String(localized: option.title)) { onSelect(option) }
            }
        } label: {
            Text(priority.map { String(localized: $0.title) } ?? String(localized: "Mixed"))
        }
        .menuStyle(.borderlessButton)
        .fixedSize()
    }
}
