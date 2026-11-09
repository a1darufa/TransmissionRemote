using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace TransmissionRemote.App;

/// <summary>
/// The entry point, written by hand (DISABLE_XAML_GENERATED_MAIN) so that work such as the single-instance
/// check can happen before XAML starts.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(callbackParameters =>
        {
            // Awaits in the view models resume on the UI thread through this context.
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
