namespace TransmissionKit.Tests;

/// <summary>Speed limit menu presets.</summary>
[TestClass]
public sealed class SpeedLimitPresetsTests
{
    [TestMethod]
    public void DefaultListMatchesTransmissionRemoteGui()
    {
        SequenceAssert.AreEqual(
            SpeedLimitPresets.Parse(SpeedLimitPresets.DefaultList),
            0, 10, 25, 50, 100, 250, 500, 750, 1000, 2500, 5000, 7000);
    }

    [TestMethod]
    public void SpacesDuplicatesAndOrderDontMatter()
    {
        SequenceAssert.AreEqual(SpeedLimitPresets.Parse(" 500, 100 ,100;250 "), 100, 250, 500);
    }

    [TestMethod]
    public void InvalidEntriesAreSkipped()
    {
        SequenceAssert.AreEqual(SpeedLimitPresets.Parse("50, fast, -10, 2.5, 75"), 50, 75);
        Assert.IsEmpty(SpeedLimitPresets.Parse(""));
    }
}
