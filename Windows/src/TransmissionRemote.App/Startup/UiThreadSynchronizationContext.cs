using Microsoft.UI.Dispatching;

namespace TransmissionRemote.App.Startup;

/// <summary>
/// Resumes awaits in the view models on the UI thread, as WinUI's DispatcherQueueSynchronizationContext does, but
/// without its exception once the UI thread has ended. A request still in flight when the window closes finishes
/// after the queue has shut down; there's nothing left on screen to update then, and throwing on a thread-pool
/// thread would end the process before the log is flushed.
/// </summary>
internal sealed class UiThreadSynchronizationContext(DispatcherQueue queue) : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        // False only after the queue has shut down at exit.
        _ = queue.TryEnqueue(() => d(state));
    }

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("Waiting for the UI thread could deadlock it; post instead.");

    public override SynchronizationContext CreateCopy() => new UiThreadSynchronizationContext(queue);
}
