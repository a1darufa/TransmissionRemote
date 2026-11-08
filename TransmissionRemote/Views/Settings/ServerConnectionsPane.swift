import SwiftUI
import TransmissionKit

/// Settings → Application Options → Transmission: how the app connects to each server.
/// Laid out like Transmission Remote GUI's "Transmission" tab: pick a connection at the top,
/// edit it below. Switching between servers stays in the toolbar's Server menu.
struct ServerConnectionsPane: View {
    @Environment(AppModel.self) private var model
    @Binding var selectedID: UUID?

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                ServerPicker(selection: $selectedID)
                Button("Add Server", systemImage: "plus") { addServer() }
                Button("Remove Server", systemImage: "minus") { removeSelected() }
                    .disabled(selectedID == nil)
            }
            .labelStyle(.iconOnly)
            .padding()

            Divider()

            if let selectedID, let server = model.servers.server(withID: selectedID) {
                ServerEditor(original: server)
                    .id(selectedID)
            } else {
                ContentUnavailableView(
                    "No Server Selected",
                    systemImage: "server.rack",
                    description: Text("Add a server with the + button.")
                )
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
    }

    private func addServer() {
        var profile = ServerProfile()
        profile.name = String(localized: "New Server")
        model.servers.save(profile)
        selectedID = profile.id
    }

    private func removeSelected() {
        guard let selectedID else { return }
        let wasActive = model.servers.activeServer?.id == selectedID
        model.servers.remove(selectedID)
        self.selectedID = model.servers.servers.first?.id
        if wasActive { model.connectToActiveServer() }
    }
}

/// Chooses which server the Transmission, Proxy and Paths panes edit, like Transmission
/// Remote GUI's "Connection name" list. The panes share one selection.
struct ServerPicker: View {
    @Environment(AppModel.self) private var model
    @Binding var selection: UUID?

    var body: some View {
        Picker("Server", selection: $selection) {
            ForEach(model.servers.servers) { server in
                Text(server.displayName).tag(Optional(server.id))
            }
        }
        .disabled(model.servers.servers.isEmpty)
    }
}

/// Opens Settings on the Transmission pane, where servers are added and edited.
struct ManageServersLink<Label: View>: View {
    @ViewBuilder let label: () -> Label

    var body: some View {
        SettingsPaneLink(pane: .transmission, label: label)
    }
}

/// Edits a copy of a server profile; nothing is saved until the user clicks Save.
private struct ServerEditor: View {
    @Environment(AppModel.self) private var model
    let original: ServerProfile

    @State private var draft = ServerProfile()
    @State private var password = ""
    @State private var certificateChange: ClientCertificateChange?
    @State private var testResult: TestResult?
    @State private var isTesting = false
    @State private var saveError: String?

    private enum TestResult {
        case success(String)
        case failure(String)
    }

