import SwiftUI
import TransmissionKit

/// One Transmission pane of Settings. It edits the shared draft and offers Revert and Apply,
/// because daemon settings go over the network and shouldn't be sent on every keystroke.
struct ServerSettingsDetail<Content: View>: View {
    @Environment(AppModel.self) private var model
    @Bindable var draft: ServerSettingsDraft
    var showsApplyBar = true
    @ViewBuilder let content: (Binding<SessionSettings>) -> Content

    var body: some View {
        if model.connection.isConnected && draft.isLoaded {
            VStack(spacing: 0) {
                content($draft.settings)
                if showsApplyBar {
                    Divider()
                    applyBar
                }
            }
        } else {
            ContentUnavailableView {
                Label("Not Connected", systemImage: "bolt.horizontal.circle")
            } description: {
                Text("Connect to a Transmission server to change its options.")
            } actions: {
                ManageServersLink { Text("Manage Servers…") }
            }
        }
    }

    private var applyBar: some View {
        HStack {
            if let errorMessage = draft.errorMessage {
                Label(errorMessage, systemImage: "exclamationmark.triangle.fill")
                    .foregroundStyle(.red)
                    .lineLimit(2)
            } else if draft.hasChanges {
                Text("Changes are sent to the server when you click Apply.")
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Button("Revert") { draft.revert() }
                .disabled(!draft.hasChanges || draft.isApplying)
            Button("Apply") {
                Task { await draft.apply(using: model.connection) }
            }
            .keyboardShortcut(.defaultAction)
            .disabled(!draft.hasChanges || draft.isApplying)
        }
        .padding()
    }
}

extension Binding where Value == SessionSettings {
    /// Edits an optional setting as if it were required, falling back to a default.
    subscript<Field>(
        _ keyPath: WritableKeyPath<SessionSettings,
        Field?>,
        default defaultValue: Field
    ) -> Binding<Field> {
        Binding<Field> {
            wrappedValue[keyPath: keyPath] ?? defaultValue
        } set: {
            wrappedValue[keyPath: keyPath] = $0
        }
    }
}

// MARK: - Panes, grouped and named like Transmission Remote GUI's "Transmission options"

struct DownloadSettings: View {
    @Environment(AppModel.self) private var model
    @Binding var settings: SessionSettings

    var body: some View {
        Form {
            Section {
                TextField("Default download folder on remote host", text: $settings[\.downloadDir, default: ""])
                Toggle("Add .part extension to incomplete files", isOn: $settings[\.renamePartialFiles, default: true])
                Toggle("Directory for incomplete files", isOn: $settings[\.incompleteDirEnabled, default: false])
                TextField("Incomplete files folder", text: $settings[\.incompleteDir, default: ""])
                    .disabled(settings.incompleteDirEnabled != true)
            }
            Section {
                HStack {
                    Toggle("Seed ratio", isOn: $settings[\.seedRatioLimited, default: false])
                    Spacer()
                    TextField(
                        "Seed ratio",
                        value: $settings[\.seedRatioLimit, default: 2],
                        format: .number.precision(.fractionLength(0...2))
                    )
                        .labelsHidden()
                        .multilineTextAlignment(.trailing)
                        .frame(width: 80)
                        .disabled(settings.seedRatioLimited != true)
                }
                ToggleNumberRow(
                    title: "Stop seeding when inactive for",
                    isOn: $settings[\.idleSeedingLimitEnabled, default: false],
                    value: $settings[\.idleSeedingLimit, default: 30],
                    unit: "minutes"
                )
                NumberRow(title: "Disk cache size", value: $settings[\.cacheSizeMb, default: 4], unit: "MB")
            }
            // Not in Transmission Remote GUI's dialog, but the daemon has them.
            Section {
                Toggle("Start torrents when added", isOn: $settings[\.startAddedTorrents, default: true])
                Toggle(
                    "Delete .torrent files from the watch folder after adding",
                    isOn: $settings[\.trashOriginalTorrentFiles, default: false]
                )
                if model.connection.serverInfo?.supportsSequentialDownload == true {
                    Toggle("Download pieces in order by default", isOn: $settings[\.sequentialDownload, default: false])
                }
            }
        }
        .formStyle(.grouped)
    }
}

struct NetworkSettings: View {
    @Environment(AppModel.self) private var model
    @Binding var settings: SessionSettings
    @State private var portTestResult: PortTestResult?
    @State private var isTestingPort = false

