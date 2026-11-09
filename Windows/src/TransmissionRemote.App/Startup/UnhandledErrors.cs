using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TransmissionRemote.App.Logging;
using TransmissionRemote.App.Shell;
using TransmissionRemote.ViewModels;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TransmissionRemote.App.Startup;

/// <summary>
/// Catches errors nothing else catches: logs them as Critical with the exception, then offers to open the logs
/// folder. Errors on the UI thread or a background thread end the app; a failed task nobody awaited doesn't.
/// </summary>
/// <remarks>
/// The message box is Windows' own rather than a XAML dialog, because XAML may be what failed. Its texts are
/// loaded at startup so that reporting an error doesn't depend on loading resources.
/// </remarks>
internal sealed class UnhandledErrors(ILogger<UnhandledErrors> logger)
{
    private readonly string _title = Strings.TransmissionRemote;
    private readonly string _fatalMessage = Strings.TransmissionRemoteRanIntoAProblemAndHas;
    private readonly string _backgroundMessage = Strings.TransmissionRemoteRanIntoAProblemInThe;

    private DispatcherQueue? _uiThread;
    private int _isShowingMessage;

    /// <summary>Errors outside XAML: background threads and unobserved tasks. Call first thing in Main.</summary>
    public void WatchProcess()
    {
        AppDomain.CurrentDomain.UnhandledException += OnBackgroundThreadError;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskError;
    }

    /// <summary>Errors in XAML event handlers and bindings. Call when the application object exists.</summary>
    public void WatchApplication(Application application)
    {
        _uiThread = DispatcherQueue.GetForCurrentThread();
        application.UnhandledException += OnUiThreadError;
    }

    private void OnUiThreadError(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.UnhandledUiError(logger, e.Exception);
        // Handled, so the app can exit through Main and flush the log, rather than being torn down by WinUI.
        e.Handled = true;
        ShowMessage(_fatalMessage);
        Application.Current.Exit();
    }

    private void OnBackgroundThreadError(object sender, System.UnhandledExceptionEventArgs e)
    {
        Log.UnhandledBackgroundError(logger, e.ExceptionObject as Exception);
        // The process ends after this handler, so write the log before waiting for the user.
        AppLogging.Stop();
        ShowMessage(_fatalMessage);
    }

    private void OnUnobservedTaskError(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.UnobservedTaskError(logger, e.Exception);
        e.SetObserved();
        // This runs on the finalizer thread, which must not wait for the user.
        if (_uiThread is null || !_uiThread.TryEnqueue(() => ShowMessage(_backgroundMessage)))
        {
            _ = Task.Run(() => ShowMessage(_backgroundMessage));
        }
    }

    /// <summary>Asks whether to open the logs folder; one message at a time, however many errors follow.</summary>
    private void ShowMessage(string message)
    {
        if (Interlocked.Exchange(ref _isShowingMessage, 1) == 1)
        {
            return;
        }
        try
        {
            var style = MESSAGEBOX_STYLE.MB_YESNO | MESSAGEBOX_STYLE.MB_ICONERROR | MESSAGEBOX_STYLE.MB_TASKMODAL
                | MESSAGEBOX_STYLE.MB_SETFOREGROUND;
            if (PInvoke.MessageBox(HWND.Null, message, _title, style) == MESSAGEBOX_RESULT.IDYES)
            {
                LogsFolder.Open(logger);
            }
        }
        finally
        {
            Volatile.Write(ref _isShowingMessage, 0);
        }
    }
}
