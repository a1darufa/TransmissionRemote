import Foundation

extension TransmissionClient {
    /// Bandwidth groups arrived in Transmission 4.0 (RPC 17).
    public var supportsBandwidthGroups: Bool { rpcVersion >= 17 }

    /// Every bandwidth group on the server, by name.
    public func bandwidthGroups() async throws(TransmissionError) -> [BandwidthGroup] {
        let response = try await call(.groupGet, params: Empty(), returning: GroupGetResponse.self)
        return response.group
            .filter { !$0.name.isEmpty }
            .sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
    }

    /// Creates the group, or replaces the limits of the group with the same name.
    public func save(_ group: BandwidthGroup) async throws(TransmissionError) {
        try await call(.groupSet, params: group, scope: .bandwidthGroup)
    }
}
