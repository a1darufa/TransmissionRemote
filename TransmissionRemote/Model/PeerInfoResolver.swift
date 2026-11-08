import Foundation
import Observation
import os
import TransmissionKit

/// Host names and countries for the Peers tab, like Transmission Remote GUI's "Resolve host
/// name" and "Resolve country". Host names come from reverse DNS; countries from DB-IP's free
/// database, downloaded only after the user asks for it and refreshed monthly after that.
@Observable
final class PeerInfoResolver {
    enum DatabaseState: Equatable {
        case notDownloaded
        case downloading
        case loading
        case ready
        case failed(String)
    }

    private(set) var databaseState: DatabaseState = .notDownloaded
    /// The month of the downloaded database, such as "2026-11".
    private(set) var databaseMonth = UserDefaults.standard.string(forKey: AppSettings.countryDatabaseMonth)
    private(set) var hostNames: [String: String] = [:]

    private var database: CountryDatabase?
    private var addressesWithoutName: Set<String> = []
    private var queuedLookups: [String] = []
    private var lookupsInFlight: Set<String> = []
    private var lastUpdateAttempt: Date?

    private static let maximumConcurrentLookups = 4
    /// A long session can see many thousands of peers; forget names past this many.
    private static let maximumCachedNames = 5_000
    private static let logger = Logger(subsystem: "TransmissionRemote", category: "PeerInfo")

    // MARK: Countries

    func countryCode(for address: String) -> String? {
        database?.countryCode(for: address)
    }

    /// Loads the downloaded database, and refreshes it in the background once a newer month is out.
    func prepareCountries() async {
        if database == nil, databaseState != .loading, let file = Self.databaseFile,
           FileManager.default.fileExists(atPath: file.path(percentEncoded: false)) {
            await load(from: file)
        }
        if databaseState == .ready, isDatabaseOutdated, canRetryUpdate {
            await downloadDatabase()
        }
    }

    func downloadDatabase() async {
        guard databaseState != .downloading else { return }
        let hadDatabase = database != nil
        databaseState = .downloading
        lastUpdateAttempt = .now
        do {
            let (month, compressed) = try await Self.fetchNewestDatabase()
            let parsed = try await Self.parse(compressed)
            guard let file = Self.databaseFile else { throw CocoaError(.fileNoSuchFile) }
            try FileManager.default.createDirectory(
                at: file.deletingLastPathComponent(), withIntermediateDirectories: true
            )
            try compressed.write(to: file, options: .atomic)
            database = parsed
            databaseMonth = month
            UserDefaults.standard.set(month, forKey: AppSettings.countryDatabaseMonth)
            databaseState = .ready
        } catch {
            Self.logger.error("Country database download failed: \(error.localizedDescription, privacy: .public)")
            // A failed monthly refresh keeps the database that already works.
            databaseState = hadDatabase ? .ready : .failed(error.localizedDescription)
        }
    }

    private func load(from file: URL) async {
        databaseState = .loading
        do {
            let compressed = try Data(contentsOf: file)
            database = try await Self.parse(compressed)
            databaseState = .ready
        } catch {
            databaseState = .failed(error.localizedDescription)
        }
    }

    private var isDatabaseOutdated: Bool {
        guard let databaseMonth else { return true }
        return databaseMonth < Self.monthString(for: .now)
    }

    /// One attempt a day, so a failing download isn't retried on every refresh.
    private var canRetryUpdate: Bool {
        lastUpdateAttempt.map { Date.now.timeIntervalSince($0) > 24 * 60 * 60 } ?? true
    }

    // MARK: Host names

    /// Starts reverse DNS lookups for addresses not looked up yet. Views read `hostNames`.
    func resolveHostNames(for addresses: [String]) {
        if hostNames.count > Self.maximumCachedNames {
            hostNames = [:]
            addressesWithoutName = []
        }
        for address in addresses where hostNames[address] == nil
            && !addressesWithoutName.contains(address)
            && !lookupsInFlight.contains(address)
            && !queuedLookups.contains(address) {
            queuedLookups.append(address)
        }
        startQueuedLookups()
    }

    /// At most a few lookups run at once: each one can block a thread for seconds.
    private func startQueuedLookups() {
        while lookupsInFlight.count < Self.maximumConcurrentLookups, !queuedLookups.isEmpty {
            let address = queuedLookups.removeFirst()
            lookupsInFlight.insert(address)
            Task {
                let name = await ReverseDNS.hostName(for: address)
                lookupsInFlight.remove(address)
                if let name { hostNames[address] = name } else { addressesWithoutName.insert(address) }
                startQueuedLookups()
            }
        }
    }

    // MARK: Database files

    private static var databaseFile: URL? {
        try? FileManager.default
            .url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
            .appending(components: "GeoIP", "dbip-country-lite.csv.gz")
    }

    private static func monthString(for date: Date) -> String {
        let components = Calendar(identifier: .gregorian).dateComponents(in: .gmt, from: date)
        return String(format: "%04d-%02d", components.year ?? 0, components.month ?? 0)
    }

    /// DB-IP publishes each month's file early in that month, so fall back to last month's.
    private static func fetchNewestDatabase() async throws -> (month: String, data: Data) {
        let thisMonth = monthString(for: .now)
        let monthAgo = Calendar(identifier: .gregorian).date(byAdding: .month, value: -1, to: .now) ?? .now
        let lastMonth = monthString(for: monthAgo)
        var lastError: any Error = URLError(.fileDoesNotExist)
        for month in [thisMonth, lastMonth] {
            let address = "https://download.db-ip.com/free/dbip-country-lite-\(month).csv.gz"
            guard let url = URL(string: address) else { continue }
            do {
                let (data, response) = try await URLSession.shared.data(from: url)
                if (response as? HTTPURLResponse)?.statusCode == 200 { return (month, data) }
                lastError = URLError(.fileDoesNotExist)
            } catch {
                lastError = error
            }
        }
        throw lastError
    }

    /// Unpacking and parsing takes about a second, so it runs off the main thread.
    private static func parse(_ compressed: Data) async throws -> CountryDatabase {
        let database = try await Task.detached(priority: .utility) {
            CountryDatabase(csv: try Gzip.decompress(compressed))
        }.value
        guard !database.isEmpty else { throw CocoaError(.fileReadCorruptFile) }
        return database
    }
}

/// Reverse DNS through `getnameinfo`, which blocks, so it runs on a background queue.
nonisolated enum ReverseDNS {
    private static let queue = DispatchQueue(label: "ReverseDNS", qos: .utility, attributes: .concurrent)

    static func hostName(for address: String) async -> String? {
        await withCheckedContinuation { continuation in
            queue.async { continuation.resume(returning: lookUp(address)) }
        }
    }

    private static func lookUp(_ address: String) -> String? {
        var hints = addrinfo()
        hints.ai_flags = AI_NUMERICHOST
        var result: UnsafeMutablePointer<addrinfo>?
        guard getaddrinfo(address, nil, &hints, &result) == 0, let info = result else { return nil }
        defer { freeaddrinfo(result) }

        var host = [CChar](repeating: 0, count: Int(NI_MAXHOST))
        let status = getnameinfo(
            info.pointee.ai_addr, info.pointee.ai_addrlen, &host, socklen_t(host.count), nil, 0, NI_NAMEREQD
        )
        guard status == 0 else { return nil }
        let bytes = host.prefix { $0 != 0 }.map { UInt8(bitPattern: $0) }
        guard let name = String(bytes: bytes, encoding: .utf8), !name.isEmpty, name != address else { return nil }
        return name
    }
}
