using System.Text.Json.Nodes;

namespace TransmissionKit;

/// <summary>
/// A named set of speed limits that torrents can share, so for example all torrents from one
/// tracker stay under 2 MB/s together. Transmission 4.0 added these; they can be created and
/// changed over RPC, but not deleted.
/// </summary>
/// <param name="HonorsSessionLimits">
/// Whether the server's global (and alternative) limits apply on top of the group's.
/// </param>
/// <param name="SpeedLimitDown">KB/s.</param>
/// <param name="SpeedLimitUp">KB/s.</param>
public sealed record BandwidthGroup(
    string Name,
    bool HonorsSessionLimits = true,
    bool SpeedLimitDownEnabled = false,
    int SpeedLimitDown = 0,
    bool SpeedLimitUpEnabled = false,
    int SpeedLimitUp = 0)
{
    internal static BandwidthGroup FromJson(JsonObject json) => new(
        json.GetString("name") ?? "",
        json.GetBoolean("honorsSessionLimits") ?? true,
        json.GetBoolean("speedLimitDownEnabled") ?? false,
        json.GetInt32("speedLimitDown") ?? 0,
        json.GetBoolean("speedLimitUpEnabled") ?? false,
        json.GetInt32("speedLimitUp") ?? 0);

    internal JsonObject ToJson() => new()
    {
        ["name"] = Name,
        ["honorsSessionLimits"] = HonorsSessionLimits,
        ["speedLimitDownEnabled"] = SpeedLimitDownEnabled,
        ["speedLimitDown"] = SpeedLimitDown,
        ["speedLimitUpEnabled"] = SpeedLimitUpEnabled,
        ["speedLimitUp"] = SpeedLimitUp,
    };
}
