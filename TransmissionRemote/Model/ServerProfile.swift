import Foundation
import TransmissionKit

/// A saved daemon connection. The password is kept in the Keychain, never in this struct.
nonisolated struct ServerProfile: Identifiable, Codable, Hashable, Sendable {
    var id = UUID()
    var name = ""
    var endpoint = ServerEndpoint(host: "")
    var username = ""
    var protocolPreference = ProtocolPreference.automatic
    /// SHA-256 of a self-signed certificate the user explicitly chose to trust.
    var pinnedCertificateFingerprint: String?
    /// Plain HTTP to a public address exposes the password, so the user has to opt in per server.
    var allowsUnencryptedInternetConnection = false
    /// Download folders used on this server, for the "Save to" suggestions.
    var recentFolders: [RecentFolder] = []
    /// Where this server's folders are on this Mac, for Open and Show in Finder.
    var pathMappings: [PathMapping] = []
    /// An optional proxy to reach this server through.
    var proxy = ProxySettings()
    /// The chosen client certificate's subject, for display. The certificate itself is in the Keychain.
    var clientCertificateName: String?

    var displayName: String {
        name.isEmpty ? endpoint.host : name
    }

    var isBlockedByTransportPolicy: Bool {
        endpoint.transportSecurity == .unencryptedInternet && !allowsUnencryptedInternetConnection
    }

    /// Most-used folders first, like Transmission Remote GUI's folder history.
    var suggestedFolders: [String] {
        recentFolders
            .sorted { ($0.useCount, $0.lastUsed) > ($1.useCount, $1.lastUsed) }
            .map(\.path)
    }

    /// Remembers a folder for "Save to", keeping at most `limit` folders (Settings → Paths).
    mutating func recordUse(ofFolder path: String, limit: Int) {
        let path = path.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !path.isEmpty else { return }

        if let index = recentFolders.firstIndex(where: { $0.path == path }) {
            recentFolders[index].useCount += 1
            recentFolders[index].lastUsed = .now
        } else {
            recentFolders.append(RecentFolder(path: path, useCount: 1, lastUsed: .now))
        }
        trimFolderHistory(to: limit)
    }

    /// Keeps the `limit` most recently used folders.
    mutating func trimFolderHistory(to limit: Int) {
        guard recentFolders.count > limit else { return }
        recentFolders = Array(recentFolders.sorted { $0.lastUsed > $1.lastUsed }.prefix(max(limit, 1)))
    }
}

nonisolated struct RecentFolder: Codable, Hashable, Sendable {
    var path: String
    var useCount: Int
    var lastUsed: Date
}

nonisolated extension ServerProfile {
    enum CodingKeys: String, CodingKey {
        case id, name, endpoint, username, protocolPreference, pinnedCertificateFingerprint
        case allowsUnencryptedInternetConnection, recentFolders, pathMappings, proxy, clientCertificateName
    }

    /// Missing keys fall back to the defaults. Without this, adding a setting would make profiles
    /// saved by an older version fail to load, and the next save would overwrite them.
    init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let defaults = ServerProfile()
        id = try container.decodeIfPresent(UUID.self, forKey: .id) ?? defaults.id
        name = try container.decodeIfPresent(String.self, forKey: .name) ?? defaults.name
        endpoint = try container.decodeIfPresent(ServerEndpoint.self, forKey: .endpoint) ?? defaults.endpoint
        username = try container.decodeIfPresent(String.self, forKey: .username) ?? defaults.username
        // Settings that can't be read (say, a value a newer version added) fall back to their
        // defaults rather than making the whole server unreadable.
        func setting<Value: Decodable>(_ key: CodingKeys) -> Value? {
            try? container.decodeIfPresent(Value.self, forKey: key)
        }
        protocolPreference = setting(.protocolPreference) ?? defaults.protocolPreference
        pinnedCertificateFingerprint = setting(.pinnedCertificateFingerprint)
        allowsUnencryptedInternetConnection = setting(.allowsUnencryptedInternetConnection)
            ?? defaults.allowsUnencryptedInternetConnection
        recentFolders = setting(.recentFolders) ?? defaults.recentFolders
        pathMappings = setting(.pathMappings) ?? defaults.pathMappings
        proxy = setting(.proxy) ?? defaults.proxy
        clientCertificateName = setting(.clientCertificateName)
    }
}
