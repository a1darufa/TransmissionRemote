import Foundation
import Testing
@testable import TransmissionKit

@Suite("Key mapping between Swift names and both wire dialects")
struct KeyMappingTests {
    @Test(arguments: [
        ("rateDownload", "rate_download"),
        ("filesWanted", "files_wanted"),
        ("rpcVersionSemver", "rpc_version_semver"),
        ("ids", "ids")
    ])
    func jsonRPCUsesSnakeCase(camel: String, expected: String) {
        #expect(RPCDialect.jsonRPC.wireName(for: camel, scope: .torrentArguments) == expected)
    }

    @Test func legacyTorrentFieldsAreMostlyCamelCase() {
        let legacy = RPCDialect.legacy
        #expect(legacy.wireName(for: "rateDownload", scope: .torrentFields) == "rateDownload")
        #expect(legacy.wireName(for: "fileCount", scope: .torrentFields) == "file-count")
        #expect(legacy.wireName(for: "peerLimit", scope: .torrentFields) == "peer-limit")
        #expect(legacy.wireName(for: "primaryMimeType", scope: .torrentFields) == "primary-mime-type")
    }

    @Test func legacyTorrentArgumentsUseKebabCaseExceptions() {
        let legacy = RPCDialect.legacy
        #expect(legacy.wireName(for: "filesWanted", scope: .torrentArguments) == "files-wanted")
        #expect(legacy.wireName(for: "downloadDir", scope: .torrentArguments) == "download-dir")
        #expect(legacy.wireName(for: "deleteLocalData", scope: .torrentArguments) == "delete-local-data")
        #expect(legacy.wireName(for: "bandwidthPriority", scope: .torrentArguments) == "bandwidthPriority")
        #expect(legacy.wireName(for: "seedRatioLimit", scope: .torrentArguments) == "seedRatioLimit")
    }

    @Test func legacySessionKeysAreKebabCaseExceptRatio() {
        let legacy = RPCDialect.legacy
        #expect(legacy.wireName(for: "speedLimitDown", scope: .session) == "speed-limit-down")
        #expect(legacy.wireName(for: "altSpeedTimeBegin", scope: .session) == "alt-speed-time-begin")
        #expect(legacy.wireName(for: "seedRatioLimit", scope: .session) == "seedRatioLimit")
        #expect(legacy.wireName(for: "seedRatioLimited", scope: .session) == "seedRatioLimited")
        #expect(legacy.wireName(for: "cacheSizeMb", scope: .session) == "cache-size-mb")
        #expect(RPCDialect.jsonRPC.wireName(for: "cacheSizeMb", scope: .session) == "cache_size_mb")
    }

    @Test(arguments: [
        ("rate_download", "rateDownload"),
        ("rate-download", "rateDownload"),
        ("rateDownload", "rateDownload"),
        ("isUTP", "isUtp"),
        ("is_utp", "isUtp"),
        ("torrent-added", "torrentAdded"),
        ("total_size", "totalSize")
    ])
    func serverKeysNormalizeToCamelCase(wire: String, expected: String) {
        #expect(KeyCase.camelCase(wire) == expected)
    }

    @Test func methodNamesFollowDialect() {
        #expect(RPCDialect.jsonRPC.wireName(for: .torrentSetLocation) == "torrent_set_location")
        #expect(RPCDialect.legacy.wireName(for: .torrentSetLocation) == "torrent-set-location")
        #expect(RPCDialect.legacy.wireName(for: .queueMoveTop) == "queue-move-top")
    }
}

@Suite("Request envelopes")
struct EnvelopeTests {
    private func encodedObject(_ dialect: RPCDialect, _ mutation: TorrentMutation) throws -> [String: Any] {
        let envelope = RequestEnvelope(dialect: dialect, method: .torrentSet, params: mutation, tag: 7)
        let data = try RPCCoding.encoder(for: dialect, scope: .torrentArguments).encode(envelope)
        return try #require(JSONSerialization.jsonObject(with: data) as? [String: Any])
    }

    private var sampleMutation: TorrentMutation {
        var mutation = TorrentMutation(ids: [3])
        mutation.filesWanted = [0, 2]
        mutation.bandwidthPriority = .high
        return mutation
    }

    @Test func jsonRPCEnvelope() throws {
        let object = try encodedObject(.jsonRPC, sampleMutation)
        #expect(object["jsonrpc"] as? String == "2.0")
        #expect(object["method"] as? String == "torrent_set")
        #expect(object["id"] as? Int == 7)
        let params = try #require(object["params"] as? [String: Any])
        #expect(params["files_wanted"] as? [Int] == [0, 2])
        #expect(params["bandwidth_priority"] as? Int == 1)
        #expect(params["ids"] as? [Int] == [3])
        #expect(params.count == 3, "nil properties must not be sent")
    }

    @Test func legacyEnvelope() throws {
        let object = try encodedObject(.legacy, sampleMutation)
        #expect(object["jsonrpc"] == nil)
        #expect(object["method"] as? String == "torrent-set")
        #expect(object["tag"] as? Int == 7)
        let arguments = try #require(object["arguments"] as? [String: Any])
        #expect(arguments["files-wanted"] as? [Int] == [0, 2])
        #expect(arguments["bandwidthPriority"] as? Int == 1)
    }

    @Test func encryptionUsesLegacySpellingOnOldServers() throws {
        var settings = SessionSettings()
        settings.encryption = .allowed

        let legacy = try RPCCoding.encoder(for: .legacy, scope: .session).encode(
            RequestEnvelope(dialect: .legacy, method: .sessionSet, params: settings, tag: 1)
        )
        let modern = try RPCCoding.encoder(for: .jsonRPC, scope: .session).encode(
            RequestEnvelope(dialect: .jsonRPC, method: .sessionSet, params: settings, tag: 1)
        )
        #expect(try #require(String(validating: legacy, as: UTF8.self)).contains(#""encryption":"tolerated""#))
        #expect(try #require(String(validating: modern, as: UTF8.self)).contains(#""encryption":"allowed""#))
    }
}
