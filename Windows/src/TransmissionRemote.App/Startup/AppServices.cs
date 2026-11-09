using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;
using TransmissionRemote.App.Platform;
using TransmissionRemote.App.Presentation;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Startup;

/// <summary>The composition root: one instance of each service, wired once at startup.</summary>
internal static class AppServices
{
    public static ServiceProvider Build(AppEnvironment environment)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(new SerilogLoggerProvider(dispose: false)));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(environment);
        services.AddSingleton<UnhandledErrors>();
        services.AddSingleton<RedirectedActivations>();

        // Windows' side of the view models' platform interfaces.
        services.AddSingleton<ICredentialStore, CredentialManagerStore>();
        services.AddSingleton<IClientCertificateProvider, CertificateStoreIdentities>();
        services.AddSingleton<INotificationService, NoNotificationsYet>();

        // The view models and the services behind them. Their constructors take optional loggers and clocks,
        // so they're created here explicitly with the app's.
        services.AddSingleton(provider => new SettingsStore(
            environment, provider.GetRequiredService<ILogger<SettingsStore>>(), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new ServerStore(
            environment,
            provider.GetRequiredService<ICredentialStore>(),
            provider.GetRequiredService<ILogger<ServerStore>>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new ServerConnection(
            provider.GetRequiredService<SettingsStore>(),
            provider.GetRequiredService<IClientCertificateProvider>(),
            provider.GetRequiredService<INotificationService>(),
            provider.GetRequiredService<ILoggerFactory>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new AddTorrentFlow(
            provider.GetRequiredService<ServerConnection>(),
            provider.GetRequiredService<SettingsStore>(),
            provider.GetRequiredService<ILogger<AddTorrentFlow>>()));
        services.AddSingleton(provider => new MainViewModel(
            provider.GetRequiredService<ServerStore>(),
            provider.GetRequiredService<SettingsStore>(),
            provider.GetRequiredService<ServerConnection>(),
            provider.GetRequiredService<AddTorrentFlow>(),
            provider.GetRequiredService<TimeProvider>()));

        // Sizes, speeds and durations in the regional format, with unit names in the display language.
        services.AddSingleton(_ => AppFormatting.CreateFormatter());
        services.AddSingleton(provider => new TorrentCellText(provider.GetRequiredService<DisplayFormatter>(), new TorrentListWords()));

        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }
}
