import SwiftUI
import TransmissionKit

struct PeersTab: View {
    @Environment(AppModel.self) private var model
    let peers: [Peer]?

    @AppStorage(AppSettings.resolvePeerHostNames) private var resolvesHostNames = true
    @AppStorage(AppSettings.showPeerCountries) private var showsCountries = true
    @State private var sortOrder = [KeyPathComparator(\Peer.rateToClient, order: .reverse)]

    var body: some View {
        if let peers {
            VStack(spacing: 0) {
                if showsCountries {
                    CountryDatabaseBanner(resolver: model.peerInfo, showsCountries: $showsCountries)
                }
                table(peers)
            }
            .task(id: peers.map(\.address)) {
                if resolvesHostNames { model.peerInfo.resolveHostNames(for: peers.map(\.address)) }
            }
            .task(id: showsCountries) {
                if showsCountries { await model.peerInfo.prepareCountries() }
            }
        } else {
            ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }

    private func table(_ peers: [Peer]) -> some View {
        Table(peers.sorted(using: sortOrder), sortOrder: $sortOrder) {
            TableColumn("Address", value: \.address) { peer in
                AddressCell(peer: peer, hostName: resolvesHostNames ? model.peerInfo.hostNames[peer.address] : nil)
            }
            .width(min: 120, ideal: 200)

            TableColumn("Port", value: \.port) { Text(String($0.port)).monospacedDigit() }
                .width(min: 40, ideal: 55)
                .alignment(.trailing)

            TableColumn("Country") { peer in
                if showsCountries, let code = model.peerInfo.countryCode(for: peer.address) {
                    Text("\(CountryDisplay.flag(for: code)) \(CountryDisplay.name(for: code))")
                        .lineLimit(1)
                }
            }
            .width(min: 60, ideal: 130)

            TableColumn("Client", value: \.clientName)
                .width(min: 80, ideal: 150)

            TableColumn("Progress", value: \.progress) { peer in
                Text(Format.percent(peer.progress)).monospacedDigit()
            }
            .width(min: 50, ideal: 70)
            .alignment(.trailing)

            TableColumn("Down", value: \.rateToClient) { peer in
                Text(peer.rateToClient.formattedSpeedOrBlank).monospacedDigit()
            }
            .width(min: 60, ideal: 85)
            .alignment(.trailing)

            TableColumn("Up", value: \.rateToPeer) { peer in
                Text(peer.rateToPeer.formattedSpeedOrBlank).monospacedDigit()
            }
            .width(min: 60, ideal: 85)
            .alignment(.trailing)

            TableColumn("Flags", value: \.flagStr) { peer in
                Text(peer.flagStr).font(.system(.body, design: .monospaced))
                    .help(PeerFlags.explanation(for: peer.flagStr))
            }
            .width(min: 50, ideal: 70)
        }
        .contextMenu(forSelectionType: Peer.ID.self) { _ in
            Toggle("Resolve Host Names", isOn: $resolvesHostNames)
            Toggle("Show Countries", isOn: $showsCountries)
        }
        .overlay {
            if peers.isEmpty {
                ContentUnavailableView("No Connected Peers", systemImage: "person.2.slash")
            }
        }
    }
}

/// The peer's host name when reverse DNS found one, otherwise its IP address.
private struct AddressCell: View {
    let peer: Peer
    let hostName: String?

    var body: some View {
        HStack(spacing: 4) {
            Text(hostName ?? peer.address)
                .monospacedDigit()
                .lineLimit(1)
                .truncationMode(.head)
                .help(hostName.map { "\($0) (\(peer.address))" } ?? peer.address)
            if peer.isEncrypted {
                Image(systemName: "lock.fill").foregroundStyle(.secondary).help("Encrypted")
            }
            if peer.isUtp {
                Text("µTP").font(.caption2).foregroundStyle(.secondary)
            }
        }
    }
}

/// Offers the one-time download of the country database, and shows its progress.
private struct CountryDatabaseBanner: View {
    let resolver: PeerInfoResolver
    @Binding var showsCountries: Bool

    var body: some View {
        switch resolver.databaseState {
        case .ready, .loading:
            EmptyView()
        case .downloading:
            banner {
                ProgressView().controlSize(.small)
                Text("Downloading the country database…")
            }
        case .notDownloaded, .failed:
            banner {
                Image(systemName: "globe").foregroundStyle(.secondary)
                VStack(alignment: .leading, spacing: 2) {
                    Text("Show where peers are from with DB-IP's free country database, about 5 MB.")
                    if case .failed(let message) = resolver.databaseState {
                        Text("Download failed: \(message)").font(.caption).foregroundStyle(.red)
                    }
                }
                Spacer()
                Button("Hide Countries") { showsCountries = false }
                Button("Download") { Task { await resolver.downloadDatabase() } }
                    .buttonStyle(.borderedProminent)
            }
        }
    }

    private func banner(@ViewBuilder content: () -> some View) -> some View {
        HStack(spacing: 8) { content() }
            .font(.callout)
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(.quaternary.opacity(0.5))
    }
}

/// Decodes Transmission's peer flag letters for the tooltip.
enum PeerFlags {
    private static let meanings: [Character: LocalizedStringResource] = [
        "O": "Optimistic unchoke",
        "D": "Downloading from this peer",
        "d": "We would download from this peer if they'd let us",
        "U": "Uploading to peer",
        "u": "We would upload to this peer if they'd ask",
        "K": "Peer has unchoked us, but we're not interested",
        "?": "We unchoked this peer, but they're not interested",
        "E": "Encrypted connection",
        "H": "Peer found through DHT",
        "X": "Peer found through peer exchange (PEX)",
        "I": "Incoming connection",
        "T": "Connected over µTP"
    ]

    static func explanation(for flags: String) -> String {
        flags.compactMap { meanings[$0].map { String(localized: $0) } }.joined(separator: "\n")
    }
}
