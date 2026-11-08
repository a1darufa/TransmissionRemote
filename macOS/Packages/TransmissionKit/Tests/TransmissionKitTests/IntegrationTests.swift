import Foundation
import Testing
import TransmissionKit

/// End-to-end tests against a real daemon. Skipped unless these environment variables are set:
///
/// - `TRANSMISSION_IT_HOST`, `TRANSMISSION_IT_PORT`
/// - `TRANSMISSION_IT_USER`, `TRANSMISSION_IT_PASSWORD_FILE` (optional)
/// - `TRANSMISSION_IT_TORRENT`: a multi-file `.torrent` whose data already sits in
///   `TRANSMISSION_IT_DOWNLOAD_DIR` on the server, so verification completes instantly.
///
/// Use a throwaway daemon: the tests add, edit and remove torrents and change session settings
/// (restoring them afterwards). They never delete downloaded data.
@Suite(
    "Integration with a live Transmission daemon",
    .serialized,
    .enabled(if: IntegrationEnvironment.current != nil)
)
struct IntegrationTests {
    private let environment: IntegrationEnvironment

    init() throws {
        // The suite is only enabled when the environment variables are set.
        environment = try #require(IntegrationEnvironment.current)
    }

    @Test(arguments: [ProtocolPreference.automatic, .legacy])
    func fullTorrentLifecycle(preference: ProtocolPreference) async throws {
        let client = try environment.makeClient(preference: preference)
        let info = try await client.connect()
        let dialect = await client.dialect
        #expect(dialect == (preference == .legacy || !info.supportsJSONRPC ? .legacy : .jsonRPC))

        let torrentData = try Data(contentsOf: environment.torrentFile)
        let metainfo = try TorrentMetainfo(data: torrentData)
        try await removeExisting(hash: metainfo.infoHash, using: client)

        let id = try await addPausedSkippingLastFile(torrentData, metainfo: metainfo, using: client)
        try await checkFileSelection(of: id, metainfo: metainfo, using: client)
        try await checkLabelsAndOptions(of: id, using: client)

        // Verification finds the existing data.
        try await client.perform(.verify, ids: [id])
        let verified = try await waitUntil(client: client, id: id) { $0.percentDone >= 1 }
        #expect(verified.isComplete)

        try await checkTrackerReplacement(of: id, using: client)
        try await checkDetailTabs(of: id, using: client)
        try await checkQueueRenameAndMove(of: id, metainfo: metainfo, using: client)

        try await client.remove(ids: [id], deletingData: false)
        #expect(try await client.allTorrents().contains { $0.id == id } == false)
    }

    @Test(arguments: [ProtocolPreference.automatic, .legacy])
    func sessionSettingsRoundTrip(preference: ProtocolPreference) async throws {
        let client = try environment.makeClient(preference: preference)
        try await client.connect()

        let original = try await client.sessionSettings()
        #expect(original.downloadDir?.isEmpty == false)
        #expect(original.peerPort != nil)
        #expect(original.encryption != nil)

        var edited = original
        edited.speedLimitDown = 4321
        edited.speedLimitDownEnabled = true
        edited.seedRatioLimit = 3.25 // camelCase on legacy servers, snake_case on JSON-RPC
        edited.encryption = .allowed // "tolerated" on legacy servers
        try await client.updateSession(edited.changes(from: original))

        let saved = try await client.sessionSettings()
        #expect(saved.speedLimitDown == 4321)
        #expect(saved.speedLimitDownEnabled == true)
        #expect(saved.seedRatioLimit == 3.25)
        #expect(saved.encryption == .allowed)

        try await client.updateSession(original.changes(from: saved))
        let restored = try await client.sessionSettings()
        #expect(restored.speedLimitDown == original.speedLimitDown)
        #expect(restored.encryption == original.encryption)

        let stats = try await client.sessionStats()
        #expect(stats.torrentCount >= 0)
        let freeSpace = try await client.freeSpace(at: environment.downloadDir)
        #expect(freeSpace.sizeBytes > 0)
    }

