import SwiftUI
import TransmissionKit

struct PeersTab: View {
    let peers: [Peer]?

    @State private var sortOrder = [KeyPathComparator(\Peer.rateToClient, order: .reverse)]

    var body: some View {
        if let peers {
            Table(peers.sorted(using: sortOrder), sortOrder: $sortOrder) {
                TableColumn("Address", value: \.address) { peer in
                    HStack(spacing: 4) {
                        Text(peer.address).monospacedDigit()
                        if peer.isEncrypted {
                            Image(systemName: "lock.fill").foregroundStyle(.secondary).help("Encrypted")
                        }
                        if peer.isUtp {
                            Text("µTP").font(.caption2).foregroundStyle(.secondary)
                        }
                    }
                }
                .width(min: 120, ideal: 180)

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
            .overlay {
                if peers.isEmpty {
                    ContentUnavailableView("No Connected Peers", systemImage: "person.2.slash")
                }
            }
        } else {
            ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
        }
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