    var body: some View {
        Form {
            Section {
                NumberRow(title: "Incoming port", value: $settings[\.peerPort, default: 51413], unit: nil)
                Toggle(
                    "Pick random port on Transmission launch",
                    isOn: $settings[\.peerPortRandomOnStart, default: false]
                )
                HStack {
                    Button("Test Port") { runPortTest() }
                        .disabled(isTestingPort)
                    if isTestingPort { ProgressView().controlSize(.small) }
                    if let portTestResult { PortTestResultLabel(result: portTestResult) }
                }
                Toggle("Enable port forwarding", isOn: $settings[\.portForwardingEnabled, default: true])
            }
            Section {
                Picker("Encryption", selection: $settings[\.encryption, default: .preferred]) {
                    Text("Require encryption").tag(EncryptionMode.required)
                    Text("Prefer encryption").tag(EncryptionMode.preferred)
                    Text("Allow encryption").tag(EncryptionMode.allowed)
                }
                NumberRow(title: "Global peer limit", value: $settings[\.peerLimitGlobal, default: 200], unit: nil)
                NumberRow(
                    title: "Per torrent peer limit",
                    value: $settings[\.peerLimitPerTorrent, default: 50],
                    unit: nil
                )
            }
            Section {
                Toggle("Enable Peer Exchange", isOn: $settings[\.pexEnabled, default: true])
                Toggle("Enable DHT", isOn: $settings[\.dhtEnabled, default: true])
                Toggle("Enable Local Peer Discovery", isOn: $settings[\.lpdEnabled, default: false])
                Toggle("Enable µTP", isOn: $settings[\.utpEnabled, default: true])
            }
            BlocklistSection(settings: $settings)
        }
        .formStyle(.grouped)
    }

    private func runPortTest() {
        isTestingPort = true
        portTestResult = nil
        Task {
            defer { isTestingPort = false }
            do {
                let isOpen = try await model.connection.testPort()
                portTestResult = isOpen ? .open : .closed
            } catch {
                portTestResult = .failed(error.localizedDescription)
            }
        }
    }
}

/// The blocklist rows. Transmission Remote GUI keeps them on its Network (WAN) tab.
private struct BlocklistSection: View {
    @Environment(AppModel.self) private var model
    @Binding var settings: SessionSettings
    @State private var status: String?
    @State private var isUpdating = false

    var body: some View {
        Section {
            Toggle("Enable blocklist", isOn: $settings[\.blocklistEnabled, default: false])
            TextField("Blocklist URL", text: $settings[\.blocklistUrl, default: ""])
                .disabled(settings.blocklistEnabled != true)
            LabeledContent("Rules", value: (settings.blocklistSize ?? 0).formatted())
            HStack {
                Button("Update Now") { update() }
                    .disabled(isUpdating || settings.blocklistEnabled != true)
                if isUpdating { ProgressView().controlSize(.small) }
                if let status { Text(status).foregroundStyle(.secondary) }
            }
        } footer: {
            Text("Save the URL first if you changed it; the server downloads the list itself.")
        }
    }

    private func update() {
        isUpdating = true
        status = nil
        Task {
            defer { isUpdating = false }
            do {
                let count = try await model.connection.updateBlocklist()
                settings.blocklistSize = count
                status = String(localized: "Updated: \(count) rules")
            } catch {
                status = error.localizedDescription
            }
        }
    }
}

struct BandwidthSettings: View {
    @Binding var settings: SessionSettings

