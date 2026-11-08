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

    /// <summary>Visible list columns in order, by <see cref="TorrentColumn"/> name; empty means the default set.</summary>
    public IReadOnlyList<string> TorrentColumns { get; set; } = [];

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
}
