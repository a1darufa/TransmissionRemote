import SwiftUI
import TransmissionKit

/// Per-torrent limits (Transmission Remote GUI's "Torrent properties"), applied to all selected torrents.
struct TorrentOptionsSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let ids: Set<Torrent.ID>

    @State private var options: TorrentDetails?
    @State private var original: TorrentDetails?
    @State private var bandwidthPriority = Priority.normal
    /// Empty means no group. Nil until loaded, or on servers without groups.
    @State private var bandwidthGroup: String?
    @State private var groupNames: [String] = []
    @State private var loadError: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            if let binding = Binding($options) {
                form(binding)
            } else if let loadError {
                ContentUnavailableView(
                    "Couldn't Load Options",
                    systemImage: "exclamationmark.triangle",
                    description: Text(loadError)
                )
            } else {
                ProgressView().frame(maxWidth: .infinity, minHeight: 200)
            }
            Divider()
            HStack {
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button("Apply") { apply() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(options == nil)
            }
            .padding()
        }
        .frame(width: 460)
        .task { await load() }
    }

    private func form(_ options: Binding<TorrentDetails>) -> some View {
        Form {
            speedSection(options)
            seedingSection(options)

            Section("Peers") {
                NumberField("Maximum peers", value: options.peerLimit, format: .number)
                if model.connection.serverInfo?.supportsSequentialDownload == true {
                    Toggle("Download in order", isOn: Binding(
                        get: { options.wrappedValue.sequentialDownload ?? false },
                        set: { options.wrappedValue.sequentialDownload = $0 }
                    ))
                }
            }
        }
        .formStyle(.grouped)
    }

    private func speedSection(_ options: Binding<TorrentDetails>) -> some View {
        Section("Speed") {
            LimitRow(
                title: "Limit download speed",
                isOn: options.downloadLimited,
                value: options.downloadLimit,
                unit: "kB/s"
            )
            LimitRow(
                title: "Limit upload speed",
                isOn: options.uploadLimited,
                value: options.uploadLimit,
                unit: "kB/s"
            )
            Toggle("Follow the server's global limits", isOn: options.honorsSessionLimits)
            Picker("Bandwidth priority", selection: $bandwidthPriority) {
                ForEach(Priority.allCases.reversed(), id: \.self) { Text($0.title).tag($0) }
            }
            if let selectedGroup = Binding($bandwidthGroup) {
                Picker("Bandwidth group", selection: selectedGroup) {
                    Text("None").tag("")
                    ForEach(groupChoices, id: \.self) { Text($0).tag($0) }
                }
            }
        }
    }

    private func seedingSection(_ options: Binding<TorrentDetails>) -> some View {
        Section("Seeding") {
            Picker("Stop at ratio", selection: options.seedRatioMode) {
                Text("Server default").tag(LimitMode.global)
                Text("Custom").tag(LimitMode.single)
                Text("Never").tag(LimitMode.unlimited)
            }
            if options.wrappedValue.seedRatioMode == .single {
                NumberField("Ratio", value: options.seedRatioLimit, format: .number.precision(.fractionLength(0...2)))
            }
            Picker("Stop when idle", selection: options.seedIdleMode) {
                Text("Server default").tag(LimitMode.global)
                Text("Custom").tag(LimitMode.single)
                Text("Never").tag(LimitMode.unlimited)
            }
            if options.wrappedValue.seedIdleMode == .single {
                NumberField("Idle minutes", value: options.seedIdleLimit, format: .number)
            }
        }
    }

    /// The server's groups, plus the torrent's current one in case the list couldn't be read.
    private var groupChoices: [String] {
        let current = bandwidthGroup.map { $0.isEmpty ? [] : [$0] } ?? []
        return Array(Set(groupNames + current)).sorted { $0.localizedStandardCompare($1) == .orderedAscending }
    }

    private func load() async {
        guard let firstID = ids.min() else { return }
        do {
            let loaded = try await model.connection.options(of: firstID)
            options = loaded
            original = loaded
            bandwidthPriority = model.connection.torrents.first { $0.id == firstID }?.bandwidthPriority ?? .normal
            if model.connection.supportsBandwidthGroups {
                groupNames = (try? await model.connection.bandwidthGroups().map(\.name)) ?? []
                bandwidthGroup = loaded?.bandwidthGroup ?? ""
            }
        } catch {
            loadError = error.localizedDescription
        }
    }

    private func apply() {
        guard let options else { return }
        var mutation = TorrentMutation(ids: ids.sorted())
        mutation.downloadLimited = options.downloadLimited
        mutation.downloadLimit = options.downloadLimit
        mutation.uploadLimited = options.uploadLimited
        mutation.uploadLimit = options.uploadLimit
        mutation.honorsSessionLimits = options.honorsSessionLimits
        mutation.bandwidthPriority = bandwidthPriority
        mutation.seedRatioMode = options.seedRatioMode
        mutation.seedRatioLimit = options.seedRatioLimit
        mutation.seedIdleMode = options.seedIdleMode
        mutation.seedIdleLimit = options.seedIdleLimit
        mutation.peerLimit = options.peerLimit
        if options.sequentialDownload != original?.sequentialDownload {
            mutation.sequentialDownload = options.sequentialDownload
        }
        if let bandwidthGroup, bandwidthGroup != (original?.bandwidthGroup ?? "") || ids.count > 1 {
            mutation.group = bandwidthGroup
        }
        model.connection.apply(mutation)
        dismiss()
    }
}

/// A checkbox that enables a numeric limit next to it.
struct LimitRow: View {
    let title: LocalizedStringResource
    @Binding var isOn: Bool
    @Binding var value: Int
    let unit: LocalizedStringResource

    var body: some View {
        HStack {
            Toggle(String(localized: title), isOn: $isOn)
            Spacer()
            NumberField(verbatim: String(localized: title), value: $value, format: .number)
                .labelsHidden()
                .multilineTextAlignment(.trailing)
                .frame(width: 80)
                .disabled(!isOn)
            Text(unit).foregroundStyle(.secondary)
        }
    }
}
