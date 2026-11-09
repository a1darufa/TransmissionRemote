using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace TransmissionRemote.App;

/// <summary>The main window: Mica behind a custom title bar.</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The name in the taskbar and Alt+Tab.
        Title = Strings.TransmissionRemote;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        // Tall caption buttons match the 48 epx TitleBar control.
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        // The taskbar and Alt+Tab icon; the .exe's own icon only covers File Explorer and shortcuts.
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
    }
}
