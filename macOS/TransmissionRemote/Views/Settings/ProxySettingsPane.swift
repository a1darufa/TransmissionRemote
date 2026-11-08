import SwiftUI

/// Settings → Application Options → Proxy, like Transmission Remote GUI's tab: reach the selected
/// server through an HTTP or SOCKS 5 proxy.
struct ProxySettingsPane: View {
    @Environment(AppModel.self) private var model
    @Binding var serverID: UUID?

    var body: some View {
        VStack(spacing: 0) {
            ServerPicker(selection: $serverID)
                .padding()

            Divider()

            if let serverID, let server = model.servers.server(withID: serverID) {
                ProxyEditor(original: server)
                    .id(serverID)
            } else {
                ContentUnavailableView(
                    "No Server Selected",
                    systemImage: "server.rack",
                    description: Text("Add a server in Settings → Transmission first.")
                )
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
    }
}

/// Edits a copy of one server's proxy; nothing changes until Save and Connect.
private struct ProxyEditor: View {
    @Environment(AppModel.self) private var model
    let original: ServerProfile

    @State private var proxy = ProxySettings()
    @State private var password = ""
    @State private var savedPassword = ""
    @State private var saveError: String?

    var body: some View {
        VStack(spacing: 0) {
            Form {
                Section {
                    Toggle("Connect to Transmission using proxy server", isOn: $proxy.isEnabled)
                }

                Section {
                    TextField("Proxy server", text: $proxy.host, prompt: Text("proxy.example.com"))
                        .autocorrectionDisabled()
                    NumberRow(title: "Proxy port", value: $proxy.port, unit: nil)
                    Toggle("SOCKS 5", isOn: $proxy.usesSOCKS5)
                }
                .disabled(!proxy.isEnabled)

                Section {
                    Toggle("Authentication required", isOn: $proxy.requiresAuthentication)
                    Group {
                        TextField("Proxy user name", text: $proxy.username)
                            .textContentType(.username)
                            .autocorrectionDisabled()
                        SecureField("Proxy password", text: $password)
                            .textContentType(.password)
                    }
                    .disabled(!proxy.requiresAuthentication)
                }
                .disabled(!proxy.isEnabled)
            }
            .formStyle(.grouped)

            Divider()
            footer
        }
        .onAppear {
            proxy = original.proxy
            savedPassword = model.servers.proxyPassword(for: original.id) ?? ""
            password = savedPassword
        }
    }

    private var footer: some View {
        HStack {
            if let message = saveError ?? proxy.validationError {
                Label(message, systemImage: "exclamationmark.circle")
                    .foregroundStyle(.red)
                    .lineLimit(2)
            }
            Spacer()
            Button("Save and Connect") { saveAndConnect() }
                .keyboardShortcut(.defaultAction)
                .disabled(!hasChanges || proxy.validationError != nil)
        }
        .padding()
    }

    private var hasChanges: Bool {
        proxy != original.proxy || password != savedPassword
    }

    private func saveAndConnect() {
        // Start from the latest saved profile so edits made elsewhere meanwhile are kept.
        var profile = model.servers.server(withID: original.id) ?? original
        profile.proxy = proxy
        do {
            // A password the proxy no longer needs shouldn't linger in the Keychain.
            try model.servers.setProxyPassword(proxy.requiresAuthentication ? password : "", for: profile)
            model.servers.save(profile)
            model.switchToServer(profile.id)
            savedPassword = proxy.requiresAuthentication ? password : ""
            saveError = nil
        } catch {
            saveError = error.localizedDescription
        }
    }
}
