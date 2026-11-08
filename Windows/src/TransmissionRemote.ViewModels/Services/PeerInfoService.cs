using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

public enum CountryDatabaseState
{
    /// <summary>The user hasn't asked for countries yet; the Peers tab offers the download.</summary>
    NotDownloaded,

    Downloading,

    /// <summary>Reading the saved database, which takes about a second.</summary>
    Loading,

    Ready,

    /// <summary>There's no working database; <see cref="PeerInfoService.DatabaseFailure"/> says why.</summary>
    Failed,
}

public enum CountryDatabaseFailure
{
    /// <summary>No connection, or DB-IP didn't answer in time.</summary>
    Network,

    /// <summary>
    /// DB-IP answered with an error, for example when neither this month's nor last month's file is out.
    /// </summary>
    ServerError,

    /// <summary>The file is damaged or isn't a country database.</summary>
    Unreadable,

    /// <summary>The file couldn't be saved to, or read from, the app's data folder.</summary>
    FileError,
}

/// <summary>A reverse DNS lookup found a peer's host name.</summary>
public sealed class HostNameResolvedEventArgs(string address, string hostName) : EventArgs
{
    public string Address { get; } = address;
    public string HostName { get; } = hostName;
}

/// <summary>
/// Host names and countries for the Peers tab, like Transmission Remote GUI's "Resolve host name" and "Resolve
/// country". Host names come from reverse DNS; countries from DB-IP's free database (CC BY 4.0), downloaded only
/// after the user asks for it and refreshed monthly after that.
/// </summary>
/// <remarks>
/// Use from the UI thread: property changes and <see cref="HostNameResolved"/> are raised after awaits that return
/// to it. Whether to show countries and host names at all is the app's setting to check
/// (<see cref="AppSettings.ShowPeerCountries"/>, <see cref="AppSettings.ResolvePeerHostNames"/>).
/// </remarks>
public sealed class PeerInfoService : ObservableObject
{
    // Each reverse lookup can hold a thread for seconds while DNS times out.
    private const int MaximumConcurrentLookups = 4;

    // A long session can see many thousands of peers; forget what was learned past this many addresses.
    private const int MaximumCachedAddresses = 5_000;

    // One download attempt a day, so a failing download isn't retried on every refresh.
    private static readonly TimeSpan RetryInterval = TimeSpan.FromDays(1);

    private readonly SettingsStore _settings;
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly string _appVersion;
    private readonly string _databasePath;
    private readonly Func<IPAddress, Task<string?>> _reverseLookup;

    private CountryDatabase? _database;
    private DateTimeOffset? _lastUpdateAttempt;
    private CountryDatabaseState _databaseState = CountryDatabaseState.NotDownloaded;
    private CountryDatabaseFailure? _databaseFailure;
    private string? _databaseMonth;

    // Host names, guarded by _lookupLock: lookups finish on whatever thread the caller's context gives them.
    private readonly Lock _lookupLock = new();
    private readonly Dictionary<string, string> _hostNames = [];
    private readonly HashSet<string> _addressesWithoutName = [];
    private readonly Queue<string> _queuedLookups = [];
    private readonly HashSet<string> _queuedOrInFlight = [];
    private int _runningLookups;

    /// <param name="httpClient">The app's shared client; tests pass one with a fake handler.</param>
    public PeerInfoService(
        SettingsStore settings,
        AppEnvironment environment,
        HttpClient httpClient,
        ILogger<PeerInfoService>? logger = null,
        TimeProvider? time = null)
        : this(settings, environment, httpClient, ReverseLookupAsync, logger, time)
    {
    }

    /// <summary>For tests, which mustn't depend on real DNS.</summary>
    internal PeerInfoService(
        SettingsStore settings,
        AppEnvironment environment,
        HttpClient httpClient,
        Func<IPAddress, Task<string?>> reverseLookup,
        ILogger<PeerInfoService>? logger = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _reverseLookup = reverseLookup ?? throw new ArgumentNullException(nameof(reverseLookup));
        _logger = logger ?? NullLogger<PeerInfoService>.Instance;
        _time = time ?? TimeProvider.System;
        _appVersion = environment.AppVersion;
        _databasePath = Path.Combine(environment.DataDirectory, "GeoIP", "dbip-country-lite.csv.gz");
        _databaseMonth = settings.Current.CountryDatabaseMonth;
    }

