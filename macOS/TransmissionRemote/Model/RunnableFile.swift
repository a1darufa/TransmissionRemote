import AppKit

/// Asks before opening apps, scripts and installers from a torrent.
///
/// macOS checks files downloaded from the internet before they first run, because the browser
/// marks them. Files the Transmission daemon wrote to a network share carry no such mark, so
/// double-clicking an app or script inside a torrent would run it with no warning at all
/// (transgui pull request #1583).
enum RunnableFile {
    /// Kinds of files that run something, or install something, as soon as they're opened.
    private static let runnableExtensions: Set<String> = [
        "app", "command", "tool", "sh", "bash", "zsh", "csh", "ksh", "terminal", "jar",
        "pkg", "mpkg", "action", "workflow", "scpt", "applescript", "osax", "prefpane",
        "saver", "qlgenerator", "kext", "plugin", "appex", "webloc", "inetloc"
    ]

    static func isRunnable(_ url: URL) -> Bool {
        runnableExtensions.contains(url.pathExtension.lowercased())
    }

    /// True when the file may be opened: it's ordinary, or the user chose Open Anyway.
    /// Choosing Show in Finder selects it there instead.
    static func confirmOpening(_ url: URL) -> Bool {
        guard isRunnable(url) else { return true }
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = String(localized: "“\(url.lastPathComponent)” is an app or script")
        alert.informativeText = String(localized: """
            It came from a torrent, and macOS hasn't checked it the way it checks downloads. \
            Open it only if you trust where it came from.
            """)
        alert.addButton(withTitle: String(localized: "Show in Finder"))
        alert.addButton(withTitle: String(localized: "Open Anyway"))
        alert.addButton(withTitle: String(localized: "Cancel"))
        switch alert.runModal() {
        case .alertFirstButtonReturn:
            NSWorkspace.shared.activateFileViewerSelecting([url])
            return false
        case .alertSecondButtonReturn:
            return true
        default:
            return false
        }
    }
}
