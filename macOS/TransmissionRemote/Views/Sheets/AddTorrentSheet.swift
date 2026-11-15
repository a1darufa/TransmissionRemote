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
    @State private var hasServerDefaults = false
    @State private var isAdding = false
    /// Set once the server has the torrent, so the sheet no longer offers to add it.
    @State private var added: AddTorrentResult?
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
        .onChange(of: connection.session != nil) { applyLateServerDefaults() }
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

            FileTreeTable(nodes: fileTree, onToggleWanted: toggle, onSetWanted: setWanted)
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
            if added != nil {
                // Only after the torrent was added but the rename failed: adding again would do nothing.
                Button("Done") { dismiss() }
                    .keyboardShortcut(.defaultAction)
            } else {
                Button("Cancel", role: .cancel) { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button("Add") { add() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(isAdding || destination.trimmingCharacters(in: .whitespaces).isEmpty
                              || allFilesSkipped || (canRename && TorrentName.validated(name) == nil))
            }
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
    private var canRename: Bool { !pending.files.isEmpty && connection.serverInfo?.supportsRenaming != false }

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

    /// Whether the server had this torrent before it was added from this sheet. Once adding has
    /// started, the server's list soon includes the new torrent, which isn't a duplicate.
    private var isDuplicate: Bool {
        guard !isAdding, added == nil, let hash = pending.infoHash?.lowercased() else { return false }
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

    private func setWanted(_ wanted: Bool, nodes: [FileNode]) {
        let files = nodes.flatMap(\.fileIDs)
        if wanted {
            unwantedFiles.subtract(files)
        } else {
            unwantedFiles.formUnion(files)
        }
    }

    private func applyDefaults() {
        name = pending.name
        destination = model.startingFolder
        startWhenAdded = connection.session?.startAddedTorrents ?? true
        hasServerDefaults = connection.session != nil
    }

    /// Opening a .torrent from Finder while the app isn't running shows this sheet before the
    /// server's settings arrive (transgui issue #790). Fill in what was missing once they do.
    private func applyLateServerDefaults() {
        guard !hasServerDefaults, let session = connection.session else { return }
        if destination.isEmpty { destination = model.startingFolder }
        startWhenAdded = session.startAddedTorrents ?? startWhenAdded
        hasServerDefaults = true
    }

    private func add() {
        let options = AddTorrentOptions(
            downloadDir: ServerFolder.cleaned(destination),
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
                added = result
                model.recordFolderUse(options.downloadDir ?? "")
                applyLabelsIfNeeded(to: result)
                guard await renameIfNeeded(result, to: newName) else { return }
                dismiss()
            } catch {
                errorMessage = error.localizedDescription
            }
        }
    }

    /// Transmission can't name a torrent while adding it, so its top-level item is renamed right
    /// after. A duplicate keeps its name: it's the torrent already on the server. Returns false when
    /// the rename failed; the sheet then stays open and says why.
    private func renameIfNeeded(_ result: AddTorrentResult, to newName: String?) async -> Bool {
        guard let newName, let torrent = result.torrentAdded, torrent.name != newName else { return true }
        do {
            try await connection.renameAddedTorrent(torrent, to: newName)
            return true
        } catch {
            let reason = error.localizedDescription
            errorMessage = String(localized: "The torrent was added, but it couldn't be renamed: \(reason)")
            return false
        }
    }

    /// Transmission 3.00 can label torrents but not while adding them, so label right after.
    private func applyLabelsIfNeeded(to result: AddTorrentResult) {
        guard let serverInfo, serverInfo.supportsLabels, !serverInfo.supportsLabelsOnAdd,
              !labels.isEmpty, let torrent = result.torrentAdded
        else { return }
        connection.setLabels(labels, on: [torrent.id])
    }
}
