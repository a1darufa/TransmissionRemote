import Foundation

/// A folder on the server, as a node in a browsable tree.
///
/// Transmission's RPC can't list directories, so the tree is built only from folders the
/// daemon already reported (where torrents are saved, the default download folder) and the
/// user's folder history. Parent folders of those are included so the tree can be walked.
public struct RemoteFolder: Identifiable, Hashable, Sendable {
    /// The full path on the server.
    public let path: String
    /// What to show: the last path component, or the full path for a top-level node.
    public let name: String
    /// Torrents saved directly in this folder, not in its subfolders.
    public let torrentCount: Int
    /// `nil` for folders without known subfolders, so outline views show no disclosure triangle.
    public let children: [Self]?

    public var id: String { path }
}

public enum RemoteFolderTree {
    /// Builds the tree from known folder paths and how many torrents each holds.
    ///
    /// A chain of single folders at the top (`/mnt` → `HD` → `HD_a2`) is shown as one node,
    /// so the first thing the user sees is where their downloads actually are.
    public static func build(torrentCounts: [String: Int]) -> [RemoteFolder] {
        let entries = torrentCounts.compactMap { path, count in
            RemotePath(path).map { Entry(components: $0.components, count: count, root: $0) }
        }
        return makeNodes(entries, depth: 0).map(collapsingSingleChildChain)
    }

    private struct Entry {
        let components: [String]
        let count: Int
        let root: RemotePath
    }

    private static func makeNodes(_ entries: [Entry], depth: Int) -> [RemoteFolder] {
        let groups = Dictionary(grouping: entries.filter { $0.components.count > depth }) {
            $0.components[depth]
        }
        return groups.values
            .map { group in
                let sample = group[0]
                let endsHere = group.filter { $0.components.count == depth + 1 }
                let children = makeNodes(group, depth: depth + 1)
                return RemoteFolder(
                    path: sample.root.joined(sample.components.prefix(depth + 1)),
                    name: sample.components[depth],
                    torrentCount: endsHere.reduce(0) { $0 + $1.count },
                    children: children.isEmpty ? nil : children
                )
            }
            .sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
    }

    /// Merges a top-level folder into its only child while the folder itself holds nothing,
    /// and names the result by its full path.
    private static func collapsingSingleChildChain(_ node: RemoteFolder) -> RemoteFolder {
        var node = node
        while node.torrentCount == 0, let children = node.children, children.count == 1 {
            node = children[0]
        }
        return RemoteFolder(path: node.path, name: node.path, torrentCount: node.torrentCount, children: node.children)
    }
}

/// A server path split into folders. Daemons on Linux and macOS use `/`; on Windows the
/// path may use `\` and start with a drive letter, so the separator is taken from the path.
private struct RemotePath {
    let components: [String]
    private let separator: Character
    private let isAbsolute: Bool

    init?(_ path: String) {
        let path = path.trimmingCharacters(in: .whitespacesAndNewlines)
        separator = !path.contains("/") && path.contains("\\") ? "\\" : "/"
        isAbsolute = path.first == separator
        components = path.split(separator: separator).map(String.init)
        guard !components.isEmpty else { return nil }
    }

    func joined(_ prefix: ArraySlice<String>) -> String {
        let body = prefix.joined(separator: String(separator))
        if isAbsolute { return String(separator) + body }
        // A bare drive ("C:") means the current folder on that drive, not its root.
        return prefix.count == 1 && body.hasSuffix(":") ? body + String(separator) : body
    }
}
