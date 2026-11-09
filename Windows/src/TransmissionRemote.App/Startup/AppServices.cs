using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;
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
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }
}
