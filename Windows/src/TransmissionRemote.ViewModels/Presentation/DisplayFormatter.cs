using System.Globalization;
using System.Text;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>
/// Unit names for sizes and speeds. The defaults are File Explorer's English ones; the app loads localized ones
/// from its resources.
/// </summary>
public sealed record ByteUnitLabels
{
    public string Bytes { get; init; } = "bytes";
    public string Kilobytes { get; init; } = "KB";
    public string Megabytes { get; init; } = "MB";
    public string Gigabytes { get; init; } = "GB";
    public string Terabytes { get; init; } = "TB";

    /// <summary>Appended to a size to make it a speed: "1.50 MB" and "/s" make "1.50 MB/s".</summary>
    public string PerSecond { get; init; } = "/s";
}

/// <summary>
/// Narrow duration units, each a format with the number as <c>{0}</c>, so languages can place the unit and
/// spacing their own way ("{0}h" makes "2h", "{0} ч" makes "2 ч").
/// </summary>
public sealed record DurationUnitLabels
{
    public string Days { get; init; } = "{0}d";
    public string Hours { get; init; } = "{0}h";
    public string Minutes { get; init; } = "{0}m";
    public string Seconds { get; init; } = "{0}s";

    /// <summary>Between the two units of "2h 5m".</summary>
    public string Separator { get; init; } = " ";
}

/// <summary>
/// Turns sizes, speeds, durations, ratios and dates into text for the lists and panes, with the app's culture and
/// unit labels. Free of words itself, so it's tested without resources.
/// </summary>
/// <remarks>
/// Sizes use binary units with KB, MB, GB and TB, like File Explorer, while the Mac app shows Transmission's own
/// decimal units the way Finder does: each app looks like its platform.
/// </remarks>
public sealed class DisplayFormatter
{
    private static readonly long[] UnitSizes = [1L << 10, 1L << 20, 1L << 30, 1L << 40];

    private readonly ByteUnitLabels _byteUnits;
    private readonly string _separator;
    private readonly CompositeFormat _days;
    private readonly CompositeFormat _hours;
    private readonly CompositeFormat _minutes;
    private readonly CompositeFormat _seconds;
    private readonly TimeZoneInfo _timeZone;

