import Foundation
import Testing
@testable import TransmissionKit

@Suite("Bandwidth groups on Transmission 4.0 and 4.1")
struct BandwidthGroupTests {
    private let group = BandwidthGroup(
        name: "Linux ISOs", honorsSessionLimits: false,
        speedLimitDownEnabled: true, speedLimitDown: 2_000, speedLimitUpEnabled: false, speedLimitUp: 500
    )

    @Test func legacyGroupKeysAreKebabCaseExceptHonorsSessionLimits() throws {
        #expect(try encodedKeys(for: .legacy) == [
            "name", "honorsSessionLimits", "speed-limit-down-enabled", "speed-limit-down",
            "speed-limit-up-enabled", "speed-limit-up"
        ])
    }

    @Test func jsonRPCGroupKeysAreSnakeCase() throws {
        #expect(try encodedKeys(for: .jsonRPC) == [
            "name", "honors_session_limits", "speed_limit_down_enabled", "speed_limit_down",
            "speed_limit_up_enabled", "speed_limit_up"
        ])
    }

    @Test func decodesTransmission40Groups() throws {
        let response = #"""
        {"result": "success", "arguments": {"group": [{
          "name": "Linux ISOs", "honorsSessionLimits": false,
          "speed-limit-down-enabled": true, "speed-limit-down": 2000,
          "speed-limit-up-enabled": false, "speed-limit-up": 500
        }]}}
        """#
        let decoded = try RPCCoding.decoder(for: .legacy).decode(
            LegacyResponse<GroupGetResponse>.self, from: Data(response.utf8)
        )
        #expect(decoded.arguments?.group == [group])
    }

    @Test func decodesTransmission41GroupsWithDoubleSpeeds() throws {
        // Transmission 4.1.0 briefly sent the limits as doubles.
        let response = #"""
        {"jsonrpc": "2.0", "id": 3, "result": {"group": [{
          "name": "Linux ISOs", "honors_session_limits": false,
          "speed_limit_down_enabled": true, "speed_limit_down": 2000.0,
          "speed_limit_up_enabled": false, "speed_limit_up": 500.0
        }]}}
        """#
        let decoded = try RPCCoding.decoder(for: .jsonRPC).decode(
            JSONRPCResponse<GroupGetResponse>.self, from: Data(response.utf8)
        )
        #expect(decoded.result?.group == [group])
    }

    @Test func groupFieldNeedsTransmission40() {
        #expect(TorrentField.group.minimumRPCVersion == 17)
    }

    /// The group's wire keys when sent as request parameters, one level inside the envelope.
    private func encodedKeys(for dialect: RPCDialect) throws -> Set<String> {
        struct Envelope: Encodable {
            let params: BandwidthGroup
        }
        let data = try RPCCoding.encoder(for: dialect, scope: .bandwidthGroup).encode(Envelope(params: group))
        let object = try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        let params = try #require(object["params"] as? [String: Any])
        return Set(params.keys)
    }
}
