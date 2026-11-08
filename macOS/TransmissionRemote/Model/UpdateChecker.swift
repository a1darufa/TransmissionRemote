import AppKit
import Observation
import TransmissionKit

/// Looks for newer releases on GitHub, like Transmission Remote GUI's "Check for updates".
/// Releases are downloaded by hand and approved in System Settings, so the app only points to
/// the release page; it never downloads or installs anything itself.
@Observable
final class UpdateChecker {
    enum Outcome: Equatable {
        case available(LatestRelease)
        case upToDate
        case failed(String)
    }

    private(set) var isChecking = false
    /// What the last check found that the user should hear about; the main window shows it.
    var outcome: Outcome?

    private static let latestReleaseURL =
        URL(string: "https://api.github.com/repos/a1darufa/TransmissionRemote/releases/latest")!
    private static let checkInterval: Duration = .seconds(24 * 60 * 60)
    private static let log = DiagnosticLog(category: "Updates")
    private var automaticChecks: Task<Void, Never>?

    static var currentVersionText: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "?"
    }

    private var currentVersion: ReleaseVersion? { ReleaseVersion(Self.currentVersionText) }

    /// Checks at launch and then every few hours whether a day has passed since the last check,
    /// so a Mac that stays on for weeks still hears about new releases.
    func startAutomaticChecks() {
        automaticChecks?.cancel()
        automaticChecks = Task { [weak self] in
            while !Task.isCancelled {
                await self?.checkIfDue()
                try? await Task.sleep(for: .seconds(6 * 60 * 60))
            }
        }
    }

    /// `userInitiated` checks always report back; automatic ones stay quiet unless there's a
    /// release the user hasn't chosen to skip.
    func check(userInitiated: Bool) async {
        guard !isChecking else { return }
        isChecking = true
        defer { isChecking = false }

        do {
            let release = try await fetchLatestRelease()
            UserDefaults.standard.set(Date.now, forKey: AppSettings.lastUpdateCheck)
            let isNewer = currentVersion.map { release.version > $0 } ?? false
            let isSkipped = release.tagName == UserDefaults.standard.string(forKey: AppSettings.skippedUpdateVersion)
            if isNewer, userInitiated || !isSkipped {
                outcome = .available(release)
            } else if userInitiated {
                outcome = .upToDate
            }
        } catch {
            Self.log.warning("Update check failed: \(error) – \(error.localizedDescription)")
            if userInitiated { outcome = .failed(error.localizedDescription) }
        }
    }

    func skip(_ release: LatestRelease) {
        UserDefaults.standard.set(release.tagName, forKey: AppSettings.skippedUpdateVersion)
    }

    func openReleasePage(_ release: LatestRelease) {
        NSWorkspace.shared.open(release.pageURL)
    }

    private func checkIfDue() async {
        let defaults = UserDefaults.standard
        guard defaults.bool(forKey: AppSettings.checkForUpdates) else { return }
        let lastCheck = defaults.object(forKey: AppSettings.lastUpdateCheck) as? Date ?? .distantPast
        guard Date.now.timeIntervalSince(lastCheck) >= Self.checkInterval.timeInterval else { return }
        await check(userInitiated: false)
    }

    private func fetchLatestRelease() async throws -> LatestRelease {
        var request = URLRequest(url: Self.latestReleaseURL, timeoutInterval: 20)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        let (data, response) = try await URLSession.shared.data(for: request)
        guard (response as? HTTPURLResponse)?.statusCode == 200 else {
            throw URLError(.badServerResponse)
        }
        return try LatestRelease(gitHubResponse: data)
    }
}

private extension Duration {
    var timeInterval: TimeInterval {
        let (seconds, attoseconds) = components
        return TimeInterval(seconds) + TimeInterval(attoseconds) / 1e18
    }
}
