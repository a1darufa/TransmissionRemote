import SwiftUI
import TransmissionKit

/// A consistent frame for small sheets: title, content, Cancel and a primary button.
struct SheetScaffold<Content: View>: View {
    @Environment(\.dismiss) private var dismiss
    let title: LocalizedStringResource
    let primaryTitle: LocalizedStringResource
    var isPrimaryDisabled = false
    let primaryAction: () -> Void
    @ViewBuilder let content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(title).font(.headline)
            content
            HStack {
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button(String(localized: primaryTitle)) {
                    primaryAction()
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
                .disabled(isPrimaryDisabled)
            }
        }
        .padding(20)
        .frame(width: 480)
    }
}

struct AddLinkSheet: View {
    @Environment(AppModel.self) private var model
    @State private var text = ""

    private var isValid: Bool {
        text.split(whereSeparator: \.isNewline).contains { line in
            MagnetLink(String(line)) != nil || String(line).isTorrentDownloadURL
        }
    }

    var body: some View {
        SheetScaffold(title: "Add Torrent Link", primaryTitle: "Continue", isPrimaryDisabled: !isValid) {
            model.addFlow.openLinksAfterSheetDismissal(text)
        } content: {
            Text("Paste magnet links or .torrent URLs, one per line.")
                .foregroundStyle(.secondary)
            TextEditor(text: $text)
                .font(.system(.body, design: .monospaced))
                .frame(height: 90)
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(.quaternary))
        }
    }
}

struct MoveTorrentSheet: View {
    @Environment(AppModel.self) private var model
    let ids: Set<Torrent.ID>
    @State private var location = ""
    @State private var movesData = true

    var body: some View {
        SheetScaffold(title: "Move Data", primaryTitle: "Move", isPrimaryDisabled: location.isEmpty) {
            model.connection.setLocation(ids, to: location, movingData: movesData)
            model.recordFolderUse(location)
        } content: {
            FolderField(path: $location)
            FreeSpaceLabel(path: location)
            Toggle("Move the downloaded files to the new folder", isOn: $movesData)
            Text(movesData
                 ? "The server moves existing files."
                 : "The server looks for the files in the new folder instead of moving them.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .onAppear {
            location = model.connection.torrents.first { ids.contains($0.id) }?.downloadDir ?? ""
        }
    }
}

/// Asks for a new name for a torrent, or for a file or folder inside one.
struct RenameSheet: View {
    let title: LocalizedStringResource
    let explanation: LocalizedStringResource
    let currentName: String
    let onRename: (String) -> Void
    @State private var name = ""

    var body: some View {
        SheetScaffold(title: title, primaryTitle: "Rename", isPrimaryDisabled: !isValid) {
            if let newName = TorrentName.validated(name) {
                onRename(newName)
            }
        } content: {
            TextField("Name", text: $name)
                .textFieldStyle(.roundedBorder)
            Text(explanation)
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .onAppear { name = currentName }
    }

    private var isValid: Bool {
        guard let newName = TorrentName.validated(name) else { return false }
        return newName != currentName
    }
}

struct LabelsSheet: View {
    @Environment(AppModel.self) private var model
    let ids: Set<Torrent.ID>
    @State private var labelsText = ""

    private var knownLabels: [String] {
        Array(Set(model.connection.torrents.flatMap(\.labels))).sorted()
    }

    private var labels: [String] {
        labelsText.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
    }

    var body: some View {
        SheetScaffold(title: "Labels", primaryTitle: "Apply") {
            model.connection.setLabels(labels, on: ids)
        } content: {
            TextField("Labels, comma-separated", text: $labelsText)
                .textFieldStyle(.roundedBorder)
            if !knownLabels.isEmpty {
                Text("Existing labels").font(.caption).foregroundStyle(.secondary)
                FlowLayout(spacing: 6) {
                    ForEach(knownLabels, id: \.self) { label in
                        Button(label) { add(label) }
                            .buttonStyle(.bordered)
                            .controlSize(.small)
                            .disabled(labels.contains(label))
                    }
                }
            }
        }
        .onAppear {
            let selected = model.connection.torrents.filter { ids.contains($0.id) }
            labelsText = Array(Set(selected.flatMap(\.labels))).sorted().joined(separator: ", ")
        }
    }

    private func add(_ label: String) {
        labelsText = (labels + [label]).joined(separator: ", ")
    }
}

/// Wraps its children onto new lines, like tags in Finder.
struct FlowLayout: Layout {
    var spacing: CGFloat = 8

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        arrange(subviews, width: proposal.width ?? .infinity).size
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        let arrangement = arrange(subviews, width: bounds.width)
        for (subview, origin) in zip(subviews, arrangement.origins) {
            subview.place(at: CGPoint(x: bounds.minX + origin.x, y: bounds.minY + origin.y), proposal: .unspecified)
        }
    }

    private func arrange(_ subviews: Subviews, width: CGFloat) -> (size: CGSize, origins: [CGPoint]) {
        var origins: [CGPoint] = []
        var cursor = CGPoint.zero
        var lineHeight: CGFloat = 0
        var widest: CGFloat = 0

        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if cursor.x > 0, cursor.x + size.width > width {
                cursor = CGPoint(x: 0, y: cursor.y + lineHeight + spacing)
                lineHeight = 0
            }
            origins.append(cursor)
            cursor.x += size.width + spacing
            lineHeight = max(lineHeight, size.height)
            widest = max(widest, cursor.x - spacing)
        }
        return (CGSize(width: widest, height: cursor.y + lineHeight), origins)
    }
}
