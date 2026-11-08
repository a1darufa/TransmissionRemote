import SwiftUI

/// A text field for a folder on the server, with a browser of the known folders and the
/// folder history in a pull-down menu. Remote paths can't be browsed with an Open panel.
struct FolderField: View {
    @Environment(AppModel.self) private var model
    @Binding var path: String
    @State private var isBrowsing = false

    private var suggestions: [String] { model.suggestedFolders }

    var body: some View {
        HStack(spacing: 4) {
            TextField("Folder on the server", text: $path)
                .textFieldStyle(.roundedBorder)
                .autocorrectionDisabled()
            Button {
                isBrowsing = true
            } label: {
                Image(systemName: "folder")
            }
            .buttonStyle(.borderless)
            .help("Browse folders on the server")
            .popover(isPresented: $isBrowsing, arrowEdge: .bottom) {
                FolderBrowser(
                    folders: model.knownFolders,
                    currentPath: path,
                    defaultFolder: model.connection.session?.downloadDir
                ) { chosen in
                    path = chosen
                    isBrowsing = false
                } onCancel: {
                    isBrowsing = false
                }
            }
            Menu {
                ForEach(suggestions, id: \.self) { folder in
                    Button(folder) { path = folder }
                }
                Divider()
                // A way out for folders that no longer exist (transgui issues #310, #738).
                Button("Clear History") { model.clearFolderHistory() }
            } label: {
                Image(systemName: "clock.arrow.circlepath")
            }
            .menuStyle(.borderlessButton)
            .fixedSize()
            .disabled(suggestions.isEmpty)
            .help("Recent folders")
        }
    }
}

/// Free space for a folder on the server, refreshed shortly after the user stops typing.
struct FreeSpaceLabel: View {
    @Environment(AppModel.self) private var model
    let path: String
    @State private var freeBytes: Int64?

    var body: some View {
        Group {
            if let freeBytes {
                Text("\(freeBytes.formattedBytes) free on the server")
            } else {
                Text(" ")
            }
        }
        .font(.caption)
        .foregroundStyle(.secondary)
        .task(id: path) {
            try? await Task.sleep(for: .milliseconds(400))
            guard !Task.isCancelled, !path.isEmpty else { return }
            freeBytes = await model.connection.freeSpace(at: path)?.sizeBytes
        }
    }
}
