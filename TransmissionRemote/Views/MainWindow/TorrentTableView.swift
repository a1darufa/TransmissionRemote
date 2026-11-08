import SwiftUI
import TransmissionKit

struct TorrentTableView: View {
    @Environment(AppModel.self) private var model
    let torrents: [Torrent]

    @AppStorage(AppSettings.torrentColumns) private var columnData = Data()
    @AppStorage(AppSettings.sortColumn) private var sortColumn = TorrentSortColumn.added.rawValue
    @AppStorage(AppSettings.sortAscending) private var sortAscending = false
    @AppStorage(AppSettings.showDetailPane) private var showsDetailPane = true

    var body: some View {
        @Bindable var connection = model.connection

        Table(
            torrents.sorted(using: sortOrder.wrappedValue),
            selection: $connection.selection,
            sortOrder: sortOrder,
            columnCustomization: columnCustomization
        ) {
            primaryColumns
            secondaryColumns
            extraColumns
        }
        .contextMenu(forSelectionType: Torrent.ID.self) { ids in
            TorrentActionsMenu(model: model, ids: ids)
        } primaryAction: { _ in
            showsDetailPane = true
        }
        .overlay {
            if torrents.isEmpty {
                ContentUnavailableView(
                    "No Torrents",
                    systemImage: "tray",
                    description: Text("Drop .torrent files or magnet links here, or use Add Torrent.")
                )
            }
        }
    }

    @TableColumnBuilder<Torrent, KeyPathComparator<Torrent>>
    private var primaryColumns: some TableColumnContent<Torrent, KeyPathComparator<Torrent>> {
        TableColumn("Name", value: \.name) { torrent in
            TorrentNameCell(torrent: torrent)
        }
        .width(min: 180, ideal: 340)
        .customizationID("name")
        .disabledCustomizationBehavior(.visibility)

        TableColumn("Size", value: \.sizeWhenDone) { Text($0.sizeWhenDone.formattedBytes).monospacedDigit() }
            .width(min: 60, ideal: 80)
            .alignment(.trailing)
            .customizationID("size")

        TableColumn("Done", value: \.percentDone) { TorrentProgressCell(torrent: $0) }
            .width(min: 80, ideal: 120)
            .customizationID("done")

        TableColumn("Status", value: \.statusText) { torrent in
            Text(torrent.statusText)
                .foregroundStyle(torrent.hasError ? Color.orange : Color.primary)
        }
            .width(min: 70, ideal: 110)
            .customizationID("status")

        TableColumn("Seeds", value: \.peersSendingToUs) { Text($0.seedsText).monospacedDigit() }
            .width(min: 40, ideal: 75)
            .alignment(.trailing)
            .customizationID("seeds")

        TableColumn("Peers", value: \.peersGettingFromUs) { Text($0.peersText).monospacedDigit() }
            .width(min: 40, ideal: 75)
            .alignment(.trailing)
            .customizationID("peers")

        TableColumn("Down", value: \.rateDownload) { Text($0.rateDownload.formattedSpeedOrBlank).monospacedDigit() }
            .width(min: 60, ideal: 85)
            .alignment(.trailing)
            .customizationID("down")

        TableColumn("Up", value: \.rateUpload) { Text($0.rateUpload.formattedSpeedOrBlank).monospacedDigit() }
            .width(min: 60, ideal: 85)
            .alignment(.trailing)
            .customizationID("up")

        TableColumn("ETA", value: \.etaSortKey) { Text(Format.eta($0.eta)).monospacedDigit() }
            .width(min: 50, ideal: 70)
            .alignment(.trailing)
            .customizationID("eta")

        TableColumn("Ratio", value: \.uploadRatio) { Text(Format.ratio($0.uploadRatio)).monospacedDigit() }
            .width(min: 40, ideal: 55)
            .alignment(.trailing)
            .customizationID("ratio")
    }

