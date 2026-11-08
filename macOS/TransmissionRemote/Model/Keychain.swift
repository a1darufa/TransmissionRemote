import Foundation
import Security

/// Stores server and proxy passwords, and client certificates, as generic-password Keychain
/// items, up to three per server.
///
/// This uses the login keychain rather than the data-protection keychain: the latter needs a
/// provisioning profile with a keychain access group, which a locally signed app doesn't have.
/// Login-keychain items are still bound to this app's code signature by their access control list.
nonisolated enum Keychain {
    struct Failure: LocalizedError {
        let status: OSStatus

        var errorDescription: String? {
            let message = SecCopyErrorMessageString(status, nil) as String? ?? "status \(status)"
            return "Couldn't update the Keychain: \(message)"
        }
    }

    private static let service = (Bundle.main.bundleIdentifier ?? "TransmissionRemote") + ".server"

    static func password(for serverID: UUID) -> String? {
        readPassword(account: serverID.uuidString)
    }

    /// Saves the password; an empty password removes the item.
    static func setPassword(_ password: String, for serverID: UUID, label: String) throws {
        try writePassword(password, account: serverID.uuidString, label: "Transmission Remote — \(label)")
    }

    static func deletePassword(for serverID: UUID) throws {
        try deleteItem(account: serverID.uuidString)
    }

    static func proxyPassword(for serverID: UUID) -> String? {
        readPassword(account: proxyAccount(for: serverID))
    }

    /// Saves the proxy password; an empty password removes the item.
    static func setProxyPassword(_ password: String, for serverID: UUID, label: String) throws {
        try writePassword(password, account: proxyAccount(for: serverID), label: "Transmission Remote proxy — \(label)")
    }

    static func deleteProxyPassword(for serverID: UUID) throws {
        try deleteItem(account: proxyAccount(for: serverID))
    }

    /// A client certificate file (PKCS #12) and the password that unlocks it.
    struct StoredCertificate: Codable {
        let pkcs12: Data
        let password: String
    }

    static func clientCertificate(for serverID: UUID) -> StoredCertificate? {
        readData(account: certificateAccount(for: serverID))
            .flatMap { try? PropertyListDecoder().decode(StoredCertificate.self, from: $0) }
    }

    static func setClientCertificate(_ certificate: StoredCertificate, for serverID: UUID, label: String) throws {
        let data = try PropertyListEncoder().encode(certificate)
        let itemLabel = "Transmission Remote certificate — \(label)"
        try writeData(data, account: certificateAccount(for: serverID), label: itemLabel)
    }

    static func deleteClientCertificate(for serverID: UUID) throws {
        try deleteItem(account: certificateAccount(for: serverID))
    }

    private static func proxyAccount(for serverID: UUID) -> String {
        serverID.uuidString + ".proxy"
    }

    private static func certificateAccount(for serverID: UUID) -> String {
        serverID.uuidString + ".certificate"
    }

    private static func readPassword(account: String) -> String? {
        readData(account: account).flatMap { String(data: $0, encoding: .utf8) }
    }

    private static func readData(account: String) -> Data? {
        var query = baseQuery(account: account)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne

        var result: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess else { return nil }
        return result as? Data
    }

    private static func writePassword(_ password: String, account: String, label: String) throws {
        guard !password.isEmpty else {
            try deleteItem(account: account)
            return
        }
        try writeData(Data(password.utf8), account: account, label: label)
    }

    private static func writeData(_ data: Data, account: String, label: String) throws {
        let attributes: [String: Any] = [
            kSecValueData as String: data,
            kSecAttrLabel as String: label
        ]
        let updateStatus = SecItemUpdate(baseQuery(account: account) as CFDictionary, attributes as CFDictionary)
        if updateStatus == errSecSuccess { return }
        guard updateStatus == errSecItemNotFound else { throw Failure(status: updateStatus) }

        let addQuery = baseQuery(account: account).merging(attributes) { _, new in new }
        let addStatus = SecItemAdd(addQuery as CFDictionary, nil)
        guard addStatus == errSecSuccess else { throw Failure(status: addStatus) }
    }

    private static func deleteItem(account: String) throws {
        let status = SecItemDelete(baseQuery(account: account) as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else { throw Failure(status: status) }
    }

    private static func baseQuery(account: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account
        ]
    }
}
