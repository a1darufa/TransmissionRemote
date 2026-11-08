import SwiftUI
import TransmissionKit

struct TrackersTab: View {
    @Environment(AppModel.self) private var model
    let torrent: Torrent
    let details: TorrentDetails?

    var body: some View {
        VStack(spacing: 0) {
            if let trackers = details?.trackerStats {
                Table(trackers) {
                    TableColumn("Tier") { Text("\($0.tier + 1)").monospacedDigit() }
                        .width(min: 30, ideal: 40)
                    TableColumn("Tracker") { tracker in
                        Text(tracker.announce).lineLimit(1).truncationMode(.middle).help(tracker.announce)
                    }
                    .width(min: 160, ideal: 300)
                    TableColumn("Last Announce") { tracker in
                        TrackerStatusText(tracker: tracker)
                    }
                    .width(min: 120, ideal: 220)
                    TableColumn("Seeds") { Text("\($0.seederCount)").monospacedDigit() }
                        .width(min: 40, ideal: 55)
                        .alignment(.trailing)
                    TableColumn("Leechers") { Text("\($0.leecherCount)").monospacedDigit() }
                        .width(min: 40, ideal: 60)
                        .alignment(.trailing)
                    TableColumn("Downloads") { Text("\($0.downloadCount)").monospacedDigit() }
                        .width(min: 40, ideal: 70)
                        .alignment(.trailing)
                    TableColumn("Next Announce") { tracker in
                        if let next = tracker.nextAnnounceTime, next > .now {
                            Text(next, style: .relative)
                        }
                    }
                    .width(min: 70, ideal: 110)
                }
            } else {
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
            }

            Divider()
            HStack {
                Spacer()
                Button("Ask for More Peers") { model.connection.perform(.reannounce, on: [torrent.id]) }
                Button("Edit Trackers…") { model.router.sheet = .editTrackers(torrent.id) }
            }
            .controlSize(.small)
            .padding(6)
        }
    }
}

private struct TrackerStatusText: View {
    let tracker: TrackerStat

    var body: some View {
        if tracker.hasAnnounced {
            HStack(spacing: 4) {
                Image(systemName: tracker.lastAnnounceSucceeded ? "checkmark.circle.fill" : "xmark.octagon.fill")
                    .foregroundStyle(tracker.lastAnnounceSucceeded ? Color.green : Color.orange)
                Text(tracker.lastAnnounceSucceeded
                     ? String(localized: "\(tracker.lastAnnouncePeerCount) peers")
                     : tracker.lastAnnounceResult)
                    .lineLimit(1)
            }
            .help(tracker.lastAnnounceResult)
        } else {
            Text(tracker.isBackup ? "Backup" : "Not yet announced").foregroundStyle(.secondary)
        }
    }
}
