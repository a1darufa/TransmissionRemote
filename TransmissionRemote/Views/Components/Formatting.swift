import SwiftUI
import TransmissionKit

/// Transmission reports decimal units (1 kB = 1000 B), which `.file` style matches.
nonisolated extension Int64 {
    var formattedBytes: String {
        formatted(.byteCount(style: .file))
    }

    var formattedSpeed: String {
        "\(formatted(.byteCount(style: .file)))/s"
    }

    /// Blank for zero, so idle rows in the torrent list stay quiet.
    var formattedSpeedOrBlank: String {
        self > 0 ? formattedSpeed : ""
    }
}

nonisolated extension Int {
    /// Transmission speed limits are in kB/s.
    var formattedSpeedLimit: String {
        (Int64(self) * 1000).formattedSpeed
    }
}

nonisolated enum Format {
    static func percent(_ fraction: Double) -> String {
        fraction.formatted(.percent.precision(.fractionLength(fraction < 1 ? 1 : 0)))
    }

    static func eta(_ seconds: Int) -> String {
        seconds >= 0 ? duration(seconds) : ""
    }

    static func duration(_ seconds: Int) -> String {
        Duration.seconds(seconds).formatted(
            .units(allowed: [.days, .hours, .minutes, .seconds], width: .narrow, maximumUnitCount: 2)
        )
    }

    /// Transmission uses -1 for "not available" and -2 for "infinite".
    static func ratio(_ ratio: Double) -> String {
        switch ratio {
        case -2: "∞"
        case ..<0: "–"
        default: ratio.formatted(.number.precision(.fractionLength(2)))
        }
    }

    static func date(_ date: Date?) -> String {
        date?.formatted(date: .abbreviated, time: .shortened) ?? ""
    }
}

nonisolated extension Torrent {
    var statusText: String {
        if hasError, !errorString.isEmpty {
            return String(localized: "Error: \(errorString)")
        }
        switch status {
        case .stopped: return isComplete ? String(localized: "Finished") : String(localized: "Stopped")
        case .queuedToVerify: return String(localized: "Queued to verify")
        case .verifying: return String(localized: "Verifying \(Format.percent(recheckProgress))")
        case .queuedToDownload: return String(localized: "Queued")
        case .downloading:
            return metadataPercentComplete < 1
                ? String(localized: "Getting metadata")
                : String(localized: "Downloading")
        case .queuedToSeed: return String(localized: "Queued to seed")
        case .seeding: return String(localized: "Seeding")
        }
    }

    var statusSymbol: String {
        if hasError { return "exclamationmark.triangle.fill" }
        switch status {
        case .stopped: return isComplete ? "checkmark.circle.fill" : "pause.circle.fill"
        case .queuedToVerify, .queuedToDownload, .queuedToSeed: return "clock.fill"
        case .verifying: return "arrow.triangle.2.circlepath.circle.fill"
        case .downloading: return "arrow.down.circle.fill"
        case .seeding: return "arrow.up.circle.fill"
        }
    }

    var statusColor: Color {
        if hasError { return .orange }
        switch status {
        case .downloading: return .blue
        case .seeding: return .green
        case .verifying: return .purple
        default: return .secondary
        }
    }

    /// The progress worth showing: verification, metadata download, or data.
    var displayedProgress: Double {
        if status == .verifying { return recheckProgress }
        if metadataPercentComplete < 1 { return metadataPercentComplete }
        return percentDone
    }

    // Comparable stand-ins for optional values, so every column can sort.
    var etaSortKey: Int { eta >= 0 ? eta : .max }
    var addedSortKey: Date { addedDate ?? .distantPast }
    var doneSortKey: Date { doneDate ?? .distantPast }
    var labelsText: String { labels.joined(separator: ", ") }
    var primaryTracker: String { trackerHosts.first ?? "" }
}

nonisolated extension Priority {
    var title: LocalizedStringResource {
        switch self {
        case .high: "High"
        case .normal: "Normal"
        case .low: "Low"
        }
    }
}

nonisolated extension TransportSecurity {
    var symbol: String {
        switch self {
        case .tls: "lock.fill"
        case .tailscale: "lock.shield.fill"
        case .loopback, .localNetwork: "house.fill"
        case .unencryptedInternet: "lock.open.trianglebadge.exclamationmark.fill"
        }
    }

    var color: Color {
        switch self {
        case .tls, .tailscale: .green
        case .loopback, .localNetwork: .secondary
        case .unencryptedInternet: .red
        }
    }

    var summary: LocalizedStringResource {
        switch self {
        case .tls: "Encrypted with HTTPS."
        case .tailscale: "Plain HTTP inside a Tailscale tunnel, which encrypts the traffic."
        case .loopback: "Connection to this Mac."
        case .localNetwork: "Plain HTTP on a private network. Devices on the same network could read the password."
        case .unencryptedInternet:
            "Plain HTTP over the internet. The password and all traffic can be read by anyone along the way."
        }
    }
}
