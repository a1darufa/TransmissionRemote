using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>What an update check found: <see cref="UpdateAvailable"/>, <see cref="UpToDate"/> or a failure.</summary>
public abstract record UpdateCheckOutcome;

/// <summary>A newer release for this PC. The app offers to open <see cref="LatestRelease.PageUrl"/>.</summary>
public sealed record UpdateAvailable(LatestRelease Release) : UpdateCheckOutcome;

/// <summary>No newer release has a download for this PC.</summary>
public sealed record UpToDate : UpdateCheckOutcome;

/// <summary>The check didn't get an answer it could use. Only reported for checks the user started.</summary>
/// <param name="StatusCode">GitHub's HTTP status for <see cref="UpdateCheckFailure.ServerError"/>.</param>
public sealed record UpdateCheckFailed(UpdateCheckFailure Kind, int? StatusCode = null) : UpdateCheckOutcome;

public enum UpdateCheckFailure
{
    /// <summary>No connection, a name that didn't resolve, or a broken TLS connection.</summary>
    Network,

    TimedOut,

    /// <summary>GitHub answered with an error, such as 403 when its hourly limit for this address is used up.</summary>
    ServerError,

    /// <summary>The answer wasn't a list of releases.</summary>
    InvalidResponse,
}

/// <summary>
/// Looks for newer releases on GitHub, like Transmission Remote GUI's "Check for updates": once a day when
/// <see cref="AppSettings.CheckForUpdates"/> is on, and whenever the user asks. It only points to the release page;
/// it never downloads or installs anything itself.
/// </summary>
/// <remarks>
/// <para>
/// The Mac and Windows apps publish to the same GitHub releases, and a release may carry only one of them. So
/// this asks for the recent releases rather than just the latest one, and offers the newest that has a download
/// for this PC: a Mac-only release must neither be offered here nor hide an earlier Windows release.
/// </para>
/// <para>
/// Asset naming: a Windows download's file name, without its extension, ends in <c>-win-x64</c> or
/// <c>-win-arm64</c>, for example <c>TransmissionRemote-1.3-win-x64.exe</c> (installer) and
/// <c>TransmissionRemote-1.3-win-arm64.zip</c> (portable). Only the running process's architecture counts, so an
/// x64 build updates to x64 even under emulation on ARM64. Checksum files such as <c>…-win-x64.exe.sha256</c>
/// don't count. The Mac's <c>TransmissionRemote-1.3.dmg</c> never matches.
/// </para>
/// <para>Use from the UI thread: results are set after awaits that return to it.</para>
/// </remarks>
public sealed class UpdateChecker : ObservableObject, IDisposable
{
    // The repository's 30 most recent releases: far more than the gap between two Windows releases.
    internal static readonly Uri ReleasesUrl =
        new("https://api.github.com/repos/a1darufa/TransmissionRemote/releases?per_page=30");

    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    // Waking up every few hours to see whether a day has passed, so a PC that stays on for weeks still hears
    // about new releases.
    private static readonly TimeSpan WakeInterval = TimeSpan.FromHours(6);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly SettingsStore _settings;
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly string _appVersion;
    private readonly ReleaseVersion? _currentVersion;
    private CancellationTokenSource? _automaticChecks;
    private bool _isChecking;
    private UpdateCheckOutcome? _outcome;

    /// <param name="httpClient">The app's shared client; tests pass one with a fake handler.</param>
    public UpdateChecker(
        SettingsStore settings,
        AppEnvironment environment,
        HttpClient httpClient,
        ILogger<UpdateChecker>? logger = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? NullLogger<UpdateChecker>.Instance;
        _time = time ?? TimeProvider.System;
        _appVersion = environment.AppVersion;
        _currentVersion = ReleaseVersion.TryParse(environment.AppVersion, out var version) ? version : null;
    }

    public bool IsChecking
    {
        get => _isChecking;
        private set => SetProperty(ref _isChecking, value);
    }

    /// <summary>What the last check found that the user should hear about; the main window shows it.</summary>
    public UpdateCheckOutcome? Outcome
    {
        get => _outcome;
        private set => SetProperty(ref _outcome, value);
    }

    /// <summary>Which downloads count as this PC's; replaceable so tests can check both architectures.</summary>
    internal Architecture ProcessArchitecture { get; init; } = RuntimeInformation.ProcessArchitecture;

    /// <summary>Checks now, then every few hours whether a day has passed since the last check.</summary>
    public void StartAutomaticChecks()
    {
        StopAutomaticChecks();
        _automaticChecks = new CancellationTokenSource();
        _ = RunAutomaticChecksAsync(_automaticChecks.Token);
    }

    public void StopAutomaticChecks()
    {
        _automaticChecks?.Cancel();
        _automaticChecks?.Dispose();
        _automaticChecks = null;
    }

