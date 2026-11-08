using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>
/// The connected server's settings while they're edited. Edits stay local until Apply sends only what changed.
/// Ported from the Mac app's ServerSettingsDraft.
/// </summary>
public sealed partial class SessionSettingsViewModel(ServerConnection connection) : ObservableObject
{
    private readonly ServerConnection _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    private Guid? _profileId;

    /// <summary>The server's settings when editing started, to work out what changed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    public partial SessionSettings Original { get; private set; } = new();

    /// <summary>Edit with <c>with</c>, for example <c>Settings = Settings with { PeerPort = 51413 }</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial SessionSettings Settings { get; set; } = new();

    [ObservableProperty]
    public partial bool IsLoaded { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial bool IsApplying { get; private set; }

    [ObservableProperty]
    public partial TransmissionException? Error { get; private set; }

    public bool HasChanges => Settings != Original;

    /// <summary>
    /// Picks up the server's current settings unless there are edits not yet applied. Switching to another
    /// server always starts over, so edits never reach the wrong server.
    /// </summary>
    public void Sync()
    {
        var isOtherServer = _connection.Profile?.Id != _profileId;
        if (!isOtherServer && HasChanges)
        {
            return;
        }
        _profileId = _connection.Profile?.Id;
        Error = null;
        if (_connection.Session is { } session)
        {
            Original = session;
            Settings = session;
            IsLoaded = true;
        }
        else
        {
            IsLoaded = false;
        }
    }

    [RelayCommand]
    private void Revert()
    {
        Settings = Original;
        Error = null;
    }

    private bool CanApply() => HasChanges && !IsApplying;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        IsApplying = true;
        Error = null;
        try
        {
            await _connection.UpdateSessionAsync(Settings.ChangesFrom(Original));
            // The update refreshes the session, so the server's answer becomes the new baseline.
            Original = Settings;
            Sync();
        }
        catch (TransmissionException error)
        {
            Error = error;
        }
        finally
        {
            IsApplying = false;
        }
    }
}

/// <summary>The torrent Options dialog, for one torrent or several (which start from the first one's options).</summary>
public sealed partial class TorrentOptionsViewModel : ObservableObject
{
    private readonly ServerConnection _connection;
    private readonly int[] _ids;
    private TorrentDetails? _original;

    public TorrentOptionsViewModel(ServerConnection connection, IEnumerable<int> ids)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        ArgumentNullException.ThrowIfNull(ids);
        _ids = ids.Distinct().Order().ToArray();
    }

    /// <summary>The options being edited; change them with <c>with</c>. Null until loaded.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial TorrentDetails? Options { get; set; }

    [ObservableProperty]
    public partial Priority BandwidthPriority { get; set; } = Priority.Normal;

    /// <summary>The bandwidth group's name, empty for none; null when the server has no groups.</summary>
    [ObservableProperty]
    public partial string? BandwidthGroup { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> GroupNames { get; private set; } = [];

    /// <summary>The torrent was removed on the server while the dialog opened (transgui PR #1593).</summary>
    [ObservableProperty]
    public partial bool IsGone { get; private set; }

    [ObservableProperty]
    public partial TransmissionException? LoadError { get; private set; }

    public bool AppliesToSeveral => _ids.Length > 1;

    public async Task LoadAsync()
    {
        if (_ids.Length == 0)
        {
            return;
        }
        try
        {
            var loaded = await _connection.GetOptionsAsync(_ids[0]);
            if (loaded is null)
            {
                IsGone = true;
                return;
            }
            _original = loaded;
            Options = loaded;
            BandwidthPriority = _connection.Torrents.FirstOrDefault(torrent => torrent.Id == _ids[0])?.BandwidthPriority
                ?? Priority.Normal;
            if (_connection.SupportsBandwidthGroups)
            {
                var groups = await _connection.GetBandwidthGroupsAsync();
                BandwidthGroup = loaded.BandwidthGroup ?? "";
                GroupNames = groups.Select(group => group.Name)
                    .Append(BandwidthGroup)
                    .Where(name => name.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .Order(NaturalStringComparer.Instance)
                    .ToList();
            }
        }
        catch (TransmissionException error)
        {
            LoadError = error;
        }
    }

    private bool CanApply() => Options is not null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task ApplyAsync() => Options is { } options ? _connection.ApplyAsync(MutationFor(options)) : Task.CompletedTask;

    internal TorrentMutation MutationFor(TorrentDetails options)
    {
        var mutation = new TorrentMutation(_ids)
        {
            DownloadLimited = options.DownloadLimited,
            DownloadLimit = options.DownloadLimit,
            UploadLimited = options.UploadLimited,
            UploadLimit = options.UploadLimit,
            HonorsSessionLimits = options.HonorsSessionLimits,
            BandwidthPriority = BandwidthPriority,
            SeedRatioMode = options.SeedRatioMode,
            SeedRatioLimit = options.SeedRatioLimit,
            SeedIdleMode = options.SeedIdleMode,
            SeedIdleLimit = options.SeedIdleLimit,
            PeerLimit = options.PeerLimit,
        };
        // Only what the user changed, so older servers aren't sent settings they don't know.
        if (options.SequentialDownload != _original?.SequentialDownload)
        {
            mutation = mutation with { SequentialDownload = options.SequentialDownload };
        }
        if (BandwidthGroup is { } group && (group != (_original?.BandwidthGroup ?? "") || AppliesToSeveral))
        {
            mutation = mutation with { Group = group };
        }
        return mutation;
    }
}

/// <summary>Edits a torrent's announce URLs as text: one URL per line, a blank line between tiers.</summary>
public sealed partial class EditTrackersViewModel(ServerConnection connection, int torrentId) : ObservableObject
{
    private static readonly string[] AllowedSchemes = ["http", "https", "udp", "wss"];
    private readonly ServerConnection _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    private IReadOnlyList<TrackerStat> _current = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InvalidLines))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsLoaded { get; private set; }

    /// <summary>Without this, an empty editor would offer to save over nothing.</summary>
    [ObservableProperty]
    public partial bool IsGone { get; private set; }

    [ObservableProperty]
    public partial TransmissionException? LoadError { get; private set; }

    /// <summary>Tiers of announce URLs, parsed from <see cref="Text"/>.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Tiers => ParseTiers(Text);

    /// <summary>Lines that aren't http, https, udp or wss URLs with a host.</summary>
    public IReadOnlyList<string> InvalidLines => Tiers.SelectMany(tier => tier)
        .Where(line => !Uri.TryCreate(line, UriKind.Absolute, out var url)
            || !AllowedSchemes.Contains(url.Scheme, StringComparer.OrdinalIgnoreCase)
            || url.Host.Length == 0)
        .ToList();

    public async Task LoadAsync()
    {
        try
        {
            if (await _connection.GetTrackersAsync(torrentId) is not { } details)
            {
                IsGone = true;
                return;
            }
            _current = details.TrackerStats;
            // Transmission before 4.0 has no trackerList; rebuild it from the tiers.
            Text = details.TrackerList is { Length: > 0 } list
                ? list.Trim()
                : string.Join("\n\n", _current.GroupBy(tracker => tracker.Tier)
                    .OrderBy(tier => tier.Key)
                    .Select(tier => string.Join('\n', tier.Select(tracker => tracker.Announce))));
            IsLoaded = true;
        }
        catch (TransmissionException error)
        {
            LoadError = error;
        }
    }

    internal static List<IReadOnlyList<string>> ParseTiers(string text) => text.ReplaceLineEndings("\n")
        .Split("\n\n")
        .Select(block => (IReadOnlyList<string>)block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Where(tier => tier.Count > 0)
        .ToList();

    private bool CanSave() => IsLoaded && InvalidLines.Count == 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => _connection.ReplaceTrackersAsync(torrentId, _current, Tiers);
}