    var body: some View {
        VStack(spacing: 0) {
            Form {
                Section("Server") {
                    TextField("Name", text: $draft.name, prompt: Text("My NAS"))
                    TextField("Host", text: $draft.endpoint.host, prompt: Text("192.168.1.10 or nas.tailnet.ts.net"))
                        .autocorrectionDisabled()
                    TextField("Port", value: $draft.endpoint.port, format: .number.grouping(.never))
                    TextField("RPC path", text: $draft.endpoint.rpcPath)
                        .autocorrectionDisabled()
                    Toggle(isOn: $draft.endpoint.usesTLS) {
                        Text("Use HTTPS")
                        Text("Only with a reverse proxy in front of the daemon. Transmission itself serves plain HTTP.")
                    }
                    if let validationError {
                        Label(validationError, systemImage: "exclamationmark.circle").foregroundStyle(.red)
                    }
                }

                Section("Authentication") {
                    TextField("Username", text: $draft.username)
                        .textContentType(.username)
                        .autocorrectionDisabled()
                    SecureField("Password", text: $password)
                        .textContentType(.password)
                    ClientCertificateRow(certificateName: $draft.clientCertificateName, change: $certificateChange)
                }

                Section("Security") {
                    securitySummary
                    if draft.endpoint.transportSecurity == .unencryptedInternet {
                        Toggle("Allow unencrypted connection anyway", isOn: $draft.allowsUnencryptedInternetConnection)
                    }
                    if let fingerprint = draft.pinnedCertificateFingerprint {
                        LabeledContent("Trusted certificate") {
                            VStack(alignment: .trailing, spacing: 4) {
                                Text(fingerprint)
                                    .font(.system(.caption2, design: .monospaced))
                                    .textSelection(.enabled)
                                    .multilineTextAlignment(.trailing)
                                Button("Forget") { draft.pinnedCertificateFingerprint = nil }
                                    .controlSize(.small)
                            }
                        }
                    }
                }

                Section("Advanced") {
                    Picker("Protocol", selection: $draft.protocolPreference) {
                        Text("Automatic").tag(ProtocolPreference.automatic)
                        Text("Legacy RPC only").tag(ProtocolPreference.legacy)
                    }
                    Text("""
                        Automatic uses JSON-RPC 2.0 on Transmission 4.1 and later, \
                        and the legacy protocol on older versions such as 3.00.
                        """)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            .formStyle(.grouped)

            Divider()
            footer
        }
        .onAppear {
            draft = original
            password = model.servers.password(for: original.id) ?? ""
        }
    }

    private var securitySummary: some View {
        let security = draft.endpoint.transportSecurity
        return Label {
            Text(security.summary)
        } icon: {
            Image(systemName: security.symbol).foregroundStyle(security.color)
        }
    }

    private var footer: some View {
        HStack {
            switch testResult {
            case .success(let message):
                Label(message, systemImage: "checkmark.circle.fill").foregroundStyle(.green)
            case .failure(let message):
                Label(message, systemImage: "xmark.octagon.fill").foregroundStyle(.red).lineLimit(2)
            case nil:
                if let saveError { Text(saveError).foregroundStyle(.red) }
            }
            Spacer()
            if isTesting { ProgressView().controlSize(.small) }
            Button("Test Connection") { runConnectionTest() }
                .disabled(isTesting || validationError != nil)
            Button("Save and Connect") { saveAndConnect() }
                .keyboardShortcut(.defaultAction)
                .disabled(validationError != nil)
        }
        .padding()
    }

    private var validationError: String? {
        do {
            _ = try draft.endpoint.url()
            return nil
        } catch {
            return draft.endpoint.host.isEmpty ? nil : error.localizedDescription
        }
    }

    private func saveAndConnect() {
        var profile = model.servers.server(withID: draft.id) ?? draft
        profile.name = draft.name
        profile.endpoint = draft.endpoint
        profile.username = draft.username
        profile.protocolPreference = draft.protocolPreference
        profile.pinnedCertificateFingerprint = draft.pinnedCertificateFingerprint
        profile.allowsUnencryptedInternetConnection = draft.allowsUnencryptedInternetConnection
        profile.clientCertificateName = draft.clientCertificateName
        do {
            try model.servers.setPassword(password, for: profile)
            try saveCertificateChange(for: profile)
            model.servers.save(profile)
            model.switchToServer(profile.id)
            saveError = nil
        } catch {
            saveError = error.localizedDescription
        }
    }

    private func saveCertificateChange(for profile: ServerProfile) throws {
        switch certificateChange {
        case .replace(let certificate): try model.servers.setClientCertificate(certificate, for: profile)
        case .remove: try model.servers.setClientCertificate(nil, for: profile)
        case nil: return
        }
        certificateChange = nil
    }

    /// The certificate the editor would save: a newly chosen one, or the stored one.
    private func certificateForTest() throws -> ClientIdentity? {
        switch certificateChange {
        case .replace(let certificate): try ClientCertificates.identity(from: certificate)
        case .remove: nil
        case nil: try ClientCertificates.identity(for: draft)
        }
    }

    /// Connects with a throwaway client, without touching the live connection.
    private func runConnectionTest() {
        guard !draft.isBlockedByTransportPolicy else {
            testResult = .failure(String(localized: "Plain HTTP over the internet is blocked for this server."))
            return
        }
        isTesting = true
        testResult = nil
        let profile = draft
        let credentials = profile.username.isEmpty ? nil : Credentials(username: profile.username, password: password)

        Task {
            defer { isTesting = false }
            do {
                let client = try TransmissionClient(
                    endpoint: profile.endpoint,
                    credentials: credentials,
                    pinnedCertificateFingerprint: profile.pinnedCertificateFingerprint,
                    protocolPreference: profile.protocolPreference,
                    timeout: .seconds(10),
                    proxy: profile.proxy.configuration(password: model.servers.proxyPassword(for: profile.id)),
                    clientIdentity: try certificateForTest()
                )
                let info = try await client.connect()
                let dialect = await client.dialect
                let protocolName = dialect == .jsonRPC ? "JSON-RPC" : String(localized: "legacy RPC")
                let message = String(localized: "Connected to Transmission \(info.version) using \(protocolName).")
                testResult = .success(message)
            } catch TransmissionError.untrustedCertificate(let fingerprint) {
                let fingerprintStart = fingerprint.prefix(23)
                testResult = .failure(String(localized: """
                    Untrusted certificate. Connect from the main window to review and trust it \
                    (\(fingerprintStart)…).
                    """))
            } catch {
                testResult = .failure(error.localizedDescription)
            }
        }
    }
}
