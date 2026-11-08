import AppKit
import Foundation
import Observation
import TransmissionKit

enum DetailTab: String, CaseIterable, Identifiable {
    case general, trackers, peers, files, speed

    var id: Self { self }

    var title: LocalizedStringResource {
        switch self {
        case .general: "General"
        case .trackers: "Trackers"
        case .peers: "Peers"
        case .files: "Files"
        case .speed: "Speed"
        }
    }

    /// Only the visible tab's data is fetched, so a torrent with thousands of files
    /// doesn't slow down every refresh.
    var fields: [TorrentField] {
        switch self {
        case .general: TorrentDetails.generalFields
        case .trackers: TorrentDetails.trackerFields
        case .peers: TorrentDetails.peerFields
        case .files: TorrentDetails.fileFields
        case .speed: []
        }
    }
}

/// Why the connection stopped, and what the user can do about it.
struct ConnectionFailure: Equatable {
    enum Remedy: Equatable {
        case none
        case trustCertificate(fingerprint: String)
        case allowUnencryptedConnection
        case editServer
    }

    let message: String
    let remedy: Remedy
}

/// The server couldn't be reached; the connection keeps trying on its own.
struct ReconnectAttempt: Equatable {
    /// Why the last attempt failed.
    let message: String
    /// When the next attempt starts, or nil while an attempt is in flight.
    let nextAttempt: Date?
}

/// The live connection to one Transmission daemon: polls it, holds what it reported,
/// and runs the user's actions against it.
@Observable
final class ServerConnection {
    enum State: Equatable {
        case disconnected
        case connecting
        case reconnecting(ReconnectAttempt)
        case connected
        case failed(ConnectionFailure)
    }

    private(set) var state: State = .disconnected
    private(set) var profile: ServerProfile?
    private(set) var serverInfo: ServerInfo?
    private(set) var dialect: RPCDialect?
    private(set) var torrents: [Torrent] = []
    private(set) var session: SessionSettings?
    private(set) var stats: SessionStats?
    private(set) var freeSpace: FreeSpace?
    private(set) var details: TorrentDetails?
    private(set) var speedHistory = SpeedHistory()
    /// A temporary problem while polling (for example a network blip). Data stays visible.
    private(set) var pollingProblem: String?
    /// When the torrent list last refreshed successfully, to show how stale it is during a problem.
    private(set) var lastUpdated: Date?
    /// The last failed user action, shown as an alert.
    var actionError: String?

    var selection: Set<Torrent.ID> = [] {
        didSet { if oldValue != selection { reloadDetails() } }
    }

    var detailTab: DetailTab = .general {
        didSet { if oldValue != detailTab { reloadDetails() } }
    }

    private var client: TransmissionClient?
    private var pollTask: Task<Void, Never>?
    /// The current wait between polls or reconnect attempts; cancelling it retries at once.
    private var pendingWait: Task<Void, Never>?
    private let connectivity = ConnectivityMonitor()
    private var isRefreshing = false
    private var isRefreshRequested = false
    private var refreshCount = 0
    /// The latest tracker stats per torrent, attached to each list refresh until the next fetch.
    private var trackerSummaries: [Torrent.ID: TrackerSummary] = [:]
    /// Whether the next refresh may fetch only recently changed torrents.
    /// Used by ServerConnection+TorrentList.swift.
    var listPlan = TorrentListRefreshPlan()
    private let notifier = CompletionNotifier()
    private static let log = DiagnosticLog(category: "Connection")

    init() {
        connectivity.start { [weak self] in self?.retryNow() }
    }

    var isConnected: Bool { state == .connected }

    var selectedTorrents: [Torrent] {
        torrents.filter { selection.contains($0.id) }
    }

    var singleSelectedTorrent: Torrent? {
        guard selection.count == 1, let id = selection.first else { return nil }
        return torrents.first { $0.id == id }
    }

    // MARK: - Connecting

