import AppKit
import TransmissionKit

/// UserDefaults keys and defaults for app-wide preferences.
enum AppSettings {
    static let refreshInterval = "refreshInterval"
    static let backgroundRefreshInterval = "backgroundRefreshInterval"
    static let showAddTorrentDialog = "showAddTorrentDialog"
    static let notifyOnCompletion = "notifyOnCompletion"
    static let showMenuBarExtra = "showMenuBarExtra"
    static let showSpeedsInMenuBar = "showSpeedsInMenuBar"
    static let showDetailPane = "showDetailPane"
    static let torrentColumns = "torrentTableColumns"
    static let sortColumn = "torrentSortColumn"
    static let sortAscending = "torrentSortAscending"
    static let settingsPane = "settingsPane"
    static let folderHistoryLimit = "folderHistoryLimit"
    static let downloadSpeedPresets = "downloadSpeedPresets"
    static let uploadSpeedPresets = "uploadSpeedPresets"
    static let checkForUpdates = "checkForUpdates"
    static let lastUpdateCheck = "lastUpdateCheck"
    static let skippedUpdateVersion = "skippedUpdateVersion"
    static let resolvePeerHostNames = "resolvePeerHostNames"
    static let showPeerCountries = "showPeerCountries"
    static let countryDatabaseMonth = "countryDatabaseMonth"

    /// Must run before any view reads `@AppStorage`, so defaults are consistent everywhere.
    static func registerDefaults() {
        UserDefaults.standard.register(defaults: [
            refreshInterval: 5,
            backgroundRefreshInterval: 20,
            showAddTorrentDialog: true,
            notifyOnCompletion: true,
            showMenuBarExtra: true,
            showSpeedsInMenuBar: false,
            showDetailPane: true,
            sortAscending: false,
            folderHistoryLimit: 50,
            checkForUpdates: true,
            resolvePeerHostNames: true,
            showPeerCountries: true,
            downloadSpeedPresets: SpeedLimitPresets.defaultList,
            uploadSpeedPresets: SpeedLimitPresets.defaultList
        ])
    }

    /// How many download folders each server remembers, like Transmission Remote GUI's 50.
    static var currentFolderHistoryLimit: Int {
        max(UserDefaults.standard.integer(forKey: folderHistoryLimit), 1)
    }

    /// Polling interval: faster while the app is in front, slower in the background.
    static var currentRefreshInterval: Duration {
        let key = NSApplication.shared.isActive ? refreshInterval : backgroundRefreshInterval
        return .seconds(max(UserDefaults.standard.integer(forKey: key), 1))
    }
}
