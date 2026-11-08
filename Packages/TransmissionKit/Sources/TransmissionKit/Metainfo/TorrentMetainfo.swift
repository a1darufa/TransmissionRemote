import CryptoKit
import Foundation

/// The parts of a `.torrent` file we show before adding it: name, files and info hash.
public struct TorrentMetainfo: Sendable, Equatable {
    /// Larger files are almost certainly not torrents; refuse them rather than parse.
    public static let maximumFileSize = 32 * 1024 * 1024

    public let name: String
    /// Files in the same order (and with the same indices) Transmission will use.
    public let files: [FileEntry]
    /// Lowercase hex SHA-1 of the info dictionary (the v1 info hash).
    public let infoHash: String?
    public let comment: String?
    public let isPrivate: Bool

    /// The size of the real files; padding only aligns pieces and isn't kept.
    public var totalSize: Int64 { files.filter { !$0.isPadding }.reduce(0) { $0 + $1.length } }

    public enum ParseError: Error, Equatable {
        case tooLarge
        case malformed(BencodeError)
        case missingInfoDictionary
    }

    public init(data: Data) throws(ParseError) {
        guard data.count <= Self.maximumFileSize else { throw .tooLarge }

        let document: BencodeParser.Document
        do {
            document = try BencodeParser.parse(data)
        } catch {
            throw .malformed(error)
        }
        guard let info = document.root["info"]?.dictionary else { throw .missingInfoDictionary }

        let name = Self.text(info, "name") ?? "Untitled"
        self.name = name
        self.files = Self.fileEntries(in: info, torrentName: name)
        self.comment = Self.text(document.root.dictionary ?? [:], "comment")
        self.isPrivate = info["private"]?.integer == 1
        self.infoHash = document.infoRange.map { range in
            Insecure.SHA1.hash(data: data.subdata(in: range))
                .map { String(format: "%02x", $0) }
                .joined()
        }
    }

    /// Prefers the `.utf-8` variant some clients write next to legacy-encoded keys.
    private static func text(_ dictionary: [String: BencodeValue], _ key: String) -> String? {
        (dictionary["\(key).utf-8"] ?? dictionary[key])?.string
    }

    /// The files in the daemon's order, so their numbers match `files-wanted` and friends.
    /// Like libtransmission, a `files` list wins over a single-file `length`.
    private static func fileEntries(in info: [String: BencodeValue], torrentName: String) -> [FileEntry] {
        guard let fileList = info["files"]?.list else {
            guard let length = info["length"]?.integer else { return [] }
            return [FileEntry(id: 0, path: torrentName, length: length)]
        }

        var entries: [FileEntry] = []
        for file in fileList {
            guard let fields = file.dictionary, let length = fields["length"]?.integer else { continue }
            let components = (fields["path.utf-8"] ?? fields["path"])?.list?.compactMap(\.string) ?? []
            let path = ([torrentName] + components).joined(separator: "/")
            // Padding files stay in the list: Transmission counts them, so skipping them would
            // shift every later file's number and unselect the wrong files.
            let isPadding = fields["attr"]?.string?.contains("p") == true || FileEntry.looksLikePadding(path)
            entries.append(FileEntry(id: entries.count, path: path, length: length, isPadding: isPadding))
        }
        return entries
    }
}
