import CFNetwork
import Foundation

/// A proxy between this Mac and the daemon, like Transmission Remote GUI's Proxy tab.
public struct ProxyConfiguration: Sendable, Equatable {
    public enum Kind: String, Sendable, Codable, CaseIterable {
        case http
        case socks5
    }

    public var kind: Kind
    public var host: String
    public var port: Int
    public var credentials: Credentials?

    public init(kind: Kind, host: String, port: Int, credentials: Credentials? = nil) {
        self.kind = kind
        self.host = host
        self.port = port
        self.credentials = credentials
    }

    /// Settings for `URLSessionConfiguration.connectionProxyDictionary`.
    ///
    /// An HTTP proxy is used for both schemes; HTTPS goes through it in a CONNECT tunnel.
    /// A SOCKS 5 login has no challenge, so it travels in the dictionary.
    var connectionProxyDictionary: [AnyHashable: Any] {
        switch kind {
        case .http:
            return [
                kCFNetworkProxiesHTTPEnable as String: true,
                kCFNetworkProxiesHTTPProxy as String: host,
                kCFNetworkProxiesHTTPPort as String: port,
                kCFNetworkProxiesHTTPSEnable as String: true,
                kCFNetworkProxiesHTTPSProxy as String: host,
                kCFNetworkProxiesHTTPSPort as String: port
            ]
        case .socks5:
            var dictionary: [AnyHashable: Any] = [
                kCFNetworkProxiesSOCKSEnable as String: true,
                kCFNetworkProxiesSOCKSProxy as String: host,
                kCFNetworkProxiesSOCKSPort as String: port,
                kCFStreamPropertySOCKSVersion as String: kCFStreamSocketSOCKSVersion5 as String
            ]
            if let credentials {
                dictionary[kCFStreamPropertySOCKSUser as String] = credentials.username
                dictionary[kCFStreamPropertySOCKSPassword as String] = credentials.password
            }
            return dictionary
        }
    }

    /// The login the session delegate offers when an HTTP proxy asks for one while opening
    /// an HTTPS tunnel.
    var httpProxyCredentials: Credentials? {
        kind == .http ? credentials : nil
    }

    /// The HTTP proxy login, sent with every request to a plain-HTTP server.
    ///
    /// URLSession doesn't turn a proxy's 407 into a challenge for plain-HTTP requests, so the
    /// login has to go up front. Proxies drop this header before forwarding. It's never sent to an
    /// HTTPS server: inside the tunnel it would reach the server instead of the proxy.
    func authorizationHeader(forServerUsingTLS usesTLS: Bool) -> String? {
        guard kind == .http, !usesTLS, let credentials else { return nil }
        return credentials.basicAuthorizationHeader
    }
}
