import Testing
@testable import TransmissionKit

@Suite("Remote to local path mappings")
struct PathMappingTests {
    private let share = PathMapping(remotePath: "/mnt/HD/HD_a2", localPath: "/Volumes/HD_a2")

    @Test func mappedFolderItselfBecomesTheLocalFolder() {
        #expect(share.localPath(forRemote: "/mnt/HD/HD_a2") == "/Volumes/HD_a2")
    }

    @Test func pathsInsideKeepTheirSubfolders() {
        #expect(share.localPath(forRemote: "/mnt/HD/HD_a2/Movies/Film.mkv") == "/Volumes/HD_a2/Movies/Film.mkv")
    }

    @Test func onlyWholeFolderNamesMatch() {
        #expect(share.localPath(forRemote: "/mnt/HD/HD_a20/Movies") == nil)
        #expect(share.localPath(forRemote: "/mnt/HD") == nil)
    }

    @Test func trailingSlashesAndSpacesDontMatter() {
        let typed = PathMapping(remotePath: " /mnt/HD/HD_a2/ ", localPath: "/Volumes/HD_a2/")
        #expect(typed.localPath(forRemote: "/mnt/HD/HD_a2/Movies/") == "/Volumes/HD_a2/Movies")
    }

    @Test func rootMappingCoversEverything() {
        let root = PathMapping(remotePath: "/", localPath: "/Volumes/NAS")
        #expect(root.localPath(forRemote: "/downloads/file.iso") == "/Volumes/NAS/downloads/file.iso")
    }

    @Test func unfinishedMappingsAreIgnored() {
        #expect(PathMapping(remotePath: "/mnt", localPath: "").localPath(forRemote: "/mnt/a") == nil)
        #expect(PathMapping(remotePath: "", localPath: "/Volumes/x").localPath(forRemote: "/mnt/a") == nil)
    }

    @Test func deepestMappingWins() {
        let movies = PathMapping(remotePath: "/mnt/HD/HD_a2/Movies", localPath: "/Volumes/Movies")
        let mappings = [share, movies]
        #expect(mappings.localPath(forRemote: "/mnt/HD/HD_a2/Movies/Film.mkv") == "/Volumes/Movies/Film.mkv")
        #expect(mappings.localPath(forRemote: "/mnt/HD/HD_a2/Music/Song.flac") == "/Volumes/HD_a2/Music/Song.flac")
        #expect(mappings.localPath(forRemote: "/srv/other") == nil)
    }

    @Test func windowsDaemonPathsMapInsideTheirFolder() {
        let drive = PathMapping(remotePath: #"D:\Torrents"#, localPath: "/Volumes/Torrents")
        #expect(drive.localPath(forRemote: #"D:\Torrents\TV\Show"#) == "/Volumes/Torrents/TV/Show")
        // Joining a torrent's name onto its folder adds a forward slash.
        #expect(drive.localPath(forRemote: #"D:\Torrents/Film.mkv"#) == "/Volumes/Torrents/Film.mkv")
        // Windows ignores case.
        #expect(drive.localPath(forRemote: #"d:\torrents\tv"#) == "/Volumes/Torrents/tv")
        #expect(drive.localPath(forRemote: #"D:\TorrentsOld\x"#) == nil)
    }

    @Test func aWholeWindowsDriveCanBeMapped() {
        let drive = PathMapping(remotePath: #"E:\"#, localPath: "/Volumes/E")
        #expect(drive.localPath(forRemote: #"E:\Downloads\a.iso"#) == "/Volumes/E/Downloads/a.iso")
    }
}
