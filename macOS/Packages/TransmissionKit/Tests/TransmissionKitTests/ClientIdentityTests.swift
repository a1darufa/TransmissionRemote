import Foundation
import Testing
@testable import TransmissionKit

@Suite("Client certificates for HTTPS reverse proxies")
struct ClientIdentityTests {
    /// A throwaway self-signed test certificate ("Transmission Remote test client", EC P-256)
    /// with its private key, exported by `openssl pkcs12 -export` with the password below.
    /// It protects nothing.
    private static let testPassword = "test-password"
    private static let testPKCS12 = Data(base64Encoded: """
        MIIENAIBAzCCA+IGCSqGSIb3DQEHAaCCA9MEggPPMIIDyzCCAnoGCSqGSIb3DQEHBqCCAmswggJnAgEAMIICYAYJKoZIhvcNAQcB\
        MF8GCSqGSIb3DQEFDTBSMDEGCSqGSIb3DQEFDDAkBBDgg9N2/VP9jzcELIuGBJwYAgIIADAMBggqhkiG9w0CCQUAMB0GCWCGSAFl\
        AwQBKgQQV1PnIj9tUqCZw/nTE3TPEYCCAfAe2CNPEAQYCpqp0GS5idrpu1SHNQuqg9AhJkOvNN9fqRqa8kZLLXXHF6+R6+yZL9t6\
        nbXZb3T3u8YuFDG3oxNViQS5ERI7/Fbpa9I4GhwVFtSz2YRnyRc8zfePG0ZutZSHW44Z54+mW+ly8i/YPCYzWSVpTCUEH2skRdPv\
        f+34djy64I3pI0n/VCFxxzxxeAQwwXlmwkFIq2jpfgskeHjEVVeCBJzgKC8E4fcgFk2782a82dGoJAyhqcLd/cHkyYR1F2Y2bzPq\
        CiIIVgCq0zikoeMCspjHuYZe6YhIKivOhYQ9S7/UObjkyJo/WGZ/Kb+9r4fr5LhGn2irAPqnI0gODiG8ms+3iJs//QsuFmbXwPVu\
        Q3V1c2b6h85LQiCC4t+1EESQU5rK3A+xGTt0IQ/nnzI6QWgeRTlhEzNbAKj6STsgNZltjMiKNK3lAAKIw6TBHt0mqssSQvbgiuAW\
        I6vAfZaYmaKof36tQO5okij53sBIFspIl2RTZ6Bx7nBaDgpi29lU00iRHX0wMmatjzK/H3OGjlUqT88VkVbPbpUjkmI2oXzgQl+6\
        diL0i3MxG8v3kAjLwTVM3qWoVpE+ONOHiVv6AfagFXiwbmzD/XIQW2YiKxJJyGJl3UH7aDffKsi1JiBfZqRmMJh+wfTtbDHFMIIB\
        SQYJKoZIhvcNAQcBoIIBOgSCATYwggEyMIIBLgYLKoZIhvcNAQwKAQKggfcwgfQwXwYJKoZIhvcNAQUNMFIwMQYJKoZIhvcNAQUM\
        MCQEEMRE+vb8tawGV52MUidfWZMCAggAMAwGCCqGSIb3DQIJBQAwHQYJYIZIAWUDBAEqBBAc2m1FkVSG92ukJ16e4aanBIGQoAoI\
        Qs0kYgMZ/nWZlkcX/PR+ohXhfl2JSrt79yA96yn+cdlG358RhjxbmVxIxJT4P9zkG5THVNcxGn8uGVntJawO9AgqVtwPOHxTx8Yo\
        66Z9lhQPzPfV0xlPCY7KY7Wm1hKzY4fbIOHUwZnePWl6SUcUieCIhZkLBGCkYlLYSWPl29IckimLR6EyjCgKPJKVMSUwIwYJKoZI\
        hvcNAQkVMRYEFMcZiEWFRN0HZxSao7jOGnQ65TdEMEkwMTANBglghkgBZQMEAgEFAAQgYJLSUAbCwR7ojgGZmzeNYXKlUqVDlFuV\
        nJbvjUDN4oIEEBSYiA/39e6tkZp1Gasw9foCAggA
        """)

    @Test func importsTheCertificateAndKey() throws {
        let identity = try ClientIdentity(pkcs12: #require(Self.testPKCS12), password: Self.testPassword)
        #expect(identity.summary == "Transmission Remote test client")
        #expect(identity.expiryDate.map { $0 > .now } == true)
    }

    @Test func reportsAWrongPassword() throws {
        let data = try #require(Self.testPKCS12)
        #expect(throws: ClientIdentity.ImportError.wrongPassword) {
            try ClientIdentity(pkcs12: data, password: "not it")
        }
    }

    @Test func reportsAFileThatIsNotPKCS12() {
        #expect(throws: ClientIdentity.ImportError.unreadable) {
            try ClientIdentity(pkcs12: Data("not a certificate".utf8), password: "")
        }
    }

    @Test func answersAClientCertificateRequest() async throws {
        let identity = try ClientIdentity(pkcs12: #require(Self.testPKCS12), password: Self.testPassword)
        let delegate = ConnectionSecurityDelegate(pinnedFingerprint: nil, clientIdentity: identity)
        let challenge = Self.clientCertificateChallenge()
        let (disposition, credential) = await delegate.urlSession(.shared, didReceive: challenge)
        #expect(disposition == .useCredential)
        #expect(credential?.identity != nil)
    }

    @Test func goesOnWithoutACertificateWhenNoneIsChosen() async {
        let delegate = ConnectionSecurityDelegate(pinnedFingerprint: nil)
        let challenge = Self.clientCertificateChallenge()
        let (disposition, credential) = await delegate.urlSession(.shared, didReceive: challenge)
        #expect(disposition == .performDefaultHandling)
        #expect(credential == nil)
    }

    private static func clientCertificateChallenge() -> URLAuthenticationChallenge {
        let space = URLProtectionSpace(
            host: "nas.example.com", port: 443, protocol: "https", realm: nil,
            authenticationMethod: NSURLAuthenticationMethodClientCertificate
        )
        return URLAuthenticationChallenge(
            protectionSpace: space, proposedCredential: nil, previousFailureCount: 0,
            failureResponse: nil, error: nil, sender: UnusedChallengeSender()
        )
    }
}

/// `URLAuthenticationChallenge` needs a sender, though the async delegate API never calls it.
private final class UnusedChallengeSender: NSObject, URLAuthenticationChallengeSender {
    func use(_ credential: URLCredential, for challenge: URLAuthenticationChallenge) {}
    func continueWithoutCredential(for challenge: URLAuthenticationChallenge) {}
    func cancel(_ challenge: URLAuthenticationChallenge) {}
}
