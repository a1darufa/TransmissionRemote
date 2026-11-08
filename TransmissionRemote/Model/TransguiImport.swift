import AppKit
import TransmissionKit
import UniformTypeIdentifiers

/// What an import from transgui.ini did, for the summary shown afterwards.
struct TransguiImportSummary: Identifiable {
    let id = UUID()
    var importedNames: [String] = []
    var alreadyAddedNames: [String] = []
    var firstImportedID: UUID?
    var hasPathMappings = false
    var hasClientCertificates = false

    var message: String {
        var lines: [String] = []
        if importedNames.isEmpty {
            lines.append(String(localized: "Every server in transgui.ini is already set up here."))
        } else {
            let names = importedNames.formatted(.list(type: .and))
            lines.append(String(localized: "Added \(names), with passwords, proxies and folder history."))
        }
        if !alreadyAddedNames.isEmpty, !importedNames.isEmpty {
            let names = alreadyAddedNames.formatted(.list(type: .and))
            lines.append(String(localized: "Skipped \(names), already set up here."))
        }
        if hasPathMappings {
            lines.append(String(localized: """
                To open downloaded files, choose each local folder once in Settings → Paths, \
                so macOS lets the app open it.
                """))
        }
        if hasClientCertificates {
            lines.append(String(localized: """
                Client certificates weren't imported. Choose them in Settings → Transmission.
                """))
        }
        return lines.joined(separator: "\n\n")
    }
}

struct TransguiImportFailure: LocalizedError {
    var errorDescription: String? {
        String(localized: "No servers were found in this file. Choose transgui.ini from Transmission Remote GUI.")
    }
}

extension AppModel {
    /// Asks for transgui.ini, starting in the folder where transgui keeps it on a Mac. The panel
    /// shows hidden folders, because that folder is inside the hidden ~/.config.
    func chooseTransguiSettingsFile() -> URL? {
        let panel = NSOpenPanel()
        panel.title = String(localized: "Import from Transmission Remote GUI")
        panel.message = String(localized: "Choose transgui.ini. Transmission Remote GUI keeps it in ~/.config.")
        panel.prompt = String(localized: "Import")
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.showsHiddenFiles = true
        panel.allowedContentTypes = [UTType(filenameExtension: "ini") ?? .plainText]
        if let home = Self.userHomeDirectory {
            panel.directoryURL = home.appending(
                components: ".config", "Transmission Remote GUI", directoryHint: .isDirectory
            )
        }
        return panel.runModal() == .OK ? panel.url : nil
    }

    /// Adds the servers from transgui.ini, skipping ones already set up, along with their
    /// passwords (into the Keychain), proxies, path mappings and folder history.
    func importTransguiSettings(from url: URL) throws -> TransguiImportSummary {
        let isAccessing = url.startAccessingSecurityScopedResource()
        defer { if isAccessing { url.stopAccessingSecurityScopedResource() } }
        let data = try Data(contentsOf: url)
        let text = String(bytes: data, encoding: .utf8) ?? String(bytes: data, encoding: .isoLatin1) ?? ""
        let settings = TransguiSettings(iniText: text)
        guard !settings.connections.isEmpty else { throw TransguiImportFailure() }

        let hadServers = !servers.servers.isEmpty
        var summary = TransguiImportSummary()
        var activeID: UUID?
        for connection in settings.connections {
            if servers.servers.contains(where: { $0.matches(connection) }) {
                summary.alreadyAddedNames.append(connection.name)
                continue
            }
            let profile = ServerProfile(transgui: connection)
            servers.save(profile)
            if let password = connection.password { try servers.setPassword(password, for: profile) }
            if let proxyPassword = connection.proxy?.password {
                try servers.setProxyPassword(proxyPassword, for: profile)
            }

            summary.importedNames.append(connection.name)
            summary.firstImportedID = summary.firstImportedID ?? profile.id
            summary.hasPathMappings = summary.hasPathMappings || !connection.pathMappings.isEmpty
            summary.hasClientCertificates = summary.hasClientCertificates || connection.hasClientCertificate
            if connection.name == settings.activeConnectionName { activeID = profile.id }
        }

        applyAppWideSettings(from: settings)
        // Someone setting up the app for the first time continues where they left off in transgui.
        if !hadServers, let id = activeID ?? summary.firstImportedID {
            switchToServer(id)
        }
        return summary
    }

    /// transgui keeps speed menus per connection; this app has one set, taken from the
    /// connection transgui last used.
    private func applyAppWideSettings(from settings: TransguiSettings) {
        let defaults = UserDefaults.standard
        let active = settings.connections.first { $0.name == settings.activeConnectionName }
            ?? settings.connections.first
        if let speeds = active?.downloadSpeeds { defaults.set(speeds, forKey: AppSettings.downloadSpeedPresets) }
        if let speeds = active?.uploadSpeeds { defaults.set(speeds, forKey: AppSettings.uploadSpeedPresets) }
        if let limit = settings.folderHistoryLimit, limit > 0 {
            defaults.set(min(limit, 999), forKey: AppSettings.folderHistoryLimit)
        }
    }

    /// The real home folder. Inside the sandbox, `FileManager` reports the app's container instead.
    private static var userHomeDirectory: URL? {
        guard let entry = getpwuid(getuid()), let path = entry.pointee.pw_dir else { return nil }
        return URL(filePath: String(cString: path), directoryHint: .isDirectory)
    }
}

extension ServerProfile {
    init(transgui connection: TransguiSettings.Connection) {
        self.init()
        name = connection.name
        endpoint = ServerEndpoint(
            host: connection.host,
            port: connection.port,
            rpcPath: connection.rpcPath,
            usesTLS: connection.usesTLS
        )
        username = connection.username
        pathMappings = connection.pathMappings.map { PathMapping(remotePath: $0.remotePath, localPath: $0.localPath) }
        recentFolders = connection.folders.map {
            RecentFolder(path: $0.path, useCount: $0.useCount, lastUsed: $0.lastUsed ?? .distantPast)
        }
        if let transguiProxy = connection.proxy {
            proxy.isEnabled = true
            proxy.host = transguiProxy.host
            proxy.port = transguiProxy.port
            proxy.usesSOCKS5 = transguiProxy.usesSOCKS5
            proxy.username = transguiProxy.username
            proxy.requiresAuthentication = !transguiProxy.username.isEmpty
        }
    }

    /// The same daemon, whatever it's called.
    func matches(_ connection: TransguiSettings.Connection) -> Bool {
        let other = ServerEndpoint(host: connection.host, port: connection.port, rpcPath: connection.rpcPath)
        return endpoint.normalizedHost == other.normalizedHost
            && endpoint.port == connection.port
            && endpoint.rpcPath == connection.rpcPath
    }
}
