using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace TransmissionRemote.App.Startup;

/// <summary>
/// Launches handed over by later copies of the app, delivered on the UI thread. Those that arrive before the
/// window is ready wait for it, so a .torrent file opened during startup isn't lost.
/// </summary>
internal sealed class RedirectedActivations
{
    private readonly Lock _gate = new();
    private readonly Queue<AppActivationArguments> _waiting = new();
    private Action<AppActivationArguments>? _handler;
    private DispatcherQueue? _uiThread;

    /// <summary>Takes a hand-over. Called on a background thread.</summary>
    public void Receive(AppActivationArguments arguments)
    {
        lock (_gate)
        {
            if (_handler is null || _uiThread is null)
            {
                _waiting.Enqueue(arguments);
                return;
            }
            Deliver(arguments);
        }
    }

    /// <summary>
    /// Starts delivering hand-overs to <paramref name="handler"/> on the calling (UI) thread, first those that
    /// were waiting.
    /// </summary>
    public void DeliverTo(Action<AppActivationArguments> handler)
    {
        lock (_gate)
        {
            _handler = handler;
            _uiThread = DispatcherQueue.GetForCurrentThread();
            while (_waiting.TryDequeue(out var arguments))
            {
                Deliver(arguments);
            }
        }
    }

    private void Deliver(AppActivationArguments arguments)
    {
        var handler = _handler!;
        _uiThread!.TryEnqueue(() => handler(arguments));
    }
}
