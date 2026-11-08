namespace TransmissionKit;

/// <summary>Delays between attempts to reach a server that stopped answering.</summary>
/// <remarks>
/// Quick at first, because most drops (Wi-Fi roaming, a VPN reconnecting) last seconds, then
/// backing off to a minute so a server that's really down isn't hammered. Transmission Remote GUI
/// used a similar 5–60 second schedule.
/// </remarks>
public static class RetryBackoff
{
    public static IReadOnlyList<TimeSpan> Delays { get; } =
    [
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60),
    ];

    /// <summary>How long to wait after <paramref name="failures"/> failed attempts in a row.</summary>
    public static TimeSpan DelayAfterFailures(int failures) =>
        Delays[Math.Clamp(failures - 1, 0, Delays.Count - 1)];
}
