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

    private struct StoredState: Codable {
        var servers: [ServerProfile]
        var activeServerID: UUID?
    }

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

    private func load() {
        guard let data = try? Data(contentsOf: fileURL) else { return }
        do {
            let state = try JSONDecoder().decode(StoredState.self, from: data)
            servers = state.servers
            activeServerID = state.activeServerID
        } catch {
            Self.logger.error("Couldn't read saved servers: \(error.localizedDescription, privacy: .public)")
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
