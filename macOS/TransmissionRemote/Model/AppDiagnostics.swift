import AppKit
import TransmissionKit

/// Starts the diagnostic log files and opens their folder for the user.
///
/// The files live in the sandbox container's Library/Logs folder, are kept for 14 days and hold
/// connections, failed calls and other errors, so a bug report can include what went wrong.
enum AppDiagnostics {
    private static let log = DiagnosticLog(category: "App")

    static let logDirectory = URL.libraryDirectory.appending(path: "Logs", directoryHint: .isDirectory)

    static func start() {
        do {
            try DiagnosticLog.startFileLogging(in: logDirectory)
        } catch {
            log.error("Couldn't open the log folder \(displayPath(of: logDirectory)): \(error)")
        }
        let info = Bundle.main.infoDictionary
        let version = info?["CFBundleShortVersionString"] as? String ?? "?"
        let build = info?["CFBundleVersion"] as? String ?? "?"
        let system = ProcessInfo.processInfo.operatingSystemVersionString
        log.info("TransmissionRemote \(version) (\(build)) started on macOS \(system), \(architecture)")
    }

    static func showLogFiles() {
        NSWorkspace.shared.open(DiagnosticLog.logDirectory ?? logDirectory)
    }

    /// A path with the home folder written as "~", so shared logs don't reveal the user's name.
    static func displayPath(of url: URL) -> String {
        (url.path as NSString).abbreviatingWithTildeInPath
    }

    private static var architecture: String {
        #if arch(arm64)
        "Apple silicon"
        #else
        "Intel"
        #endif
    }
}
