import Foundation
import TransmissionKit

/// Turns a server's stored client certificate into the identity its connection presents.
nonisolated enum ClientCertificates {
    struct Failure: LocalizedError {
        let errorDescription: String?
    }

    /// Nil when the server has no certificate; throws when it has one that can't be used.
    static func identity(for profile: ServerProfile) throws -> ClientIdentity? {
        guard profile.clientCertificateName != nil else { return nil }
        guard let stored = Keychain.clientCertificate(for: profile.id) else {
            throw Failure(errorDescription: String(localized: """
                This server's client certificate is missing from the Keychain. \
                Choose it again in Settings → Transmission.
                """))
        }
        return try identity(from: stored)
    }

    static func identity(from stored: Keychain.StoredCertificate) throws -> ClientIdentity {
        do {
            return try ClientIdentity(pkcs12: stored.pkcs12, password: stored.password)
        } catch {
            throw Failure(errorDescription: message(for: error))
        }
    }

    static func message(for error: ClientIdentity.ImportError) -> String {
        switch error {
        case .wrongPassword:
            String(localized: "The certificate file's password is wrong.")
        case .unreadable:
            String(localized: "This isn't a certificate file in PKCS #12 (.p12) format.")
        case .noPrivateKey:
            String(localized: "The file has no private key. Export the certificate together with its key.")
        }
    }
}
