using System.Globalization;
using System.Text.Json.Nodes;

namespace TransmissionKit;

public sealed partial class TransmissionClient
{
    /// <summary>Bandwidth groups arrived in Transmission 4.0 (RPC version 17).</summary>
    public bool SupportsBandwidthGroups => RpcVersion >= 17;

    public Task<SessionSettings> GetSessionSettingsAsync(CancellationToken cancellationToken = default) =>
        CallAsync(RpcMethod.SessionGet, null, KeyScope.Session, SessionSettings.FromJson, cancellationToken);

    /// <summary>
    /// Sends only the values set in <paramref name="changes"/>; see <see cref="SessionSettings.ChangesFrom"/>.
    /// </summary>
    public Task UpdateSessionAsync(SessionSettings changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.IsEmpty)
        {
            return Task.CompletedTask;
        }
        return CallAsync(RpcMethod.SessionSet, changes.ToJson(Dialect), KeyScope.Session, cancellationToken);
    }

    public Task<SessionStats> GetSessionStatsAsync(CancellationToken cancellationToken = default) =>
        CallAsync(RpcMethod.SessionStats, null, KeyScope.Plain, SessionStats.FromJson, cancellationToken);

    public Task<FreeSpace> GetFreeSpaceAsync(string path, CancellationToken cancellationToken = default) =>
        CallAsync(
            RpcMethod.FreeSpace, new JsonObject { ["path"] = path }, KeyScope.Plain, FreeSpace.FromJson,
            cancellationToken);

    /// <summary>Asks the daemon whether its peer port is reachable from the internet.</summary>
    public Task<bool> TestPortAsync(CancellationToken cancellationToken = default) =>
        CallAsync(
            RpcMethod.PortTest, null, KeyScope.Plain,
            result => result.GetBoolean("portIsOpen")
                ?? throw TransmissionException.InvalidResponse("port-test has no port-is-open"),
            cancellationToken);

    /// <summary>Downloads the blocklist from the configured URL; returns the number of rules.</summary>
    public Task<int> UpdateBlocklistAsync(CancellationToken cancellationToken = default) =>
        CallAsync(
            RpcMethod.BlocklistUpdate, null, KeyScope.Plain,
            result => result.GetInt32("blocklistSize")
                ?? throw TransmissionException.InvalidResponse("blocklist-update has no blocklist-size"),
            cancellationToken);

    /// <summary>Every named bandwidth group on the server, sorted by name.</summary>
    public Task<List<BandwidthGroup>> GetBandwidthGroupsAsync(CancellationToken cancellationToken = default) =>
        CallAsync(
            RpcMethod.GroupGet, null, KeyScope.Plain,
            result => result.GetObjects("group")
                .Select(BandwidthGroup.FromJson)
                .Where(group => group.Name.Length > 0)
                .OrderBy(group => group.Name, StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))
                .ToList(),
            cancellationToken);

    /// <summary>Creates the group, or replaces the limits of the group with the same name.</summary>
    public Task SaveBandwidthGroupAsync(BandwidthGroup group, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        return CallAsync(RpcMethod.GroupSet, group.ToJson(), KeyScope.BandwidthGroup, cancellationToken);
    }
}
