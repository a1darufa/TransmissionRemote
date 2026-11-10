namespace TransmissionRemote.ViewModels;

/// <summary>A point of the Speed tab's chart, both coordinates from 0 to 1, with Y growing upwards.</summary>
public readonly record struct ChartPoint(double X, double Y);

/// <summary>
/// The Speed tab's chart, ready to draw: the session's download and upload speed across the recorded samples,
/// scaled to a round top value with horizontal grid lines at round speeds.
/// </summary>
/// <param name="Scale">The speed at the top of the chart, in bytes per second.</param>
/// <param name="GridLines">The speeds that get a grid line and a label, from the lowest; the last is <paramref name="Scale"/>.</param>
public sealed record SpeedChart(
    long Scale,
    IReadOnlyList<long> GridLines,
    IReadOnlyList<ChartPoint> Download,
    IReadOnlyList<ChartPoint> Upload,
    long CurrentDownload,
    long CurrentUpload,
    long PeakDownload,
    long PeakUpload)
{
    /// <summary>An idle session still gets a readable scale instead of one that tops out at a byte per second.</summary>
    internal const long MinimumScale = 10_000;

    /// <summary>Aim for about four grid lines.</summary>
    private const int GridLineTarget = 4;

    /// <summary>The chart for these samples, oldest first, or null with fewer than two: one point draws no line.</summary>
    public static SpeedChart? For(IReadOnlyList<SpeedSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count < 2)
        {
            return null;
        }
        var peakDownload = samples.Max(sample => sample.Download);
        var peakUpload = samples.Max(sample => sample.Upload);
        var top = (double)Math.Max(Math.Max(peakDownload, peakUpload), MinimumScale);
        var step = RoundStep(top / GridLineTarget);
        var lines = (long)Math.Ceiling(top / step);
        var scale = step * lines;
        var gridLines = Enumerable.Range(1, (int)lines).Select(line => step * line).ToList();

        var start = samples[0].Time;
        var span = (samples[^1].Time - start).TotalSeconds;
        // Samples a refresh apart never share a time; if they did, spread them evenly instead of dividing by zero.
        double X(int index) => span > 0 ? (samples[index].Time - start).TotalSeconds / span : (double)index / (samples.Count - 1);
        List<ChartPoint> Line(Func<SpeedSample, long> speed) =>
            [.. samples.Select((sample, index) => new ChartPoint(X(index), (double)speed(sample) / scale))];

        return new SpeedChart(
            scale,
            gridLines,
            Line(sample => sample.Download),
            Line(sample => sample.Upload),
            samples[^1].Download,
            samples[^1].Upload,
            peakDownload,
            peakUpload);
    }

    /// <summary>The smallest of 1, 2 or 5 times a power of ten that's at least <paramref name="value"/>.</summary>
    internal static long RoundStep(double value)
    {
        var power = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(value, 1))));
        foreach (var multiple in new[] { 1, 2, 5, 10 })
        {
            if (multiple * power >= value)
            {
                return (long)(multiple * power);
            }
        }
        return (long)(10 * power);
    }
}