    @TableColumnBuilder<Torrent, KeyPathComparator<Torrent>>
    private var secondaryColumns: some TableColumnContent<Torrent, KeyPathComparator<Torrent>> {
        TableColumn("Added", value: \.addedSortKey) { Text(Format.date($0.addedDate)) }
            .width(min: 90, ideal: 140)
            .customizationID("added")

        TableColumn("Completed", value: \.doneSortKey) { Text(Format.date($0.doneDate)) }
            .width(min: 90, ideal: 140)
            .customizationID("completed")
            .defaultVisibility(.hidden)

        TableColumn("Folder", value: \.downloadDir) { Text($0.downloadDir).help($0.downloadDir) }
            .width(min: 80, ideal: 180)
            .customizationID("folder")
            .defaultVisibility(.hidden)

        TableColumn("Labels", value: \.labelsText) { Text($0.labelsText) }
            .width(min: 60, ideal: 100)
            .customizationID("labels")
            .defaultVisibility(.hidden)

        TableColumn("Tracker", value: \.primaryTracker) { Text($0.primaryTracker) }
            .width(min: 80, ideal: 140)
            .customizationID("tracker")
            .defaultVisibility(.hidden)

        TableColumn("Queue", value: \.queuePosition) { Text("\($0.queuePosition + 1)").monospacedDigit() }
            .width(min: 40, ideal: 50)
            .alignment(.trailing)
            .customizationID("queue")
            .defaultVisibility(.hidden)

        TableColumn("Downloaded", value: \.downloadedEver) { Text($0.downloadedEver.formattedBytes).monospacedDigit() }
            .width(min: 60, ideal: 85)
            .alignment(.trailing)
            .customizationID("downloaded")
            .defaultVisibility(.hidden)

        TableColumn("Uploaded", value: \.uploadedEver) { Text($0.uploadedEver.formattedBytes).monospacedDigit() }
            .width(min: 60, ideal: 85)
            .alignment(.trailing)
            .customizationID("uploaded")
            .defaultVisibility(.hidden)

        TableColumn("Priority", value: \.bandwidthPriority) { Text($0.bandwidthPriority.title) }
            .width(min: 50, ideal: 65)
            .customizationID("priority")
            .defaultVisibility(.hidden)
    }

    /// Columns transgui has that most people leave off; hidden until chosen from the header menu.
    @TableColumnBuilder<Torrent, KeyPathComparator<Torrent>>
    private var extraColumns: some TableColumnContent<Torrent, KeyPathComparator<Torrent>> {
        TableColumn("Total Size", value: \.totalSize) { Text($0.totalSize.formattedBytes).monospacedDigit() }
            .width(min: 60, ideal: 80)
            .alignment(.trailing)
            .customizationID("totalSize")
            .defaultVisibility(.hidden)

        TableColumn("Size Left", value: \.leftUntilDone) { Text($0.leftUntilDone.formattedBytes).monospacedDigit() }
            .width(min: 60, ideal: 80)
            .alignment(.trailing)
            .customizationID("sizeLeft")
            .defaultVisibility(.hidden)

        TableColumn("Last Active", value: \.activitySortKey) { Text(Format.date($0.activityDate)) }
            .width(min: 90, ideal: 140)
            .customizationID("lastActive")
            .defaultVisibility(.hidden)

        TableColumn("Seeding Time", value: \.secondsSeeding) { torrent in
            Text(torrent.secondsSeeding > 0 ? Format.duration(torrent.secondsSeeding) : "").monospacedDigit()
        }
            .width(min: 60, ideal: 85)
            .alignment(.trailing)
            .customizationID("seedingTime")
            .defaultVisibility(.hidden)

        TableColumn("Tracker Status", value: \.trackerStatusText) { torrent in
            Text(torrent.trackerStatusText).help(torrent.trackerStatusText)
        }
            .width(min: 80, ideal: 140)
            .customizationID("trackerStatus")
            .defaultVisibility(.hidden)

        TableColumn("Private", value: \.privateSortKey) { torrent in
            if torrent.isPrivate {
                Image(systemName: "lock.fill")
                    .foregroundStyle(.secondary)
                    .accessibilityLabel("Private")
            }
        }
            .width(min: 40, ideal: 50)
            .alignment(.center)
            .customizationID("private")
            .defaultVisibility(.hidden)

        TableColumn("Bandwidth Group", value: \.bandwidthGroup) { Text($0.bandwidthGroup) }
            .width(min: 60, ideal: 110)
            .customizationID("bandwidthGroup")
            .defaultVisibility(.hidden)

        TableColumn("ID", value: \.id) { Text("\($0.id)").monospacedDigit() }
            .width(min: 30, ideal: 45)
            .alignment(.trailing)
            .customizationID("id")
            .defaultVisibility(.hidden)
    }

    // MARK: - Persistence

