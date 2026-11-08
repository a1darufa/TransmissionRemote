import Foundation

/// Servers and folder history from Transmission Remote GUI's transgui.ini, so people moving
/// over don't have to set everything up again. Only reads; transgui's file is left untouched.
public struct TransguiSettings: Sendable, Equatable {
    public struct Connection: Sendable, Equatable {
        public var name: String
        public var host: String
        public var port: Int
        public var rpcPath: String
        public var usesTLS: Bool
        public var username: String
        /// Nil when no password is saved, including when transgui asks for it on every connect.
        public var password: String?
        public var proxy: Proxy?
        public var pathMappings: [MappedFolder]
        /// Speed menu items in KB/s, comma-separated, the same format this app uses.
        public var downloadSpeeds: String?
        public var uploadSpeeds: String?
        /// transgui keeps certificates as file paths, which this app can't open without the user
        /// choosing the file, so they're reported rather than imported.
        public var hasClientCertificate: Bool
        public var folders: [Folder]
    }

    public struct Proxy: Sendable, Equatable {
        public var host: String
        public var port: Int
        public var usesSOCKS5: Bool
        public var username: String
        public var password: String?
    }

    public struct MappedFolder: Sendable, Equatable {
        public var remotePath: String
        public var localPath: String
    }

    public struct Folder: Sendable, Equatable {
        public var path: String
        public var useCount: Int
        public var lastUsed: Date?
    }

    public var connections: [Connection]
    /// The connection transgui last used.
    public var activeConnectionName: String?
    public var folderHistoryLimit: Int?

    public init(iniText: String) {
        let ini = IniFile(text: iniText)
        let names = Self.connectionNames(in: ini)
        connections = names.compactMap { Self.connection(named: $0, in: ini) }
        activeConnectionName = ini.value("CurHost", in: "Hosts").flatMap { $0.isEmpty ? nil : $0 }
        folderHistoryLimit = ini.value("MaxFoldersHistory", in: "Interface").flatMap { Int($0) }
    }

    /// transgui lists connections under [Hosts]; very old versions had a single [Connection].
    private static func connectionNames(in ini: IniFile) -> [String] {
        let count = ini.value("Count", in: "Hosts").flatMap { Int($0) } ?? 0
        let listed = (1...max(count, 1)).compactMap { ini.value("Host\($0)", in: "Hosts") }.filter { !$0.isEmpty }
        if !listed.isEmpty { return listed }
        let sectionNames = ini.sectionNames.compactMap { name -> String? in
            name.lowercased().hasPrefix("connection.") ? String(name.dropFirst("connection.".count)) : nil
        }
        if !sectionNames.isEmpty { return sectionNames }
        return ini.value("Host", in: "Connection").map { [$0] } ?? []
    }

    private static func connection(named name: String, in ini: IniFile) -> Connection? {
        let section = ini.hasSection("Connection.\(name)") ? "Connection.\(name)" : "Connection"
        func string(_ key: String) -> String? { ini.value(key, in: section) }
        func bool(_ key: String) -> Bool { string(key).map(IniFile.bool) ?? false }

        guard let host = string("Host")?.trimmingCharacters(in: .whitespaces), !host.isEmpty else { return nil }
        let rpcPath = string("RpcPath").flatMap { $0.isEmpty ? nil : $0 } ?? "/transmission/rpc"

        return Connection(
            name: name,
            host: host,
            port: string("Port").flatMap { Int($0) } ?? 9091,
            rpcPath: rpcPath,
            usesTLS: bool("UseSSL"),
            username: string("UserName") ?? "",
            password: string("Password").flatMap(decodedPassword),
            proxy: bool("UseProxy") ? proxy(string: string, usesSOCKS5: bool("UseSockProxy")) : nil,
            pathMappings: string("PathMap").map(mappedFolders) ?? [],
            downloadSpeeds: string("DownSpeeds").flatMap { $0.isEmpty ? nil : $0 },
            uploadSpeeds: string("UpSpeeds").flatMap { $0.isEmpty ? nil : $0 },
            hasClientCertificate: !(string("CertFile") ?? "").isEmpty,
            folders: folders(in: ini, connection: name)
        )
    }

