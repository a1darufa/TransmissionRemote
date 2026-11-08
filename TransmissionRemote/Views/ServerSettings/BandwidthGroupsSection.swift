import SwiftUI
import TransmissionKit

/// The server's bandwidth groups (Transmission 4.0+), shown under the Bandwidth pane. Changes
/// go to the server right away, separate from the pane's Apply button, because each group is
/// saved on its own.
struct BandwidthGroupsSection: View {
    @Environment(AppModel.self) private var model
    @State private var groups: [BandwidthGroup]?
    @State private var editing: EditedGroup?
    @State private var problem: String?

    var body: some View {
        Section {
            if !model.connection.supportsBandwidthGroups {
                Text("Bandwidth groups need Transmission 4.0 or later.")
                    .foregroundStyle(.secondary)
            } else if let groups {
                if groups.isEmpty {
                    Text("No groups yet.").foregroundStyle(.secondary)
                }
                ForEach(groups) { group in
                    LabeledContent {
                        Button("Edit…") { editing = EditedGroup(group: group, isNew: false) }
                    } label: {
                        Text(group.name)
                        Text(group.limitsSummary)
                    }
                }
                Button("Add Group…") { editing = EditedGroup(group: BandwidthGroup(name: ""), isNew: true) }
            } else {
                ProgressView().controlSize(.small)
            }
            if let problem {
                Label(problem, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.red)
            }
        } header: {
            Text("Bandwidth groups")
        } footer: {
            Text("""
                Torrents in a group share its limits. Choose a torrent's group in Torrent Options. \
                Transmission doesn't let remote apps delete groups.
                """)
        }
        .task { await load() }
        .sheet(item: $editing) { edited in
            BandwidthGroupEditor(
                group: edited.group,
                isNew: edited.isNew,
                takenNames: Set((groups ?? []).map(\.name))
            ) { saved in
                Task { await save(saved) }
            }
        }
    }

    private func load() async {
        guard model.connection.supportsBandwidthGroups else { return }
        do {
            groups = try await model.connection.bandwidthGroups()
            problem = nil
        } catch {
            groups = groups ?? []
            problem = error.localizedDescription
        }
    }

    private func save(_ group: BandwidthGroup) async {
        do {
            try await model.connection.saveBandwidthGroup(group)
            await load()
        } catch {
            problem = error.localizedDescription
        }
    }
}

private struct EditedGroup: Identifiable {
    let id = UUID()
    let group: BandwidthGroup
    let isNew: Bool
}

/// Edits one group's limits. A group's name is fixed once it exists: Transmission has no way
/// to rename one, and saving under a new name would create a second group.
private struct BandwidthGroupEditor: View {
    @Environment(\.dismiss) private var dismiss
    @State private var group: BandwidthGroup
    let isNew: Bool
    let takenNames: Set<String>
    let onSave: (BandwidthGroup) -> Void

    init(group: BandwidthGroup, isNew: Bool, takenNames: Set<String>, onSave: @escaping (BandwidthGroup) -> Void) {
        _group = State(initialValue: group)
        self.isNew = isNew
        self.takenNames = takenNames
        self.onSave = onSave
    }

    var body: some View {
        VStack(spacing: 0) {
            Form {
                if isNew {
                    TextField("Name", text: $group.name, prompt: Text("Private trackers"))
                } else {
                    LabeledContent("Name", value: group.name)
                }
                LimitRow(
                    title: "Maximum download speed",
                    isOn: $group.speedLimitDownEnabled,
                    value: $group.speedLimitDown,
                    unit: "kB/s"
                )
                LimitRow(
                    title: "Maximum upload speed",
                    isOn: $group.speedLimitUpEnabled,
                    value: $group.speedLimitUp,
                    unit: "kB/s"
                )
                Toggle("Also follow the server's global limits", isOn: $group.honorsSessionLimits)
            }
            .formStyle(.grouped)
            Divider()
            HStack {
                if let nameProblem {
                    Text(nameProblem).foregroundStyle(.red)
                }
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button(isNew ? "Add" : "Save") {
                    group.name = trimmedName
                    onSave(group)
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
                .disabled(nameProblem != nil || trimmedName.isEmpty)
            }
            .padding()
        }
        .frame(width: 440)
    }

    private var trimmedName: String { group.name.trimmingCharacters(in: .whitespaces) }

    private var nameProblem: String? {
        guard isNew, takenNames.contains(trimmedName) else { return nil }
        return String(localized: "A group with this name already exists.")
    }
}

extension BandwidthGroup {
    /// "↓ 2,000 kB/s · ↑ unlimited", plus a note when the server's limits don't apply.
    var limitsSummary: String {
        let down = speedLimitDownEnabled
            ? String(localized: "↓ \(speedLimitDown.formatted()) kB/s")
            : String(localized: "↓ unlimited")
        let up = speedLimitUpEnabled
            ? String(localized: "↑ \(speedLimitUp.formatted()) kB/s")
            : String(localized: "↑ unlimited")
        let parts = [down, up] + (honorsSessionLimits ? [] : [String(localized: "ignores global limits")])
        return parts.joined(separator: " · ")
    }
}
