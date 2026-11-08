import Foundation
import TransmissionKit

/// A server's proxy, like Transmission Remote GUI's Proxy tab. The password is in the Keychain.
nonisolated struct ProxySettings: Codable, Hashable, Sendable {
    var isEnabled = false
    var host = ""
    var port = 8080
    var usesSOCKS5 = false
    var requiresAuthentication = false
    var username = ""

    /// Why these settings can't be used yet, or nil when they're fine (or the proxy is off).
    var validationError: String? {
        guard isEnabled else { return nil }
        if host.trimmingCharacters(in: .whitespaces).isEmpty {
            return String(localized: "Enter the proxy server's address.")
        }
        if !(1...65_535).contains(port) {
            return String(localized: "The proxy port must be between 1 and 65535.")
        }
        return nil
    }

    /// What the client needs, or nil when no proxy should be used.
    func configuration(password: String?) -> ProxyConfiguration? {
        guard isEnabled, validationError == nil else { return nil }
        let credentials = requiresAuthentication && !username.isEmpty
            ? Credentials(username: username, password: password ?? "")
            : nil
        return ProxyConfiguration(
            kind: usesSOCKS5 ? .socks5 : .http,
            host: host.trimmingCharacters(in: .whitespaces),
            port: port,
            credentials: credentials
        )
    }
}

nonisolated extension ProxySettings {
    enum CodingKeys: String, CodingKey {
        case isEnabled, host, port, usesSOCKS5, requiresAuthentication, username
    }

    /// Missing keys fall back to the defaults, like `ServerProfile`.
    init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let defaults = ProxySettings()
        isEnabled = try container.decodeIfPresent(Bool.self, forKey: .isEnabled) ?? defaults.isEnabled
        host = try container.decodeIfPresent(String.self, forKey: .host) ?? defaults.host
        port = try container.decodeIfPresent(Int.self, forKey: .port) ?? defaults.port
        usesSOCKS5 = try container.decodeIfPresent(Bool.self, forKey: .usesSOCKS5) ?? defaults.usesSOCKS5
        requiresAuthentication = try container.decodeIfPresent(Bool.self, forKey: .requiresAuthentication)
            ?? defaults.requiresAuthentication
        username = try container.decodeIfPresent(String.self, forKey: .username) ?? defaults.username
    }
}
