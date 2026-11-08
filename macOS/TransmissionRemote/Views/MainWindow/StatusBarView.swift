import SwiftUI
import TransmissionKit

/// The strip under the torrent list: connection security, speeds, free space.
struct StatusBarView: View {
    @Environment(AppModel.self) private var model

    private var connection: ServerConnection { model.connection }
    private var session: SessionSettings? { connection.session }

    var body: some View {
        HStack(spacing: 16) {
            connectionInfo
            if let problem = connection.pollingProblem {
                pollingProblem(problem)
            }
            Spacer()
            speed(
                connection.stats?.downloadSpeed ?? 0,
                symbol: "arrow.down",
                limit: activeLimit(
                    regular: session?.speedLimitDown,
                    enabled: session?.speedLimitDownEnabled,
                    alternative: session?.altSpeedDown
                )
            )
            speed(
                connection.stats?.uploadSpeed ?? 0,
                symbol: "arrow.up",
                limit: activeLimit(
                    regular: session?.speedLimitUp,
                    enabled: session?.speedLimitUpEnabled,
                    alternative: session?.altSpeedUp
                )
            )
            if let freeSpace = connection.freeSpace {
                Label("\(freeSpace.sizeBytes.formattedBytes) free", systemImage: "internaldrive")
                    .help(freeSpace.path)
            }
            alternativeSpeedToggle
        }
        .font(.callout)
        .foregroundStyle(.secondary)
        .labelStyle(.titleAndIcon)
        .padding(.horizontal, 12)
        .padding(.vertical, 5)
        .background(.bar)
        .overlay(alignment: .top) { Divider() }
    }

    @ViewBuilder
    private var connectionInfo: some View {
        if let profile = connection.profile {
            let security = profile.endpoint.transportSecurity
            Label {
                Text("\(connection.torrents.count) torrents")
            } icon: {
                Image(systemName: security.symbol).foregroundStyle(security.color)
            }
            .help(Text(security.summary))
        }
    }

    /// The list stays visible during a network problem, so say how old it is and offer a retry.
    private func pollingProblem(_ problem: String) -> some View {
        HStack(spacing: 8) {
            Label(problem, systemImage: "exclamationmark.triangle.fill")
                .foregroundStyle(.orange)
                .lineLimit(1)
            if let lastUpdated = connection.lastUpdated {
                TimelineView(.periodic(from: .now, by: 10)) { _ in
                    Text("Updated \(lastUpdated, format: .relative(presentation: .named))")
                        .lineLimit(1)
                }
            }
            Button("Try Now") { connection.retryNow() }
                .controlSize(.small)
        }
    }

    private func speed(_ bytesPerSecond: Int64, symbol: String, limit: Int?) -> some View {
        Label {
            HStack(spacing: 4) {
                Text(bytesPerSecond.formattedSpeed).monospacedDigit()
                if let limit {
                    Text("(\(limit.formattedSpeedLimit))").foregroundStyle(.tertiary)
                }
            }
        } icon: {
            Image(systemName: symbol)
        }
    }

    /// Alternative limits replace the regular ones while turtle mode is on.
    private func activeLimit(regular: Int?, enabled: Bool?, alternative: Int?) -> Int? {
        if session?.altSpeedEnabled == true { return alternative }
        return enabled == true ? regular : nil
    }

    private var alternativeSpeedToggle: some View {
        let isOn = session?.altSpeedEnabled ?? false
        return Button {
            connection.setAlternativeSpeedEnabled(!isOn)
        } label: {
            Image(systemName: "tortoise.fill")
                .foregroundStyle(isOn ? Color.accentColor : Color.secondary)
        }
        .buttonStyle(.borderless)
        .help(isOn ? "Alternative speed limits are on" : "Turn on alternative speed limits")
    }
}
