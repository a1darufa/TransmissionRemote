import Foundation

/// Cleans up a folder path typed for the server before it's sent or remembered.
nonisolated enum ServerFolder {
    /// Trims spaces and trailing slashes, so "/data/ " and "/data" are the same folder in the
    /// history and the daemon never gets a path ending in a separator (transgui issue #1143).
    /// A root ("/", "C:\") keeps its separator.
    static func cleaned(_ path: String) -> String {
        var path = path.trimmingCharacters(in: .whitespacesAndNewlines)
        while path.count > 1, let last = path.last, last == "/" || last == "\\" {
            let withoutLast = path.dropLast()
            // Keep "C:\" whole: without the backslash it means "the current folder on C:".
            if withoutLast.count == 2, withoutLast.last == ":" { break }
            path = String(withoutLast)
        }
        return path
    }
}
