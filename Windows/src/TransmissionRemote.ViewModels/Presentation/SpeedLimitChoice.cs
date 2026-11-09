using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>
/// One item of a speed limit menu, like Transmission Remote GUI's: "Unlimited" (a null limit) or a speed in kB/s,
/// and whether it's the limit in force.
/// </summary>
public sealed record SpeedLimitChoice(int? Limit, bool IsCurrent)
{
    /// <summary>
    /// The speeds listed after "Unlimited": the presets from the settings, smallest first. "Unlimited" is always
    /// offered, so a 0 in the list, which is how Transmission Remote GUI wrote it, isn't repeated.
    /// </summary>
    public static IReadOnlyList<int> Speeds(string presets) =>
        [.. SpeedLimitPresets.Parse(presets).Where(speed => speed > 0)];
}
