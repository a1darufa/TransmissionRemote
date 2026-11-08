import SwiftUI
import TransmissionKit

/// Settings → Application Options → Misc, like Transmission Remote GUI's tab: the speeds offered
/// in the Download and Upload Speed Limit menus.
struct MiscSettings: View {
    @AppStorage(AppSettings.downloadSpeedPresets) private var downloadPresets = SpeedLimitPresets.defaultList
    @AppStorage(AppSettings.uploadSpeedPresets) private var uploadPresets = SpeedLimitPresets.defaultList

    var body: some View {
        Form {
            Section {
                PresetListField(title: "Download speeds (kB/s)", text: $downloadPresets)
                PresetListField(title: "Upload speeds (kB/s)", text: $uploadPresets)
            } header: {
                Text("Speed limit menu items")
            } footer: {
                Text("""
                    Comma-separated speeds for the Speed Limit menus in the torrent menu and the Server menu. \
                    Unlimited is always offered.
                    """)
            }
        }
        .formStyle(.grouped)
    }
}

/// A label above a full-width field, so long lists stay readable, as in transgui.
private struct PresetListField: View {
    let title: LocalizedStringResource
    @Binding var text: String

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title)
            TextField(String(localized: title), text: $text)
                .labelsHidden()
                .autocorrectionDisabled()
        }
    }
}
