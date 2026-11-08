using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TransmissionRemote.ViewModels;

/// <summary>App-wide preferences, persisted as <c>settings.json</c> in the app's data folder.</summary>
/// <remarks>Use from the UI thread.</remarks>
public sealed class SettingsStore
{
    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    public SettingsStore(AppEnvironment environment, ILogger<SettingsStore>? logger = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _filePath = Path.Combine(environment.DataDirectory, "settings.json");
        _logger = logger ?? NullLogger<SettingsStore>.Instance;
        _time = time ?? TimeProvider.System;
        Current = Load();
    }

    /// <summary>Raised after the settings changed.</summary>
    public event EventHandler? Changed;

    public AppSettings Current { get; private set; }

    /// <summary>Changes and saves the settings, for example <c>store.Update(s => s with { CloseToTray = true })</c>.</summary>
    public void Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var updated = change(Current);
        if (updated == Current)
        {
            return;
        }
        Current = updated;
        try
        {
            StorageFiles.WriteAtomically(
                _filePath, JsonSerializer.SerializeToUtf8Bytes(updated, StorageJsonContext.Default.AppSettings));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.SettingsNotSaved(_logger, error, StorageFiles.DisplayPath(_filePath));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Unreadable settings fall back to the defaults, keeping a copy of the file.</summary>
    private AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }
        byte[] contents = [];
        try
        {
            contents = File.ReadAllBytes(_filePath);
            return JsonSerializer.Deserialize(contents, StorageJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (JsonException error)
        {
            var copy = StorageFiles.KeepUnreadableCopy(_filePath, contents, _time.GetLocalNow());
            Log.SettingsUnreadable(_logger, error, StorageFiles.DisplayPath(copy));
            return new AppSettings();
        }
        catch (IOException error)
        {
            Log.SettingsUnreadable(_logger, error, StorageFiles.DisplayPath(_filePath));
            return new AppSettings();
        }
    }
}
