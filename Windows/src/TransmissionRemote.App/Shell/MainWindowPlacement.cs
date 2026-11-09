using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using TransmissionRemote.ViewModels;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TransmissionRemote.App.Shell;

/// <summary>
/// Where the main window opens and how small it may get. The position, size, maximized state and monitor are
/// saved and restored with Windows' own GetWindowPlacement and SetWindowPlacement, which also bring a window
/// back on screen when its monitor is gone. The first launch gets the Mac app's 1280 × 820, centred.
/// </summary>
internal static class MainWindowPlacement
{
    /// <summary>The Mac app's default window size, in effective pixels.</summary>
    private const double FirstWidth = 1280;
    private const double FirstHeight = 820;

    /// <summary>Enough for the sidebar, a few columns and the status bar, in effective pixels.</summary>
    private const double MinimumWidth = 800;
    private const double MinimumHeight = 520;

    /// <summary>Puts the window where it was last time, or centres it at the default size. Call before showing it.</summary>
    public static void Restore(AppWindow window, WindowPlacement? saved)
    {
        if (saved is { IsUsable: true })
        {
            var placement = new WINDOWPLACEMENT
            {
                length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>(),
                showCmd = saved.IsMaximized ? SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED : SHOW_WINDOW_CMD.SW_SHOWNORMAL,
                rcNormalPosition = new RECT(saved.Left, saved.Top, saved.Right, saved.Bottom),
            };
            if (PInvoke.SetWindowPlacement(Handle(window), in placement))
            {
                return;
            }
        }
        CentreOnMainDisplay(window);
    }

    /// <summary>The window's restored bounds and whether it's maximized, also while it's minimized.</summary>
    public static WindowPlacement? Capture(AppWindow window)
    {
        var placement = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!PInvoke.GetWindowPlacement(Handle(window), ref placement))
        {
            return null;
        }
        var isMaximized = placement.showCmd == SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED
            || (placement.showCmd == SHOW_WINDOW_CMD.SW_SHOWMINIMIZED
                && placement.flags.HasFlag(WINDOWPLACEMENT_FLAGS.WPF_RESTORETOMAXIMIZED));
        var bounds = placement.rcNormalPosition;
        return new WindowPlacement(bounds.left, bounds.top, bounds.right, bounds.bottom, isMaximized);
    }

    /// <summary>
    /// The smallest size the user can drag the window to. Windows counts physical pixels, so this is set again
    /// whenever the window moves to a display with a different scale.
    /// </summary>
    public static void ApplyMinimumSize(AppWindow window, double scale)
    {
        if (window.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)Math.Ceiling(MinimumWidth * scale);
            presenter.PreferredMinimumHeight = (int)Math.Ceiling(MinimumHeight * scale);
        }
    }

    private static void CentreOnMainDisplay(AppWindow window)
    {
        var scale = PInvoke.GetDpiForWindow(Handle(window)) / 96.0;
        var area = DisplayArea.Primary.WorkArea;
        var width = Math.Min((int)(FirstWidth * scale), area.Width);
        var height = Math.Min((int)(FirstHeight * scale), area.Height);
        window.MoveAndResize(new RectInt32(
            area.X + ((area.Width - width) / 2), area.Y + ((area.Height - height) / 2), width, height));
    }

    private static HWND Handle(AppWindow window) =>
        new(Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.Id));
}