    @Test func wrongPasswordIsReportedAsUnauthorized() async throws {
        let client = try TransmissionClient(
            endpoint: environment.endpoint,
            credentials: Credentials(username: environment.username ?? "nobody", password: "definitely-wrong")
        )
        await #expect(throws: TransmissionError.unauthorized) { try await client.connect() }
    }

    // MARK: - Lifecycle stages

    /// Adds the torrent paused with its last file skipped, then checks how it's listed.
    private func addPausedSkippingLastFile(
        _ torrentData: Data,
        metainfo: TorrentMetainfo,
        using client: TransmissionClient
    ) async throws -> Torrent.ID {
        var request = AddTorrentRequest.torrentFile(torrentData)
        request.downloadDir = environment.downloadDir
        request.paused = true
        request.filesUnwanted = [metainfo.files.count - 1]
        let added = try #require(try await client.add(request).torrentAdded)
        #expect(added.hashString.lowercased() == metainfo.infoHash)
        #expect(try await client.add(request).isDuplicate)

        // The list decodes, and file indices line up with our own .torrent parser.
        let listed = try #require(try await client.allTorrents().first { $0.id == added.id })
        #expect(listed.name == metainfo.name)
        #expect(listed.totalSize == metainfo.totalSize)
        #expect(listed.status == .stopped)
        #expect(listed.trackerHosts == ["127.0.0.1"])
        return added.id
    }

    private func checkFileSelection(
        of id: Torrent.ID,
        metainfo: TorrentMetainfo,
        using client: TransmissionClient
    ) async throws {
        let skippedIndex = metainfo.files.count - 1
        var files = try await details(of: id, TorrentDetails.fileFields, using: client).files
        #expect(files.map(\.path) == metainfo.files.map(\.path))
        #expect(files[skippedIndex].wanted == false)

        // Toggle the skipped file back on and raise a priority.
        try await client.apply(.wanted(true, files: [skippedIndex], in: id))
        try await client.apply(.priority(.high, files: [0], in: id))
        files = try await details(of: id, TorrentDetails.fileFields, using: client).files
        let allWanted = files.allSatisfy(\.wanted) // A key path inside #expect doesn't compile.
        #expect(allWanted)
        #expect(files[0].priority == .high)
    }

    private func checkLabelsAndOptions(of id: Torrent.ID, using client: TransmissionClient) async throws {
        var mutation = TorrentMutation(ids: [id])
        mutation.labels = ["integration", "test"]
        mutation.bandwidthPriority = .low
        try await client.apply(mutation)
        let relabeled = try #require(try await client.allTorrents().first { $0.id == id })
        #expect(Set(relabeled.labels) == ["integration", "test"])
        #expect(relabeled.bandwidthPriority == .low)

        // Per-torrent options round-trip.
        var options = TorrentMutation(ids: [id])
        options.downloadLimited = true
        options.downloadLimit = 321
        options.seedRatioMode = .single
        options.seedRatioLimit = 1.5
        try await client.apply(options)
        let optionDetails = try await details(of: id, TorrentDetails.optionFields, using: client)
        #expect(optionDetails.downloadLimited && optionDetails.downloadLimit == 321)
        #expect(optionDetails.seedRatioMode == .single && optionDetails.seedRatioLimit == 1.5)
    }

    /// Replaces the announce list. Transmission merges trackers on the same host into one tier,
    /// so the new tiers use different loopback addresses.
    private func checkTrackerReplacement(of id: Torrent.ID, using client: TransmissionClient) async throws {
        let original = try await details(of: id, TorrentDetails.trackerFields, using: client).trackerStats
        #expect(original.map(\.announce) == ["http://127.0.0.1:6969/announce"])
        try await client.replaceTrackers(
            of: id,
            current: original,
            with: [["http://127.0.0.2:6970/announce"], ["http://127.0.0.3:6971/announce"]]
        )
        let replaced = try await details(of: id, TorrentDetails.trackerFields, using: client).trackerStats
        #expect(replaced.map(\.announce) == ["http://127.0.0.2:6970/announce", "http://127.0.0.3:6971/announce"])
        #expect(replaced.map(\.tier) == [0, 1])
    }

