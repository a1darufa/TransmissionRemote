import CryptoKit
import Foundation
import Synchronization

/// Enforces the client's transport rules for one `URLSession`:
///
/// * Server certificates must be trusted by the system **or** match a fingerprint the user pinned
///   (the safe alternative to "ignore certificate errors" for self-signed reverse proxies).
/// * Redirects are refused. Every request carries a Basic auth header, and following a redirect
///   could hand that header to a different host.
/// * An HTTP proxy's login request for an HTTPS tunnel is answered once with the proxy credentials.
///   (For plain-HTTP servers the login is sent up front; see `ProxyConfiguration`.)
/// * A server asking for a client certificate gets the one the user chose, if any.
final class ConnectionSecurityDelegate: NSObject, URLSessionDelegate, URLSessionTaskDelegate, Sendable {
    private let pinnedFingerprint: String?
    private let proxyCredentials: Credentials?
    private let clientIdentity: ClientIdentity?
    private let lastRejectedFingerprint = Mutex<String?>(nil)
    private static let log = DiagnosticLog(category: "TLS", subsystem: "TransmissionKit")

    init(pinnedFingerprint: String?, proxyCredentials: Credentials? = nil, clientIdentity: ClientIdentity? = nil) {
        self.pinnedFingerprint = pinnedFingerprint.map(CertificateFingerprint.normalize)
        self.proxyCredentials = proxyCredentials
        self.clientIdentity = clientIdentity
    }

    /// Returns (and clears) the fingerprint of the most recently rejected certificate, so the
    /// client can offer the user to trust it.
    func takeRejectedFingerprint() -> String? {
        lastRejectedFingerprint.withLock { fingerprint in
            defer { fingerprint = nil }
            return fingerprint
        }
    }

    func urlSession(
        _ session: URLSession,
        didReceive challenge: URLAuthenticationChallenge
    ) async -> (URLSession.AuthChallengeDisposition, URLCredential?) {
        if challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodClientCertificate {
            return clientCertificateResponse()
        }
        guard challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodServerTrust,
              let trust = challenge.protectionSpace.serverTrust
        else {
            return (.performDefaultHandling, nil)
        }

        if SecTrustEvaluateWithError(trust, nil) {
            return (.useCredential, URLCredential(trust: trust))
        }

        guard let fingerprint = CertificateFingerprint.leafSHA256(of: trust) else {
            return (.cancelAuthenticationChallenge, nil)
        }
        if fingerprint == pinnedFingerprint {
            return (.useCredential, URLCredential(trust: trust))
        }
        Self.log.warning("Rejected the server's untrusted TLS certificate (SHA-256 \(fingerprint))")

        lastRejectedFingerprint.withLock { $0 = fingerprint }
        return (.cancelAuthenticationChallenge, nil)
    }

    /// Task-level challenges: a proxy asking for its login, or server trust if it arrives here.
    func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        didReceive challenge: URLAuthenticationChallenge
    ) async -> (URLSession.AuthChallengeDisposition, URLCredential?) {
        let method = challenge.protectionSpace.authenticationMethod
        if method == NSURLAuthenticationMethodServerTrust || method == NSURLAuthenticationMethodClientCertificate {
            return await urlSession(session, didReceive: challenge)
        }
        guard challenge.protectionSpace.isProxy() else {
            return (.performDefaultHandling, nil)
        }
        // A second request means the proxy rejected the login; asking again would just loop.
        guard let proxyCredentials, challenge.previousFailureCount == 0 else {
            return (.cancelAuthenticationChallenge, nil)
        }
        let credential = URLCredential(
            user: proxyCredentials.username,
            password: proxyCredentials.password,
            persistence: .forSession
        )
        return (.useCredential, credential)
    }

    /// Without a certificate, carry on without one: the server decides whether that's enough.
    private func clientCertificateResponse() -> (URLSession.AuthChallengeDisposition, URLCredential?) {
        guard let clientIdentity else { return (.performDefaultHandling, nil) }
        return (.useCredential, clientIdentity.credential)
    }

    func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        willPerformHTTPRedirection response: HTTPURLResponse,
        newRequest request: URLRequest
    ) async -> URLRequest? {
        // Host and path only: a query string could carry a token.
        let target = request.url.map { "\($0.host() ?? "")\($0.path())" } ?? "an unknown address"
        Self.log.warning("Refused the server's redirect to \(target)")
        return nil
    }
}

public enum CertificateFingerprint {
    /// SHA-256 of the leaf certificate's DER bytes, formatted like `AB:CD:…`.
    static func leafSHA256(of trust: SecTrust) -> String? {
        guard let chain = SecTrustCopyCertificateChain(trust) as? [SecCertificate],
              let leaf = chain.first
        else { return nil }

        let derBytes = SecCertificateCopyData(leaf) as Data
        return SHA256.hash(data: derBytes)
            .map { String(format: "%02X", $0) }
            .joined(separator: ":")
    }

    /// Accepts fingerprints with or without colons, in any letter case.
    public static func normalize(_ fingerprint: String) -> String {
        let hex = fingerprint.uppercased().filter(\.isHexDigit)
        var pairs: [String] = []
        var index = hex.startIndex
        while index < hex.endIndex {
            let next = hex.index(index, offsetBy: 2, limitedBy: hex.endIndex) ?? hex.endIndex
            pairs.append(String(hex[index..<next]))
            index = next
        }
        return pairs.joined(separator: ":")
    }
}
