using System.Diagnostics;

namespace TransmissionKit;

/// <summary>
/// Notices that the daemon restarted between two refreshes. A restarted daemon numbers its torrents afresh,
/// so torrent IDs kept from before (the selection, the list that recent changes are applied to) may now
/// mean other torrents.
/// </summary>
/// <remarks>
/// Transmission doesn't announce a restart, but its uptime shows it: the current session's
/// <see cref="SessionTotals.SecondsActive"/> starts again from zero. Comparing the uptime with the time
/// that passed, not just with the previous uptime, also catches a restart while the app wasn't refreshing
/// for longer than the new run has lasted, such as during a long outage or while the PC slept (the
/// timestamps keep counting during sleep). The cumulative session count isn't used: Transmission only
/// saves it when something was transferred, so an idle daemon reports 1 after every restart.
/// Times are timestamps from the detector's <see cref="TimeProvider"/> (<see cref="Stopwatch.GetTimestamp"/>
/// by default).
/// </remarks>
/// <param name="time">Where timestamps come from; tests pass a fake one.</param>
public sealed class DaemonRestartDetector(TimeProvider? time = null)
{
    /// <summary>
    /// Transmission counts uptime in whole seconds, so it can grow by up to a second less than the time
    /// that passed; the rest allows for a small adjustment of the server's clock.
    /// </summary>
    internal static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private (TimeSpan Uptime, long AnsweredAt)? _last;

    /// <summary>Compares session stats with the previous ones, and keeps them for the next call.</summary>
    /// <param name="stats">A session-stats answer.</param>
    /// <param name="requestedAt">When the request was sent.</param>
    /// <param name="answeredAt">When its answer had arrived; later is fine.</param>
    /// <returns>Whether the daemon restarted since the previous stats.</returns>
    public bool Observe(SessionStats stats, long requestedAt, long answeredAt)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (stats.CurrentStats is not { } current)
        {
            return false;
        }
        var uptime = TimeSpan.FromSeconds(current.SecondsActive);
        var restarted = _last is { } last && HasRestarted(last.Uptime, last.AnsweredAt, uptime, requestedAt);
        _last = (uptime, answeredAt);
        return restarted;
    }

    /// <summary>Forgets the previous stats, for example after reconnecting.</summary>
    public void Reset() => _last = null;

    private bool HasRestarted(TimeSpan previousUptime, long previousAnswer, TimeSpan uptime, long requestedAt)
    {
        // A daemon that doesn't report its uptime reads as 0 every time; there's nothing to compare then.
        if (previousUptime == TimeSpan.Zero)
        {
            return false;
        }
        // The daemon read its clock for the previous answer before that answer arrived, and for this one
        // after the request was sent, so at least this much time passed between the two readings.
        var passed = _time.GetElapsedTime(previousAnswer, requestedAt);
        if (passed < TimeSpan.Zero)
        {
            passed = TimeSpan.Zero;
        }
        return uptime < previousUptime + passed - Tolerance;
    }
}
