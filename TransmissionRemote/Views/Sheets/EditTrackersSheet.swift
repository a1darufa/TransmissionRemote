import SwiftUI
import TransmissionKit

/// Edits a torrent's announce URLs as text: one URL per line, a blank line between tiers.
struct EditTrackersSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let torrentID: Torrent.ID

    @State private var text = ""
    @State private var current: [TrackerStat] = []
    @State private var isLoaded = false
    @State private var loadError: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Edit Trackers").font(.headline)
            Text("One announce URL per line. Separate tiers with a blank line.")
                .foregroundStyle(.secondary)

            if let loadError {
                Label(loadError, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.red)
            }

            TextEditor(text: $text)
                .font(.system(.body, design: .monospaced))
                .frame(minHeight: 200)
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(.quaternary))
                .disabled(!isLoaded)

            if let invalid = invalidLines.first {
                Label("“\(invalid)” isn't a valid tracker URL.", systemImage: "exclamationmark.circle")
                    .foregroundStyle(.orange)
            }

            HStack {
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button("Save") { save() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(!isLoaded || !invalidLines.isEmpty)
            }
        }
        .padding(20)
        .frame(width: 560)
        .task { await load() }
    }

    /// Tiers of announce URLs, parsed from the text.
    private var tiers: [[String]] {
        text.components(separatedBy: "\n\n")
            .map { block in
                block.split(whereSeparator: \.isNewline)
                    .map { $0.trimmingCharacters(in: .whitespaces) }
                    .filter { !$0.isEmpty }
            }
            .filter { !$0.isEmpty }
    }

    private var invalidLines: [String] {
        tiers.joined().filter { line in
            guard let url = URL(string: line), let scheme = url.scheme?.lowercased() else { return true }
            return !["http", "https", "udp", "wss"].contains(scheme) || url.host() == nil
        }
    }

    private func load() async {
        do {
            guard let details = try await model.connection.trackers(of: torrentID) else {
                // Without this, an empty editor would offer to save over nothing.
                loadError = String(localized: "This torrent is no longer on the server.")
                return
            }
            current = details.trackerStats
            if let list = details.trackerList, !list.isEmpty {
                text = list.trimmingCharacters(in: .whitespacesAndNewlines)
            } else {
                // Transmission before 4.0 has no trackerList; rebuild it from the tiers.
                text = Dictionary(grouping: current, by: \.tier)
                    .sorted { $0.key < $1.key }
                    .map { $0.value.map(\.announce).joined(separator: "\n") }
                    .joined(separator: "\n\n")
            }
            isLoaded = true
        } catch {
            loadError = error.localizedDescription
        }
    }

    private func save() {
        model.connection.replaceTrackers(of: torrentID, current: current, with: tiers)
        dismiss()
    }
}
