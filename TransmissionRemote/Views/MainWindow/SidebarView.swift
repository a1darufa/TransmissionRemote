import SwiftUI
import TransmissionKit

struct SidebarView: View {
    @Environment(AppModel.self) private var model
    @Binding var selection: SidebarItem?

    var body: some View {
        let summary = SidebarSummary(torrents: model.connection.torrents)

        List(selection: $selection) {
            Section("Status") {
                ForEach(StatusFilter.allCases) { filter in
                    Label(String(localized: filter.title), systemImage: filter.systemImage)
                        .badge(summary.statusCounts[filter, default: 0])
                        .tag(SidebarItem.status(filter))
                }
            }

            if !summary.folders.isEmpty {
                Section("Folders") {
                    ForEach(summary.folders) { folder in
                        Label(folder.name.lastPathComponentForDisplay, systemImage: "folder")
                            .badge(folder.count)
                            .help(folder.name)
                            .tag(SidebarItem.folder(folder.name))
                    }
                }
            }

            if !summary.labels.isEmpty {
                Section("Labels") {
                    ForEach(summary.labels) { label in
                        Label(label.name, systemImage: "tag")
                            .badge(label.count)
                            .tag(SidebarItem.label(label.name))
                    }
                }
            }

            if !summary.trackers.isEmpty {
                Section("Trackers") {
                    ForEach(summary.trackers) { tracker in
                        Label(tracker.name, systemImage: "antenna.radiowaves.left.and.right")
                            .badge(tracker.count)
                            .tag(SidebarItem.tracker(tracker.name))
                    }
                }
            }
        }
        .listStyle(.sidebar)
    }
}
