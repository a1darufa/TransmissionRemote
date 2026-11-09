using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace TransmissionRemote.App;

/// <summary>The WinUI application: shows the main window once XAML is running.</summary>
public sealed partial class App : Application
{
    private readonly IServiceProvider _services;

    internal App(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _services.GetRequiredService<MainWindow>().Activate();
    }
}
