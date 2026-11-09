using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using TransmissionRemote.App.Logging;
using TransmissionRemote.App.Startup;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App;

/// <summary>The WinUI application: shows the main window once XAML is running.</summary>
public sealed partial class App : Application
{
    private readonly IServiceProvider _services;

    internal App(IServiceProvider services)
    {
        _services = services;
        services.GetRequiredService<UnhandledErrors>().WatchApplication(this);
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var mainWindow = _services.GetRequiredService<MainWindow>();
        mainWindow.Activate();
        _services.GetRequiredService<MainViewModel>().Start();

        var logger = _services.GetRequiredService<ILogger<App>>();
        _services.GetRequiredService<RedirectedActivations>().DeliverTo(activation =>
        {
            Log.LaunchReceived(logger, activation.Kind);
            mainWindow.BringToFront();
        });
    }
}
