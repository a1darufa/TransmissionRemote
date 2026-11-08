import Foundation
import Testing
@testable import TransmissionKit

@Suite("Decoding responses from both protocol generations")
struct DecodingTests {
    /// Shape of a Transmission 3.00 `torrent-get` response.
    private let legacyTorrentResponse = #"""
    {
      "result": "success",
      "tag": 4,
      "arguments": {
        "torrents": [{
          "id": 12, "name": "ubuntu-24.04.iso", "hashString": "abc123", "status": 4,
          "error": 0, "errorString": "", "totalSize": 6000000000, "sizeWhenDone": 6000000000,
          "leftUntilDone": 3000000000, "percentDone": 0.5, "metadataPercentComplete": 1,
          "rateDownload": 1250000, "rateUpload": 50000, "eta": 2400, "uploadRatio": 0.12,
          "addedDate": 1700000000, "doneDate": 0, "downloadDir": "/mnt/HD/HD_a2/Transmission",
          "labels": ["linux"], "queuePosition": 2, "peersSendingToUs": 14, "peersGettingFromUs": 3,
          "isFinished": false, "bandwidthPriority": 1,
          "trackers": [
            {"announce": "https://torrent.ubuntu.com/announce", "id": 0, "tier": 0},
            {"announce": "https://ipv6.torrent.ubuntu.com/announce", "id": 1, "tier": 1}
          ]
        }]
      }
    }
    """#

    /// Shape of a Transmission 4.1 `torrent_get` response.
    private let jsonRPCTorrentResponse = #"""
    {
      "jsonrpc": "2.0",
      "id": 4,
      "result": {
        "torrents": [{
          "id": 12, "name": "ubuntu-24.04.iso", "hash_string": "abc123", "status": 6,
          "error": 0, "error_string": "", "total_size": 6000000000, "size_when_done": 6000000000,
          "left_until_done": 0, "percent_done": 1.0, "rate_download": 0, "rate_upload": 50000,
          "eta": -1, "upload_ratio": 1.5, "added_date": 1700000000, "done_date": 1700003600,
          "download_dir": "/downloads", "labels": [], "queue_position": 0,
          "peers_sending_to_us": 0, "peers_getting_from_us": 3, "is_finished": false,
          "bandwidth_priority": 0, "trackers": []
        }]
      }
    }
    """#

    @Test func decodesLegacyTorrentList() throws {
        let decoder = RPCCoding.decoder(for: .legacy)
        let response = try decoder.decode(
            LegacyResponse<TorrentGetResponse<Torrent>>.self,
            from: Data(legacyTorrentResponse.utf8)
        )
        let torrent = try #require(response.arguments?.torrents.first)

        #expect(response.result == "success")
        #expect(torrent.id == 12)
        #expect(torrent.status == .downloading)
        #expect(torrent.rateDownload == 1_250_000)
        #expect(torrent.labels == ["linux"])
        #expect(torrent.doneDate == nil, "0 means never")
        #expect(torrent.bandwidthPriority == .high)
        #expect(torrent.trackerHosts == ["torrent.ubuntu.com", "ipv6.torrent.ubuntu.com"])
    }

    @Test func decodesJSONRPCTorrentList() throws {
        let decoder = RPCCoding.decoder(for: .jsonRPC)
        let response = try decoder.decode(
            JSONRPCResponse<TorrentGetResponse<Torrent>>.self,
            from: Data(jsonRPCTorrentResponse.utf8)
        )
        let torrent = try #require(response.result?.torrents.first)

        #expect(torrent.hashString == "abc123")
        #expect(torrent.status == .seeding)
        #expect(torrent.isComplete)
        #expect(torrent.doneDate == Date(timeIntervalSince1970: 1_700_003_600))
    }

    @Test func decodesJSONRPCError() throws {
        let body = #"""
            {"jsonrpc": "2.0", "id": 1,
             "error": {"code": 7, "message": "backend error", "data": {"error_string": "Couldn't test port"}}}
            """#
        let response = try RPCCoding.decoder(for: .jsonRPC).decode(JSONRPCResponse<Empty>.self, from: Data(body.utf8))
        #expect(response.error?.data?.errorString == "Couldn't test port")
    }

    @Test func fileStatsAcceptLegacyIntegersAndBooleans() throws {
        let legacy = #"""
            {"id": 1,
             "files": [{"name": "a/b.mkv", "length": 10, "bytesCompleted": 5},
                       {"name": "a/c.txt", "length": 2, "bytesCompleted": 0}],
             "fileStats": [{"bytesCompleted": 5, "wanted": 1, "priority": 1},
                           {"bytesCompleted": 0, "wanted": 0, "priority": -1}]}
            """#
        let modern = #"""
            {"id": 1,
             "files": [{"name": "a/b.mkv", "length": 10, "bytes_completed": 5},
                       {"name": "a/c.txt", "length": 2, "bytes_completed": 0}],
             "file_stats": [{"bytes_completed": 5, "wanted": true, "priority": 1},
                            {"bytes_completed": 0, "wanted": false, "priority": -1}]}
            """#

        for (dialect, body) in [(RPCDialect.legacy, legacy), (.jsonRPC, modern)] {
            let details = try RPCCoding.decoder(for: dialect).decode(TorrentDetails.self, from: Data(body.utf8))
            #expect(details.files.map(\.wanted) == [true, false])
            #expect(details.files.map(\.priority) == [.high, .low])
            #expect(details.files.map(\.path) == ["a/b.mkv", "a/c.txt"])
        }
    }

    @Test func peerUTPFlagDecodesInBothDialects() throws {
        let legacy = #"{"address":"1.2.3.4","port":51413,"isUTP":true,"clientName":"qBittorrent"}"#
        let modern = #"{"address":"1.2.3.4","port":51413,"is_utp":true,"client_name":"qBittorrent"}"#
        #expect(try RPCCoding.decoder(for: .legacy).decode(Peer.self, from: Data(legacy.utf8)).isUtp)
        #expect(try RPCCoding.decoder(for: .jsonRPC).decode(Peer.self, from: Data(modern.utf8)).isUtp)
    }

    @Test func sessionToleratesFloatSpeedLimitsAndOldEncryptionName() throws {
        // Transmission 4.1.0 briefly reported speed limits as doubles; 3.00 calls "allowed" "tolerated".
        let body = #"""
            {"speed-limit-down": 250.0, "speed-limit-down-enabled": true, "encryption": "tolerated",
             "seedRatioLimit": 2, "download-dir": "/data"}
            """#
        let settings = try RPCCoding.decoder(for: .legacy).decode(SessionSettings.self, from: Data(body.utf8))
        #expect(settings.speedLimitDown == 250)
        #expect(settings.speedLimitDownEnabled == true)
        #expect(settings.encryption == .allowed)
        #expect(settings.seedRatioLimit == 2.0)
        #expect(settings.downloadDir == "/data")
        #expect(settings.peerPort == nil)
    }

    @Test func sessionDecodesDiskCacheSizeInBothDialects() throws {
        let legacy = #"{"cache-size-mb": 4}"#
        let modern = #"{"cache_size_mb": 16}"#
        let legacySettings = try RPCCoding.decoder(for: .legacy).decode(SessionSettings.self, from: Data(legacy.utf8))
        let modernSettings = try RPCCoding.decoder(for: .jsonRPC).decode(SessionSettings.self, from: Data(modern.utf8))
        #expect(legacySettings.cacheSizeMb == 4)
        #expect(modernSettings.cacheSizeMb == 16)
    }

    @Test func serverInfoDetectsJSONRPCSupport() {
        #expect(ServerInfo(version: "4.1.1", rpcVersion: 19, rpcVersionSemver: "6.0.1").supportsJSONRPC)
        #expect(!ServerInfo(version: "4.0.6", rpcVersion: 17, rpcVersionSemver: "5.3.0").supportsJSONRPC)
        #expect(!ServerInfo(version: "3.00", rpcVersion: 16, rpcVersionSemver: nil).supportsJSONRPC)
    }
}

@Suite("Session settings diff")
struct SessionDiffTests {
    @Test func onlyChangedWritableKeysAreSent() throws {
        var original = SessionSettings()
        original.speedLimitDown = 100
        original.speedLimitDownEnabled = false
        original.downloadDir = "/data"
        original.version = "4.1.1"

        var edited = original
        edited.speedLimitDownEnabled = true
        edited.version = "tampered"

        let patch = try edited.changes(from: original)
        var expected = SessionSettings()
        expected.speedLimitDownEnabled = true
        #expect(patch == expected)
    }

    @Test func diskCacheSizeIsWritable() throws {
        var original = SessionSettings()
        original.cacheSizeMb = 4
        var edited = original
        edited.cacheSizeMb = 16

        var expected = SessionSettings()
        expected.cacheSizeMb = 16
        #expect(try edited.changes(from: original) == expected)
    }

    @Test func noChangesProducesEmptyPatch() throws {
        var settings = SessionSettings()
        settings.peerPort = 51413
        #expect(try settings.changes(from: settings).isEmpty)
    }
}
