using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TransmissionRemote.App.Logging;
using TransmissionRemote.App.Startup;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Shell;

/// <summary>Help → Open logs folder, also offered after an error nothing else caught.</summary>
internal static class LogsFolder
{
    /// <summary>Opens the folder in File Explorer; a failure is logged, not thrown.</summary>
    public static void Open(ILogger logger)
    {
        try
        {
            Directory.CreateDirectory(AppFolders.Logs);
            Process.Start(new ProcessStartInfo(AppFolders.Logs) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException
            or IOException or UnauthorizedAccessException)
        {
            Log.LogsFolderNotOpened(logger, error, LogPath.Display(AppFolders.Logs));
        }
    }
}