    /// <summary>Checks when automatic checks are on and the last successful one is a day old.</summary>
    public async Task CheckIfDueAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settings.Current;
        var lastCheck = settings.LastUpdateCheck ?? DateTimeOffset.MinValue;
        if (!settings.CheckForUpdates || _time.GetUtcNow() - lastCheck < CheckInterval)
        {
            return;
        }
        await CheckAsync(userInitiated: false, cancellationToken);
    }

    /// <summary>
    /// Checks GitHub and sets <see cref="Outcome"/>. Checks the user started always report back; automatic ones
    /// stay quiet unless there's a release the user hasn't chosen to skip. Returns what was reported, or null.
    /// </summary>
    public async Task<UpdateCheckOutcome?> CheckAsync(bool userInitiated, CancellationToken cancellationToken = default)
    {
        if (IsChecking)
        {
            return null;
        }
        IsChecking = true;
        try
        {
            var outcome = await FindOutcomeAsync(userInitiated, cancellationToken);
            if (outcome is not null)
            {
                Outcome = outcome;
            }
            return outcome;
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>Automatic checks won't offer this release again; a check the user starts still will.</summary>
    public void Skip(LatestRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        _settings.Update(settings => settings with { SkippedUpdateVersion = release.TagName });
    }

    /// <summary>Clears <see cref="Outcome"/> once the user has seen it.</summary>
    public void Dismiss() => Outcome = null;

    public void Dispose() => StopAutomaticChecks();

    /// <summary>
    /// The newest published release with a download for <paramref name="architecture"/>, or null when there's none.
    /// Drafts, pre-releases and entries that don't read as releases are skipped, so one odd release can't hide
    /// the others.
    /// </summary>
    /// <exception cref="JsonException">The response isn't a list.</exception>
    internal static LatestRelease? NewestRelease(byte[] gitHubResponse, Architecture architecture)
    {
        using var document = JsonDocument.Parse(gitHubResponse);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("GitHub's answer isn't a list of releases.");
        }

        LatestRelease? newest = null;
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || IsTrue(entry, "draft")
                || IsTrue(entry, "prerelease")
                || !AssetNames(entry).Any(name => IsWindowsAsset(name, architecture)))
            {
                continue;
            }
            LatestRelease release;
            try
            {
                release = LatestRelease.ParseGitHubResponse(Encoding.UTF8.GetBytes(entry.GetRawText()));
            }
            catch (JsonException)
            {
                continue;
            }
            if (newest is null || release.Version > newest.Version)
            {
                newest = release;
            }
        }
        return newest;
    }

    /// <summary>Whether a release asset is the Windows download for an architecture (see the class remarks).</summary>
    internal static bool IsWindowsAsset(string assetName, Architecture architecture)
    {
        var suffix = architecture switch
        {
            Architecture.X64 => "-win-x64",
            Architecture.Arm64 => "-win-arm64",
            // The app is only built for these two.
            _ => null,
        };
        var extensionStart = assetName.LastIndexOf('.');
        return suffix is not null
            && extensionStart > 0
            && assetName.AsSpan(0, extensionStart).EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunAutomaticChecksAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(WakeInterval, _time);
        try
        {
            do
            {
                await CheckIfDueAsync(cancellationToken);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopped.
        }
    }

    private async Task<UpdateCheckOutcome?> FindOutcomeAsync(bool userInitiated, CancellationToken cancellationToken)
    {
        LatestRelease? newest;
        try
        {
            newest = await FetchNewestReleaseAsync(cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or TimeoutException or JsonException)
        {
            Log.UpdateCheckFailed(_logger, error);
            return userInitiated ? Failure(error) : null;
        }

        _settings.Update(settings => settings with { LastUpdateCheck = _time.GetUtcNow() });
        Log.UpdateChecked(_logger, newest?.TagName ?? "none", _appVersion);

        var isNewer = newest is not null && _currentVersion is not null && newest.Version > _currentVersion;
        var isSkipped = newest?.TagName == _settings.Current.SkippedUpdateVersion;
        if (isNewer && (userInitiated || !isSkipped))
        {
            return new UpdateAvailable(newest!);
        }
        return userInitiated ? new UpToDate() : null;
    }

    private async Task<LatestRelease?> FetchNewestReleaseAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(RequestTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        // GitHub's API refuses requests without a User-Agent.
        if (!request.Headers.UserAgent.TryParseAdd($"TransmissionRemote/{_appVersion}"))
        {
            request.Headers.UserAgent.ParseAdd("TransmissionRemote");
        }
        try
        {
            using var response = await _http.SendAsync(request, linked.Token);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsByteArrayAsync(linked.Token);
            return NewestRelease(body, ProcessArchitecture);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            // Our timeout or the HttpClient's own: either way GitHub didn't answer in time.
            throw new TimeoutException($"GitHub didn't answer within {RequestTimeout.TotalSeconds:0} seconds.", error);
        }
    }

    private static UpdateCheckFailed Failure(Exception error) => error switch
    {
        HttpRequestException { StatusCode: { } status } => new(UpdateCheckFailure.ServerError, (int)status),
        HttpRequestException => new(UpdateCheckFailure.Network),
        TimeoutException => new(UpdateCheckFailure.TimedOut),
        _ => new(UpdateCheckFailure.InvalidResponse),
    };

    private static bool IsTrue(JsonElement entry, string property) =>
        entry.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static IEnumerable<string> AssetNames(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind == JsonValueKind.Object
                && asset.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String)
            {
                yield return name.GetString()!;
            }
        }
    }
}
