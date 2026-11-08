using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TransmissionRemote.ViewModels;

/// <summary>Saved server profiles, persisted as <c>servers.json</c> in the app's data folder.</summary>
/// <remarks>Use from the UI thread.</remarks>
public sealed class ServerStore
{
    private readonly string _filePath;
    private readonly ICredentialStore _credentials;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private List<ServerProfile> _servers = [];

    public ServerStore(
        AppEnvironment environment,
        ICredentialStore credentials,
        ILogger<ServerStore>? logger = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _filePath = Path.Combine(environment.DataDirectory, "servers.json");
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _logger = logger ?? NullLogger<ServerStore>.Instance;
        _time = time ?? TimeProvider.System;
        Load();
    }

    /// <summary>Raised after any change, so lists and menus can refresh.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<ServerProfile> Servers => _servers;

    public Guid? ActiveServerId { get; private set; }

    public ServerProfile? ActiveServer =>
        _servers.Find(server => server.Id == ActiveServerId) ?? _servers.FirstOrDefault();

    public ServerProfile? Find(Guid id) => _servers.Find(server => server.Id == id);

    public void Save(ServerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var index = _servers.FindIndex(server => server.Id == profile.Id);
        if (index >= 0)
        {
            _servers[index] = profile;
        }
        else
        {
            _servers.Add(profile);
        }
        ActiveServerId ??= profile.Id;
        Persist();
    }

    /// <summary>Removes the server and its passwords.</summary>
    public void Remove(Guid id)
    {
        _servers.RemoveAll(server => server.Id == id);
        _credentials.DeletePassword(id, CredentialKind.Server);
        _credentials.DeletePassword(id, CredentialKind.Proxy);
        if (ActiveServerId == id)
        {
            ActiveServerId = _servers.FirstOrDefault()?.Id;
        }
        Persist();
    }

    public void Activate(Guid id)
    {
        ActiveServerId = id;
        Persist();
    }

    public void RecordFolderUse(string path, Guid serverId, int limit) =>
        Update(serverId, server => server.WithFolderUse(path, limit, _time.GetUtcNow()));

    public void ClearFolderHistory(Guid serverId) => Update(serverId, server => server with { RecentFolders = [] });

    /// <summary>Applies a lowered folder history limit to every server right away.</summary>
    public void TrimFolderHistories(int limit)
    {
        _servers = _servers.Select(server => server.WithFolderHistoryTrimmed(limit)).ToList();
        Persist();
    }

    public string? Password(Guid serverId) => _credentials.GetPassword(serverId, CredentialKind.Server);

    public void SetPassword(ServerProfile profile, string password)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _credentials.SetPassword(profile.Id, CredentialKind.Server, password, profile.DisplayName);
    }

    public string? ProxyPassword(Guid serverId) => _credentials.GetPassword(serverId, CredentialKind.Proxy);

    public void SetProxyPassword(ServerProfile profile, string password)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _credentials.SetPassword(profile.Id, CredentialKind.Proxy, password, profile.DisplayName);
    }

    private void Update(Guid serverId, Func<ServerProfile, ServerProfile> change)
    {
        var index = _servers.FindIndex(server => server.Id == serverId);
        if (index < 0)
        {
            return;
        }
        _servers[index] = change(_servers[index]);
        Persist();
    }

    /// <summary>
    /// Reads servers one by one, so a single unreadable entry costs only that entry. Whenever anything can't
    /// be read, a copy of the file is kept first, because the next save rewrites it.
    /// </summary>
    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }
        byte[] contents;
        try
        {
            contents = File.ReadAllBytes(_filePath);
        }
        catch (IOException error)
        {
            Log.ServersUnreadable(_logger, error, StorageFiles.DisplayPath(_filePath));
            return;
        }

        if (StorageFiles.TryParse(contents, out var document) is { } parseError)
        {
            KeepCopy(contents, $"the file isn't valid JSON ({parseError.Message})");
            return;
        }
        using (document)
        {
            var root = document!.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("servers", out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                KeepCopy(contents, "the file has no list of servers");
                return;
            }

            var skipped = 0;
            foreach (var entry in list.EnumerateArray())
            {
                try
                {
                    if (entry.Deserialize(StorageJsonContext.Default.ServerProfile) is { } server)
                    {
                        _servers.Add(server);
                        continue;
                    }
                }
                catch (JsonException error)
                {
                    Log.ServerEntryUnreadable(_logger, error);
                }
                skipped++;
            }
            if (root.TryGetProperty("activeServerId", out var active) && active.TryGetGuid(out var activeId))
            {
                ActiveServerId = activeId;
            }
            if (skipped > 0)
            {
                KeepCopy(contents, $"{skipped} saved servers couldn't be read");
            }
        }
    }

    private void KeepCopy(byte[] contents, string reason)
    {
        try
        {
            var copy = StorageFiles.KeepUnreadableCopy(_filePath, contents, _time.GetLocalNow());
            Log.KeptUnreadableServers(_logger, reason, StorageFiles.DisplayPath(copy));
        }
        catch (IOException error)
        {
            Log.CouldNotKeepUnreadableServers(_logger, error, reason);
        }
    }

    private void Persist()
    {
        try
        {
            var stored = new StoredServers(_servers, ActiveServerId);
            StorageFiles.WriteAtomically(
                _filePath, JsonSerializer.SerializeToUtf8Bytes(stored, StorageJsonContext.Default.StoredServers));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.ServersNotSaved(_logger, error, StorageFiles.DisplayPath(_filePath));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
