import Foundation
import Observation
import TransmissionKit
import UniformTypeIdentifiers

extension UTType {
    static let torrent = UTType(importedAs: "org.bittorrent.torrent")
}

/// Something the user wants to add, waiting for confirmation in the Add Torrent sheet.
struct PendingTorrent: Identifiable {
    enum Source {
        case torrentFile(Data)
        /// A magnet link or an http(s) URL the daemon fetches itself.
        case link(String)
    }

    let id = UUID()
    let source: Source
    let name: String
    let infoHash: String?
    /// Known only for `.torrent` files; magnet links have no file list until the daemon gets metadata.
    let files: [FileEntry]

    var totalSize: Int64? {
        files.isEmpty ? nil : files.reduce(0) { $0 + $1.length }
    }
}

/// Collects torrents from every entry point (Open dialog, drag and drop, Finder, browser
/// magnet links, paste) and queues them for the Add Torrent sheet.
@Observable
final class AddTorrentFlow {
    private(set) var queue: [PendingTorrent] = []
    /// Shown as an alert when something can't be opened.
    var problem: String?

    private let connection: ServerConnection
    /// Links typed into the Add Link sheet, held until that sheet has fully closed:
    /// presenting the Add Torrent sheet while another sheet is closing can drop it.
    private var linksAwaitingDismissal: String?

    init(connection: ServerConnection) {
        self.connection = connection
    }

    var current: PendingTorrent? { queue.first }

    func finishCurrent() {
        if !queue.isEmpty { queue.removeFirst() }
    }

    func openLinksAfterSheetDismissal(_ text: String) {
        linksAwaitingDismissal = text
    }

    func sheetDidDismiss() {
        guard let text = linksAwaitingDismissal else { return }
        linksAwaitingDismissal = nil
        openLinks(in: text)
    }

    // MARK: - Entry points

    func open(_ urls: [URL]) {
        for url in urls {
            if url.isFileURL {
                openTorrentFile(at: url)
            } else {
                openLink(url.absoluteString)
            }
        }
    }

    /// Handles pasted text: magnet links and http(s) URLs, one per line.
    @discardableResult
    func openLinks(in text: String) -> Bool {
        let links = text.split(whereSeparator: \.isNewline)
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { MagnetLink($0) != nil || $0.isTorrentDownloadURL }
        links.forEach(openLink)
        return !links.isEmpty
    }

    func openLink(_ text: String) {
        let link = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if let magnet = MagnetLink(link) {
            enqueue(PendingTorrent(
                source: .link(magnet.url.absoluteString),
                name: magnet.displayName ?? String(localized: "Magnet link"),
                infoHash: magnet.infoHash,
                files: []
            ))
        } else if link.isTorrentDownloadURL, let url = URL(string: link) {
            enqueue(PendingTorrent(source: .link(link), name: url.lastPathComponent, infoHash: nil, files: []))
        } else {
            problem = String(localized: "“\(link)” isn't a magnet link or a torrent URL.")
        }
    }

    private func openTorrentFile(at url: URL) {
        let isScoped = url.startAccessingSecurityScopedResource()
        defer { if isScoped { url.stopAccessingSecurityScopedResource() } }

        do {
            let size = try url.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0
            guard size <= TorrentMetainfo.maximumFileSize else {
                problem = String(localized: "“\(url.lastPathComponent)” is too large to be a torrent file.")
                return
            }
            let data = try Data(contentsOf: url)
            let metainfo = try TorrentMetainfo(data: data)
            enqueue(PendingTorrent(
                source: .torrentFile(data),
                name: metainfo.name,
                infoHash: metainfo.infoHash,
                files: metainfo.files
            ))
        } catch {
            problem = String(localized: "“\(url.lastPathComponent)” couldn't be opened as a torrent file.")
        }
    }

    private func enqueue(_ pending: PendingTorrent) {
        if UserDefaults.standard.bool(forKey: AppSettings.showAddTorrentDialog) {
            queue.append(pending)
        } else {
            addWithDefaults(pending)
        }
    }

    /// Skips the sheet: adds to the server's default folder and starts per the server's setting.
    private func addWithDefaults(_ pending: PendingTorrent) {
        Task {
            do {
                _ = try await connection.add(AddTorrentRequest(pending, options: AddTorrentOptions()))
            } catch {
                problem = error.localizedDescription
            }
        }
    }
}

/// What the user chose in the Add Torrent sheet.
struct AddTorrentOptions {
    var downloadDir: String?
    var startWhenAdded: Bool?
    var priority: Priority = .normal
    var unwantedFiles: Set<Int> = []
    var labels: [String] = []
    var sequentialDownload = false
}

extension AddTorrentRequest {
    init(_ pending: PendingTorrent, options: AddTorrentOptions, serverInfo: ServerInfo? = nil) {
        switch pending.source {
        case .torrentFile(let data): self = .torrentFile(data)
        case .link(let link): self = .magnetOrURL(link)
        }
        downloadDir = options.downloadDir
        paused = options.startWhenAdded.map { !$0 }
        bandwidthPriority = options.priority == .normal ? nil : options.priority
        filesUnwanted = options.unwantedFiles.isEmpty ? nil : options.unwantedFiles.sorted()
        if serverInfo?.supportsLabelsOnAdd == true, !options.labels.isEmpty {
            labels = options.labels
        }
        if serverInfo?.supportsSequentialDownload == true, options.sequentialDownload {
            sequentialDownload = true
        }
    }
}
