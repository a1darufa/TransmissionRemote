import AppKit
import TransmissionKit

/// A torrent's data on this Mac, found through the server's path mappings (Settings → Paths).
struct LocalFileLocation {
    let url: URL
    /// The bookmarked mapped folder. The sandbox only lets the app open files inside folders
    /// the user picked, and the bookmark carries that permission across launches.
    let folderBookmark: Data?

    /// Runs `body` with access to the mapped folder.
    func withAccess<Result>(_ body: (URL) -> Result) -> Result {
        var isStale = false
        let folder = folderBookmark.flatMap {
            try? URL(resolvingBookmarkData: $0, options: .withSecurityScope, bookmarkDataIsStale: &isStale)
        }
        let isAccessing = folder?.startAccessingSecurityScopedResource() ?? false
        defer { if isAccessing { folder?.stopAccessingSecurityScopedResource() } }
        return body(existingURL)
    }

    /// The file as it is on disk. Transmission can add ".part" to files it hasn't finished,
    /// so fall back to that name when the plain one isn't there.
    private var existingURL: URL {
        let partial = url.appendingPathExtension("part")
        let fileManager = FileManager.default
        if !fileManager.fileExists(atPath: url.path(percentEncoded: false)),
           fileManager.fileExists(atPath: partial.path(percentEncoded: false)) {
            return partial
        }
        return url
    }
}

extension Torrent {
    /// The server-side path of the torrent's top-level file or folder.
    var remotePath: String { remotePath(of: name) }

    /// The server-side path of a file or folder inside the torrent, given relative to its download folder.
    func remotePath(of relativePath: String) -> String {
        (downloadDir as NSString).appendingPathComponent(relativePath)
    }
}

extension AppModel {
    /// Where a server path is on this Mac, or nil when no path mapping covers it.
    func localFileLocation(forRemotePath remotePath: String) -> LocalFileLocation? {
        // Read the saved profile, not the connection's copy, so new mappings apply without reconnecting.
        guard let serverID = connection.profile?.id, let profile = servers.server(withID: serverID),
              let mapping = profile.pathMappings.mapping(forRemote: remotePath),
              let localPath = mapping.localPath(forRemote: remotePath)
        else { return nil }
        return LocalFileLocation(url: URL(filePath: localPath), folderBookmark: mapping.localBookmark)
    }

    /// Where the torrent's data is on this Mac, or nil when no path mapping covers its folder.
    func localFileLocation(for torrent: Torrent) -> LocalFileLocation? {
        localFileLocation(forRemotePath: torrent.remotePath)
    }

    /// Opens the torrent's file or folder with its default app.
    func openLocally(_ torrent: Torrent) {
        openLocally(remotePath: torrent.remotePath)
    }

    /// Opens a server file or folder with its default app on this Mac.
    func openLocally(remotePath: String) {
        guard let location = localFileLocation(forRemotePath: remotePath) else { return }
        let opened = location.withAccess { NSWorkspace.shared.open($0) }
        if !opened {
            connection.actionError = String(localized: """
                Couldn't open \(location.url.path(percentEncoded: false)). \
                Check that the share is mounted, and choose its folder again in Settings → Paths.
                """)
        }
    }

    /// Selects the torrents' files or folders in Finder.
    func revealLocally(_ torrents: [Torrent]) {
        revealLocally(remotePaths: torrents.map(\.remotePath))
    }

    /// Selects server files or folders in Finder, skipping those no path mapping covers.
    func revealLocally(remotePaths: [String]) {
        let locations = remotePaths.compactMap(localFileLocation(forRemotePath:))
        guard !locations.isEmpty else { return }
        // Keep every mapped folder accessible while Finder is asked to select the items.
        func reveal(_ remaining: ArraySlice<LocalFileLocation>, urls: [URL]) {
            guard let next = remaining.first else {
                NSWorkspace.shared.activateFileViewerSelecting(urls)
                return
            }
            next.withAccess { reveal(remaining.dropFirst(), urls: urls + [$0]) }
        }
        reveal(locations[...], urls: [])
    }

    /// Copies paths to the clipboard, one per line: the path on this Mac when a path mapping
    /// covers it, otherwise the path on the server.
    func copyPaths(_ remotePaths: [String]) {
        let paths = remotePaths.map { remotePath in
            localFileLocation(forRemotePath: remotePath)?.url.path(percentEncoded: false) ?? remotePath
        }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(paths.joined(separator: "\n"), forType: .string)
    }
}