/// <summary>Problems that keep a server profile from being saved, for messages next to the fields.</summary>
public enum ServerProfileProblem
{
    None,

    /// <summary>The address is invalid; <see cref="ServerEditorViewModel.EndpointError"/> says why.</summary>
    InvalidEndpoint,

    /// <summary>The proxy is on but incomplete; see <see cref="ProxySettings.ValidationError"/>.</summary>
    InvalidProxy,
}

/// <summary>Adding or editing a saved server, with a connection test.</summary>
public sealed partial class ServerEditorViewModel : ObservableObject
{
    private readonly ServerStore _servers;

    public ServerEditorViewModel(ServerStore servers, ServerProfile? existing = null)
    {
        _servers = servers ?? throw new ArgumentNullException(nameof(servers));
        IsNew = existing is null;
        Profile = existing ?? new ServerProfile();
        Password = existing is null ? "" : servers.Password(existing.Id) ?? "";
        ProxyPassword = existing is null ? "" : servers.ProxyPassword(existing.Id) ?? "";
    }

    public bool IsNew { get; }

    /// <summary>The profile being edited; change it with <c>with</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(EndpointError), nameof(TransportSecurity))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial ServerProfile Profile { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; }

    [ObservableProperty]
    public partial string ProxyPassword { get; set; }

    /// <summary>The server's version after a successful <see cref="TestConnectionCommand"/>.</summary>
    [ObservableProperty]
    public partial ServerInfo? TestResult { get; private set; }

    [ObservableProperty]
    public partial TransmissionException? TestError { get; private set; }

    /// <summary>How the connection protects the password, for the lock icon and its explanation.</summary>
    public TransportSecurity TransportSecurity => Profile.Endpoint.TransportSecurity;

    public TransmissionException? EndpointError
    {
        get
        {
            try
            {
                Profile.Endpoint.ToUri();
                return null;
            }
            catch (TransmissionException error)
            {
                return error;
            }
        }
    }

    public ServerProfileProblem Problem => EndpointError is not null
        ? ServerProfileProblem.InvalidEndpoint
        : Profile.Proxy.ValidationError != ProxyValidationError.None
            ? ServerProfileProblem.InvalidProxy
            : ServerProfileProblem.None;

    private bool CanSave() => Problem == ServerProfileProblem.None;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var profile = Profile with { Name = Profile.Name.Trim() };
        _servers.Save(profile);
        _servers.SetPassword(profile, Password);
        if (profile.Proxy.RequiresAuthentication)
        {
            _servers.SetProxyPassword(profile, ProxyPassword);
        }
    }

    /// <summary>Connects once with the entered settings, without saving them.</summary>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        TestResult = null;
        TestError = null;
        try
        {
            var options = new TransmissionClientOptions(Profile.Endpoint)
            {
                Credentials = Profile.Username.Length > 0 ? new Credentials(Profile.Username, Password) : null,
                PinnedCertificateFingerprint = Profile.PinnedCertificateFingerprint,
                ProtocolPreference = Profile.ProtocolPreference,
                Proxy = Profile.Proxy.ToConfiguration(ProxyPassword),
            };
            using var client = new TransmissionClient(options);
            TestResult = await client.ConnectAsync();
        }
        catch (TransmissionException error)
        {
            TestError = error;
        }
    }
}
