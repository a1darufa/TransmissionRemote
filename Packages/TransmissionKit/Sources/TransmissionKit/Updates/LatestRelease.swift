import Foundation

/// A release version such as "1.2" or "v1.2.3", compared number by number, so 1.10 is newer than 1.9.
public struct ReleaseVersion: Comparable, Hashable, Sendable, CustomStringConvertible {
    public let components: [Int]

    /// Nil for text without a leading number, such as an empty tag.
    public init?(_ text: String) {
        let trimmed = text.trimmingCharacters(in: .whitespaces)
        let withoutPrefix = trimmed.hasPrefix("v") || trimmed.hasPrefix("V") ? trimmed.dropFirst() : Substring(trimmed)
        // "1.2.0-beta" compares as 1.2.0: anything after the numbers is ignored.
        let numbers = withoutPrefix.split(separator: ".").map { $0.prefix(while: \.isNumber) }
        let components = Array(numbers.prefix(while: { !$0.isEmpty }).compactMap { Int($0) })
        guard !components.isEmpty else { return nil }
        self.components = components
    }

    public var description: String { components.map(String.init).joined(separator: ".") }

    public static func < (lhs: Self, rhs: Self) -> Bool {
        let length = max(lhs.components.count, rhs.components.count)
        func padded(_ version: Self) -> [Int] {
            version.components + Array(repeating: 0, count: length - version.components.count)
        }
        return padded(lhs).lexicographicallyPrecedes(padded(rhs))
    }

    public static func == (lhs: Self, rhs: Self) -> Bool { !(lhs < rhs) && !(rhs < lhs) }

    public func hash(into hasher: inout Hasher) {
        var trimmed = components
        while trimmed.count > 1, trimmed.last == 0 { trimmed.removeLast() }
        hasher.combine(trimmed)
    }
}

/// The newest published release, from GitHub's `releases/latest` endpoint.
public struct LatestRelease: Equatable, Sendable {
    public let version: ReleaseVersion
    public let tagName: String
    /// The release's page on github.com, where the disk image can be downloaded.
    public let pageURL: URL

    /// Decodes GitHub's response, refusing anything that doesn't point to a github.com page,
    /// so the app never opens an unexpected address.
    public init(gitHubResponse data: Data) throws {
        let response = try JSONDecoder().decode(GitHubReleaseResponse.self, from: data)
        guard let version = ReleaseVersion(response.tagName) else {
            throw Self.unexpected("Unreadable tag \(response.tagName)")
        }
        guard let url = URL(string: response.htmlURL), url.scheme == "https", url.host() == "github.com" else {
            throw Self.unexpected("Unexpected release URL")
        }
        self.version = version
        self.tagName = response.tagName
        self.pageURL = url
    }

    private static func unexpected(_ description: String) -> DecodingError {
        .dataCorrupted(.init(codingPath: [], debugDescription: description))
    }
}

private struct GitHubReleaseResponse: Decodable {
    let tagName: String
    let htmlURL: String

    private enum CodingKeys: String, CodingKey {
        case tagName = "tag_name"
        case htmlURL = "html_url"
    }
}
