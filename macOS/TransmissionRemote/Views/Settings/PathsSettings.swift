import SwiftUI
import TransmissionKit

/// Settings → Application Options → Paths, like Transmission Remote GUI's tab: how many folders
/// "Save to" remembers, and where each server's folders are on this Mac.
struct PathsSettings: View {
    @Environment(AppModel.self) private var model
    @Binding var serverID: UUID?
    @AppStorage(AppSettings.folderHistoryLimit) private var folderHistoryLimit = 50

    var body: some View {
        Form {
            Section {
                NumberRow(title: "Maximum folder history", value: $folderHistoryLimit, unit: "folders")
            } footer: {
                Text("How many download folders each server remembers for “Save to”.")
            }

            Section {
                ServerPicker(selection: $serverID)
                ForEach(mappings) { $mapping in
                    PathMappingRow(mapping: $mapping) {
                        mappings.wrappedValue.removeAll { $0.id == mapping.id }
                    }
                }
                Button("Add Mapping", systemImage: "plus") {
                    mappings.wrappedValue.append(PathMapping())
                }
                .disabled(serverID == nil)
            } header: {
                Text("Remote to local path mappings")
            } footer: {
                Text("""
                    For example, /mnt/HD/HD_a2 on the server is /Volumes/HD_a2 on this Mac when the NAS \
                    share is mounted there. Open and Show in Finder use these.
                    """)
            }
        }
        .formStyle(.grouped)
        .onChange(of: folderHistoryLimit) { _, newLimit in
            model.servers.trimFolderHistories(to: max(newLimit, 1))
        }
    }

    /// The selected server's mappings; every edit is saved to its profile right away.
    private var mappings: Binding<[PathMapping]> {
        Binding {
            serverID.flatMap(model.servers.server(withID:))?.pathMappings ?? []
        } set: { newMappings in
            guard let serverID, var profile = model.servers.server(withID: serverID) else { return }
            profile.pathMappings = newMappings
            model.servers.save(profile)
        }
    }
}

private struct PathMappingRow: View {
    @Binding var mapping: PathMapping
    let remove: () -> Void
    @State private var isChoosingFolder = false

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                TextField("Folder on the server", text: $mapping.remotePath, prompt: Text("/mnt/HD/HD_a2"))
                Button("Remove Mapping", systemImage: "minus.circle", action: remove)
                    .labelStyle(.iconOnly)
                    .buttonStyle(.borderless)
            }
            LabeledContent("Folder on this Mac") {
                HStack {
                    Text(mapping.localPath.isEmpty ? String(localized: "Not chosen") : mapping.localPath)
                        .foregroundStyle(mapping.localPath.isEmpty ? .secondary : .primary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                    Button("Choose…") { isChoosingFolder = true }
                }
            }
        }
        .padding(.vertical, 2)
        // Picking the folder (rather than typing it) is what grants the sandboxed app access to it.
        .fileImporter(isPresented: $isChoosingFolder, allowedContentTypes: [.folder]) { result in
            guard case .success(let folder) = result else { return }
            let isAccessing = folder.startAccessingSecurityScopedResource()
            defer { if isAccessing { folder.stopAccessingSecurityScopedResource() } }
            mapping.localPath = folder.path(percentEncoded: false)
            mapping.localBookmark = try? folder.bookmarkData(
                options: [.withSecurityScope, .securityScopeAllowOnlyReadAccess],
                includingResourceValuesForKeys: nil,
                relativeTo: nil
            )
        }
    }
}
