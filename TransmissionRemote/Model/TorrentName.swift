import Foundation

/// Checks a new name for a torrent's top-level file or folder before sending it to the daemon.
enum TorrentName {
    /// The trimmed name, or nil when Transmission would reject it: empty, "." or "..",
    /// or containing "/" (a rename can't move the item into another folder).
    static func validated(_ text: String) -> String? {
        let name = text.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty, name != ".", name != "..", !name.contains("/") else { return nil }
        return name
    }
}
