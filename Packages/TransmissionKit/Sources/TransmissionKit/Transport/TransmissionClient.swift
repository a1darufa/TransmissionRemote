import Foundation
import os

public struct Credentials: Sendable, Equatable {
    public var username: String
    public var password: String

    public init(username: String, password: String) {
        self.username = username
        self.password = password
    }

    var basicAuthorizationHeader: String {
        "Basic " + Data("\(username):\(password)".utf8).base64EncodedString()
    }
}

/// Lets a user force the legacy protocol, e.g. behind a proxy that mangles JSON-RPC.
public enum ProtocolPreference: String, Sendable, Codable, CaseIterable {
    case automatic
    case legacy
}

/// Talks to one Transmission daemon.
///
/// Call `connect()` first: it detects the server version and picks the best protocol.
/// All requests then go through `call(_:params:scope:returning:)`, which handles the
/// CSRF token dance (HTTP 409) transparently.
public actor TransmissionClient {
    nonisolated public let endpoint: ServerEndpoint

    public private(set) var dialect: RPCDialect = .legacy
    public private(set) var serverInfo: ServerInfo?

    private let url: URL
    private let authorizationHeader: String?
    private let protocolPreference: ProtocolPreference
    private let session: URLSession
    private let securityDelegate: ConnectionSecurityDelegate
    private var csrfToken: String?
    private var nextTag = 1

    private static let logger = Logger(subsystem: "TransmissionKit", category: "RPC")

    public init(
        endpoint: ServerEndpoint,
        credentials: Credentials?,
        pinnedCertificateFingerprint: String? = nil,
        protocolPreference: ProtocolPreference = .automatic,
        timeout: Duration = .seconds(30),
        proxy: ProxyConfiguration? = nil,
        clientIdentity: ClientIdentity? = nil,
        baseConfiguration: URLSessionConfiguration = .ephemeral
    ) throws(TransmissionError) {
        self.endpoint = endpoint
        self.url = try endpoint.url()
        self.authorizationHeader = credentials.map(\.basicAuthorizationHeader)
        self.protocolPreference = protocolPreference
        self.securityDelegate = ConnectionSecurityDelegate(
            pinnedFingerprint: pinnedCertificateFingerprint,
            proxyCredentials: proxy?.httpProxyCredentials,
            clientIdentity: clientIdentity
        )
        self.session = URLSession(
            configuration: Self.hardenedConfiguration(
                from: baseConfiguration,
                timeout: timeout,
                proxy: proxy,
                serverUsesTLS: endpoint.usesTLS
            ),
            delegate: securityDelegate,
            delegateQueue: nil
        )
    }

    deinit {
        session.invalidateAndCancel()
    }

    /// An ephemeral session that never writes cookies, caches or credentials to disk.
    static func hardenedConfiguration(
        from base: URLSessionConfiguration,
        timeout: Duration,
        proxy: ProxyConfiguration? = nil,
        serverUsesTLS: Bool = false
    ) -> URLSessionConfiguration {
        let configuration = base.copy() as? URLSessionConfiguration ?? .ephemeral
        configuration.timeoutIntervalForRequest = TimeInterval(timeout.components.seconds)
        // Long enough for calls the daemon only answers when it's done, such as moving data;
        // ordinary requests are still cut off by the per-request timeout above.
        configuration.timeoutIntervalForResource = TimeInterval(Self.slowCallTimeout.components.seconds)
        configuration.tlsMinimumSupportedProtocolVersion = .TLSv12
        configuration.httpCookieStorage = nil
        configuration.httpShouldSetCookies = false
        configuration.urlCache = nil
        configuration.urlCredentialStorage = nil
        configuration.requestCachePolicy = .reloadIgnoringLocalCacheData
        if let proxy {
            configuration.connectionProxyDictionary = proxy.connectionProxyDictionary
            if let login = proxy.authorizationHeader(forServerUsingTLS: serverUsesTLS) {
                var headers = configuration.httpAdditionalHeaders ?? [:]
                headers["Proxy-Authorization"] = login
                configuration.httpAdditionalHeaders = headers
            }
        }
        return configuration
    }

    /// How long to wait for calls the daemon answers only after finishing the work: moving data
    /// and deleting it. Large torrents on a NAS can take many minutes.
    static let slowCallTimeout: Duration = .seconds(30 * 60)

    // MARK: - Connecting

    /// Detects the server version and switches to JSON-RPC when the server supports it.
    @discardableResult
    public func connect() async throws(TransmissionError) -> ServerInfo {
        // Every Transmission release understands a legacy `session-get`, so we always start there.
        dialect = .legacy
        let fields = ServerInfo.fieldNames.map { dialect.wireName(for: $0, scope: .session) }
        let info = try await call(.sessionGet, params: FieldsRequest(fields: fields), returning: ServerInfo.self)

        guard info.rpcVersion >= ServerInfo.minimumRPCVersion else {
            throw .unsupportedServer(version: info.version)
        }
        if protocolPreference == .automatic, info.supportsJSONRPC {
            dialect = .jsonRPC
        }
        serverInfo = info
        let version = info.version
        let protocolName = dialect.rawValue
        Self.logger.info(
            "Connected to Transmission \(version, privacy: .public) using \(protocolName, privacy: .public)"
        )
        return info
    }

    var rpcVersion: Int { serverInfo?.rpcVersion ?? ServerInfo.minimumRPCVersion }

    /// Converts our camelCase field names to the server's spelling.
    func wireNames(_ camelNames: [String], scope: KeyScope) -> [String] {
        camelNames.map { dialect.wireName(for: $0, scope: scope) }
    }

    // MARK: - Calling

    /// Sends a request and decodes its result.
    func call<Params: Encodable & Sendable, Result: Decodable & Sendable>(
        _ method: RPCMethod,
        params: Params,
        scope: KeyScope = .plain,
        returning resultType: Result.Type
    ) async throws(TransmissionError) -> Result {
        guard let result = try await send(method, params: params, scope: scope, timeout: nil, resultType: resultType)
        else {
            throw .invalidResponse(details: "the response to \(method.rawValue) had no result")
        }
        return result
    }

    /// Sends a request whose result we don't need.
    func call<Params: Encodable & Sendable>(
        _ method: RPCMethod,
        params: Params,
        scope: KeyScope = .plain,
        timeout: Duration? = nil
    ) async throws(TransmissionError) {
        _ = try await send(method, params: params, scope: scope, timeout: timeout, resultType: Empty.self)
    }

    private func send<Params: Encodable, Result: Decodable>(
        _ method: RPCMethod,
        params: Params,
        scope: KeyScope,
        timeout: Duration?,
        resultType: Result.Type
    ) async throws(TransmissionError) -> Result? {
        let requestDialect = dialect
        let body = try encodeRequest(method, params: params, scope: scope, dialect: requestDialect)

        // Transmission answers the first request (and any request after the token expires)
        // with 409 and a fresh CSRF token; retry once with the new token.
        for _ in 0..<2 {
            let (data, response) = try await post(body, timeout: timeout)
            switch response.statusCode {
            case 409:
                guard let token = response.value(forHTTPHeaderField: "X-Transmission-Session-Id") else {
                    throw .invalidResponse(details: "HTTP 409 without a session id")
                }
                csrfToken = token
            case 204:
                return nil
            case 200..<300:
                return try decodeResponse(data, as: resultType, dialect: requestDialect)
            case 300..<400:
                throw .redirectRefused
            case 401:
                throw .unauthorized
            case 403:
                let serverMessage = String(validating: data, as: UTF8.self).map { String($0.prefix(512)) }
                throw .ipNotAllowed(serverMessage: serverMessage ?? "")
            case 421:
                throw .hostNotAllowed
            default:
                throw .httpStatus(response.statusCode)
            }
        }
        throw .invalidResponse(details: "the server rejected its own session id")
    }

    private func post(_ body: Data, timeout: Duration?) async throws(TransmissionError) -> (Data, HTTPURLResponse) {
        var request = URLRequest(url: url)
        if let timeout {
            request.timeoutInterval = TimeInterval(timeout.components.seconds)
        }
        request.httpMethod = "POST"
        request.httpBody = body
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        if let authorizationHeader {
            request.setValue(authorizationHeader, forHTTPHeaderField: "Authorization")
        }
        if let csrfToken {
            request.setValue(csrfToken, forHTTPHeaderField: "X-Transmission-Session-Id")
        }

        do {
            let (data, response) = try await dataRetryingDroppedConnection(for: request)
            guard let httpResponse = response as? HTTPURLResponse else {
                throw TransmissionError.invalidResponse(details: "not an HTTP response")
            }
            return (data, httpResponse)
        } catch let error as TransmissionError {
            throw error
        } catch {
            if let fingerprint = securityDelegate.takeRejectedFingerprint() {
                throw .untrustedCertificate(sha256Fingerprint: fingerprint)
            }
            let urlError = error as? URLError
            // URLSession only says "a TLS error occurred"; name the usual cause instead.
            if endpoint.usesTLS, urlError?.code == .secureConnectionFailed {
                throw .httpsHandshakeFailed
            }
            throw .network(code: urlError?.errorCode ?? -1, description: error.localizedDescription)
        }
    }

    /// Proxies and NAS web servers close idle keep-alive connections, often after 5 seconds,
    /// the same as the refresh interval. URLSession then fails the next request on that connection
    /// instead of retrying it, because it's a POST. Transmission's calls are safe to repeat
    /// (adding a torrent twice reports a duplicate), so one retry on a fresh connection is fine.
    private func dataRetryingDroppedConnection(for request: URLRequest) async throws -> (Data, URLResponse) {
        do {
            return try await session.data(for: request)
        } catch let error as URLError where error.code == .networkConnectionLost {
            return try await session.data(for: request)
        }
    }

    private func encodeRequest<Params: Encodable>(
        _ method: RPCMethod,
        params: Params,
        scope: KeyScope,
        dialect: RPCDialect
    ) throws(TransmissionError) -> Data {
        let tag = nextTag
        nextTag += 1
        let envelope = RequestEnvelope(dialect: dialect, method: method, params: params, tag: tag)
        do {
            return try RPCCoding.encoder(for: dialect, scope: scope).encode(envelope)
        } catch {
            throw .invalidResponse(details: "couldn't encode \(method.rawValue): \(error)")
        }
    }

    private func decodeResponse<Result: Decodable>(
        _ data: Data,
        as resultType: Result.Type,
        dialect: RPCDialect
    ) throws(TransmissionError) -> Result? {
        let decoder = RPCCoding.decoder(for: dialect)
        do {
            switch dialect {
            case .jsonRPC:
                let response = try decoder.decode(JSONRPCResponse<Result>.self, from: data)
                if let error = response.error {
                    throw TransmissionError.rpcFailure(message: error.data?.errorString ?? error.message)
                }
                return response.result
            case .legacy:
                let response = try decoder.decode(LegacyResponse<Result>.self, from: data)
                guard response.result == "success" else {
                    throw TransmissionError.rpcFailure(message: response.result)
                }
                return response.arguments
            }
        } catch let error as TransmissionError {
            throw error
        } catch let error as DecodingError {
            // A wrong RPC path or port often reaches a web page, such as a NAS's admin site.
            if Self.looksLikeWebPage(data) { throw .notTransmissionRPC }
            throw .invalidResponse(details: error.shortDescription)
        } catch {
            throw .invalidResponse(details: error.localizedDescription)
        }
    }
}

extension TransmissionClient {
    static func looksLikeWebPage(_ data: Data) -> Bool {
        guard let start = String(validating: data.prefix(256), as: UTF8.self) else { return false }
        return start.trimmingCharacters(in: .whitespacesAndNewlines).hasPrefix("<")
    }
}

extension DecodingError {
    /// "torrents[3].name: expected String" instead of a page of debug output.
    var shortDescription: String {
        func path(_ context: Context) -> String {
            context.codingPath.map { $0.intValue.map { "[\($0)]" } ?? ".\($0.stringValue)" }
                .joined()
                .trimmingCharacters(in: CharacterSet(charactersIn: "."))
        }
        switch self {
        case .typeMismatch(let type, let context): return "\(path(context)): expected \(type)"
        case .valueNotFound(let type, let context): return "\(path(context)): missing \(type)"
        case .keyNotFound(let key, let context): return "\(path(context)): missing key \(key.stringValue)"
        case .dataCorrupted(let context): return "\(path(context)): \(context.debugDescription)"
        @unknown default: return localizedDescription
        }
    }
}
