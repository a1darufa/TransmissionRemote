import Foundation
import Testing
@testable import TransmissionKit

@Suite("Looking up peer countries")
struct CountryDatabaseTests {
    /// A few lines in DB-IP's format, including a gap (1.0.4.0 – 1.0.7.255) and IPv6.
    private let csv = """
        0.0.0.0,0.255.255.255,ZZ
        1.0.0.0,1.0.0.255,AU
        1.0.1.0,1.0.3.255,CN
        1.0.8.0,1.0.15.255,DE
        2001:200::,2001:200:ffff:ffff:ffff:ffff:ffff:ffff,JP
        2a00:1450::,2a00:1450:ffff:ffff:ffff:ffff:ffff:ffff,IE
        """

    private var database: CountryDatabase { CountryDatabase(csv: Data(csv.utf8)) }

    @Test(arguments: [
        ("1.0.0.1", "AU"), ("1.0.2.200", "CN"), ("1.0.15.255", "DE"),
        ("2001:200::1", "JP"), ("2a00:1450:4001:80b::200e", "IE")
    ])
    func findsTheCountry(address: String, country: String) {
        #expect(database.countryCode(for: address) == country)
    }

    @Test(arguments: ["0.0.0.1", "1.0.5.1", "1.0.16.0", "255.255.255.255", "2001:100::1", "not an address", ""])
    func returnsNilForUnknownAddresses(address: String) {
        #expect(database.countryCode(for: address) == nil)
    }

    @Test func skipsMalformedLines() {
        let messy = "garbage\n1.0.0.0,1.0.0.255,AU\r\n1.0.1.0,1.0.1.255,toolong\n,,\n"
        let database = CountryDatabase(csv: Data(messy.utf8))
        #expect(database.countryCode(for: "1.0.0.9") == "AU")
        #expect(database.countryCode(for: "1.0.1.9") == nil)
    }

    @Test func handlesARangeEndingAtTheLastAddress() {
        let database = CountryDatabase(csv: Data("0.0.0.0,255.255.255.255,US\n".utf8))
        #expect(database.countryCode(for: "255.255.255.255") == "US")
    }

    @Test func drawsFlagsFromCountryCodes() {
        #expect(CountryDisplay.flag(for: "DE") == "🇩🇪")
        #expect(CountryDisplay.flag(for: "jp") == "🇯🇵")
        #expect(CountryDisplay.name(for: "DE", locale: Locale(identifier: "en")) == "Germany")
    }

    @Test func unpacksGzipWithAFileNameHeader() throws {
        let sample = try #require(Data(base64Encoded: """
            H4sICAAAAAAC/2RiaXAuY3N2ADPQMwBBHQM9I1NTGNaJiuIyhEpAaJCgYyhY0BAqaAwWdPbjMjIwMLQCElZWOnBm\
            GhDgJHS8ArgAO5oVIngAAAA=
            """))
        let csv = try Gzip.decompress(sample)
        let database = CountryDatabase(csv: csv)
        #expect(database.countryCode(for: "1.0.0.7") == "AU")
        #expect(database.countryCode(for: "2001:200::5") == "JP")
    }

    @Test func rejectsDataThatIsNotGzip() {
        #expect(throws: Gzip.Failure.notGzip) { try Gzip.decompress(Data(repeating: 0, count: 40)) }
    }
}
