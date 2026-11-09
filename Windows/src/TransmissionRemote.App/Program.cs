using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Serilog.Extensions.Logging;
using TransmissionRemote.App.Logging;
using TransmissionRemote.App.Startup;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App;

/// <summary>
/// The entry point, written by hand (DISABLE_XAML_GENERATED_MAIN) so that logging and the single-instance check
/// start before XAML does.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        AppLogging.Start(AppFolders.Logs);
        DevelopmentOverrides.ApplyLanguage();

        var services = BuildServices();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("TransmissionRemote.App");
        try
        {
            services.GetRequiredService<UnhandledErrors>().WatchProcess();
            if (!SingleInstance.TryBecomeMainInstance(services.GetRequiredService<RedirectedActivations>(), logger))
            {
                return;
            }
            LogStartup(logger);
            FollowDetailedLoggingSetting(services.GetRequiredService<SettingsStore>());

            Application.Start(callbackParameters =>
            {
                // Awaits in the view models resume on the UI thread through this context.
                var context = new UiThreadSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App(services);
            });

            Log.Exiting(logger);
        }
        finally
        {
            services.Dispose();
            AppLogging.Stop();
        }
    }

    /// <summary>
    /// A mistake in the composition root ends the app before the error handlers are watching, so it's logged
    /// here, through a logger made just for it.
    /// </summary>
    private static ServiceProvider BuildServices()
    {
        try
        {
            return AppServices.Build(new AppEnvironment(AppFolders.DataDirectory, SystemInfo.AppVersion));
        }
        catch (Exception error)
        {
            using (var loggers = new SerilogLoggerFactory(dispose: false))
            {
                Log.ServicesNotCreated(loggers.CreateLogger("TransmissionRemote.App"), error);
            }
            AppLogging.Stop();
            throw;
        }
    }

    private static void LogStartup(ILogger logger) => Log.Started(
        logger,
        SystemInfo.InformationalVersion,
        SystemInfo.WindowsDescription(),
        SystemInfo.Architecture(),
        RuntimeInformation.FrameworkDescription,
        LogPath.Display(AppFolders.DataDirectory));

    private static void FollowDetailedLoggingSetting(SettingsStore settings)
    {
        AppLogging.SetDetailed(settings.Current.DetailedLogging);
        settings.Changed += (_, _) => AppLogging.SetDetailed(settings.Current.DetailedLogging);
    }
}
