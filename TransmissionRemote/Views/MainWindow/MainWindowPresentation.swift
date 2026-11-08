import SwiftUI
import TransmissionKit

/// Every sheet, dialog and alert the main window can present, in one place.
struct MainWindowPresentation: ViewModifier {
    @Environment(AppModel.self) private var model

    func body(content: Content) -> some View {
        @Bindable var router = model.router
        @Bindable var connection = model.connection
        @Bindable var addFlow = model.addFlow

        content
            .sheet(item: $router.sheet, onDismiss: addFlow.sheetDidDismiss) { sheet in
                sheetContent(sheet)
                    .environment(model)
            }
            .sheet(item: currentAddition) { pending in
                AddTorrentSheet(pending: pending)
                    .environment(model)
            }
            .fileImporter(
                isPresented: $router.isImportingTorrentFiles,
                allowedContentTypes: [.torrent],
                allowsMultipleSelection: true
            ) { result in
                if case .success(let urls) = result { addFlow.open(urls) }
            }
            .confirmationDialog(
                removalTitle,
                isPresented: Binding(get: { router.removal != nil }, set: { if !$0 { router.removal = nil } }),
                presenting: router.removal
            ) { request in
                Button(
                    request.deletingData ? "Remove and Delete Data" : "Remove",
                    role: request.deletingData ? .destructive : nil
                ) {
                    connection.remove(request.ids, deletingData: request.deletingData)
                }
                Button("Cancel", role: .cancel) {}
            } message: { request in
                if request.deletingData {
                    Text("The downloaded files will be deleted from the server. This can't be undone.")
                } else {
                    Text("Downloaded files stay on the server.")
                }
            }
            .modifier(MainWindowAlerts())
    }

    /// The torrent at the front of the add queue; dismissing its sheet moves to the next one.
    private var currentAddition: Binding<PendingTorrent?> {
        Binding {
            model.addFlow.current
        } set: { newValue in
            if newValue == nil { model.addFlow.finishCurrent() }
        }
    }

    private var removalTitle: String {
        let count = model.router.removal?.ids.count ?? 0
        return String(localized: "Remove ^[\(count) torrent](inflect: true)?")
    }

    @ViewBuilder
    private func sheetContent(_ sheet: MainSheet) -> some View {
        switch sheet {
        case .addLink: AddLinkSheet()
        case .move(let ids): MoveTorrentSheet(ids: ids)
        case .rename(let torrent):
            RenameSheet(
                title: "Rename Torrent",
                explanation: "Renames the torrent's top-level file or folder on the server.",
                currentName: torrent.name
            ) { model.connection.rename(torrent, to: $0) }
        case .renameFile(let torrentID, let path):
            RenameSheet(
                title: "Rename",
                explanation: "Renames the file or folder on the server.",
                currentName: (path as NSString).lastPathComponent
            ) { model.connection.renameFile(in: torrentID, path: path, to: $0) }
        case .labels(let ids): LabelsSheet(ids: ids)
        case .options(let ids): TorrentOptionsSheet(ids: ids)
        case .editTrackers(let id): EditTrackersSheet(torrentID: id)
        }
    }
}

/// Error alerts for failed actions and torrents that couldn't be opened.
private struct MainWindowAlerts: ViewModifier {
    @Environment(AppModel.self) private var model

    func body(content: Content) -> some View {
        let connection = model.connection
        let addFlow = model.addFlow

        content
            .alert(
                "Action Failed",
                isPresented: Binding { connection.actionError != nil } set: { if !$0 { connection.actionError = nil } },
                presenting: connection.actionError
            ) { _ in
                Button("OK") {}
            } message: { message in
                Text(message)
            }
            .modifier(UpdateAlerts(updates: model.updates))
            .alert(
                "Couldn't Add Torrent",
                isPresented: Binding { addFlow.problem != nil } set: { if !$0 { addFlow.problem = nil } },
                presenting: addFlow.problem
            ) { _ in
                Button("OK") {}
            } message: { message in
                Text(message)
            }
    }
}
