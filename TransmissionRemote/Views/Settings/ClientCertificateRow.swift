import SwiftUI
import TransmissionKit
import UniformTypeIdentifiers

private struct ChosenFile {
    let name: String
    let data: Data
}

/// A change to a server's client certificate, applied when the server is saved.
enum ClientCertificateChange {
    case replace(Keychain.StoredCertificate)
    case remove
}

/// Chooses the client certificate a server's HTTPS reverse proxy asks for, like Transmission
/// Remote GUI's "Client Certificate" field. The file's password is checked right away, so a
/// wrong one shows up here rather than as a failed connection later.
struct ClientCertificateRow: View {
    /// The chosen certificate's subject, shown in the row.
    @Binding var certificateName: String?
    @Binding var change: ClientCertificateChange?

    @State private var isChoosingFile = false
    @State private var chosenFile: ChosenFile?
    @State private var filePassword = ""
    @State private var problem: String?

    var body: some View {
        LabeledContent("Client certificate") {
            HStack {
                Text(certificateName ?? String(localized: "None"))
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                Button("Choose…") { isChoosingFile = true }
                if certificateName != nil {
                    Button("Remove") {
                        certificateName = nil
                        change = .remove
                    }
                }
            }
        }
        .fileImporter(isPresented: $isChoosingFile, allowedContentTypes: [.pkcs12]) { result in
            readChosenFile(result)
        }
        .alert(
            "Certificate Password",
            isPresented: Binding { chosenFile != nil } set: { if !$0 { chosenFile = nil } },
            presenting: chosenFile
        ) { file in
            SecureField("Password", text: $filePassword)
            Button("Use Certificate") { use(file) }
            Button("Cancel", role: .cancel) { filePassword = "" }
        } message: { file in
            Text("Enter the password that protects \(file.name).")
        }

        Text("Only for HTTPS reverse proxies that ask for one. Choose a .p12 file with the certificate and its key.")
            .font(.caption)
            .foregroundStyle(.secondary)
        if let problem {
            Label(problem, systemImage: "exclamationmark.circle").foregroundStyle(.red)
        }
    }

    private func readChosenFile(_ result: Result<URL, any Error>) {
        problem = nil
        guard case .success(let url) = result else { return }
        let isAccessing = url.startAccessingSecurityScopedResource()
        defer { if isAccessing { url.stopAccessingSecurityScopedResource() } }
        do {
            chosenFile = ChosenFile(name: url.lastPathComponent, data: try Data(contentsOf: url))
        } catch {
            problem = error.localizedDescription
        }
    }

    private func use(_ file: ChosenFile) {
        defer { filePassword = "" }
        let stored = Keychain.StoredCertificate(pkcs12: file.data, password: filePassword)
        do {
            let identity = try ClientCertificates.identity(from: stored)
            certificateName = identity.summary.isEmpty ? file.name : identity.summary
            change = .replace(stored)
            problem = nil
        } catch {
            problem = error.localizedDescription
        }
    }
}
