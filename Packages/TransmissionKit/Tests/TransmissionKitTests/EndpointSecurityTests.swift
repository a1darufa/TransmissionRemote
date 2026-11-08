import Foundation
import Testing
@testable import TransmissionKit

@Suite("Endpoint validation and transport security")
struct EndpointSecurityTests {
    @Test func buildsDefaultURL() throws {
        let url = try ServerEndpoint(host: "100.64.12.34").url()
        #expect(url.absoluteString == "http://100.64.12.34:9091/transmission/rpc")
    }

    @Test func bracketsIPv6Hosts() throws {
        let url = try ServerEndpoint(host: "fd7a:115c:a1e0::1", port: 443, usesTLS: true).url()
        #expect(url.absoluteString == "https://[fd7a:115c:a1e0::1]:443/transmission/rpc")
    }

    @Test(arguments: [
        ServerEndpoint(host: ""),
        ServerEndpoint(host: "http://nas.local"),
        ServerEndpoint(host: "nas local"),
        ServerEndpoint(host: "user@nas"),
        ServerEndpoint(host: "nas", port: 0),
        ServerEndpoint(host: "nas", port: 70_000),
        ServerEndpoint(host: "nas", rpcPath: "transmission/rpc")
    ])
    func rejectsInvalidEndpoints(endpoint: ServerEndpoint) {
        #expect(throws: TransmissionError.self) { try endpoint.url() }
    }

    @Test(arguments: [
        ("100.64.12.34", TransportSecurity.tailscale),
        ("fd7a:115c:a1e0::53", .tailscale),
        ("nas.tail1234.ts.net", .tailscale),
        ("127.0.0.1", .loopback),
        ("::1", .loopback),
        ("localhost", .loopback),
        ("192.168.1.20", .localNetwork),
        ("10.0.0.5", .localNetwork),
        ("172.20.1.1", .localNetwork),
        ("fe80::1", .localNetwork),
        ("mycloud.local", .localNetwork),
        ("mycloud", .localNetwork),
        ("8.8.8.8", .unencryptedInternet),
        ("172.32.0.1", .unencryptedInternet),
        ("100.128.0.1", .unencryptedInternet),
        ("seedbox.example.com", .unencryptedInternet),
        ("2001:db8::1", .unencryptedInternet)
    ])
    func classifiesPlainHTTPHosts(host: String, expected: TransportSecurity) {
        #expect(ServerEndpoint(host: host).transportSecurity == expected)
    }

    @Test func tlsIsAlwaysProtected() {
        let endpoint = ServerEndpoint(host: "seedbox.example.com", port: 443, usesTLS: true)
        #expect(endpoint.transportSecurity == .tls)
        #expect(endpoint.transportSecurity.isProtected)
    }

    @Test func fingerprintNormalization() {
        #expect(CertificateFingerprint.normalize("ab cd:EF01") == "AB:CD:EF:01")
    }

    @Test func basicAuthHeader() {
        let header = Credentials(username: "user", password: "pa:ss").basicAuthorizationHeader
        #expect(header == "Basic dXNlcjpwYTpzcw==")
    }
}
