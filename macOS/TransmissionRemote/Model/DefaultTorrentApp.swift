import AppKit
import TransmissionKit
import UniformTypeIdentifiers

/// Whether this app opens .torrent files and magnet links, and making it do so, like Transmission
/// Remote GUI's option to handle them. Finder shows .torrent files with the default app's
/// document icon, so this also gives them this app's icon.
@Observable
final class DefaultTorrentApp {
    private(set) var isDefault = false
    private static let log = DiagnosticLog(category: "DefaultApp")
    /// Any magnet link: macOS picks the handler by the scheme alone.
    private static let sampleMagnetLink = URL(string: "magnet:?xt=urn:btih:0")

    private var appURL: URL { Bundle.main.bundleURL.resolvingSymlinksInPath() }

    /// Checks again, for example when Settings opens: the user can change it in Finder too.
    func refresh() {
        let workspace = NSWorkspace.shared
        let torrentApp = workspace.urlForApplication(toOpen: .torrent)
        let magnetApp = Self.sampleMagnetLink.flatMap(workspace.urlForApplication(toOpen:))
        // Compared by location, so another copy of this app (an old build, say) doesn't count.
        isDefault = [torrentApp, magnetApp].allSatisfy { $0?.resolvingSymlinksInPath() == appURL }
    }

    /// macOS may ask the user to confirm. Declining, or a failure, leaves things as they were.
    func makeDefault() async {
        do {
            try await NSWorkspace.shared.setDefaultApplication(at: appURL, toOpen: .torrent)
            try await NSWorkspace.shared.setDefaultApplication(at: appURL, toOpenURLsWithScheme: "magnet")
            Self.log.info("Made the app the default for .torrent files and magnet links")
        } catch {
            Self.log.warning("Couldn't become the default for .torrent files and magnet links: \(error)")
        }
        refresh()
    }
}