    private static func proxy(string: (String) -> String?, usesSOCKS5: Bool) -> Proxy? {
        guard let host = string("ProxyHost")?.trimmingCharacters(in: .whitespaces), !host.isEmpty else { return nil }
        return Proxy(
            host: host,
            port: string("ProxyPort").flatMap { Int($0) } ?? 8080,
            usesSOCKS5: usesSOCKS5,
            username: string("ProxyUser") ?? "",
            password: string("ProxyPass").flatMap(decodedPassword)
        )
    }

    /// transgui saves passwords as base64, and "-" when it should ask every time.
    private static func decodedPassword(_ stored: String) -> String? {
        guard !stored.isEmpty, stored != "-", let data = Data(base64Encoded: stored) else { return nil }
        return String(bytes: data, encoding: .utf8)
    }

    /// "remote=local" pairs separated by "|".
    private static func mappedFolders(_ text: String) -> [MappedFolder] {
        text.split(separator: "|").compactMap { pair in
            let parts = pair.split(separator: "=", maxSplits: 1).map { $0.trimmingCharacters(in: .whitespaces) }
            guard parts.count == 2, !parts[0].isEmpty, !parts[1].isEmpty else { return nil }
            return MappedFolder(remotePath: parts[0], localPath: parts[1])
        }
    }

    private static func folders(in ini: IniFile, connection name: String) -> [Folder] {
        let section = "AddTorrent.\(name)"
        let count = ini.value("FolderCount", in: section).flatMap { Int($0) } ?? 0
        guard count > 0 else { return [] }
        return (0..<min(count, 999)).compactMap { index in
            guard let path = ini.value("Folder\(index)", in: section), !path.isEmpty else { return nil }
            return Folder(
                path: path,
                useCount: max(ini.value("FolHit\(index)", in: section).flatMap { Int($0) } ?? 1, 1),
                lastUsed: ini.value("LastDt\(index)", in: section).flatMap(date)
            )
        }
    }

    /// transgui writes "dd.mm.yyyy".
    private static func date(_ text: String) -> Date? {
        let parts = text.split(separator: ".").compactMap { Int($0) }
        guard parts.count == 3 else { return nil }
        let components = DateComponents(year: parts[2], month: parts[1], day: parts[0])
        return Calendar(identifier: .gregorian).date(from: components)
    }
}

/// A minimal reader for the INI files Lazarus writes: `[Section]` headers and `key=value` lines.
/// Section and key names compare case-insensitively, like Lazarus's TIniFile.
struct IniFile {
    private var sections: [String: [String: String]] = [:]
    private(set) var sectionNames: [String] = []

    init(text: String) {
        var current = ""
        for rawLine in text.split(whereSeparator: \.isNewline) {
            let line = rawLine.trimmingCharacters(in: .whitespaces)
            if line.isEmpty || line.hasPrefix(";") || line.hasPrefix("#") { continue }
            if line.hasPrefix("["), line.hasSuffix("]") {
                current = String(line.dropFirst().dropLast())
                if sections[current.lowercased()] == nil { sectionNames.append(current) }
                sections[current.lowercased(), default: [:]] = sections[current.lowercased()] ?? [:]
                continue
            }
            guard let separator = line.firstIndex(of: "=") else { continue }
            let key = line[..<separator].trimmingCharacters(in: .whitespaces).lowercased()
            let value = String(line[line.index(after: separator)...])
            sections[current.lowercased(), default: [:]][key] = value
        }
    }

    func value(_ key: String, in section: String) -> String? {
        sections[section.lowercased()]?[key.lowercased()]
    }

    func hasSection(_ section: String) -> Bool {
        sections[section.lowercased()] != nil
    }

    static func bool(_ text: String) -> Bool {
        ["1", "true", "yes"].contains(text.trimmingCharacters(in: .whitespaces).lowercased())
    }
}
