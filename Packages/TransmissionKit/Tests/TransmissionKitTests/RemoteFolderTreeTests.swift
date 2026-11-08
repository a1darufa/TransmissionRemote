import Testing
import TransmissionKit

@Suite("Known folders tree")
struct RemoteFolderTreeTests {
    @Test func collapsesTheSharedTopChainIntoOneNode() throws {
        let tree = RemoteFolderTree.build(torrentCounts: [
            "/mnt/HD/HD_a2/Movies": 3,
            "/mnt/HD/HD_a2/TV/Shows": 2,
            "/mnt/HD/HD_a2/TV": 0
        ])

        let root = try #require(tree.first)
        #expect(tree.count == 1)
        #expect(root.path == "/mnt/HD/HD_a2")
        #expect(root.name == "/mnt/HD/HD_a2", "a top-level node shows its full path")

        let children = try #require(root.children)
        #expect(children.map(\.name) == ["Movies", "TV"])
        #expect(children.map(\.torrentCount) == [3, 0])
        #expect(children[0].children == nil, "a folder with no known subfolders has no disclosure triangle")
        #expect(children[1].children?.map(\.path) == ["/mnt/HD/HD_a2/TV/Shows"])
    }

    @Test func stopsCollapsingAtAFolderThatHoldsTorrents() throws {
        let tree = RemoteFolderTree.build(torrentCounts: ["/data": 1, "/data/done/music": 4])

        let root = try #require(tree.first)
        #expect(root.path == "/data")
        #expect(root.torrentCount == 1)
        #expect(root.children?.first?.name == "done")
    }

    @Test func keepsSeparateRootsApart() {
        let tree = RemoteFolderTree.build(torrentCounts: ["/srv/a": 1, "/home/me/b": 1])
        #expect(tree.map(\.path) == ["/home/me/b", "/srv/a"])
    }

    @Test func mergesTrailingSlashesAndIgnoresEmptyPaths() throws {
        let tree = RemoteFolderTree.build(torrentCounts: ["/downloads/": 2, "/downloads": 1, "": 5, "/": 1])

        let root = try #require(tree.first)
        #expect(tree.count == 1)
        #expect(root.path == "/downloads")
        #expect(root.torrentCount == 3)
    }

    @Test func handlesWindowsPaths() throws {
        let tree = RemoteFolderTree.build(torrentCounts: [#"C:\Torrents\Done"#: 1, #"C:\Torrents\Temp"#: 1])

        let root = try #require(tree.first)
        #expect(root.path == #"C:\Torrents"#)
        #expect(root.children?.map(\.path) == [#"C:\Torrents\Done"#, #"C:\Torrents\Temp"#])
    }

    @Test func givesADriveItsRootPath() {
        let tree = RemoteFolderTree.build(torrentCounts: [#"D:\Movies"#: 1, #"D:\Music"#: 2, #"D:\"#: 1])
        #expect(tree.map(\.path) == [#"D:\"#])
    }
}
