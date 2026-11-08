import Darwin
import Foundation

/// Finds the country of an IP address, for the Peers tab's Country column, like Transmission
/// Remote GUI's GeoIP lookup. Built from DB-IP's free "IP to Country Lite" CSV
/// (`first,last,country` per line, licensed CC BY 4.0).
public struct CountryDatabase: Sendable {
    /// Range starts in ascending order, with the country of each range. Gaps between the CSV's
    /// ranges are filled with "unknown" entries, so a lookup only needs the last start at or
    /// below the address.
    private let ipv4Starts: [UInt32]
    private let ipv4Countries: [UInt16]
    private let ipv6Starts: [UInt128]
    private let ipv6Countries: [UInt16]

    /// Marks addresses the database doesn't cover. DB-IP itself uses "ZZ" for these.
    private static let unknown: UInt16 = 0

    public var isEmpty: Bool { ipv4Starts.isEmpty && ipv6Starts.isEmpty }

    /// Parses the CSV. Lines that don't parse are skipped rather than failing the whole file.
    public init(csv: Data) {
        var ipv4 = RangeBuilder<UInt32>()
        var ipv6 = RangeBuilder<UInt128>()

        for line in csv.split(separator: UInt8(ascii: "\n")) {
            let fields = line.split(separator: UInt8(ascii: ","), omittingEmptySubsequences: false)
            guard fields.count >= 3, let country = Self.countryCode(fields[2]),
                  let first = String(bytes: fields[0], encoding: .ascii),
                  let last = String(bytes: fields[1], encoding: .ascii)
            else { continue }
            if let start = Self.ipv4(first), let end = Self.ipv4(last) {
                ipv4.add(start: start, end: end, country: country)
            } else if let start = Self.ipv6(first), let end = Self.ipv6(last) {
                ipv6.add(start: start, end: end, country: country)
            }
        }

        (ipv4Starts, ipv4Countries) = ipv4.build()
        (ipv6Starts, ipv6Countries) = ipv6.build()
    }

    /// The ISO country code ("DE") for an address, or nil when it's unknown, private or unparsable.
    public func countryCode(for address: String) -> String? {
        let code: UInt16?
        if let ipv4 = Self.ipv4(address) {
            code = Self.lookUp(ipv4, starts: ipv4Starts, countries: ipv4Countries)
        } else if let ipv6 = Self.ipv6(address) {
            code = Self.lookUp(ipv6, starts: ipv6Starts, countries: ipv6Countries)
        } else {
            code = nil
        }
        guard let code, code != Self.unknown else { return nil }
        return String(bytes: [UInt8(code >> 8), UInt8(code & 0xFF)], encoding: .ascii)
    }

    private static func lookUp<Address: Comparable>(
        _ address: Address,
        starts: [Address],
        countries: [UInt16]
    ) -> UInt16? {
        // Binary search for the last range starting at or before the address.
        var low = 0
        var high = starts.count
        while low < high {
            let middle = (low + high) / 2
            if starts[middle] <= address { low = middle + 1 } else { high = middle }
        }
        return low > 0 ? countries[low - 1] : nil
    }

    /// Two uppercase ASCII letters packed into 16 bits; "ZZ" (DB-IP's "unknown") becomes `unknown`.
    private static func countryCode(_ field: some Collection<UInt8>) -> UInt16? {
        let letters = Array(field.filter { $0 != UInt8(ascii: "\r") })
        guard letters.count == 2, letters.allSatisfy({ (65...90).contains($0) }) else { return nil }
        if letters == [90, 90] { return unknown }
        return UInt16(letters[0]) << 8 | UInt16(letters[1])
    }

    static func ipv4(_ text: String) -> UInt32? {
        var address = in_addr()
        guard inet_pton(AF_INET, text, &address) == 1 else { return nil }
        return UInt32(bigEndian: address.s_addr)
    }

    static func ipv6(_ text: String) -> UInt128? {
        var address = in6_addr()
        guard inet_pton(AF_INET6, text, &address) == 1 else { return nil }
        return withUnsafeBytes(of: &address) { bytes in
            bytes.reduce(UInt128(0)) { $0 << 8 | UInt128($1) }
        }
    }
}

/// Collects ranges in file order and fills the gaps between them with "unknown".
private struct RangeBuilder<Address: FixedWidthInteger & UnsignedInteger> {
    private struct AddressRange {
        let start: Address
        let end: Address
        let country: UInt16
    }

    private var ranges: [AddressRange] = []

    mutating func add(start: Address, end: Address, country: UInt16) {
        guard start <= end else { return }
        ranges.append(AddressRange(start: start, end: end, country: country))
    }

    func build() -> ([Address], [UInt16]) {
        var starts: [Address] = []
        var countries: [UInt16] = []
        starts.reserveCapacity(ranges.count)
        countries.reserveCapacity(ranges.count)
        var nextExpected: Address? = 0

        for range in ranges.sorted(by: { $0.start < $1.start }) {
            if let expected = nextExpected, range.start > expected {
                starts.append(expected)
                countries.append(0)
            }
            starts.append(range.start)
            countries.append(range.country)
            // Nil once a range reaches the very last address, so the addition can't overflow.
            nextExpected = range.end == .max ? nil : range.end + 1
        }
        if let expected = nextExpected, !starts.isEmpty {
            starts.append(expected)
            countries.append(0)
        }
        return (starts, countries)
    }
}

/// A flag emoji and localized name for a country code, for the Peers tab.
public enum CountryDisplay {
    /// "DE" → 🇩🇪, using the regional indicator letters every Apple platform draws as flags.
    public static func flag(for code: String) -> String {
        let base: UInt32 = 0x1F1E6 - 65
        let scalars = code.uppercased().unicodeScalars.compactMap { Unicode.Scalar(base + $0.value) }
        return String(String.UnicodeScalarView(scalars))
    }

    public static func name(for code: String, locale: Locale = .current) -> String {
        locale.localizedString(forRegionCode: code) ?? code
    }
}
