import SwiftUI
import TransmissionKit

/// Shown when no server is configured or the user disconnected.
struct DisconnectedView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        if model.servers.servers.isEmpty {
            ContentUnavailableView {
                Label("No Server", systemImage: "server.rack")
            } description: {
                Text("Add the address of your Transmission daemon to get started.")
            } actions: {
                ManageServersLink { Text("Add Server…") }
                    .buttonStyle(.borderedProminent)
            }
        } else {
            ContentUnavailableView {
                Label("Not Connected", systemImage: "bolt.horizontal.circle")
            } actions: {
                Button("Connect") { model.connectToActiveServer() }
                    .buttonStyle(.borderedProminent)
            }
        }
    }
}

/// The server can't be reached right now. Counts down to the next automatic attempt
/// instead of giving up, since most drops (sleep, Wi-Fi, a VPN reconnecting) are temporary.
struct ReconnectingView: View {
    @Environment(AppModel.self) private var model
    let attempt: ReconnectAttempt

    var body: some View {
        ContentUnavailableView {
            Label("Can't Reach the Server", systemImage: "wifi.exclamationmark")
        } description: {
            VStack(spacing: 8) {
                Text(attempt.message)
                nextAttemptText
                    .foregroundStyle(.secondary)
                    .monospacedDigit()
                if isOnLocalNetwork {
                    // macOS blocks apps without Local Network access, and they only see
                    // "No route to host" or "offline" (transgui issue #1473, pull request #1614).
                    Text("""
                        If your Mac reaches the server otherwise, check that Transmission Remote is turned on \
                        in System Settings → Privacy & Security → Local Network.
                        """)
                        .font(.callout)
                        .foregroundStyle(.secondary)
                }
            }
        } actions: {
            HStack {
                Button("Try Now") { model.connection.retryNow() }
                    .buttonStyle(.borderedProminent)
                    .disabled(attempt.nextAttempt == nil)
                ManageServersLink { Text("Edit Server…") }
                Button("Stop Trying") { model.connection.disconnect() }
            }
        }
    }

    private var isOnLocalNetwork: Bool {
        model.connection.profile?.endpoint.transportSecurity == .localNetwork
    }

    @ViewBuilder
    private var nextAttemptText: some View {
        if let nextAttempt = attempt.nextAttempt, nextAttempt > .now {
            Text("Trying again in \(Text(timerInterval: Date.now...nextAttempt, countsDown: true))")
        } else {
            Text("Trying again…")
        }
    }
}

/// Explains why the connection failed and offers the matching fix.
struct ConnectionFailureView: View {
    @Environment(AppModel.self) private var model
    let failure: ConnectionFailure

    var body: some View {
        ContentUnavailableView {
            Label(title, systemImage: symbol)
        } description: {
            VStack(spacing: 12) {
                Text(failure.message)
                if case .trustCertificate(let fingerprint) = failure.remedy {
                    CertificateFingerprintView(fingerprint: fingerprint)
                }
            }
        } actions: {
            HStack {
                remedyButton
                Button("Try Again") { model.connectToActiveServer() }
                ManageServersLink { Text("Edit Server…") }
            }
        }
    }

    private var title: LocalizedStringResource {
        switch failure.remedy {
        case .trustCertificate: "Untrusted Certificate"
        case .allowUnencryptedConnection: "Unencrypted Connection Blocked"
        default: "Couldn't Connect"
        }
    }

    private var symbol: String {
        switch failure.remedy {
        case .trustCertificate: "lock.trianglebadge.exclamationmark"
        case .allowUnencryptedConnection: "lock.open.trianglebadge.exclamationmark"
        default: "exclamationmark.triangle"
        }
    }

    @ViewBuilder
    private var remedyButton: some View {
        switch failure.remedy {
        case .trustCertificate(let fingerprint):
            Button("Trust This Certificate") { model.trustCertificate(fingerprint: fingerprint) }
                .buttonStyle(.borderedProminent)
        case .allowUnencryptedConnection:
            Button("Allow Unencrypted Connection", role: .destructive) { model.allowUnencryptedConnection() }
        case .none, .editServer:
            EmptyView()
        }
    }
}

/// Shows a certificate fingerprint so the user can compare it with the server's real one.
struct CertificateFingerprintView: View {
    let fingerprint: String

    var body: some View {
        VStack(spacing: 4) {
            Text("SHA-256 fingerprint presented by the server:")
                .font(.callout)
            Text(fingerprint)
                .font(.system(.caption, design: .monospaced))
                .textSelection(.enabled)
                .multilineTextAlignment(.center)
                .frame(maxWidth: 420)
            Text("Only trust it if it matches the certificate you installed on your server.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }
}
