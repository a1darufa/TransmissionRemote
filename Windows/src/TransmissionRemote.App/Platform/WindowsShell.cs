using System.ComponentModel;
using System.Diagnostics;
using TransmissionRemote.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace TransmissionRemote.App.Platform;

/// <summary>Opening files with their default app, showing them in File Explorer, and the clipboard.</summary>
internal sealed class WindowsShell : IShell
{
    public bool Open(string localPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(localPath) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }

    /// <summary>File Explorer with the first item selected; several at once come with the Files tab (M3).</summary>
    public void Reveal(IReadOnlyList<string> localPaths)
    {
        ArgumentNullException.ThrowIfNull(localPaths);
        if (localPaths.Count == 0)
        {
            return;
        }
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        start.ArgumentList.Add($"/select,{localPaths[0]}");
        try
        {
            Process.Start(start)?.Dispose();
        }
        catch (Win32Exception)
        {
            // File Explorer is always there; if it can't start, there's nothing better to offer.
        }
    }

    public void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        // Keeps the text on the clipboard after the app exits.
        Clipboard.Flush();
    }
}
