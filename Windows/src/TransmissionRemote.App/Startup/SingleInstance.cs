using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppLifecycle;
using TransmissionRemote.App.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace TransmissionRemote.App.Startup;

/// <summary>
/// Keeps one copy of the app running. A later launch (the Start menu, a double-clicked .torrent file, a magnet
/// link) hands its activation to the running copy and exits.
/// </summary>
internal static class SingleInstance
{
    private const string Key = "main";

    /// <summary>
    /// Registers this process as the app's main instance, or hands the launch to the one that is already running.
    /// </summary>
    /// <returns>True when this process should go on and start the app; false when it should exit.</returns>
    public static bool TryBecomeMainInstance(RedirectedActivations activations, ILogger logger)
    {
        var mainInstance = AppInstance.FindOrRegisterForKey(Key);
        if (mainInstance.IsCurrent)
        {
            // Raised on a background thread; RedirectedActivations moves it to the UI thread.
            mainInstance.Activated += (_, arguments) => activations.Receive(arguments);
            return true;
        }

        var arguments = AppInstance.GetCurrent().GetActivatedEventArgs();
        try
        {
            // This launch came from the user, so Windows lets it give the running copy permission to come to
            // the front; without it the window would only flash in the taskbar.
            PInvoke.AllowSetForegroundWindow(mainInstance.ProcessId);
            RedirectAndWait(mainInstance, arguments);
            Log.LaunchHandedOver(logger, arguments.Kind, mainInstance.ProcessId);
            return false;
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or TimeoutException)
        {
            // Better a second copy than a launch that does nothing.
            Log.LaunchNotHandedOver(logger, error, mainInstance.ProcessId);
            return true;
        }
    }

    /// <summary>
    /// Waits for the hand-over without blocking this STA thread's COM message pumping, as Microsoft's
    /// single-instance sample does: the redirect runs on the thread pool and this thread waits in
    /// CoWaitForMultipleObjects.
    /// </summary>
    private static void RedirectAndWait(AppInstance mainInstance, AppActivationArguments arguments)
    {
        using var done = new ManualResetEvent(initialState: false);
        Exception? failure = null;
        _ = Task.Run(() =>
        {
            try
            {
                mainInstance.RedirectActivationToAsync(arguments).AsTask().Wait();
            }
            catch (AggregateException error)
            {
                failure = error.InnerException ?? error;
            }
            finally
            {
                done.Set();
            }
        });

        var handle = new HANDLE(done.SafeWaitHandle.DangerousGetHandle());
        var result = PInvoke.CoWaitForMultipleObjects(0, (uint)TimeSpan.FromSeconds(30).TotalMilliseconds,
            [handle], out _);
        GC.KeepAlive(done);
        if (result.Failed)
        {
            throw new TimeoutException($"The running copy didn't take the launch (0x{result.Value:X8}).");
        }
        if (failure is not null)
        {
            throw failure;
        }
    }
}
