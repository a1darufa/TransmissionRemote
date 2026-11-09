using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TransmissionRemote.App.Shell;

/// <summary>"Remove 3 torrents?" before removing (brief 4.7.9; Mac: MainWindowPresentation).</summary>
internal static class RemoveConfirmation
{
    /// <summary>
    /// True when the user confirmed. Keeping the data, Enter removes, as in transgui; deleting it, Enter cancels,
    /// so a key press never deletes downloads.
    /// </summary>
    public static async Task<bool> AskAsync(XamlRoot root, int count, bool deletingData)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = StringResources.Format(Strings.RemoveTorrentFor(count), count.ToString("N0", CultureInfo.CurrentCulture)),
            Content = new TextBlock
            {
                Text = deletingData ? Strings.TheDownloadedFilesWillBeDeletedFromThe : Strings.DownloadedFilesStayOnTheServer,
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = deletingData ? Strings.RemoveAndDeleteData : Strings.Remove,
            CloseButtonText = Strings.Cancel,
            DefaultButton = deletingData ? ContentDialogButton.Close : ContentDialogButton.Primary,
        };
        return await DialogQueue.ShowAsync(dialog) == ContentDialogResult.Primary;
    }
}
