import Foundation

/// A folder on the server and the same folder as this Mac sees it, for example a NAS share
/// mounted over SMB: `/mnt/HD/HD_a2` on the server is `/Volumes/HD_a2` here.
public struct PathMapping: Codable, Hashable, Sendable, Identifiable {
    public var id: UUID
    public var remotePath: String
    public var localPath: String
    /// A security-scoped bookmark to `localPath`, so a sandboxed app can open files there later.
    public var localBookmark: Data?

    public init(id: UUID = UUID(), remotePath: String = "", localPath: String = "", localBookmark: Data? = nil) {
        self.id = id
        self.remotePath = remotePath
        self.localPath = localPath
        self.localBookmark = localBookmark
    }

    /// `path` on this Mac, or nil when it isn't inside `remotePath`.
    /// Only whole folder names match, so a mapping for `/data` doesn't claim `/database`.
    public func localPath(forRemote path: String) -> String? {
        let prefix = Self.normalized(remotePath)
        let local = Self.normalized(localPath)
        let path = Self.normalized(path)
        guard !prefix.isEmpty, !local.isEmpty else { return nil }

        let remainder: Substring
        if path == prefix {
            remainder = ""
        } else if prefix == "/", path.hasPrefix("/") {
            remainder = path.dropFirst()
        } else if path.hasPrefix(prefix + "/") {
            remainder = path.dropFirst(prefix.count + 1)
        } else {
            return nil
        }

        guard !remainder.isEmpty else { return local }
        return local == "/" ? "/" + remainder : local + "/" + remainder
    }

    /// Trims spaces and trailing slashes, keeping a lone "/" for the root.
    static func normalized(_ path: String) -> String {
        var path = path.trimmingCharacters(in: .whitespacesAndNewlines)
        while path.count > 1, path.hasSuffix("/") {
            path.removeLast()
        }
        return path
    }
}

extension Collection where Element == PathMapping {
    /// The mapping that covers `remotePath`, preferring the deepest server folder.
    public func mapping(forRemote remotePath: String) -> PathMapping? {
        filter { $0.localPath(forRemote: remotePath) != nil }
            .max { PathMapping.normalized($0.remotePath).count < PathMapping.normalized($1.remotePath).count }
    }

    /// `remotePath` on this Mac, or nil when no mapping covers it.
    public func localPath(forRemote remotePath: String) -> String? {
        mapping(forRemote: remotePath)?.localPath(forRemote: remotePath)
    }
}
