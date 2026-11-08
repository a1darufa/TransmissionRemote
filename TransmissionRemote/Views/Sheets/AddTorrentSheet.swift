import SwiftUI
import TransmissionKit

/// Confirms one queued torrent: destination folder, which files to download, start options.
struct AddTorrentSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let pending: PendingTorrent

    @State private var name = ""
    @State private var destination = ""
    @State private var startWhenAdded = true
    @State private var priority = Priority.normal
    @State private var labelsText = ""
    @State private var sequentialDownload = false
    @State private var unwantedFiles: Set<Int> = []
    @State private var isAdding = false
    @State private var errorMessage: String?

    private var connection: ServerConnection { model.connection }
    private var serverInfo: ServerInfo? { connection.serverInfo }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            header
            destinationSection
            if !pending.files.isEmpty { fileSection }
            optionsSection
            if let errorMessage {
                Label(errorMessage, systemImage: "exclamationmark.triangle.fill")
                    .foregroundStyle(.red)
            }
            buttons
        }
        .padding(20)
        .frame(width: 640, height: pending.files.isEmpty ? nil : 560)
        .onAppear(perform: applyDefaults)
    }

    private var header: some View {
        HStack(spacing: 12) {
            Image(systemName: pending.files.isEmpty ? "link.circle.fill" : "doc.circle.fill")
                .font(.system(size: 36))
                .foregroundStyle(.tint)
            VStack(alignment: .leading, spacing: 2) {
                if canRename {
                    TextField("Name", text: $name)
                        .font(.headline)
                        .textFieldStyle(.roundedBorder)
                        .help("The name of the torrent's folder, or of the file for a single-file torrent")
                } else {
                    Text(pending.name)
                        .font(.headline)
                        .lineLimit(2)
                        .truncationMode(.middle)
                }
                if let size = pending.totalSize {
                    // Two strings, so translations only have to pluralize "files".
                    Text(verbatim: "\(String(localized: "\(realFiles.count) files")) · \(size.formattedBytes)")
                        .foregroundStyle(.secondary)
                }
                if isDuplicate {
                    Label("This torrent is already on the server.", systemImage: "exclamationmark.circle")
                        .foregroundStyle(.orange)
                }
            }
        }
    }

    private var destinationSection: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("Save to").font(.subheadline.weight(.semibold))
            FolderField(path: $destination)
            FreeSpaceLabel(path: destination)
        }
    }

    private var fileSection: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text("Files").font(.subheadline.weight(.semibold))
                Spacer()
                Text("\(selectedSize.formattedBytes) selected").foregroundStyle(.secondary)
                Button("All") { unwantedFiles = [] }
                Button("None") { unwantedFiles = Set(realFiles.map(\.id)) }
            }
            .controlSize(.small)

            FileTreeTable(nodes: fileTree, onToggleWanted: toggle)
                .frame(minHeight: 180)
        }
    }

    private var optionsSection: some View {
        Grid(alignment: .leadingFirstTextBaseline, horizontalSpacing: 12, verticalSpacing: 8) {
            GridRow {
                Toggle("Start when added", isOn: $startWhenAdded)
                Picker("Priority", selection: $priority) {
                    ForEach(Priority.allCases.reversed(), id: \.self) { Text($0.title).tag($0) }
                }
                .fixedSize()
            }
            if serverInfo?.supportsLabels == true || serverInfo?.supportsSequentialDownload == true {
                GridRow {
                    if serverInfo?.supportsSequentialDownload == true {
                        Toggle("Download in order", isOn: $sequentialDownload)
                            .help("Sequential download, useful for previewing media while it downloads")
                    }
                    if serverInfo?.supportsLabels == true {
                        TextField("Labels, comma-separated", text: $labelsText)
                            .textFieldStyle(.roundedBorder)
                    }
                }
            }
        }
    }

    private var buttons: some View {
        HStack {
            Spacer()
            Button("Cancel", role: .cancel) { dismiss() }
                .keyboardShortcut(.cancelAction)
            Button("Add") { add() }
                .keyboardShortcut(.defaultAction)
                .disabled(isAdding || destination.trimmingCharacters(in: .whitespaces).isEmpty || allFilesSkipped
                          || (canRename && TorrentName.validated(name) == nil))
        }
    }

    // MARK: - State

    private var fileTree: [FileNode] {
        let files = pending.files.map { file in
            var file = file
            file.wanted = !unwantedFiles.contains(file.id)
            return file
        }
        return FileTree.build(from: files)
    }

    private var selectedSize: Int64 {
        realFiles.filter { !unwantedFiles.contains($0.id) }.reduce(0) { $0 + $1.length }
    }

    /// Only .torrent files: a magnet link's final name isn't known until the daemon fetches
    /// its metadata, and Transmission can't rename a torrent before that.
    private var canRename: Bool { !pending.files.isEmpty }

    /// The new name, or nil to keep the one from the .torrent file.
    private var renamedTo: String? {
        guard canRename, let validName = TorrentName.validated(name), validName != pending.name else { return nil }
        return validName
    }

    /// The files to show and count; padding files are left out.
    private var realFiles: [FileEntry] { pending.files.filter { !$0.isPadding } }

    private var allFilesSkipped: Bool {
        !realFiles.isEmpty && realFiles.allSatisfy { unwantedFiles.contains($0.id) }
    }

    private var isDuplicate: Bool {
        guard let hash = pending.infoHash?.lowercased() else { return false }
        return connection.torrents.contains { $0.hashString.lowercased() == hash }
    }

    private var labels: [String] {
        labelsText.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
    }

    private func toggle(_ node: FileNode) {
        if node.wantedState == .all {
            unwantedFiles.formUnion(node.fileIDs)
        } else {
            unwantedFiles.subtract(node.fileIDs)
        }
    }

    private func applyDefaults() {
        name = pending.name
        destination = model.suggestedFolders.first ?? connection.session?.downloadDir ?? ""
        startWhenAdded = connection.session?.startAddedTorrents ?? true
    }

    private func add() {
        let options = AddTorrentOptions(
            downloadDir: destination.trimmingCharacters(in: .whitespaces),
            startWhenAdded: startWhenAdded,
            priority: priority,
            // Padding files are never needed on disk, so they're always left unselected.
            unwantedFiles: unwantedFiles.union(pending.files.filter(\.isPadding).map(\.id)),
            labels: labels,
            sequentialDownload: sequentialDownload
        )
        let newName = renamedTo
        isAdding = true
        errorMessage = nil

        Task {
            defer { isAdding = false }
            do {
                let request = AddTorrentRequest(pending, options: options, serverInfo: serverInfo)
                let result = try await connection.add(request)
                model.recordFolderUse(options.downloadDir ?? "")
                applyLabelsIfNeeded(to: result)
                renameIfNeeded(result, to: newName)
                dismiss()
            } catch {
                errorMessage = error.localizedDescription
            }
        }
    }

    /// Transmission can't name a torrent while adding it, so rename its top-level item right after.
    /// A duplicate keeps its name: it's the torrent already on the server.
    private func renameIfNeeded(_ result: AddTorrentResult, to newName: String?) {
        guard let newName, let torrent = result.torrentAdded, torrent.name != newName else { return }
        connection.renameTorrent(id: torrent.id, currentName: torrent.name, to: newName)
    }

    /// Transmission 3.00 can label torrents but not while adding them, so label right after.
    private func applyLabelsIfNeeded(to result: AddTorrentResult) {
        guard let serverInfo, serverInfo.supportsLabels, !serverInfo.supportsLabelsOnAdd,
              !labels.isEmpty, let torrent = result.torrentAdded
        else { return }
        connection.setLabels(labels, on: [torrent.id])
    }
}
