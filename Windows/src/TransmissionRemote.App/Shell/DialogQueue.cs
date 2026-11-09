using Microsoft.UI.Xaml.Controls;

namespace TransmissionRemote.App.Shell;

/// <summary>
/// Shows content dialogs one at a time: a window can only have one open, and a second ShowAsync throws (brief 4.7).
/// A dialog asked for while another is open waits its turn.
/// </summary>
internal static class DialogQueue
{
    private static readonly SemaphoreSlim Turn = new(1, 1);

    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        await Turn.WaitAsync();
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            Turn.Release();
        }
    }
}