    /// <summary>Raised when a lookup found a name, so the Peers tab can update that row.</summary>
    public event EventHandler<HostNameResolvedEventArgs>? HostNameResolved;

    public CountryDatabaseState DatabaseState
    {
        get => _databaseState;
        private set => SetProperty(ref _databaseState, value);
    }

    /// <summary>Why the database isn't there; set while <see cref="DatabaseState"/> is Failed.</summary>
    public CountryDatabaseFailure? DatabaseFailure
    {
        get => _databaseFailure;
        private set => SetProperty(ref _databaseFailure, value);
    }

    /// <summary>The month of the downloaded database, such as "2026-11".</summary>
    public string? DatabaseMonth
    {
        get => _databaseMonth;
        private set => SetProperty(ref _databaseMonth, value);
    }

    // Countries

    /// <summary>The ISO country code ("DE") of an address, or null when it's unknown or there's no database.</summary>
    public string? CountryCodeFor(string address) => _database?.CountryCodeFor(address);

    /// <summary>Loads the saved database, and refreshes it in the background once a newer month is out.</summary>
    public async Task PrepareCountriesAsync()
    {
        if (_database is null
            && DatabaseState is not (CountryDatabaseState.Loading or CountryDatabaseState.Downloading)
            && File.Exists(_databasePath))
        {
            await LoadAsync();
        }
        if (DatabaseState == CountryDatabaseState.Ready && IsDatabaseOutdated && CanRetryUpdate)
        {
            await DownloadDatabaseAsync();
        }
    }

    /// <summary>Downloads the newest monthly database. A failed refresh keeps a database that already works.</summary>
    public async Task DownloadDatabaseAsync()
    {
        if (DatabaseState == CountryDatabaseState.Downloading)
        {
            return;
        }
        var hadDatabase = _database is not null;
        DatabaseFailure = null;
        DatabaseState = CountryDatabaseState.Downloading;
        _lastUpdateAttempt = _time.GetUtcNow();
        var started = _time.GetTimestamp();
        try
        {
            var (month, compressed) = await FetchNewestDatabaseAsync();
            // Unpacking and parsing take about a second, and saving 5 MB isn't instant either.
            _database = await Task.Run(() =>
            {
                var parsed = Parse(compressed);
                StorageFiles.WriteAtomically(_databasePath, compressed);
                return parsed;
            });
            DatabaseMonth = month;
            _settings.Update(settings => settings with { CountryDatabaseMonth = month });
            DatabaseState = CountryDatabaseState.Ready;
            Log.CountryDatabaseDownloaded(_logger, month, compressed.Length, _time.GetElapsedTime(started));
        }
        catch (Exception error) when (IsExpectedFailure(error))
        {
            Log.CountryDatabaseDownloadFailed(_logger, error);
            if (!hadDatabase)
            {
                DatabaseFailure = Failure(error);
            }
            DatabaseState = hadDatabase ? CountryDatabaseState.Ready : CountryDatabaseState.Failed;
        }
    }

    private async Task LoadAsync()
    {
        DatabaseState = CountryDatabaseState.Loading;
        try
        {
            _database = await Task.Run(() => Parse(File.ReadAllBytes(_databasePath)));
            DatabaseState = CountryDatabaseState.Ready;
        }
        catch (Exception error) when (IsExpectedFailure(error))
        {
            Log.CountryDatabaseUnreadable(_logger, error, StorageFiles.DisplayPath(_databasePath));
            DatabaseFailure = Failure(error);
            DatabaseState = CountryDatabaseState.Failed;
        }
    }

    private bool IsDatabaseOutdated =>
        DatabaseMonth is null || string.CompareOrdinal(DatabaseMonth, MonthText(_time.GetUtcNow())) < 0;

    private bool CanRetryUpdate => _lastUpdateAttempt is not { } last || _time.GetUtcNow() - last > RetryInterval;

