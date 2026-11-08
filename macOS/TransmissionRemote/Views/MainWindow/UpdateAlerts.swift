import SwiftUI
import TransmissionKit

/// The alerts for a finished update check.
struct UpdateAlerts: ViewModifier {
    @Bindable var updates: UpdateChecker

    func body(content: Content) -> some View {
        content
            .alert(
                availableTitle,
                isPresented: isShowing { if case .available = $0 { true } else { false } },
                presenting: availableRelease
            ) { release in
                Button("Download Update") { updates.openReleasePage(release) }
                Button("Skip This Version") { updates.skip(release) }
                Button("Later", role: .cancel) {}
            } message: { _ in
                Text("""
                    You have version \(UpdateChecker.currentVersionText). Download the new disk image from GitHub \
                    and drag the app into Applications to replace this one.
                    """)
            }
            .alert(
                "You're Up to Date",
                isPresented: isShowing { $0 == .upToDate }
            ) {
                Button("OK") {}
            } message: {
                Text("Transmission Remote \(UpdateChecker.currentVersionText) is the newest version.")
            }
            .alert(
                "Couldn't Check for Updates",
                isPresented: isShowing { if case .failed = $0 { true } else { false } },
                presenting: failureMessage
            ) { _ in
                Button("OK") {}
            } message: { message in
                Text(message)
            }
    }

    private var availableRelease: LatestRelease? {
        if case .available(let release) = updates.outcome { release } else { nil }
    }

    private var failureMessage: String? {
        if case .failed(let message) = updates.outcome { message } else { nil }
    }

    private var availableTitle: String {
        let version = availableRelease?.version.description ?? ""
        return String(localized: "Transmission Remote \(version) Is Available")
    }

    private func isShowing(_ matches: @escaping (UpdateChecker.Outcome) -> Bool) -> Binding<Bool> {
        Binding {
            updates.outcome.map(matches) ?? false
        } set: { isPresented in
            if !isPresented { updates.outcome = nil }
        }
    }
}
