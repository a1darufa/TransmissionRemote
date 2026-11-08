import Foundation

/// The two wire protocols Transmission has shipped.
///
/// Transmission 4.1 (RPC semver 6.0.0) introduced JSON-RPC 2.0 with snake_case keys.
/// Everything older — including Transmission 3.00 still found on many NAS boxes — only
/// speaks the original "legacy" protocol, which mixes camelCase and kebab-case keys.
/// Newer servers still accept the legacy protocol, but it is deprecated, so we prefer
/// JSON-RPC whenever the server supports it.
public enum RPCDialect: String, Sendable, Codable, CaseIterable {
    case jsonRPC
    case legacy
}

/// Which family of keys a value belongs to. The legacy protocol spells keys differently
/// depending on the request, so the scope decides how a Swift property name maps to the wire.
public enum KeyScope: Sendable {
    /// Field names for `torrent-get` (camelCase in legacy, with a few kebab-case exceptions).
    case torrentFields
    /// Arguments for `torrent-set`, `torrent-add`, `torrent-remove` and friends.
    case torrentArguments
    /// Field names and arguments for `session-get` / `session-set` (kebab-case in legacy).
    case session
    /// Keys of a bandwidth group for `group-set` (kebab-case in legacy, except `honorsSessionLimits`).
    case bandwidthGroup
    /// Single-word keys that are spelled the same everywhere (`ids`, `path`, `name`).
    case plain
}

extension RPCDialect {
    /// The method name as this dialect expects it, e.g. `torrent_get` vs `torrent-get`.
    func wireName(for method: RPCMethod) -> String {
        switch self {
        case .jsonRPC: method.rawValue
        case .legacy: method.rawValue.replacingOccurrences(of: "_", with: "-")
        }
    }

    /// Converts a camelCase Swift name (our canonical spelling) to the wire spelling.
    public func wireName(for camelName: String, scope: KeyScope) -> String {
        switch self {
        case .jsonRPC: KeyCase.snakeCase(camelName)
        case .legacy: LegacyKeys.wireName(for: camelName, scope: scope)
        }
    }
}

/// Spelling rules for the legacy protocol, taken from the Transmission 3.00 / 4.0.6 RPC specs.
enum LegacyKeys {
    /// `torrent-get` fields that the legacy protocol spells in kebab-case.
    static let kebabTorrentFields: Set<String> = ["fileCount", "peerLimit", "primaryMimeType"]

    /// Torrent mutation arguments that the legacy protocol spells in kebab-case.
    static let kebabTorrentArguments: Set<String> = [
        "deleteLocalData", "downloadDir", "filesUnwanted", "filesWanted",
        "peerLimit", "priorityHigh", "priorityLow", "priorityNormal"
    ]

    /// Session keys that the legacy protocol spells in camelCase (everything else is kebab-case).
    static let camelSessionKeys: Set<String> = ["seedRatioLimit", "seedRatioLimited"]

    static func wireName(for camelName: String, scope: KeyScope) -> String {
        switch scope {
        case .torrentFields:
            kebabTorrentFields.contains(camelName) ? KeyCase.kebabCase(camelName) : camelName
        case .torrentArguments:
            kebabTorrentArguments.contains(camelName) ? KeyCase.kebabCase(camelName) : camelName
        case .session:
            camelSessionKeys.contains(camelName) ? camelName : KeyCase.kebabCase(camelName)
        case .bandwidthGroup:
            camelName == "honorsSessionLimits" ? camelName : KeyCase.kebabCase(camelName)
        case .plain:
            camelName
        }
    }
}

/// Small, dependency-free key case conversions.
enum KeyCase {
    static func snakeCase(_ camelName: String) -> String {
        separated(camelName, by: "_")
    }

    static func kebabCase(_ camelName: String) -> String {
        separated(camelName, by: "-")
    }

    /// Normalizes any server key (snake_case, kebab-case or camelCase) to camelCase so a single
    /// set of `Decodable` models works for both dialects.
    static func camelCase(_ wireKey: String) -> String {
        if let exception = camelCaseExceptions[wireKey] { return exception }
        guard wireKey.contains(where: { $0 == "_" || $0 == "-" }) else { return wireKey }

        let parts = wireKey.split(whereSeparator: { $0 == "_" || $0 == "-" })
        guard let first = parts.first else { return wireKey }
        let tail = parts.dropFirst().map { $0.prefix(1).uppercased() + $0.dropFirst() }
        return String(first) + tail.joined()
    }

    /// Legacy keys whose acronyms don't survive a mechanical conversion.
    /// (`isUTP` in legacy vs `is_utp` in JSON-RPC must land on the same Swift property.)
    private static let camelCaseExceptions: [String: String] = ["isUTP": "isUtp"]

    private static func separated(_ camelName: String, by separator: Character) -> String {
        var result = ""
        result.reserveCapacity(camelName.count + 4)
        for character in camelName {
            if character.isUppercase {
                if !result.isEmpty { result.append(separator) }
                result.append(contentsOf: character.lowercased())
            } else {
                result.append(character)
            }
        }
        return result
    }
}