    /// The remaining info pane tabs decode.
    private func checkDetailTabs(of id: Torrent.ID, using client: TransmissionClient) async throws {
        let general = try await details(of: id, TorrentDetails.generalFields, using: client)
        #expect(general.pieceCount > 0)
        #expect(general.piecesBitfield?.isEmpty == false)
        #expect(general.comment == "Local test torrent")
        _ = try await client.details(of: id, fields: TorrentDetails.peerFields)
        let magnet = try await client.torrents(TorrentDetails.self, fields: [.id, .magnetLink], ids: [id])
            .first?.magnetLink
        #expect(magnet?.hasPrefix("magnet:?xt=urn:btih:") == true)
    }

    /// Queue moves, start/stop, rename and location changes are accepted.
    private func checkQueueRenameAndMove(
        of id: Torrent.ID,
        metainfo: TorrentMetainfo,
        using client: TransmissionClient
    ) async throws {
        try await client.move(.top, ids: [id])
        try await client.move(.bottom, ids: [id])
        try await client.perform(.start, ids: [id])
        try await client.perform(.stop, ids: [id])
        try await client.perform(.reannounce, ids: [id])
        let newName = metainfo.name + " (renamed)"
        let renamed = try await client.rename(torrentID: id, path: metainfo.name, to: newName)
        #expect(renamed.name == newName)
        try await client.rename(torrentID: id, path: renamed.name, to: metainfo.name)
        try await client.setLocation(ids: [id], to: environment.downloadDir, movingData: false)
    }

    // MARK: - Helpers

    private func details(
        of id: Torrent.ID,
        _ fields: [TorrentField],
        using client: TransmissionClient
    ) async throws -> TorrentDetails {
        try #require(try await client.details(of: id, fields: fields))
    }

    private func removeExisting(hash: String?, using client: TransmissionClient) async throws {
        guard let hash else { return }
        let existing = try await client.allTorrents().filter { $0.hashString.lowercased() == hash }
        if !existing.isEmpty {
            try await client.remove(ids: existing.map(\.id), deletingData: false)
        }
    }

    private func waitUntil(
        client: TransmissionClient,
        id: Int,
        timeout: Duration = .seconds(20),
        condition: (Torrent) -> Bool
    ) async throws -> Torrent {
        let deadline = ContinuousClock.now + timeout
        while ContinuousClock.now < deadline {
            if let torrent = try await client.allTorrents().first(where: { $0.id == id }), condition(torrent) {
                return torrent
            }
            try await Task.sleep(for: .milliseconds(250))
        }
        throw TransmissionError.invalidResponse(details: "timed out waiting for torrent \(id)")
    }
}

struct IntegrationEnvironment {
    let endpoint: ServerEndpoint
    let username: String?
    let password: String?
    let torrentFile: URL
    let downloadDir: String

    static let current: Self? = {
        let variables = ProcessInfo.processInfo.environment
        guard let host = variables["TRANSMISSION_IT_HOST"],
              let torrent = variables["TRANSMISSION_IT_TORRENT"],
              let downloadDir = variables["TRANSMISSION_IT_DOWNLOAD_DIR"]
        else { return nil }

        let port = variables["TRANSMISSION_IT_PORT"].flatMap(Int.init) ?? ServerEndpoint.defaultPort
        let password = variables["TRANSMISSION_IT_PASSWORD_FILE"].flatMap {
            try? String(contentsOfFile: $0, encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines)
        }
        return Self(
            endpoint: ServerEndpoint(host: host, port: port),
            username: variables["TRANSMISSION_IT_USER"],
            password: password,
            torrentFile: URL(filePath: torrent),
            downloadDir: downloadDir
        )
    }()

    func makeClient(preference: ProtocolPreference) throws -> TransmissionClient {
        let credentials = username.map { Credentials(username: $0, password: password ?? "") }
        return try TransmissionClient(endpoint: endpoint, credentials: credentials, protocolPreference: preference)
    }
}
