import Foundation
import Synchronization
import Testing
@testable import TransmissionKit

/// A canned HTTP response, or a network failure when `failure` is set.
struct MockResponse {
    var status: Int
    var headers: [String: String] = [:]
    var body = ""
    var failure: URLError.Code?

    static func failing(with code: URLError.Code) -> Self {
        Self(status: 0, failure: code)
    }
}

/// Serves canned HTTP responses so the client can be tested without a daemon.
final class MockURLProtocol: URLProtocol, @unchecked Sendable {
    typealias Handler = @Sendable (URLRequest, Data) -> MockResponse

    static let handler = Mutex<Handler?>(nil)
    static let requests = Mutex<[(request: URLRequest, body: Data)]>([])

    override static func canInit(with request: URLRequest) -> Bool { true }
    override static func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        let body = Self.readBody(of: request)
        Self.requests.withLock { $0.append((request, body)) }
        guard let handler = Self.handler.withLock({ $0 }) else {
            client?.urlProtocol(self, didFailWithError: URLError(.badServerResponse))
            return
        }
        let reply = handler(request, body)
        if let failure = reply.failure {
            client?.urlProtocol(self, didFailWithError: URLError(failure))
            return
        }
        guard let url = request.url,
              let response = HTTPURLResponse(
                url: url, statusCode: reply.status, httpVersion: "HTTP/1.1", headerFields: reply.headers
              )
        else {
            client?.urlProtocol(self, didFailWithError: URLError(.badURL))
            return
        }
        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: Data(reply.body.utf8))
        client?.urlProtocolDidFinishLoading(self)
    }

    override func stopLoading() {}

    /// URLSession moves `httpBody` into a stream before it reaches a protocol.
    private static func readBody(of request: URLRequest) -> Data {
        if let body = request.httpBody { return body }
        guard let stream = request.httpBodyStream else { return Data() }
        stream.open()
        defer { stream.close() }
        var data = Data()
        var buffer = [UInt8](repeating: 0, count: 4096)
        while stream.hasBytesAvailable {
            let count = stream.read(&buffer, maxLength: buffer.count)
            guard count > 0 else { break }
            data.append(buffer, count: count)
        }
        return data
    }

    static func reset(_ handler: @escaping Handler) {
        self.handler.withLock { $0 = handler }
        requests.withLock { $0.removeAll() }
    }
}

@Suite("TransmissionClient over a mocked network", .serialized)
struct ClientTests {
    private static let csrfToken = "token-123"
    private static let transmission411 =
        #"{"result":"success","arguments":{"version":"4.1.1","rpc-version":19,"rpc-version-semver":"6.0.1"}}"#
    private static let transmission300 =
        #"{"result":"success","arguments":{"version":"3.00 (bb6b5a062e)","rpc-version":16}}"#

    private func makeClient(
        preference: ProtocolPreference = .automatic,
        usesTLS: Bool = false
    ) throws -> TransmissionClient {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [MockURLProtocol.self]
        return try TransmissionClient(
            endpoint: ServerEndpoint(host: "100.64.12.34", usesTLS: usesTLS),
            credentials: Credentials(username: "user", password: "secret"),
            protocolPreference: preference,
            baseConfiguration: configuration
        )
    }

    /// A daemon that demands a CSRF token, then answers with the given session-get body.
    private func simulateDaemon(sessionInfo: String, otherwise: String = #"{"result":"success","arguments":{}}"#) {
        MockURLProtocol.reset { request, body in
            guard request.value(forHTTPHeaderField: "X-Transmission-Session-Id") == Self.csrfToken else {
                return MockResponse(status: 409, headers: ["X-Transmission-Session-Id": Self.csrfToken])
            }
            let text = String(validating: body, as: UTF8.self) ?? ""
            return MockResponse(status: 200, body: text.contains("session-get") ? sessionInfo : otherwise)
        }
    }

