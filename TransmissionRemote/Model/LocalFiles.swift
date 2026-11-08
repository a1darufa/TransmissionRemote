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
        return body(url)
    }
}

extension AppModel {
    /// Where the torrent's data is on this Mac, or nil when no path mapping covers its folder.
    func localFileLocation(for torrent: Torrent) -> LocalFileLocation? {
        // Read the saved profile, not the connection's copy, so new mappings apply without reconnecting.
        guard let serverID = connection.profile?.id, let profile = servers.server(withID: serverID) else {
            return nil
        }
        let remotePath = (torrent.downloadDir as NSString).appendingPathComponent(torrent.name)
        guard let mapping = profile.pathMappings.mapping(forRemote: remotePath),
              let localPath = mapping.localPath(forRemote: remotePath)
        else { return nil }
        return LocalFileLocation(url: URL(filePath: localPath), folderBookmark: mapping.localBookmark)
    }

    /// Opens the torrent's file or folder with its default app.
    func openLocally(_ torrent: Torrent) {
        guard let location = localFileLocation(for: torrent) else { return }
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
        let locations = torrents.compactMap(localFileLocation(for:))
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
}
