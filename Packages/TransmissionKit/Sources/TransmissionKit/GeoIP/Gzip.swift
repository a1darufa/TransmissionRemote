import Foundation

/// Unpacks .gz files, such as DB-IP's country database. Foundation can inflate raw DEFLATE data,
/// so this only has to step over the gzip header and trailer around it (RFC 1952).
public enum Gzip {
    public enum Failure: Error, Equatable {
        case notGzip
        case truncated
        case corrupt
    }

    private enum HeaderFlag {
        static let headerCRC: UInt8 = 0x02
        static let extraField: UInt8 = 0x04
        static let fileName: UInt8 = 0x08
        static let comment: UInt8 = 0x10
    }

    public static func decompress(_ data: Data) throws(Failure) -> Data {
        let bytes = [UInt8](data)
        guard bytes.count >= 18, bytes[0] == 0x1F, bytes[1] == 0x8B, bytes[2] == 8 else { throw .notGzip }
        let flags = bytes[3]
        var offset = 10

        if flags & HeaderFlag.extraField != 0 {
            guard offset + 2 <= bytes.count else { throw .truncated }
            offset += 2 + Int(bytes[offset]) + Int(bytes[offset + 1]) << 8
        }
        if flags & HeaderFlag.fileName != 0 { offset = try skipZeroTerminated(bytes, from: offset) }
        if flags & HeaderFlag.comment != 0 { offset = try skipZeroTerminated(bytes, from: offset) }
        if flags & HeaderFlag.headerCRC != 0 { offset += 2 }

        // The last 8 bytes are the CRC-32 and length of the original data.
        let deflateEnd = bytes.count - 8
        guard offset < deflateEnd else { throw .truncated }
        do {
            return try (Data(bytes[offset..<deflateEnd]) as NSData).decompressed(using: .zlib) as Data
        } catch {
            throw .corrupt
        }
    }

    private static func skipZeroTerminated(_ bytes: [UInt8], from offset: Int) throws(Failure) -> Int {
        guard let end = bytes[offset...].firstIndex(of: 0) else { throw .truncated }
        return end + 1
    }
}
