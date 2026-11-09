using Microsoft.UI.Xaml;

namespace TransmissionRemote.App;

/// <summary>The WinUI application: creates the main window once XAML is running.</summary>
public sealed partial class App : Application
{
    private MainWindow? _mainWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
    }
}
