import Foundation
import TransmissionKit
import UserNotifications

/// Posts a notification when a torrent finishes downloading.
final class CompletionNotifier {
    /// The first torrent list after connecting is only a baseline; everything in it
    /// finished before we were watching.
    private var hasBaseline = false

    private var isEnabled: Bool {
        UserDefaults.standard.bool(forKey: AppSettings.notifyOnCompletion)
    }

    func reset() {
        hasBaseline = false
    }

    func requestAuthorizationIfEnabled() {
        guard isEnabled else { return }
        Task {
            _ = try? await UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound])
        }
    }

    func notifyAboutCompletions(previous: [Torrent], current: [Torrent]) {
        defer { hasBaseline = true }
        guard hasBaseline, isEnabled else { return }

        let previouslyIncomplete = Set(previous.filter { !$0.isComplete }.map(\.id))
        for torrent in current where torrent.isComplete && previouslyIncomplete.contains(torrent.id) {
            post(for: torrent)
        }
    }

    private func post(for torrent: Torrent) {
        let content = UNMutableNotificationContent()
        content.title = String(localized: "Download complete")
        content.body = torrent.name
        content.sound = .default
        let request = UNNotificationRequest(
            identifier: "complete-\(torrent.hashString)",
            content: content,
            trigger: nil
        )
        UNUserNotificationCenter.current().add(request)
    }
}
