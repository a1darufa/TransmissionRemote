import Foundation
import TransmissionKit

/// A folder or file in a torrent's file tree. Folders aggregate their contents.
struct FileNode: Identifiable, Hashable {
    enum WantedState: Hashable {
        case all, none, mixed
    }

    /// The node's path inside the torrent; unique within one tree.
    let id: String
    let name: String
    /// Every file index in this subtree, for applying a change to a whole folder.
    let fileIDs: [Int]
    let size: Int64
    let bytesCompleted: Int64
    let wantedState: WantedState
    /// `nil` when the files inside have different priorities.
    let priority: Priority?
    /// `nil` for files, so `Table` knows not to show a disclosure triangle.
    let children: [Self]?

    var isFolder: Bool { children != nil }
    var progress: Double { size > 0 ? Double(bytesCompleted) / Double(size) : 0 }
}

enum FileTree {
    /// Builds a tree from flat `a/b/c.ext` paths. A single top-level folder (the torrent's
    /// own folder) is skipped, so its contents appear directly.
    static func build(from files: [FileEntry]) -> [FileNode] {
        let items = files.map { (components: $0.path.split(separator: "/").map(String.init), file: $0) }
        let nodes = makeNodes(items, parentPath: "")
        if nodes.count == 1, let onlyChildren = nodes[0].children {
            return onlyChildren
        }
        return nodes
    }

    private typealias Item = (components: [String], file: FileEntry)

    private static func makeNodes(_ items: [Item], parentPath: String) -> [FileNode] {
        let groups = Dictionary(grouping: items) { $0.components.first ?? "" }

        let nodes = groups.flatMap { name, group -> [FileNode] in
            let path = parentPath.isEmpty ? name : "\(parentPath)/\(name)"
            let leaves = group.filter { $0.components.count <= 1 }.map { fileNode($0.file, name: name, path: path) }
            let nested = group.filter { $0.components.count > 1 }.map { (Array($0.components.dropFirst()), $0.file) }

            guard !nested.isEmpty else { return leaves }
            return leaves + [folderNode(name: name, path: path, children: makeNodes(nested, parentPath: path))]
        }
        return nodes.sorted(by: displayOrder)
    }

    private static func fileNode(_ file: FileEntry, name: String, path: String) -> FileNode {
        FileNode(
            id: path,
            name: name,
            fileIDs: [file.id],
            size: file.length,
            bytesCompleted: file.bytesCompleted,
            wantedState: file.wanted ? .all : .none,
            priority: file.priority,
            children: nil
        )
    }

    private static func folderNode(name: String, path: String, children: [FileNode]) -> FileNode {
        let wantedStates = Set(children.map(\.wantedState))
        let priorities = Set(children.map(\.priority))
        return FileNode(
            id: path + "/",
            name: name,
            fileIDs: children.flatMap(\.fileIDs),
            size: children.reduce(0) { $0 + $1.size },
            bytesCompleted: children.reduce(0) { $0 + $1.bytesCompleted },
            wantedState: wantedStates.onlyElement ?? .mixed,
            // A child folder with mixed priorities reports nil, which makes this folder mixed too.
            priority: priorities.onlyElement.flatMap(\.self),
            children: children
        )
    }

    /// Folders first, then names in Finder order.
    private static func displayOrder(_ lhs: FileNode, _ rhs: FileNode) -> Bool {
        if lhs.isFolder != rhs.isFolder { return lhs.isFolder }
        return lhs.name.localizedStandardCompare(rhs.name) == .orderedAscending
    }
}

private extension Set {
    /// The element when the set holds exactly one, otherwise nil.
    var onlyElement: Element? { count == 1 ? first : nil }
}
