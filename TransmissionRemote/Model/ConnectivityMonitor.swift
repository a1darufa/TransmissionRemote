import AppKit
import Network

/// Reports the moments when a stalled connection is worth retrying right away: the Mac woke
/// from sleep, or the network changed (Wi-Fi came back, Tailscale reconnected, another
/// interface took over). Without this, the app would sit out the rest of its retry delay.
final class ConnectivityMonitor {
    private var pathTask: Task<Void, Never>?
    private var wakeObservation: NotificationCenter.ObservationToken?

    func start(onChange: @escaping @MainActor () -> Void) {
        stop()
        pathTask = Task {
            var previous: NWPath?
            for await path in NWPathMonitor() {
                defer { previous = path }
                guard let previous, path.status == .satisfied else { continue }
                let cameOnline = previous.status != .satisfied
                let interfacesChanged = previous.availableInterfaces.map(\.name) != path.availableInterfaces.map(\.name)
                if cameOnline || interfacesChanged { onChange() }
            }
        }
        wakeObservation = NSWorkspace.shared.notificationCenter.addObserver(
            of: NSWorkspace.shared,
            for: .didWake
        ) { _ in
            onChange()
        }
    }

    func stop() {
        pathTask?.cancel()
        pathTask = nil
        if let wakeObservation {
            NSWorkspace.shared.notificationCenter.removeObserver(wakeObservation)
        }
        wakeObservation = nil
    }
}
