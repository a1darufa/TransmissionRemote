import Foundation
import Network

/// Where a Transmission daemon's RPC interface lives.
public struct ServerEndpoint: Sendable, Hashable, Codable {
    public static let defaultPort = 9091
    public static let defaultRPCPath = "/transmission/rpc"

    public var host: String
    public var port: Int
    public var rpcPath: String
    public var usesTLS: Bool

    public init(
        host: String,
        port: Int = Self.defaultPort,
        rpcPath: String = Self.defaultRPCPath,
        usesTLS: Bool = false
    ) {
        self.host = host
        self.port = port
        self.rpcPath = rpcPath
        self.usesTLS = usesTLS
    }

    /// The host without surrounding whitespace or IPv6 brackets.
    public var normalizedHost: String {
        host.trimmingCharacters(in: .whitespacesAndNewlines)
            .trimmingCharacters(in: CharacterSet(charactersIn: "[]"))
            .lowercased()
    }

    /// Validates the endpoint and builds the RPC URL.
    public func url() throws(TransmissionError) -> URL {
        let host = normalizedHost
        guard !host.isEmpty else { throw .invalidEndpoint(reason: "the host is empty.") }
        guard !host.contains("://") else {
            throw .invalidEndpoint(reason: "enter just the host name or IP address, without “http://”.")
        }
        guard host.allSatisfy(Self.isAllowedHostCharacter) else {
            throw .invalidEndpoint(reason: "the host contains characters that aren't allowed.")
        }
        guard (1...65_535).contains(port) else {
            throw .invalidEndpoint(reason: "the port must be between 1 and 65535.")
        }
        guard rpcPath.hasPrefix("/") else {
            throw .invalidEndpoint(reason: "the RPC path must start with “/”.")
        }

        var components = URLComponents()
        components.scheme = usesTLS ? "https" : "http"
        components.host = IPv6Address(host) == nil ? host : "[\(host)]"
        components.port = port
        components.path = rpcPath
        guard let url = components.url else {
            throw .invalidEndpoint(reason: "the address couldn't be turned into a URL.")
        }
        return url
    }

    /// How well the connection protects credentials and traffic in transit.
    public var transportSecurity: TransportSecurity {
        if usesTLS { return .tls }
        return TransportSecurity.classify(host: normalizedHost)
    }

    private static func isAllowedHostCharacter(_ character: Character) -> Bool {
        character.isASCII && (character.isLetter || character.isNumber || "-._:%".contains(character))
    }
}

/// Transmission's RPC server only speaks plain HTTP, and Basic auth sends the password
/// base64-encoded with every request. Plain HTTP is fine inside a trusted network or an
/// encrypted tunnel such as Tailscale, but not across the open internet.
public enum TransportSecurity: Sendable, Equatable {
    /// HTTPS (for example through a reverse proxy in front of the daemon).
    case tls
    case loopback
    /// Private LAN ranges, link-local addresses and local-only host names.
    case localNetwork
    /// Tailscale's address ranges and MagicDNS names: plain HTTP inside a WireGuard tunnel.
    case tailscale
    /// Plain HTTP to a public address: credentials can be read by anyone on the path.
    case unencryptedInternet

    public var isProtected: Bool { self != .unencryptedInternet }

    static func classify(host: String) -> Self {
        if let address = IPv4Address(host) { return classify(ipv4: [UInt8](address.rawValue)) }
        if let address = IPv6Address(host) { return classify(ipv6: [UInt8](address.rawValue)) }
        return classify(hostName: host)
    }

    private static func classify(ipv4 bytes: [UInt8]) -> Self {
        guard bytes.count == 4 else { return .unencryptedInternet }
        switch (bytes[0], bytes[1]) {
        case (127, _): return .loopback
        case (100, 64...127): return .tailscale // 100.64.0.0/10, Tailscale's CGNAT range
        case (10, _), (172, 16...31), (192, 168), (169, 254): return .localNetwork
        default: return .unencryptedInternet
        }
    }

    private static func classify(ipv6 bytes: [UInt8]) -> Self {
        guard bytes.count == 16 else { return .unencryptedInternet }
        if bytes == [UInt8](repeating: 0, count: 15) + [1] { return .loopback }
        if bytes.starts(with: Self.tailscaleIPv6Prefix) { return .tailscale }
        if bytes[0] & 0xFE == 0xFC { return .localNetwork } // fc00::/7 unique local
        if bytes[0] == 0xFE, bytes[1] & 0xC0 == 0x80 { return .localNetwork } // fe80::/10 link-local
        return .unencryptedInternet
    }

    private static func classify(hostName: String) -> Self {
        if hostName == "localhost" { return .loopback }
        if hostName.hasSuffix(".ts.net") { return .tailscale }
        let localSuffixes = [".local", ".lan", ".home.arpa", ".internal"]
        if !hostName.contains(".") || localSuffixes.contains(where: hostName.hasSuffix) {
            return .localNetwork
        }
        return .unencryptedInternet
    }

    /// fd7a:115c:a1e0::/48
    private static let tailscaleIPv6Prefix: [UInt8] = [0xFD, 0x7A, 0x11, 0x5C, 0xA1, 0xE0]
}
