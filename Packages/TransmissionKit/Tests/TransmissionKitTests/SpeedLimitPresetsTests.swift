import Testing
@testable import TransmissionKit

@Suite("Speed limit menu presets")
struct SpeedLimitPresetsTests {
    @Test func defaultListMatchesTransmissionRemoteGUI() {
        #expect(SpeedLimitPresets.parse(SpeedLimitPresets.defaultList)
            == [0, 10, 25, 50, 100, 250, 500, 750, 1000, 2500, 5000, 7000])
    }

    @Test func spacesDuplicatesAndOrderDontMatter() {
        #expect(SpeedLimitPresets.parse(" 500, 100 ,100;250 ") == [100, 250, 500])
    }

    @Test func invalidEntriesAreSkipped() {
        #expect(SpeedLimitPresets.parse("50, fast, -10, 2.5, 75") == [50, 75])
        #expect(SpeedLimitPresets.parse("").isEmpty)
    }
}