    /// The sort column and direction survive relaunches, like in Transmission Remote GUI.
    private var sortOrder: Binding<[KeyPathComparator<Torrent>]> {
        Binding {
            let column = TorrentSortColumn(rawValue: sortColumn) ?? .added
            return [column.comparator(order: sortAscending ? .forward : .reverse)]
        } set: { newValue in
            guard let first = newValue.first, let column = TorrentSortColumn(matching: first) else { return }
            sortColumn = column.rawValue
            sortAscending = first.order == .forward
        }
    }

    private var columnCustomization: Binding<TableColumnCustomization<Torrent>> {
        Binding {
            let saved = try? JSONDecoder().decode(TableColumnCustomization<Torrent>.self, from: columnData)
            return saved ?? TableColumnCustomization()
        } set: { newValue in
            columnData = (try? JSONEncoder().encode(newValue)) ?? Data()
        }
    }
}

/// Maps between persisted column names and the comparators `Table` hands back.
enum TorrentSortColumn: String, CaseIterable {
    case name, size, done, status, seeds, peers, down, up, eta, ratio
    case added, completed, folder, labels, tracker, queue, downloaded, uploaded, priority
    case totalSize, sizeLeft, lastActive, seedingTime, trackerStatus, isPrivate, id, bandwidthGroup

    init?(matching comparator: KeyPathComparator<Torrent>) {
        let matchingColumn = Self.allCases.first { $0.comparator(order: .forward).keyPath == comparator.keyPath }
        guard let column = matchingColumn else {
            return nil
        }
        self = column
    }

    func comparator(order: SortOrder) -> KeyPathComparator<Torrent> {
        switch self {
        case .name: KeyPathComparator(\.name, order: order)
        case .size: KeyPathComparator(\.sizeWhenDone, order: order)
        case .done: KeyPathComparator(\.percentDone, order: order)
        case .status: KeyPathComparator(\.statusText, order: order)
        case .seeds: KeyPathComparator(\.peersSendingToUs, order: order)
        case .peers: KeyPathComparator(\.peersGettingFromUs, order: order)
        case .down: KeyPathComparator(\.rateDownload, order: order)
        case .up: KeyPathComparator(\.rateUpload, order: order)
        case .eta: KeyPathComparator(\.etaSortKey, order: order)
        case .ratio: KeyPathComparator(\.uploadRatio, order: order)
        case .added: KeyPathComparator(\.addedSortKey, order: order)
        case .completed: KeyPathComparator(\.doneSortKey, order: order)
        case .folder: KeyPathComparator(\.downloadDir, order: order)
        case .labels: KeyPathComparator(\.labelsText, order: order)
        case .tracker: KeyPathComparator(\.primaryTracker, order: order)
        case .queue: KeyPathComparator(\.queuePosition, order: order)
        case .downloaded: KeyPathComparator(\.downloadedEver, order: order)
        case .uploaded: KeyPathComparator(\.uploadedEver, order: order)
        case .priority: KeyPathComparator(\.bandwidthPriority, order: order)
        case .totalSize: KeyPathComparator(\.totalSize, order: order)
        case .sizeLeft: KeyPathComparator(\.leftUntilDone, order: order)
        case .lastActive: KeyPathComparator(\.activitySortKey, order: order)
        case .seedingTime: KeyPathComparator(\.secondsSeeding, order: order)
        case .trackerStatus: KeyPathComparator(\.trackerStatusText, order: order)
        case .isPrivate: KeyPathComparator(\.privateSortKey, order: order)
        case .id: KeyPathComparator(\.id, order: order)
        case .bandwidthGroup: KeyPathComparator(\.bandwidthGroup, order: order)
        }
    }
}

struct TorrentNameCell: View {
    let torrent: Torrent

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: torrent.statusSymbol)
                .foregroundStyle(torrent.statusColor)
                .imageScale(.medium)
            Text(torrent.name)
                .lineLimit(1)
                .truncationMode(.middle)
        }
        .help(torrent.hasError ? torrent.errorString : torrent.name)
    }
}

struct TorrentProgressCell: View {
    let torrent: Torrent

    var body: some View {
        HStack(spacing: 6) {
            ProgressView(value: torrent.displayedProgress)
                .progressViewStyle(.linear)
                .controlSize(.small)
                .tint(torrent.isComplete ? .green : .accentColor)
            Text(Format.percent(torrent.displayedProgress))
                .monospacedDigit()
                .font(.caption)
                .frame(minWidth: 40, alignment: .trailing)
        }
    }
}
