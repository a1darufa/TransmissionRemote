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
        ApplyDevelopmentTheme();
    }

    /// <summary>Shows the window in front of others, also when it was minimized or hidden in the tray.</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }
        AppWindow.Show();
        Activate();
    }

    /// <summary>
    /// Debug builds only: TRANSMISSIONREMOTE_THEME=Light or Dark overrides the Windows theme, to take review
    /// screenshots in both without changing the PC's settings. The app itself always follows Windows.
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    private void ApplyDevelopmentTheme()
    {
        if (!Enum.TryParse<ElementTheme>(Environment.GetEnvironmentVariable("TRANSMISSIONREMOTE_THEME"), out var theme)
            || theme == ElementTheme.Default || Content is not FrameworkElement root)
        {
            return;
        }
        root.RequestedTheme = theme;
        AppWindow.TitleBar.PreferredTheme = theme == ElementTheme.Light ? TitleBarTheme.Light : TitleBarTheme.Dark;
    }
}
