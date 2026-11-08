import SwiftUI
import TransmissionKit

/// A hierarchical file list with download checkboxes. Shared by the Files tab and the
/// Add Torrent sheet; progress and priority columns are optional.
struct FileTreeTable: View {
    let nodes: [FileNode]
    var showsProgress = false
    let onToggleWanted: (FileNode) -> Void
    var onSetPriority: ((Priority, [Int]) -> Void)?

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
        }
    }

    @ViewBuilder
    private func selectionMenu(for ids: Set<FileNode.ID>) -> some View {
        let selected = Self.flatten(nodes).filter { ids.contains($0.id) }
        Button("Download") { selected.filter { $0.wantedState != .all }.forEach(onToggleWanted) }
        Button("Don't Download") { selected.filter { $0.wantedState != .none }.forEach(onToggleWanted) }
        if let onSetPriority {
            Divider()
            ForEach(Priority.allCases.reversed(), id: \.self) { priority in
                Button(String(localized: "\(String(localized: priority.title)) Priority")) {
                    onSetPriority(priority, selected.flatMap(\.fileIDs))
                }
            }
        }
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
