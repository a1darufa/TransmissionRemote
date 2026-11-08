import AppKit
import SwiftUI
import TransmissionKit

struct GeneralTab: View {
    let torrent: Torrent
    let details: TorrentDetails?

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                if torrent.hasError {
                    Label(torrent.errorString, systemImage: "exclamationmark.triangle.fill")
                        .foregroundStyle(.orange)
                        .textSelection(.enabled)
                }

                if let details, let bitfield = details.piecesBitfield, details.pieceCount > 0 {
                    PiecesBar(bitfield: bitfield, pieceCount: details.pieceCount)
                        .frame(height: 14)
                }

                HStack(alignment: .top, spacing: 32) {
                    InfoGrid(rows: transferRows)
                    InfoGrid(rows: torrentRows)
                }
            }
            .padding()
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    private var transferRows: [InfoGrid.Row] {
        var rows: [InfoGrid.Row] = [
            .init("Status", torrent.statusText),
            .init("Progress", "\(Format.percent(torrent.percentDone)) of \(torrent.sizeWhenDone.formattedBytes)"),
            .init("Downloaded", torrent.downloadedEver.formattedBytes),
            .init("Uploaded", torrent.uploadedEver.formattedBytes),
            .init("Ratio", Format.ratio(torrent.uploadRatio)),
            .init("Speed", "↓ \(torrent.rateDownload.formattedSpeed)   ↑ \(torrent.rateUpload.formattedSpeed)"),
            .init(
                "Peers",
                "\(torrent.peersSendingToUs) sending · \(torrent.peersGettingFromUs) receiving · "
                    + "\(torrent.peersConnected) connected"
            )
        ]
        if torrent.eta >= 0 { rows.append(.init("Remaining", Format.eta(torrent.eta))) }
        if let details {
            if details.corruptEver > 0 { rows.append(.init("Corrupt", details.corruptEver.formattedBytes)) }
            rows.append(.init("Downloading for", Format.duration(details.secondsDownloading)))
            rows.append(.init("Seeding for", Format.duration(details.secondsSeeding)))
        }
        return rows
    }

    private var torrentRows: [InfoGrid.Row] {
        var rows: [InfoGrid.Row] = [
            .init("Name", torrent.name),
            .init("Location", torrent.downloadDir),
            .init("Total size", torrent.totalSize.formattedBytes),
            .init("Hash", torrent.hashString, monospaced: true),
            .init("Added", Format.date(torrent.addedDate))
        ]
        if torrent.doneDate != nil { rows.append(.init("Completed", Format.date(torrent.doneDate))) }
        if torrent.isPrivate { rows.append(.init("Private", String(localized: "Yes (DHT and PEX disabled)"))) }
        if let details {
            if details.pieceCount > 0 {
                rows.append(.init("Pieces", "\(details.pieceCount) × \(details.pieceSize.formattedBytes)"))
            }
            if !details.creator.isEmpty { rows.append(.init("Created by", details.creator)) }
            if details.dateCreated != nil { rows.append(.init("Created on", Format.date(details.dateCreated))) }
            if !details.comment.isEmpty { rows.append(.init("Comment", details.comment)) }
        }
        return rows
    }
}

/// A two-column label/value grid with selectable values.
struct InfoGrid: View {
    struct Row: Identifiable {
        let label: LocalizedStringResource
        let value: String
        var monospaced = false
        var id: String { "\(label.key)" }

        init(_ label: LocalizedStringResource, _ value: String, monospaced: Bool = false) {
            self.label = label
            self.value = value
            self.monospaced = monospaced
        }
    }

    let rows: [Row]

    var body: some View {
        Grid(alignment: .leadingFirstTextBaseline, horizontalSpacing: 10, verticalSpacing: 5) {
            ForEach(rows) { row in
                GridRow {
                    Text(row.label)
                        .foregroundStyle(.secondary)
                        .gridColumnAlignment(.trailing)
                    Text(row.value)
                        .font(row.monospaced ? .system(.body, design: .monospaced) : .body)
                        .textSelection(.enabled)
                        .lineLimit(3)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// Which pieces we have, drawn as a bar: each pixel column shows the share of pieces present.
struct PiecesBar: View {
    let bitfield: Data
    let pieceCount: Int

    var body: some View {
        Canvas { context, size in
            let columns = max(Int(size.width), 1)
            let bytes = [UInt8](bitfield)
            context.fill(Path(CGRect(origin: .zero, size: size)), with: .color(.secondary.opacity(0.15)))

            for column in 0..<columns {
                let first = column * pieceCount / columns
                let last = max((column + 1) * pieceCount / columns, first + 1)
                let have = (first..<min(last, pieceCount)).count { Self.hasPiece($0, in: bytes) }
                guard have > 0 else { continue }

                let share = Double(have) / Double(last - first)
                let rect = CGRect(x: CGFloat(column), y: 0, width: 1, height: size.height)
                context.fill(Path(rect), with: .color(.accentColor.opacity(0.35 + 0.65 * share)))
            }
        }
        .clipShape(.rect(cornerRadius: 3))
        .accessibilityLabel("Piece availability")
    }

    private static func hasPiece(_ index: Int, in bytes: [UInt8]) -> Bool {
        let byte = index / 8
        guard byte < bytes.count else { return false }
        return bytes[byte] & (0x80 >> UInt8(index % 8)) != 0
    }
}