    /// <param name="culture">For digits, separators and dates; usually the app's UI culture.</param>
    /// <param name="timeZone">Where dates are shown; the PC's own by default.</param>
    /// <exception cref="FormatException">A duration label isn't a format with at most <c>{0}</c> in it.</exception>
    public DisplayFormatter(
        CultureInfo culture,
        ByteUnitLabels? byteUnits = null,
        DurationUnitLabels? durationUnits = null,
        TimeZoneInfo? timeZone = null)
    {
        Culture = culture ?? throw new ArgumentNullException(nameof(culture));
        _byteUnits = byteUnits ?? new ByteUnitLabels();
        var durations = durationUnits ?? new DurationUnitLabels();
        // Parsed here, so a broken translation fails once at startup rather than on every row.
        _days = DurationFormat(durations.Days);
        _hours = DurationFormat(durations.Hours);
        _minutes = DurationFormat(durations.Minutes);
        _seconds = DurationFormat(durations.Seconds);
        _separator = durations.Separator;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public CultureInfo Culture { get; }

    /// <summary>
    /// A size the way File Explorer writes it: three significant digits, cut rather than rounded, switching to the
    /// next unit at 1000 so a number never has four digits: "532 bytes", "1.30 KB", "22.9 KB", "0.97 MB".
    /// </summary>
    public string Bytes(long bytes)
    {
        if (bytes < 1000)
        {
            return $"{Math.Max(bytes, 0).ToString("N0", Culture)} {_byteUnits.Bytes}";
        }
        var unit = 0;
        while (unit < UnitSizes.Length - 1 && (decimal)bytes / UnitSizes[unit] >= 1000)
        {
            unit++;
        }
        var value = (decimal)bytes / UnitSizes[unit];
        var (decimals, scale) = value switch
        {
            < 10 => (2, 100m),
            < 100 => (1, 10m),
            _ => (0, 1m),
        };
        // decimal, so cutting to two places is exact: 1.30 KB mustn't become 1.29 because of binary fractions.
        var truncated = Math.Floor(value * scale) / scale;
        return $"{truncated.ToString("N" + decimals, Culture)} {UnitLabel(unit)}";
    }

    public string Speed(long bytesPerSecond) => Bytes(bytesPerSecond) + _byteUnits.PerSecond;

    /// <summary>Blank for zero, so idle rows in the torrent list stay quiet.</summary>
    public string SpeedOrBlank(long bytesPerSecond) => bytesPerSecond > 0 ? Speed(bytesPerSecond) : "";

    /// <summary>
    /// A speed limit as typed: Transmission's limits are in its kB/s (1000 bytes), and converting them to binary
    /// units would turn a typed 100 into 97.6. The label is the same KB/s, as in Transmission Remote GUI.
    /// </summary>
    public string SpeedLimit(int kilobytesPerSecond) =>
        $"{kilobytesPerSecond.ToString("N0", Culture)} {_byteUnits.Kilobytes}{_byteUnits.PerSecond}";

    /// <summary>
    /// "45.3%", or "100%" when done. Cut rather than rounded, so an unfinished torrent never reads 100.0%.
    /// </summary>
    public string Percent(double fraction)
    {
        if (double.IsNaN(fraction) || fraction <= 0)
        {
            return 0.0.ToString("P0", Culture);
        }
        if (fraction >= 1)
        {
            return 1.0.ToString("P0", Culture);
        }
        // Through decimal, so 0.453 stays 45.3% instead of becoming 45.2% after a binary multiplication.
        var truncated = Math.Floor((decimal)fraction * 1000) / 1000;
        return truncated.ToString("P1", Culture);
    }

    /// <summary>Blank when Transmission doesn't know (-1) or can't tell (-2).</summary>
    public string Eta(int seconds) => seconds >= 0 ? Duration(seconds) : "";

    /// <summary>
    /// At most two units, the largest first, like the Mac app's narrow style: "2d 5h", "1h", "45s". Smaller
    /// units are dropped, not rounded.
    /// </summary>
    public string Duration(long seconds)
    {
        if (seconds <= 0)
        {
            return string.Format(Culture, _seconds, 0);
        }
        (long Value, CompositeFormat Format)[] parts =
        [
            (seconds / 86_400, _days),
            (seconds / 3_600 % 24, _hours),
            (seconds / 60 % 60, _minutes),
            (seconds % 60, _seconds),
        ];
        var largest = Array.FindIndex(parts, part => part.Value > 0);
        var text = string.Format(Culture, parts[largest].Format, parts[largest].Value);
        if (largest + 1 < parts.Length && parts[largest + 1] is { Value: > 0 } next)
        {
            text += _separator + string.Format(Culture, next.Format, next.Value);
        }
        return text;
    }

    /// <summary>Transmission uses -1 for "not available" and -2 for "infinite".</summary>
    public string Ratio(double ratio) => ratio switch
    {
        -2 => "∞",
        < 0 or double.NaN => "–",
        _ => ratio.ToString("N2", Culture),
    };

    /// <summary>Short date and time in the culture's style, such as "11/8/2026 3:45 PM"; blank for none.</summary>
    public string Date(DateTimeOffset? date) =>
        date is { } value ? TimeZoneInfo.ConvertTime(value, _timeZone).ToString("g", Culture) : "";

    /// <summary>A count with the culture's digit grouping: "1,234".</summary>
    public string Count(int count) => count.ToString("N0", Culture);

    /// <summary>
    /// Connected seeders, then the swarm's seeders when a tracker reported them: "3 (172)", like Transmission
    /// Remote GUI.
    /// </summary>
    public string SeedsText(Torrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return CountText(torrent.PeersSendingToUs, torrent.TrackerSummary?.Seeders);
    }

    /// <summary>Connected downloading peers, then the swarm's leechers when a tracker reported them.</summary>
    public string PeersText(Torrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return CountText(torrent.PeersGettingFromUs, torrent.TrackerSummary?.Leechers);
    }

    private string CountText(int connected, int? swarm) =>
        swarm is { } total ? $"{Count(connected)} ({Count(total)})" : Count(connected);

    private string UnitLabel(int unit) => unit switch
    {
        0 => _byteUnits.Kilobytes,
        1 => _byteUnits.Megabytes,
        2 => _byteUnits.Gigabytes,
        _ => _byteUnits.Terabytes,
    };

    private static CompositeFormat DurationFormat(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        var parsed = CompositeFormat.Parse(format);
        return parsed.MinimumArgumentCount <= 1
            ? parsed
            : throw new FormatException($"The duration format \"{format}\" uses more than {{0}}.");
    }
}
