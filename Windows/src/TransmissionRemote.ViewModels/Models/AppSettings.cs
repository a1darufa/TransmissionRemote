using System.Collections.Immutable;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>App-wide preferences, saved as JSON by <see cref="SettingsStore"/>. Same defaults as the Mac app.</summary>
/// <remarks>
/// Treat it as a value and change it through <see cref="SettingsStore.Update"/>. The setters are public only
/// because the JSON reader needs them to keep the defaults of settings an older file doesn't have.
/// </remarks>
public sealed record AppSettings
{
    /// <summary>Seconds between refreshes while the window is in front.</summary>
    public int RefreshIntervalSeconds { get; set; } = 5;

    /// <summary>Seconds between refreshes while the window is minimized or in the background.</summary>
    public int BackgroundRefreshIntervalSeconds { get; set; } = 20;

    public bool ShowAddTorrentDialog { get; set; } = true;
    public bool NotifyOnCompletion { get; set; } = true;

    /// <summary>The notification-area icon, the Windows counterpart of the Mac's menu bar extra.</summary>
    public bool ShowTrayIcon { get; set; } = true;

    /// <summary>Closing the window keeps the app running in the notification area.</summary>
    public bool CloseToTray { get; set; }

    public bool ShowDetailPane { get; set; } = true;

    public bool ShowSidebar { get; set; } = true;

    /// <summary>The sidebar's width in effective pixels; see <see cref="EffectiveSidebarWidth"/>.</summary>
    public double SidebarWidth { get; set; } = DefaultSidebarWidth;

    /// <summary>Where the main window was when it last closed; null until then.</summary>
    public WindowPlacement? MainWindowPlacement { get; set; }

    /// <summary>Visible list columns in order, by <see cref="TorrentColumn"/> name; empty means the default set.</summary>
    public IReadOnlyList<string> TorrentColumns { get; set; } = [];

    /// <summary>Column widths the user set, in effective pixels, by <see cref="TorrentColumn"/> name.</summary>
    public IReadOnlyDictionary<string, double> TorrentColumnWidths { get; set; } = ImmutableDictionary<string, double>.Empty;

    public string? SortColumn { get; set; }
    public bool SortAscending { get; set; }

    /// <summary>How many download folders each server remembers, like Transmission Remote GUI's 50.</summary>
    public int FolderHistoryLimit { get; set; } = 50;

    /// <summary>The speed menu entries in kB/s, as Transmission Remote GUI writes them.</summary>
    public string DownloadSpeedPresets { get; set; } = SpeedLimitPresets.DefaultList;

    public string UploadSpeedPresets { get; set; } = SpeedLimitPresets.DefaultList;

    public bool CheckForUpdates { get; set; } = true;
    public DateTimeOffset? LastUpdateCheck { get; set; }
    public string? SkippedUpdateVersion { get; set; }

    public bool ResolvePeerHostNames { get; set; } = true;
    public bool ShowPeerCountries { get; set; } = true;

    /// <summary>The month of the downloaded country database, such as "2026-11".</summary>
    public string? CountryDatabaseMonth { get; set; }

    /// <summary>Also logs every request (debug level), for diagnosing a problem.</summary>
    public bool DetailedLogging { get; set; }

    public TimeSpan RefreshInterval(bool isInForeground) =>
        TimeSpan.FromSeconds(Math.Max(isInForeground ? RefreshIntervalSeconds : BackgroundRefreshIntervalSeconds, 1));

    public int EffectiveFolderHistoryLimit => Math.Max(FolderHistoryLimit, 1);

    /// <summary>The Mac app's sidebar sizes: 220 to start with, between 180 and 340.</summary>
    public const double DefaultSidebarWidth = 220;
    public const double MinimumSidebarWidth = 180;
    public const double MaximumSidebarWidth = 340;

    /// <summary>The saved width within the allowed range, so an edited or old file can't hide the list.</summary>
    public double EffectiveSidebarWidth =>
        double.IsFinite(SidebarWidth) ? Math.Clamp(SidebarWidth, MinimumSidebarWidth, MaximumSidebarWidth) : DefaultSidebarWidth;
}

/// <summary>
/// A window's restored position and size in physical pixels, and whether it was maximized, as Windows reports
/// them (<c>GetWindowPlacement</c>), so it comes back on the same monitor.
/// </summary>
public sealed record WindowPlacement(int Left, int Top, int Right, int Bottom, bool IsMaximized)
{
    /// <summary>False for a placement no window can have, such as one from an edited file.</summary>
    public bool IsUsable => Right > Left && Bottom > Top;
}
