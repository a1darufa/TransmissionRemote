import SwiftUI
import TransmissionKit

/// "Download Speed Limit" or "Upload Speed Limit", with the speeds from Settings → Misc,
/// like Transmission Remote GUI's speed menus.
struct SpeedLimitMenu: View {
    let direction: TransferDirection
    /// What gets a checkmark: `.some(nil)` for Unlimited, `.some(speed)`, or `nil` when unknown
    /// (several torrents selected).
    var current: Int??
    let apply: (Int?) -> Void

    @AppStorage(AppSettings.downloadSpeedPresets) private var downloadPresets = SpeedLimitPresets.defaultList
    @AppStorage(AppSettings.uploadSpeedPresets) private var uploadPresets = SpeedLimitPresets.defaultList

    var body: some View {
        Menu {
            item(String(localized: "Unlimited"), limit: nil)
            Divider()
            ForEach(speeds, id: \.self) { speed in
                item(String(localized: "\(speed) kB/s"), limit: speed)
            }
        } label: {
            switch direction {
            case .download: Label("Download Speed Limit", systemImage: "arrow.down.circle")
            case .upload: Label("Upload Speed Limit", systemImage: "arrow.up.circle")
            }
        }
    }

    /// Unlimited is always offered, so a 0 in the list isn't needed (it's what transgui used).
    private var speeds: [Int] {
        SpeedLimitPresets.parse(direction == .download ? downloadPresets : uploadPresets).filter { $0 > 0 }
    }

    @ViewBuilder
    private func item(_ title: String, limit: Int?) -> some View {
        Button {
            apply(limit)
        } label: {
            if let current, current == limit {
                Label(title, systemImage: "checkmark")
            } else {
                Text(title)
            }
        }
    }
}