    @Test func upgradesToJSONRPCOnTransmission41() async throws {
        simulateDaemon(sessionInfo: Self.transmission411, otherwise: #"{"jsonrpc":"2.0","id":2,"result":{}}"#)
        let client = try makeClient()

        let info = try await client.connect()
        #expect(info.version == "4.1.1")
        #expect(await client.dialect == .jsonRPC)

        try await client.perform(.start, ids: [1])
        let requests = MockURLProtocol.requests.withLock { $0 }
        #expect(requests.count == 3, "409 handshake, session-get, torrent_start")

        let last = try #require(requests.last)
        #expect(last.request.value(forHTTPHeaderField: "Authorization") == "Basic dXNlcjpzZWNyZXQ=")
        #expect(last.request.value(forHTTPHeaderField: "X-Transmission-Session-Id") == Self.csrfToken)
        let json = try #require(String(validating: last.body, as: UTF8.self))
        #expect(json.contains(#""method":"torrent_start""#))
        #expect(json.contains(#""jsonrpc":"2.0""#))
    }

    @Test func staysOnLegacyForTransmission300() async throws {
        simulateDaemon(sessionInfo: Self.transmission300)
        let client = try makeClient()

        try await client.connect()
        #expect(await client.dialect == .legacy)

        try await client.remove(ids: [4], deletingData: true)
        let lastBody = try #require(MockURLProtocol.requests.withLock { $0.last?.body })
        let body = try #require(String(validating: lastBody, as: UTF8.self))
        #expect(body.contains(#""method":"torrent-remove""#))
        #expect(body.contains(#""delete-local-data":true"#))
    }

    @Test func legacyPreferenceIsHonoredOnNewServers() async throws {
        simulateDaemon(sessionInfo: Self.transmission411)
        let client = try makeClient(preference: .legacy)
        try await client.connect()
        #expect(await client.dialect == .legacy)
    }

    @Test func rejectsServersOlderThanTheQueue() async throws {
        simulateDaemon(sessionInfo: #"{"result":"success","arguments":{"version":"2.22","rpc-version":13}}"#)
        let client = try makeClient()
        await #expect(throws: TransmissionError.unsupportedServer(version: "2.22")) {
            try await client.connect()
        }
    }

    @Test func mapsHTTPFailuresToActionableErrors() async throws {
        for (status, expected) in [
            (401, TransmissionError.unauthorized),
            (403, .ipNotAllowed(serverMessage: "")),
            (421, .hostNotAllowed),
            (302, .redirectRefused),
            (500, .httpStatus(500))
        ] {
            MockURLProtocol.reset { _, _ in MockResponse(status: status) }
            let client = try makeClient()
            await #expect(throws: expected) { try await client.connect() }
        }
    }

    @Test func explainsFailedHTTPSHandshakes() async throws {
        MockURLProtocol.reset { _, _ in .failing(with: .secureConnectionFailed) }
        let client = try makeClient(usesTLS: true)
        await #expect(throws: TransmissionError.httpsHandshakeFailed) { try await client.connect() }
    }

    @Test func leavesPlainHTTPFailuresUnchanged() async throws {
        MockURLProtocol.reset { _, _ in .failing(with: .secureConnectionFailed) }
        let client = try makeClient(usesTLS: false)
        let error = await #expect(throws: TransmissionError.self) { try await client.connect() }
        guard case .network(let code, _) = error else {
            Issue.record("Expected a network error, got \(String(describing: error))")
            return
        }
        #expect(code == URLError.Code.secureConnectionFailed.rawValue)
    }

    @Test func surfacesRPCFailureMessages() async throws {
        simulateDaemon(
            sessionInfo: #"{"result":"success","arguments":{"version":"3.00","rpc-version":16}}"#,
            otherwise: #"{"result":"No such file or directory","arguments":{}}"#
        )
        let client = try makeClient()
        try await client.connect()
        await #expect(throws: TransmissionError.rpcFailure(message: "No such file or directory")) {
            try await client.setLocation(ids: [1], to: "/missing", movingData: true)
        }
    }

    @Test func retriesOnceWhenAKeptAliveConnectionWasDropped() async throws {
        let attempts = Mutex(0)
        MockURLProtocol.reset { request, _ in
            let attempt = attempts.withLock { count in
                count += 1
                return count
            }
            // The proxy closed the idle connection just as the first request went out.
            if attempt == 1 { return .failing(with: .networkConnectionLost) }
            guard request.value(forHTTPHeaderField: "X-Transmission-Session-Id") == Self.csrfToken else {
                return MockResponse(status: 409, headers: ["X-Transmission-Session-Id": Self.csrfToken])
            }
            return MockResponse(status: 200, body: Self.transmission300)
        }
        let client = try makeClient()
        let info = try await client.connect()
        #expect(info.version.hasPrefix("3.00"))
    }

    @Test func explainsAWebPageInsteadOfTheRPC() async throws {
        MockURLProtocol.reset { _, _ in
            MockResponse(status: 200, body: "\n<!DOCTYPE html><html><body>My NAS</body></html>")
        }
        let client = try makeClient()
        await #expect(throws: TransmissionError.notTransmissionRPC) { try await client.connect() }
    }
}
