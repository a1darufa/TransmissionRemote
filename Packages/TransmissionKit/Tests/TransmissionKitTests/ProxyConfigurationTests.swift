import CFNetwork
import Foundation
import Testing
@testable import TransmissionKit

@Suite("Proxy configuration")
struct ProxyConfigurationTests {
    @Test func httpProxyCoversBothSchemesAndAnswersLoginInTheDelegate() {
        let proxy = ProxyConfiguration(
            kind: .http, host: "proxy.lan", port: 3128,
            credentials: Credentials(username: "me", password: "secret")
        )
        let settings = proxy.connectionProxyDictionary
        #expect(settings[kCFNetworkProxiesHTTPProxy as String] as? String == "proxy.lan")
        #expect(settings[kCFNetworkProxiesHTTPPort as String] as? Int == 3128)
        #expect(settings[kCFNetworkProxiesHTTPSProxy as String] as? String == "proxy.lan")
        #expect(settings[kCFNetworkProxiesHTTPSPort as String] as? Int == 3128)
        // The password isn't put in the dictionary; the delegate answers the proxy's 407 instead.
        #expect(!settings.values.contains { $0 as? String == "secret" })
        #expect(proxy.httpProxyCredentials?.username == "me")
    }

    @Test func socksProxyUsesVersion5AndCarriesItsLogin() {
        let proxy = ProxyConfiguration(
            kind: .socks5, host: "10.0.0.2", port: 1080,
            credentials: Credentials(username: "me", password: "secret")
        )
        let settings = proxy.connectionProxyDictionary
        #expect(settings[kCFNetworkProxiesSOCKSProxy as String] as? String == "10.0.0.2")
        #expect(settings[kCFNetworkProxiesSOCKSPort as String] as? Int == 1080)
        #expect(settings[kCFStreamPropertySOCKSVersion as String] as? String == kCFStreamSocketSOCKSVersion5 as String)
        #expect(settings[kCFStreamPropertySOCKSUser as String] as? String == "me")
        #expect(settings[kCFStreamPropertySOCKSPassword as String] as? String == "secret")
        #expect(proxy.httpProxyCredentials == nil)
    }

    @Test func socksProxyWithoutLoginLeavesItOut() {
        let settings = ProxyConfiguration(kind: .socks5, host: "10.0.0.2", port: 1080).connectionProxyDictionary
        #expect(settings[kCFStreamPropertySOCKSUser as String] == nil)
    }

    @Test func httpProxyLoginGoesUpFrontOnlyForPlainHTTPServers() {
        let proxy = ProxyConfiguration(
            kind: .http, host: "proxy.lan", port: 3128,
            credentials: Credentials(username: "me", password: "secret")
        )
        let plain = TransmissionClient.hardenedConfiguration(
            from: .ephemeral, timeout: .seconds(5), proxy: proxy, serverUsesTLS: false
        )
        let tls = TransmissionClient.hardenedConfiguration(
            from: .ephemeral, timeout: .seconds(5), proxy: proxy, serverUsesTLS: true
        )
        #expect(plain.httpAdditionalHeaders?["Proxy-Authorization"] as? String == "Basic bWU6c2VjcmV0")
        #expect(tls.httpAdditionalHeaders?["Proxy-Authorization"] == nil)
    }

    @Test func clientSessionUsesTheProxyOnlyWhenOneIsSet() {
        let proxy = ProxyConfiguration(kind: .http, host: "proxy.lan", port: 3128)
        let withProxy = TransmissionClient.hardenedConfiguration(from: .ephemeral, timeout: .seconds(5), proxy: proxy)
        let withoutProxy = TransmissionClient.hardenedConfiguration(from: .ephemeral, timeout: .seconds(5))
        #expect(withProxy.connectionProxyDictionary?[kCFNetworkProxiesHTTPProxy as String] as? String == "proxy.lan")
        #expect(withoutProxy.connectionProxyDictionary?[kCFNetworkProxiesHTTPProxy as String] == nil)
    }
}
