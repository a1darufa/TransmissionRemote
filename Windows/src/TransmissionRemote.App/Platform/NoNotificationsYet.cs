using TransmissionKit;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Platform;

/// <summary>
/// Stands in for completion notifications until they're built with <c>AppNotificationManager</c> (milestone M6):
/// finished downloads aren't announced yet.
/// </summary>
internal sealed class NoNotificationsYet : INotificationService
{
    public void ShowDownloadComplete(Torrent torrent)
    {
    }
}