    func connect(to profile: ServerProfile, password: String?) {
        disconnect()
        self.profile = profile

        guard !profile.isBlockedByTransportPolicy else {
            state = .failed(ConnectionFailure(
                message: String(localized: """
                    This server uses plain HTTP over the internet, so your password would be sent unencrypted. \
                    Use HTTPS, a VPN such as Tailscale, or allow the unencrypted connection for this server.
                    """),
                remedy: .allowUnencryptedConnection
            ))
            return
        }

        let credentials = profile.username.isEmpty
            ? nil
            : Credentials(username: profile.username, password: password ?? "")
        do {
            let client = try TransmissionClient(
                endpoint: profile.endpoint,
                credentials: credentials,
                pinnedCertificateFingerprint: profile.pinnedCertificateFingerprint,
                protocolPreference: profile.protocolPreference,
                proxy: profile.proxy.configuration(password: Keychain.proxyPassword(for: profile.id)),
                clientIdentity: try ClientCertificates.identity(for: profile)
            )
            self.client = client
            state = .connecting
            pollTask = Task { await connectAndPoll(client) }
        } catch {
            fail(with: error)
        }
    }

    func disconnect() {
        pollTask?.cancel()
        pollTask = nil
        client = nil
        notifier.reset()
        state = .disconnected
        serverInfo = nil
        dialect = nil
        torrents = []
        listPlan = TorrentListRefreshPlan()
        session = nil
        stats = nil
        freeSpace = nil
        trackerSummaries = [:]
        details = nil
        selection = []
        speedHistory = SpeedHistory()
        pollingProblem = nil
        lastUpdated = nil
    }

    /// Skips the rest of the current wait: for "Try Now", and when the Mac wakes or the
    /// network changes, since the data on screen may be stale.
    func retryNow() {
        pendingWait?.cancel()
    }

    private func connectAndPoll(_ client: TransmissionClient) async {
        guard await establishConnection(using: client) else { return }

        notifier.requestAuthorizationIfEnabled()
        while !Task.isCancelled, isCurrent(client) {
            await refresh()
            await pause(for: AppSettings.currentRefreshInterval)
        }
    }

    /// Connects, and keeps trying with growing delays while the server can't be reached.
    /// Returns false when the connection failed for good or was replaced.
    private func establishConnection(using client: TransmissionClient) async -> Bool {
        var failures = 0
        while !Task.isCancelled, isCurrent(client) {
            do {
                let info = try await client.connect()
                guard isCurrent(client) else { return false }
                serverInfo = info
                dialect = await client.dialect
                state = .connected
                return true
            } catch {
                guard isCurrent(client) else { return false }
                guard error.isTransient else {
                    fail(with: error)
                    return false
                }
                failures += 1
                let delay = RetryBackoff.delay(afterFailures: failures)
                let message = error.localizedDescription
                Self.log.info("Server unreachable (\(error)); retrying in \(delay)")
                let nextAttempt = Date.now.addingTimeInterval(delay / .seconds(1))
                state = .reconnecting(ReconnectAttempt(message: message, nextAttempt: nextAttempt))
                await pause(for: delay)
                state = .reconnecting(ReconnectAttempt(message: message, nextAttempt: nil))
            }
        }
        return false
    }

    /// Sleeps, unless `retryNow()` or cancelling the poll task ends the wait early.
    private func pause(for duration: Duration) async {
        let wait = Task { _ = try? await Task.sleep(for: duration) }
        pendingWait = wait
        await withTaskCancellationHandler {
            await wait.value
        } onCancel: {
            wait.cancel()
        }
        if pendingWait == wait { pendingWait = nil }
    }

    /// Guards against results from a connection that was replaced while a request was in flight.
    private func isCurrent(_ client: TransmissionClient) -> Bool {
        self.client === client
    }

    private func fail(with error: any Error) {
        pollTask?.cancel()
        pollTask = nil
        client = nil
        state = .failed(ConnectionFailure(error))
        Self.log.error("Connection failed: \(error) – \(error.localizedDescription)")
    }

    // MARK: - Refreshing

    /// Fetches everything the window shows. Overlapping calls are coalesced into one more pass.
    func refresh() async {
        guard isConnected, let client else { return }
        if isRefreshing {
            isRefreshRequested = true
            return
        }
        isRefreshing = true
        defer { isRefreshing = false }

        repeat {
            isRefreshRequested = false
            await refreshOnce(using: client)
        } while isRefreshRequested && isCurrent(client)
    }

