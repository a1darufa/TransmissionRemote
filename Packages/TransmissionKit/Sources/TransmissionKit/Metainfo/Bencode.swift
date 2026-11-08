import Foundation

/// A decoded bencode value (BEP 3).
public indirect enum BencodeValue: Sendable, Equatable {
    case integer(Int64)
    case bytes(Data)
    case list([Self])
    case dictionary([String: Self])

    public var integer: Int64? {
        if case .integer(let value) = self { value } else { nil }
    }

    public var data: Data? {
        if case .bytes(let value) = self { value } else { nil }
    }

    /// Byte strings decoded as UTF-8 (lossy, since torrents in the wild aren't always valid UTF-8).
    public var string: String? {
        // Lossy on purpose: a name with a few replacement characters beats no name at all.
        // swiftlint:disable:next optional_data_string_conversion
        data.map { String(decoding: $0, as: UTF8.self) }
    }

    public var list: [Self]? {
        if case .list(let value) = self { value } else { nil }
    }

    public var dictionary: [String: Self]? {
        if case .dictionary(let value) = self { value } else { nil }
    }

    public subscript(key: String) -> Self? {
        dictionary?[key]
    }
}

public enum BencodeError: Error, Equatable {
    case unexpectedEnd
    case invalidCharacter(offset: Int)
    case invalidInteger(offset: Int)
    case invalidLength(offset: Int)
    case nestingTooDeep
    case trailingData
}

/// A strict, bounded bencode parser.
///
/// `.torrent` files come from the internet, so the parser never trusts lengths, limits nesting
/// depth, and never reads past the buffer. It also records where the top-level `info`
/// dictionary sits so the info hash can be computed over the exact original bytes.
public struct BencodeParser {
    public struct Document: Sendable {
        public let root: BencodeValue
        /// Byte range of the top-level `info` value, if present.
        public let infoRange: Range<Int>?
    }

    private static let maximumDepth = 64

    private let bytes: [UInt8]
    private var offset = 0
    private var infoRange: Range<Int>?

    private init(bytes: [UInt8]) {
        self.bytes = bytes
    }

    public static func parse(_ data: Data) throws(BencodeError) -> Document {
        var parser = Self(bytes: [UInt8](data))
        let root = try parser.parseValue(depth: 0)
        guard parser.offset == parser.bytes.count else { throw .trailingData }
        return Document(root: root, infoRange: parser.infoRange)
    }

    private mutating func parseValue(depth: Int) throws(BencodeError) -> BencodeValue {
        guard depth <= Self.maximumDepth else { throw .nestingTooDeep }
        guard let marker = peek() else { throw .unexpectedEnd }

        switch marker {
        case UInt8(ascii: "i"):
            return .integer(try parseInteger())
        case UInt8(ascii: "l"):
            return .list(try parseList(depth: depth))
        case UInt8(ascii: "d"):
            return .dictionary(try parseDictionary(depth: depth))
        case UInt8(ascii: "0")...UInt8(ascii: "9"):
            return .bytes(try parseBytes())
        default:
            throw .invalidCharacter(offset: offset)
        }
    }

    private mutating func parseInteger() throws(BencodeError) -> Int64 {
        let start = offset
        offset += 1 // "i"
        let digits = try readDigits(allowingSign: true, until: UInt8(ascii: "e"))
        guard let value = Int64(digits) else { throw .invalidInteger(offset: start) }
        return value
    }

    private mutating func parseBytes() throws(BencodeError) -> Data {
        let start = offset
        let digits = try readDigits(allowingSign: false, until: UInt8(ascii: ":"))
        guard let length = Int(digits), length >= 0, length <= bytes.count - offset else {
            throw .invalidLength(offset: start)
        }
        defer { offset += length }
        return Data(bytes[offset..<offset + length])
    }

    private mutating func parseList(depth: Int) throws(BencodeError) -> [BencodeValue] {
        offset += 1 // "l"
        var values: [BencodeValue] = []
        while try !consumeEndMarker() {
            values.append(try parseValue(depth: depth + 1))
        }
        return values
    }

    private mutating func parseDictionary(depth: Int) throws(BencodeError) -> [String: BencodeValue] {
        offset += 1 // "d"
        var values: [String: BencodeValue] = [:]
        while try !consumeEndMarker() {
            // Keys aren't always text: BitTorrent v2 "piece layers" uses raw SHA-256 hashes as keys,
            // and rejecting them would reject every hybrid torrent.
            // swiftlint:disable:next optional_data_string_conversion
            let key = String(decoding: try parseBytes(), as: UTF8.self)
            let valueStart = offset
            values[key] = try parseValue(depth: depth + 1)
            if depth == 0, key == "info" {
                infoRange = valueStart..<offset
            }
        }
        return values
    }

    /// Reads ASCII digits (and an optional leading minus) up to `terminator`, consuming it.
    private mutating func readDigits(allowingSign: Bool, until terminator: UInt8) throws(BencodeError) -> String {
        let start = offset
        while let byte = peek(), byte != terminator {
            let isDigit = (UInt8(ascii: "0")...UInt8(ascii: "9")).contains(byte)
            let isSign = allowingSign && byte == UInt8(ascii: "-") && offset == start
            guard isDigit || isSign else { throw .invalidCharacter(offset: offset) }
            offset += 1
            guard offset - start <= 20 else { throw .invalidInteger(offset: start) }
        }
        guard peek() == terminator else { throw .unexpectedEnd }
        guard let digits = String(validating: bytes[start..<offset], as: UTF8.self) else {
            throw .invalidInteger(offset: start)
        }
        offset += 1
        return digits
    }

    private mutating func consumeEndMarker() throws(BencodeError) -> Bool {
        guard let byte = peek() else { throw .unexpectedEnd }
        guard byte == UInt8(ascii: "e") else { return false }
        offset += 1
        return true
    }

    private func peek() -> UInt8? {
        offset < bytes.count ? bytes[offset] : nil
    }
}