    /// <summary>DB-IP publishes each month's file early in that month, so this falls back to last month's.</summary>
    private async Task<(string Month, byte[] Data)> FetchNewestDatabaseAsync()
    {
        var now = _time.GetUtcNow();
        Exception? lastError = null;
        foreach (var month in new[] { MonthText(now), MonthText(now.AddMonths(-1)) })
        {
            var url = new Uri($"https://download.db-ip.com/free/dbip-country-lite-{month}.csv.gz");
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.TryParseAdd($"TransmissionRemote/{_appVersion}");
                using var response = await _http.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return (month, await response.Content.ReadAsByteArrayAsync());
                }
                lastError = new HttpRequestException(
                    $"DB-IP answered {(int)response.StatusCode} for {url}", null, response.StatusCode);
            }
            catch (HttpRequestException error)
            {
                lastError = error;
            }
            catch (TaskCanceledException error)
            {
                // The HttpClient's timeout; nothing else cancels this download.
                lastError = new TimeoutException($"DB-IP didn't answer in time for {url}", error);
            }
        }
        ExceptionDispatchInfo.Throw(lastError!);
        return default; // not reached
    }

    private static CountryDatabase Parse(byte[] compressed)
    {
        var database = new CountryDatabase(Gzip.Decompress(compressed));
        return database.IsEmpty ? throw new InvalidDataException("The country database has no entries.") : database;
    }

    private static string MonthText(DateTimeOffset date) =>
        date.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static bool IsExpectedFailure(Exception error) =>
        error is HttpRequestException or TimeoutException or FormatException or InvalidDataException
            or IOException or UnauthorizedAccessException;

    private static CountryDatabaseFailure Failure(Exception error) => error switch
    {
        HttpRequestException { StatusCode: not null } => CountryDatabaseFailure.ServerError,
        HttpRequestException or TimeoutException => CountryDatabaseFailure.Network,
        IOException or UnauthorizedAccessException => CountryDatabaseFailure.FileError,
        _ => CountryDatabaseFailure.Unreadable,
    };

    // Host names

    /// <summary>The host name reverse DNS found for an address, or null when there's none (yet).</summary>
    public string? HostNameFor(string address)
    {
        lock (_lookupLock)
        {
            return _hostNames.GetValueOrDefault(address);
        }
    }

    /// <summary>
    /// Starts reverse DNS lookups for addresses not looked up yet. Names arrive through
    /// <see cref="HostNameResolved"/>; read them with <see cref="HostNameFor"/>.
    /// </summary>
    public void ResolveHostNames(IEnumerable<string> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var lookupsToStart = 0;
        lock (_lookupLock)
        {
            if (_hostNames.Count + _addressesWithoutName.Count > MaximumCachedAddresses)
            {
                _hostNames.Clear();
                _addressesWithoutName.Clear();
            }
            foreach (var address in addresses)
            {
                if (!_hostNames.ContainsKey(address)
                    && !_addressesWithoutName.Contains(address)
                    && _queuedOrInFlight.Add(address))
                {
                    _queuedLookups.Enqueue(address);
                }
            }
            // A few loops share the queue; each takes the next address when its lookup is done.
            while (_runningLookups < MaximumConcurrentLookups && _runningLookups < _queuedLookups.Count)
            {
                _runningLookups++;
                lookupsToStart++;
            }
        }
        for (var index = 0; index < lookupsToStart; index++)
        {
            _ = RunLookupsAsync();
        }
    }

    private async Task RunLookupsAsync()
    {
        while (TakeQueuedLookup() is { } address)
        {
            var name = await HostNameAsync(address);
            lock (_lookupLock)
            {
                _queuedOrInFlight.Remove(address);
                if (name is not null)
                {
                    _hostNames[address] = name;
                }
                else
                {
                    _addressesWithoutName.Add(address);
                }
            }
            if (name is not null)
            {
                HostNameResolved?.Invoke(this, new HostNameResolvedEventArgs(address, name));
            }
        }
    }

    /// <summary>The next address to look up, or null when the queue is empty and this loop should end.</summary>
    private string? TakeQueuedLookup()
    {
        lock (_lookupLock)
        {
            if (_queuedLookups.TryDequeue(out var address))
            {
                return address;
            }
            _runningLookups--;
            return null;
        }
    }

    /// <summary>The address's name, or null when it has none or isn't an IP address.</summary>
    private async Task<string?> HostNameAsync(string address)
    {
        // Only IP addresses: a host name here would start a forward lookup instead.
        if (!IPAddress.TryParse(address, out var ip))
        {
            return null;
        }
        try
        {
            var name = await _reverseLookup(ip);
            var isName = !string.IsNullOrEmpty(name) && name != address && name != ip.ToString();
            return isName ? name : null;
        }
        catch (Exception error) when (error is SocketException or ArgumentException)
        {
            // No PTR record, which is common for home connections, or an address DNS can't take (0.0.0.0).
            return null;
        }
    }

    private static async Task<string?> ReverseLookupAsync(IPAddress address) =>
        (await Dns.GetHostEntryAsync(address)).HostName;
}
