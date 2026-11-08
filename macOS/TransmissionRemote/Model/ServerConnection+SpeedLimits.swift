import TransmissionKit

/// Which way a speed limit applies.
enum TransferDirection {
    case download, upload
}

extension ServerConnection {
    /// Sets or clears (`nil`) a speed limit on torrents, from the torrent menu's speed presets.
    func setSpeedLimit(_ limit: Int?, direction: TransferDirection, on ids: Set<Torrent.ID>) {
        var mutation = TorrentMutation(ids: ids.sorted())
        switch direction {
        case .download:
            mutation.downloadLimited = limit != nil
            mutation.downloadLimit = limit
        case .upload:
            mutation.uploadLimited = limit != nil
            mutation.uploadLimit = limit
        }
        apply(mutation)
    }

    /// Sets or clears (`nil`) the daemon-wide speed limit, from the Server menu's speed presets.
    /// `run` refreshes the session afterwards, which moves the menu's checkmark.
    func setGlobalSpeedLimit(_ limit: Int?, direction: TransferDirection) {
        var changes = SessionSettings()
        switch direction {
        case .download:
            changes.speedLimitDownEnabled = limit != nil
            changes.speedLimitDown = limit
        case .upload:
            changes.speedLimitUpEnabled = limit != nil
            changes.speedLimitUp = limit
        }
        let patch = changes
        run { try await $0.updateSession(patch) }
    }

    /// The daemon-wide limit in kB/s, or nil when it's off. Used for the menu's checkmark.
    func globalSpeedLimit(_ direction: TransferDirection) -> Int? {
        switch direction {
        case .download: session?.speedLimitDownEnabled == true ? session?.speedLimitDown : nil
        case .upload: session?.speedLimitUpEnabled == true ? session?.speedLimitUp : nil
        }
    }
}
