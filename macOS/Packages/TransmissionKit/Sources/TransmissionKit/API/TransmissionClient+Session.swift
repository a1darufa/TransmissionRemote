import Foundation

extension TransmissionClient {
    public func sessionSettings() async throws(TransmissionError) -> SessionSettings {
        try await call(.sessionGet, params: Empty(), scope: .session, returning: SessionSettings.self)
    }

    /// Sends only the values present in `changes`.
    public func updateSession(_ changes: SessionSettings) async throws(TransmissionError) {
        guard !changes.isEmpty else { return }
        try await call(.sessionSet, params: changes, scope: .session)
    }

    public func sessionStats() async throws(TransmissionError) -> SessionStats {
        try await call(.sessionStats, params: Empty(), returning: SessionStats.self)
    }

    public func freeSpace(at path: String) async throws(TransmissionError) -> FreeSpace {
        try await call(.freeSpace, params: PathRequest(path: path), returning: FreeSpace.self)
    }

    /// Asks the daemon to check whether its peer port is reachable from the internet.
    public func testPort() async throws(TransmissionError) -> Bool {
        try await call(.portTest, params: Empty(), returning: PortTestResult.self).portIsOpen
    }

    /// Downloads the blocklist from the configured URL; returns the number of rules.
    public func updateBlocklist() async throws(TransmissionError) -> Int {
        try await call(.blocklistUpdate, params: Empty(), returning: BlocklistUpdateResult.self).blocklistSize
    }
}
