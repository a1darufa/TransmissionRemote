using System.Globalization;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace TransmissionRemote.App.Logging;

/// <summary>
/// The log files: TransmissionRemote-&lt;date&gt;.log in the Logs folder, one per day, kept 14 days, at most 10 MB a
/// day, the same as the Mac app. Information by default; Debug while "Detailed logging" is on.
/// </summary>
internal static class AppLogging
{
    private const long MaximumBytesPerDay = 10 * 1024 * 1024;

    private const string LineTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}{EventTag}: {Message:lj}{NewLine}{Exception}";

    private static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);

    public static void Start(string logsDirectory)
    {
        Serilog.Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
            .Enrich.With<EventIdEnricher>()
            .WriteTo.Async(sink => sink.File(
                path: Path.Combine(logsDirectory, "TransmissionRemote-.log"),
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate: LineTemplate,
                rollingInterval: RollingInterval.Day,
                // Like the Mac app: when a runaway loop fills today's file, logging pauses until tomorrow
                // rather than filling the disk or pushing older days out.
                fileSizeLimitBytes: MaximumBytesPerDay,
                rollOnFileSizeLimit: false,
                retainedFileCountLimit: null,
                retainedFileTimeLimit: TimeSpan.FromDays(14),
                // A second instance that only hands its files to the running one logs to the same file.
                shared: true))
            .CreateLogger();
    }

    /// <summary>Detailed logging adds every request to the server (the core's Debug events).</summary>
    public static void SetDetailed(bool isDetailed) =>
        LevelSwitch.MinimumLevel = isDetailed ? LogEventLevel.Debug : LogEventLevel.Information;

    public static void Stop() => Serilog.Log.CloseAndFlush();

    /// <summary>
    /// Writes Microsoft.Extensions.Logging's event ID after the category (" 1101"), so a line can be matched to
    /// the event tables in Windows/README.md.
    /// </summary>
    private sealed class EventIdEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var tag = logEvent.Properties.TryGetValue("EventId", out var value)
                && value is StructureValue structure
                && structure.Properties.FirstOrDefault(property => property.Name == "Id")?.Value is ScalarValue id
                ? $" {id.Value}"
                : string.Empty;
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("EventTag", tag));
        }
    }
}