    var body: some View {
        Form {
            Section("Global bandwidth settings") {
                LimitRow(
                    title: "Maximum download speed",
                    isOn: $settings[\.speedLimitDownEnabled, default: false],
                    value: $settings[\.speedLimitDown, default: 0],
                    unit: "kB/s"
                )
                LimitRow(
                    title: "Maximum upload speed",
                    isOn: $settings[\.speedLimitUpEnabled, default: false],
                    value: $settings[\.speedLimitUp, default: 0],
                    unit: "kB/s"
                )
            }

            Section("Alternate bandwidth settings") {
                NumberRow(title: "Maximum download speed", value: $settings[\.altSpeedDown, default: 0], unit: "kB/s")
                NumberRow(title: "Maximum upload speed", value: $settings[\.altSpeedUp, default: 0], unit: "kB/s")
                Toggle("Use alternate bandwidth settings", isOn: $settings[\.altSpeedEnabled, default: false])
                Toggle(
                    "Apply alternate bandwidth settings automatically",
                    isOn: $settings[\.altSpeedTimeEnabled, default: false]
                )
                Group {
                    MinutesOfDayPicker(title: "From", minutes: $settings[\.altSpeedTimeBegin, default: 540])
                    MinutesOfDayPicker(title: "To", minutes: $settings[\.altSpeedTimeEnd, default: 1020])
                    WeekdayPicker(mask: $settings[\.altSpeedTimeDay, default: 127])
                }
                .disabled(settings.altSpeedTimeEnabled != true)
            }

            BandwidthGroupsSection()
        }
        .formStyle(.grouped)
    }
}

struct QueueSettings: View {
    @Binding var settings: SessionSettings

    var body: some View {
        Form {
            Section {
                ToggleNumberRow(
                    title: "Download queue size",
                    isOn: $settings[\.downloadQueueEnabled, default: false],
                    value: $settings[\.downloadQueueSize, default: 5],
                    unit: "torrents"
                )
                ToggleNumberRow(
                    title: "Upload queue size",
                    isOn: $settings[\.seedQueueEnabled, default: false],
                    value: $settings[\.seedQueueSize, default: 10],
                    unit: "torrents"
                )
                ToggleNumberRow(
                    title: "Consider active torrents as stalled when idle for",
                    isOn: $settings[\.queueStalledEnabled, default: true],
                    value: $settings[\.queueStalledMinutes, default: 30],
                    unit: "minutes"
                )
            }
        }
        .formStyle(.grouped)
    }
}

struct ServerAboutView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        let connection = model.connection
        Form {
            LabeledContent("Transmission", value: connection.serverInfo?.version ?? "–")
            LabeledContent("RPC version", value: rpcVersionText)
            LabeledContent(
                "Protocol",
                value: connection.dialect == .jsonRPC ? "JSON-RPC 2.0" : String(localized: "Legacy RPC")
            )
            if let profile = connection.profile {
                LabeledContent("Connection") {
                    Label {
                        Text(profile.endpoint.transportSecurity.summary)
                    } icon: {
                        Image(systemName: profile.endpoint.transportSecurity.symbol)
                            .foregroundStyle(profile.endpoint.transportSecurity.color)
                    }
                }
            }
            if let directory = connection.session?.configDir {
                LabeledContent("Config folder", value: directory)
            }
            if let totals = connection.stats?.cumulativeStats {
                Section("All-time totals") {
                    LabeledContent("Downloaded", value: totals.downloadedBytes.formattedBytes)
                    LabeledContent("Uploaded", value: totals.uploadedBytes.formattedBytes)
                    LabeledContent("Active for", value: Format.duration(totals.secondsActive))
                }
            }
        }
        .formStyle(.grouped)
    }

    private var rpcVersionText: String {
        guard let info = model.connection.serverInfo else { return "–" }
        return info.rpcVersionSemver.map { "\(info.rpcVersion) (\($0))" } ?? "\(info.rpcVersion)"
    }
}