    private func refreshOnce(using client: TransmissionClient) async {
        do {
            async let torrentList = fetchTorrentList(using: client)
            async let sessionStats = client.sessionStats()
            async let sessionSettings = client.sessionSettings()
            let (newTorrents, newStats, newSession) = try await (torrentList, sessionStats, sessionSettings)
            guard isCurrent(client) else { return }

            notifier.notifyAboutCompletions(previous: torrents, current: newTorrents)
            torrents = withTrackerSummaries(newTorrents)
            stats = newStats
            session = newSession
            speedHistory.append(download: newStats.downloadSpeed, upload: newStats.uploadSpeed)
            selection.formIntersection(Set(newTorrents.map(\.id)))
            pollingProblem = nil
            lastUpdated = .now

            try await loadDetails(using: client)
            if refreshCount.isMultiple(of: 6) {
                await loadFreeSpace(using: client)
                await loadTrackerSummaries(using: client)
            }
            refreshCount += 1
        } catch {
            guard isCurrent(client) else { return }
            if let error = error as? TransmissionError, error.requiresUserAction {
                fail(with: error)
            } else {
                pollingProblem = error.localizedDescription
            }
        }
    }

    private func loadDetails(using client: TransmissionClient) async throws {
        guard let torrent = singleSelectedTorrent else {
            details = nil
            return
        }
        let fields = detailTab.fields
        guard !fields.isEmpty else { return }

        let fetched = try await client.details(of: torrent.id, fields: fields)
        guard isCurrent(client), singleSelectedTorrent?.id == torrent.id else { return }
        details = fetched
    }

    private func loadFreeSpace(using client: TransmissionClient) async {
        guard let path = session?.downloadDir, !path.isEmpty else { return }
        freeSpace = try? await client.freeSpace(at: path)
    }

    private func reloadDetails() {
        guard let client else { return }
        details = nil
        Task {
            try? await loadDetails(using: client)
        }
    }

    func setAlternativeSpeedEnabled(_ enabled: Bool) {
        var changes = SessionSettings()
        changes.altSpeedEnabled = enabled
        let patch = changes
        session?.altSpeedEnabled = enabled // Optimistic, so the toggle doesn't flicker.
        run { try await $0.updateSession(patch) }
    }

    // MARK: - Helpers for ServerConnection+Actions.swift

    /// Runs an action against the daemon, reports failures, then refreshes so the UI catches up.
    func run(_ operation: @escaping @Sendable (TransmissionClient) async throws -> Void) {
        guard let client else { return }
        Task {
            do {
                try await operation(client)
            } catch {
                Self.log.warning("An action failed: \(error) – \(error.localizedDescription)")
                actionError = error.localizedDescription
            }
            await refresh()
        }
    }

    /// The live client, or a "Not connected" error for requests made while disconnected.
    func requireClient() throws -> TransmissionClient {
        guard let client else {
            throw TransmissionError.network(code: -1, description: String(localized: "Not connected."))
        }
        return client
    }
}

// MARK: - Tracker summaries

private extension ServerConnection {
    /// Tracker stats are bulky and only change when a tracker is contacted, so they're fetched
    /// on the same slower cadence as free space.
    func loadTrackerSummaries(using client: TransmissionClient) async {
        guard let summaries = try? await client.trackerSummaries(), isCurrent(client) else { return }
        trackerSummaries = Dictionary(summaries.map { ($0.torrentID, $0) }) { first, _ in first }
        torrents = withTrackerSummaries(torrents)
    }

    func withTrackerSummaries(_ list: [Torrent]) -> [Torrent] {
        list.map { torrent in
            var torrent = torrent
            torrent.trackerSummary = trackerSummaries[torrent.id]
            return torrent
        }
    }
}

extension ConnectionFailure {
    init(_ error: any Error) {
        let message = error.localizedDescription
        switch error as? TransmissionError {
        case .untrustedCertificate(let fingerprint):
            self.init(message: message, remedy: .trustCertificate(fingerprint: fingerprint))
        case .unauthorized, .invalidEndpoint, .hostNotAllowed, .httpsHandshakeFailed:
            self.init(message: message, remedy: .editServer)
        default:
            self.init(message: message, remedy: .none)
        }
    }
}
