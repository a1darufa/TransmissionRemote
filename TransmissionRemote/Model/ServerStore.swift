import Foundation
import Observation
import os

/// Saved server profiles, persisted as JSON in the app's sandbox container.
@Observable
final class ServerStore {
    private(set) var servers: [ServerProfile] = []
    private(set) var activeServerID: UUID?

    private let fileURL: URL
    private static let logger = Logger(subsystem: "TransmissionRemote", category: "ServerStore")

    init(fileURL: URL = ServerStore.defaultFileURL) {
        self.fileURL = fileURL
        load()
    }

    var activeServer: ServerProfile? {
        servers.first { $0.id == activeServerID } ?? servers.first
    }

    func server(withID id: UUID) -> ServerProfile? {
        servers.first { $0.id == id }
    }

    // MARK: - Editing

    func save(_ profile: ServerProfile) {
        if let index = servers.firstIndex(where: { $0.id == profile.id }) {
            servers[index] = profile
        } else {
            servers.append(profile)
        }
        if activeServerID == nil { activeServerID = profile.id }
        persist()
    }

    func remove(_ id: UUID) {
        servers.removeAll { $0.id == id }
        try? Keychain.deletePassword(for: id)
        try? Keychain.deleteProxyPassword(for: id)
        try? Keychain.deleteClientCertificate(for: id)
        if activeServerID == id { activeServerID = servers.first?.id }
        persist()
    }

    func activate(_ id: UUID) {
        activeServerID = id
        persist()
    }

    func recordFolderUse(_ path: String, onServer id: UUID) {
        guard let index = servers.firstIndex(where: { $0.id == id }) else { return }
        servers[index].recordUse(ofFolder: path, limit: AppSettings.currentFolderHistoryLimit)
        persist()
    }

    /// Applies a lowered folder history limit to every server right away.
    func trimFolderHistories(to limit: Int) {
        for index in servers.indices {
            servers[index].trimFolderHistory(to: limit)
        }
        persist()
    }

    func password(for id: UUID) -> String? {
        Keychain.password(for: id)
    }

    func setPassword(_ password: String, for profile: ServerProfile) throws {
        try Keychain.setPassword(password, for: profile.id, label: profile.displayName)
    }

    func proxyPassword(for id: UUID) -> String? {
        Keychain.proxyPassword(for: id)
    }

    func setProxyPassword(_ password: String, for profile: ServerProfile) throws {
        try Keychain.setProxyPassword(password, for: profile.id, label: profile.displayName)
    }

    func clientCertificate(for id: UUID) -> Keychain.StoredCertificate? {
        Keychain.clientCertificate(for: id)
    }

    /// Saves the certificate, or removes the stored one when `certificate` is nil.
    func setClientCertificate(_ certificate: Keychain.StoredCertificate?, for profile: ServerProfile) throws {
        if let certificate {
            try Keychain.setClientCertificate(certificate, for: profile.id, label: profile.displayName)
        } else {
            try Keychain.deleteClientCertificate(for: profile.id)
        }
    }

    // MARK: - Persistence

    private static var defaultFileURL: URL {
        URL.applicationSupportDirectory.appending(path: "servers.json")
    }

    /// Loads the saved servers. When any part can't be read, for example after going back to an
    /// older version, a copy of the file is kept first: the next save rewrites servers.json, and
    /// without the copy the unreadable servers would be gone for good (transgui issue #1344).
    private func load() {
        guard let data = try? Data(contentsOf: fileURL) else { return }
        do {
            let state = try JSONDecoder().decode(StoredState.self, from: data)
            servers = state.servers
            activeServerID = state.activeServerID
            let storedCount = (try? JSONSerialization.jsonObject(with: data) as? [String: Any])
                .flatMap { $0["servers"] as? [Any] }?.count ?? servers.count
            if storedCount > servers.count {
                keepCopy(of: data, because: "\(storedCount - servers.count) saved servers couldn't be read")
            }
        } catch {
            keepCopy(of: data, because: error.localizedDescription)
        }
    }

    private func keepCopy(of data: Data, because reason: String) {
        let stamp = Date.now.formatted(.iso8601.year().month().day().time(includingFractionalSeconds: false))
            .replacingOccurrences(of: ":", with: "-")
        let copy = fileURL.deletingLastPathComponent().appending(path: "servers-unreadable-\(stamp).json")
        do {
            try data.write(to: copy, options: .withoutOverwriting)
            Self.logger.error("\(reason, privacy: .public). Kept a copy at \(copy.path, privacy: .public)")
        } catch {
            Self.logger.error("\(reason, privacy: .public), and the copy failed: \(error.localizedDescription)")
        }
    }

    private func persist() {
        do {
            try FileManager.default.createDirectory(
                at: fileURL.deletingLastPathComponent(),
                withIntermediateDirectories: true
            )
            let data = try JSONEncoder().encode(StoredState(servers: servers, activeServerID: activeServerID))
            try data.write(to: fileURL, options: .atomic)
        } catch {
            Self.logger.error("Couldn't save servers: \(error.localizedDescription, privacy: .public)")
        }
    }
}

/// The saved file: the servers and which one is active.
private struct StoredState: Codable {
    var servers: [ServerProfile]
    var activeServerID: UUID?

    enum CodingKeys: String, CodingKey {
        case servers, activeServerID
    }

    init(servers: [ServerProfile], activeServerID: UUID?) {
        self.servers = servers
        self.activeServerID = activeServerID
    }

    /// Reads servers one by one, so a single unreadable entry costs only that entry.
    init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        activeServerID = try? container.decodeIfPresent(UUID.self, forKey: .activeServerID)
        var list = try container.nestedUnkeyedContainer(forKey: .servers)
        var servers: [ServerProfile] = []
        while !list.isAtEnd {
            if let server = try? list.decode(ServerProfile.self) {
                servers.append(server)
            } else {
                // A failed decode doesn't move past the element; skipping it does.
                _ = try list.decode(SkippedEntry.self)
            }
        }
        self.servers = servers
    }
}

private struct SkippedEntry: Decodable {
    init(from decoder: any Decoder) {}
}
