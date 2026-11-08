import CryptoKit
import Foundation
import Testing
@testable import TransmissionKit

@Suite("Bencode and .torrent parsing")
struct MetainfoTests {
    private let infoDictionary =
        "d5:filesl"
        + "d6:lengthi1000e4:pathl6:Season7:e01.mkveed4:attr1:p6:lengthi24e4:pathl4:.pad2:24ee"
        + "d6:lengthi20e4:pathl8:info.nfoee"
        + "e4:name4:Show12:piece lengthi16384e6:pieces0:7:privatei1ee"

    private var multiFileTorrent: Data {
        Data(("d8:announce23:http://tracker/announce7:comment5:hello4:info" + infoDictionary + "e").utf8)
    }

    @Test func parsesMultiFileTorrentAndKeepsPaddingFilesNumbered() throws {
        let metainfo = try TorrentMetainfo(data: multiFileTorrent)
        #expect(metainfo.name == "Show")
        #expect(metainfo.files.map(\.path) == ["Show/Season/e01.mkv", "Show/.pad/24", "Show/info.nfo"])
        // Transmission 3.00–4.1 ignore BEP 47's attr flag and number padding files like any other.
        #expect(metainfo.files.map(\.id) == [0, 1, 2])
        #expect(metainfo.files.map(\.isPadding) == [false, true, false])
        #expect(metainfo.totalSize == 1020)
        #expect(metainfo.comment == "hello")
        #expect(metainfo.isPrivate)
    }

    @Test func infoHashCoversExactInfoBytes() throws {
        let metainfo = try TorrentMetainfo(data: multiFileTorrent)
        let expected = Insecure.SHA1.hash(data: Data(infoDictionary.utf8)).map { String(format: "%02x", $0) }.joined()
        #expect(metainfo.infoHash == expected)
    }

    @Test func recognizesPaddingFilesByNameWhenTheDaemonListsThem() {
        #expect(FileEntry(id: 0, path: "Show/.pad/16384", length: 16_384).isPadding)
        #expect(FileEntry(id: 1, path: "Show/_____padding_file_0_if you see this", length: 9).isPadding)
        #expect(!FileEntry(id: 2, path: "Show/.pad", length: 9).isPadding)
        #expect(!FileEntry(id: 3, path: "Show/notes.pad", length: 9).isPadding)
    }

    @Test func prefersTheFileListOverASingleLength() throws {
        let data = Data("d4:infod5:filesld6:lengthi5e4:pathl1:aeee6:lengthi9e4:name1:Xee".utf8)
        let metainfo = try TorrentMetainfo(data: data)
        #expect(metainfo.files.map(\.path) == ["X/a"])
    }

    @Test func parsesSingleFileTorrent() throws {
        let data = Data("d4:infod6:lengthi42e4:name8:file.isoee".utf8)
        let metainfo = try TorrentMetainfo(data: data)
        #expect(metainfo.files == [FileEntry(id: 0, path: "file.iso", length: 42)])
    }

    @Test(arguments: [
        "d4:infod",                       // truncated
        "d4:info99999:x",                 // length beyond buffer
        "i12",                            // unterminated integer
        "d4:infod4:name3:abcee trailing", // trailing garbage
        "x",                              // unknown marker
        "i-e"                            // sign without digits
    ])
    func rejectsMalformedInput(input: String) {
        #expect(throws: (any Error).self) { try TorrentMetainfo(data: Data(input.utf8)) }
    }

    @Test func rejectsExcessiveNesting() {
        let nested = String(repeating: "l", count: 200) + String(repeating: "e", count: 200)
        #expect(throws: BencodeError.nestingTooDeep) { try BencodeParser.parse(Data(nested.utf8)) }
    }

    @Test func requiresInfoDictionary() {
        #expect(throws: TorrentMetainfo.ParseError.missingInfoDictionary) {
            try TorrentMetainfo(data: Data("d8:announce3:urle".utf8))
        }
    }
}

@Suite("Magnet links")
struct MagnetLinkTests {
    @Test func parsesMagnet() throws {
        let link = try #require(MagnetLink(
            "magnet:?xt=urn:btih:C12FE1C06BBA254A9DC9F519B335AA7C1367A88A&dn=Some%20Show&tr=udp%3A%2F%2Ftracker%3A80"
        ))
        #expect(link.infoHash == "c12fe1c06bba254a9dc9f519b335aa7c1367a88a")
        #expect(link.displayName == "Some Show")
        #expect(link.trackers == ["udp://tracker:80"])
    }

    @Test(arguments: ["https://example.com", "magnet:?dn=nohash", "not a link", ""])
    func rejectsNonMagnets(text: String) {
        #expect(MagnetLink(text) == nil)
    }

    @Test func recognizesDownloadURLs() {
        #expect("https://example.com/file.torrent".isTorrentDownloadURL)
        #expect(!"ftp://example.com/file.torrent".isTorrentDownloadURL)
        #expect(!"magnet:?xt=urn:btih:abc".isTorrentDownloadURL)
    }
}
