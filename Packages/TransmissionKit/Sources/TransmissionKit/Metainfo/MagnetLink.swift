import Foundation

/// A validated `magnet:` link.
public struct MagnetLink: Sendable, Equatable {
    public let url: URL
    /// Lowercased info hash from `xt=urn:btih:` (hex or base32), if present.
    public let infoHash: String?
    public let displayName: String?
    public let trackers: [String]

    /// Returns `nil` unless the text is a magnet link with a BitTorrent exact topic.
    public init?(_ text: String) {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard trimmed.lowercased().hasPrefix("magnet:?"),
              let components = URLComponents(string: trimmed),
              let url = components.url
        else { return nil }

        let items = components.queryItems ?? []
        let topics = items.filter { $0.name == "xt" }.compactMap(\.value)
        // BEP 9 (v1 "btih") and BEP 52 (v2 "btmh") topics.
        let hasInfoHash = topics.contains { topic in
            let topic = topic.lowercased()
            return topic.hasPrefix("urn:btih:") || topic.hasPrefix("urn:btmh:")
        }
        guard hasInfoHash else {
            return nil
        }

        self.url = url
        self.infoHash = topics
            .first { $0.lowercased().hasPrefix("urn:btih:") }
            .map { String($0.dropFirst("urn:btih:".count)).lowercased() }
        self.displayName = items.first { $0.name == "dn" }?.value
        self.trackers = items.filter { $0.name == "tr" }.compactMap(\.value)
    }
}

extension String {
    /// True for `http(s)://` URLs, which the daemon can download `.torrent` files from.
    public var isTorrentDownloadURL: Bool {
        guard let url = URL(string: trimmingCharacters(in: .whitespacesAndNewlines)),
              let scheme = url.scheme?.lowercased()
        else { return false }
        return (scheme == "http" || scheme == "https") && url.host() != nil
    }
}
