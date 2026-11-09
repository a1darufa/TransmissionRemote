using TransmissionKit;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Presentation;

/// <summary>The torrent list's words from the app's resources.</summary>
internal sealed class TorrentListWords : ITorrentListWords
{
    public string RowName => Strings.TorrentRowNameStatusProgress;

    public string Status(TorrentStatusLabel label) => label switch
    {
        TorrentStatusLabel.Error => Strings.Error_1100F4,
        TorrentStatusLabel.Finished => Strings.Finished,
        TorrentStatusLabel.Stopped => Strings.Stopped,
        TorrentStatusLabel.QueuedToVerify => Strings.QueuedToVerify,
        TorrentStatusLabel.Verifying => Strings.Verifying_9A7FA7,
        TorrentStatusLabel.Queued => Strings.Queued,
        TorrentStatusLabel.GettingMetadata => Strings.GettingMetadata,
        TorrentStatusLabel.Downloading => Strings.Downloading,
        TorrentStatusLabel.QueuedToSeed => Strings.QueuedToSeed,
        TorrentStatusLabel.Seeding => Strings.Seeding,
        _ => label.ToString(),
    };

    public string Priority(Priority priority) => priority switch
    {
        TransmissionKit.Priority.High => Strings.High,
        TransmissionKit.Priority.Low => Strings.Low,
        _ => Strings.Normal,
    };

    public string TrackerStatus(TrackerStatus status) => status switch
    {
        TransmissionKit.TrackerStatus.Working => Strings.Working,
        TransmissionKit.TrackerStatus.Updating => Strings.UpdatingEllipsis,
        TransmissionKit.TrackerStatus.Failed => Strings.TrackerError,
        _ => "",
    };
}
