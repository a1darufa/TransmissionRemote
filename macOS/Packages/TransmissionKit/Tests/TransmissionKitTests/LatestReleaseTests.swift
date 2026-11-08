import Foundation
import Testing
@testable import TransmissionKit

@Suite("Checking GitHub for a newer release")
struct LatestReleaseTests {
    @Test(arguments: [
        ("1.0", "1.1"), ("1.9", "1.10"), ("v1.0", "1.0.1"), ("1.2", "2.0"), ("1.0.0-beta", "1.0.1")
    ])
    func comparesNumberByNumber(older: String, newer: String) throws {
        let old = try #require(ReleaseVersion(older))
        let new = try #require(ReleaseVersion(newer))
        #expect(old < new)
        #expect(!(new < old))
    }

    @Test func treatsMissingTrailingZerosAsEqual() throws {
        #expect(try #require(ReleaseVersion("1.0")) == #require(ReleaseVersion("v1.0.0")))
    }

    @Test func rejectsTagsWithoutANumber() {
        #expect(ReleaseVersion("") == nil)
        #expect(ReleaseVersion("latest") == nil)
    }

    @Test func decodesGitHubsResponse() throws {
        let json = #"""
        {"tag_name": "v1.1", "name": "Transmission Remote 1.1",
         "html_url": "https://github.com/a1darufa/TransmissionRemote/releases/tag/v1.1", "draft": false}
        """#
        let release = try LatestRelease(gitHubResponse: Data(json.utf8))
        #expect(release.version == ReleaseVersion("1.1"))
        #expect(release.tagName == "v1.1")
        #expect(release.pageURL.absoluteString == "https://github.com/a1darufa/TransmissionRemote/releases/tag/v1.1")
    }

    @Test func refusesPagesOutsideGitHub() {
        let json = #"{"tag_name": "v9.9", "html_url": "https://example.com/download"}"#
        #expect(throws: (any Error).self) { try LatestRelease(gitHubResponse: Data(json.utf8)) }
    }
}
