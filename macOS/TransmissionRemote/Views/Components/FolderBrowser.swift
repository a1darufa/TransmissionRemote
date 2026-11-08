import SwiftUI
import TransmissionKit

/// Picks a folder on the server from the ones the app already knows about.
///
/// Transmission can't list the server's folders, so this shows a tree of folders torrents
/// are saved in, the folder history and the default folder. A folder that isn't listed can
/// still be typed into the path field: the server creates missing folders when it saves.
struct FolderBrowser: View {
    let folders: [RemoteFolder]
    let defaultFolder: String?
    let onChoose: (String) -> Void
    let onCancel: () -> Void

    @State private var selection: String?
    @State private var expanded: Set<String>

    init(
        folders: [RemoteFolder],
        currentPath: String,
        defaultFolder: String?,
        onChoose: @escaping (String) -> Void,
        onCancel: @escaping () -> Void
    ) {
        self.folders = folders
        self.defaultFolder = defaultFolder?.trimmingTrailingSlash
        self.onChoose = onChoose
        self.onCancel = onCancel

        // Open the tree at the folder that's already in the path field, if it's listed.
        let chain = Self.chain(to: currentPath.trimmingTrailingSlash, in: folders)
        _selection = State(initialValue: chain.last)
        _expanded = State(initialValue: Set(chain.dropLast()).union(folders.map(\.path)))
    }

    var body: some View {
        VStack(spacing: 0) {
            if folders.isEmpty {
                ContentUnavailableView(
                    "No Known Folders",
                    systemImage: "folder",
                    description: Text("Folders appear here once torrents are saved on this server.")
                )
            } else {
                folderList
            }
            Divider()
            footer
        }
        .frame(width: 400, height: 360)
    }

    private var folderList: some View {
        List(selection: $selection) {
            FolderOutline(folders: folders, defaultFolder: defaultFolder, expanded: $expanded)
        }
        .contextMenu(forSelectionType: String.self) { _ in
        } primaryAction: { paths in
            // Double-click or Return on a row chooses it, like an Open panel.
            if let path = paths.first { onChoose(path) }
        }
    }

    private var footer: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Only folders already used on this server are listed. To use a new one, type its path.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                Spacer()
                Button("Cancel", role: .cancel, action: onCancel)
                    .keyboardShortcut(.cancelAction)
                Button("Choose") {
                    if let selection { onChoose(selection) }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(selection == nil)
            }
        }
        .padding(12)
    }

    /// Paths from a top-level folder down to the folder at `path`, or empty if it isn't listed.
    private static func chain(to path: String, in folders: [RemoteFolder]) -> [String] {
        for folder in folders {
            if folder.path == path { return [folder.path] }
            let rest = chain(to: path, in: folder.children ?? [])
            if !rest.isEmpty { return [folder.path] + rest }
        }
        return []
    }
}

/// One level of the folder tree; nests itself for subfolders.
private struct FolderOutline: View {
    let folders: [RemoteFolder]
    let defaultFolder: String?
    @Binding var expanded: Set<String>

    var body: some View {
        ForEach(folders) { folder in
            if let children = folder.children {
                DisclosureGroup(isExpanded: isExpanded(folder)) {
                    Self(folders: children, defaultFolder: defaultFolder, expanded: $expanded)
                } label: {
                    row(for: folder)
                }
            } else {
                row(for: folder)
            }
        }
    }

    private func row(for folder: RemoteFolder) -> some View {
        FolderRow(folder: folder, isDefault: folder.path == defaultFolder)
            .tag(folder.path)
    }

    private func isExpanded(_ folder: RemoteFolder) -> Binding<Bool> {
        Binding {
            expanded.contains(folder.path)
        } set: { isOpen in
            if isOpen {
                expanded.insert(folder.path)
            } else {
                expanded.remove(folder.path)
            }
        }
    }
}

private struct FolderRow: View {
    let folder: RemoteFolder
    let isDefault: Bool

    var body: some View {
        HStack {
            Label(folder.name, systemImage: "folder")
                .lineLimit(1)
                .truncationMode(.head)
                .help(folder.path)
            Spacer()
            if isDefault {
                Text("Default")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            if folder.torrentCount > 0 {
                Text(folder.torrentCount, format: .number)
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
                    .help("Torrents saved in this folder: \(folder.torrentCount)")
            }
        }
    }
}
