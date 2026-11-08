import Foundation

public enum TransmissionError: Error, Equatable, Sendable {
    case invalidEndpoint(reason: String)
    /// The server wants a username/password and the one we sent (if any) was rejected.
    case unauthorized
    /// The server's `rpc-whitelist` doesn't include this computer's IP address.
    case ipNotAllowed(serverMessage: String)
    /// The server's `rpc-host-whitelist` doesn't include the hostname we connected with.
    case hostNotAllowed
    /// The TLS certificate isn't trusted by the system and doesn't match a pinned fingerprint.
    case untrustedCertificate(sha256Fingerprint: String)
    /// The HTTPS handshake failed before any certificate arrived, most often because the
    /// server only speaks plain HTTP, as Transmission's built-in RPC server does.
    case httpsHandshakeFailed
    /// The server tried to redirect us; we refuse so credentials can't leak to another host.
    case redirectRefused
    case httpStatus(Int)
    /// The RPC call reached the server but failed there (bad path, unknown torrent, …).
    case rpcFailure(message: String)
    case unsupportedServer(version: String)
    case invalidResponse(details: String)
    case network(code: Int, description: String)
}

extension TransmissionError: LocalizedError {
    public var errorDescription: String? {
        switch self {
        case .invalidEndpoint(let reason):
            String(localized: "The server address is invalid: \(reason)", bundle: .module)
        case .unauthorized:
            String(localized: "The server rejected the username or password.", bundle: .module)
        case .ipNotAllowed:
            String(localized: """
                The server refused this computer's IP address. \
                Add it to “rpc-whitelist” in the daemon's settings.json, or disable the whitelist.
                """, bundle: .module)
        case .hostNotAllowed:
            String(localized: """
                The server doesn't accept this hostname. Connect by IP address, \
                or add the hostname to “rpc-host-whitelist” in the daemon's settings.json.
                """, bundle: .module)
        case .httpsHandshakeFailed:
            String(localized: """
                The server didn't accept a secure (HTTPS) connection. Transmission's built-in server \
                only speaks plain HTTP, so turn off HTTPS for this server unless a reverse proxy in front \
                of the daemon provides it. Over Tailscale, plain HTTP is already encrypted by the tunnel.
                """, bundle: .module)
        case .untrustedCertificate:
            String(localized: "The server's TLS certificate isn't trusted.", bundle: .module)
        case .redirectRefused:
            String(localized: """
                The server tried to redirect the connection to a different address. \
                The request was blocked to protect your credentials.
                """, bundle: .module)
        case .httpStatus(let code):
            String(
                localized: "The server responded with HTTP \(code). Check the RPC path and port.",
                bundle: .module
            )
        case .rpcFailure(let message):
            message
        case .unsupportedServer(let version):
            String(
                localized: "Transmission \(version) is too old. Version 2.40 or newer is required.",
                bundle: .module
            )
        case .invalidResponse(let details):
            String(
                localized: "The server sent a response that couldn't be read (\(details)).",
                bundle: .module
            )
        case .network(_, let description):
            description
        }
    }

    /// Errors that won't fix themselves by retrying, so polling should stop.
    public var requiresUserAction: Bool {
        switch self {
        case .unauthorized, .ipNotAllowed, .hostNotAllowed, .untrustedCertificate,
             .redirectRefused, .unsupportedServer, .invalidEndpoint:
            true
        // A handshake failure in the middle of a working session is a network hiccup;
        // a failure on the first connection is shown either way.
        case .httpStatus, .rpcFailure, .invalidResponse, .network, .httpsHandshakeFailed:
            false
        }
    }
}
