import Foundation
import Security

/// A client certificate and its private key, for HTTPS reverse proxies that ask for one
/// (mutual TLS), like Transmission Remote GUI's "Client Certificate" option. Imported from a
/// PKCS #12 (.p12) file, the format Keychain Access exports and `openssl pkcs12 -export` writes.
///
/// `SecIdentity` and `SecCertificate` are immutable Core Foundation objects that are safe to
/// share between threads, but the SDK doesn't mark them `Sendable`, hence `@unchecked`.
public struct ClientIdentity: @unchecked Sendable {
    public enum ImportError: Error, Equatable {
        case wrongPassword
        case unreadable
        case noPrivateKey
    }

    private let identity: SecIdentity
    private let intermediates: [SecCertificate]
    /// The certificate's subject, usually its common name, for showing which one is chosen.
    public let summary: String
    public let expiryDate: Date?

    /// Imports into memory only, so the key never lands in a keychain the app doesn't manage.
    public init(pkcs12 data: Data, password: String) throws(ImportError) {
        let options: [String: Any] = [
            kSecImportExportPassphrase as String: password,
            kSecImportToMemoryOnly as String: true
        ]
        var items: CFArray?
        switch SecPKCS12Import(data as CFData, options as CFDictionary, &items) {
        case errSecSuccess: break
        case errSecAuthFailed, errSecPkcs12VerifyFailure: throw .wrongPassword
        default: throw .unreadable
        }

        guard let item = (items as? [[String: Any]])?.first,
              let identityRef = item[kSecImportItemIdentity as String] as CFTypeRef?,
              CFGetTypeID(identityRef) == SecIdentityGetTypeID()
        else { throw .noPrivateKey }
        // The type ID check above makes this cast safe; Core Foundation types can't be cast conditionally.
        let identity = identityRef as! SecIdentity // swiftlint:disable:this force_cast

        var leaf: SecCertificate?
        SecIdentityCopyCertificate(identity, &leaf)
        let chain = item[kSecImportItemCertChain as String] as? [SecCertificate] ?? []

        self.identity = identity
        self.intermediates = chain.filter { certificate in leaf.map { !CFEqual(certificate, $0) } ?? true }
        self.summary = leaf.flatMap { SecCertificateCopySubjectSummary($0) as String? } ?? ""
        self.expiryDate = leaf.flatMap { SecCertificateCopyNotValidAfterDate($0) as Date? }
    }

    /// What to answer a server's client-certificate request with.
    var credential: URLCredential {
        URLCredential(
            identity: identity,
            certificates: intermediates.isEmpty ? nil : intermediates,
            persistence: .forSession
        )
    }
}
