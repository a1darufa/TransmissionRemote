import Foundation
import Observation
import TransmissionKit

/// The connected server's settings while they're edited in Settings. Edits are shared by the
/// Transmission panes and stay local until Apply sends only what changed.
@Observable
final class ServerSettingsDraft {
    /// The server's settings when editing started, to work out what changed.
    private(set) var original = SessionSettings()
    var settings = SessionSettings()
    private(set) var isLoaded = false
    private(set) var isApplying = false
    var errorMessage: String?
    private var profileID: UUID?

    var hasChanges: Bool { settings != original }

    /// Picks up the server's current settings unless there are edits not yet applied.
    /// Switching to another server always starts over, so edits never reach the wrong server.
    func sync(with connection: ServerConnection) {
        let isOtherServer = connection.profile?.id != profileID
        guard isOtherServer || !hasChanges else { return }
        profileID = connection.profile?.id
        errorMessage = nil
        if let session = connection.session {
            original = session
            settings = session
            isLoaded = true
        } else {
            isLoaded = false
        }
    }

    func revert() {
        settings = original
        errorMessage = nil
    }

    func apply(using connection: ServerConnection) async {
        isApplying = true
        errorMessage = nil
        defer { isApplying = false }
        do {
            try await connection.updateSession(settings.changes(from: original))
            // updateSession refreshes the session, so the server's answer is the new baseline.
            original = settings
            sync(with: connection)
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
