namespace TransmissionKit.Tests;

/// <summary>
/// End-to-end tests against a real daemon, shown as inconclusive unless these environment variables are set:
/// <c>TRANSMISSION_IT_HOST</c>, <c>TRANSMISSION_IT_PORT</c>, and optionally <c>TRANSMISSION_IT_USER</c> and
/// <c>TRANSMISSION_IT_PASSWORD_FILE</c> (a file holding the password, so it never appears in a command line).
/// </summary>
/// <remarks>
/// Use a throwaway daemon with a test password, never a real server: the tests add, edit and remove a
/// torrent and change session settings, restoring them afterwards. They never delete downloaded data.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class IntegrationTests
{
    /// <summary>A made-up info hash, so the daemon never finds peers or data for it.</summary>
    private const string Magnet =
        "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=integration-test"
        + "&tr=http%3A%2F%2F127.0.0.1%3A6969%2Fannounce";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(ProtocolPreference.Automatic)]
    [DataRow(ProtocolPreference.Legacy)]
    public async Task TorrentLifecycle(ProtocolPreference preference)
    {
        using var client = MakeClient(preference);
        var info = await client.ConnectAsync(Cancellation);
        var expectedDialect = preference == ProtocolPreference.Legacy || !info.SupportsJsonRpc
            ? RpcDialect.Legacy
            : RpcDialect.JsonRpc;
        Assert.AreEqual(expectedDialect, client.Dialect);

        await RemoveLeftoversAsync(client);
        var request = AddTorrentRequest.MagnetOrUrl(Magnet) with { Paused = true };
        var added = (await client.AddAsync(request, Cancellation)).TorrentAdded;
        Assert.IsNotNull(added);
        Assert.IsTrue((await client.AddAsync(request, Cancellation)).IsDuplicate);

        var listed = (await client.GetAllTorrentsAsync(Cancellation)).Single(torrent => torrent.Id == added.Id);
        Assert.AreEqual(TorrentStatus.Stopped, listed.Status);
        CollectionAssert.AreEqual(new[] { "127.0.0.1" }, listed.TrackerHosts.ToArray());

        await CheckLabelsAndOptionsAsync(client, added.Id);
        await CheckTrackerReplacementAsync(client, added.Id);

        await client.MoveInQueueAsync(QueueMove.Top, [added.Id], Cancellation);
        await client.PerformAsync(TorrentAction.Reannounce, [added.Id], Cancellation);
        var summaries = await client.GetTrackerSummariesAsync(Cancellation);
        Assert.IsTrue(summaries.Any(summary => summary.TorrentId == added.Id));

        await client.RemoveAsync([added.Id], deletingData: false, Cancellation);
        var remaining = await client.GetAllTorrentsAsync(Cancellation);
        Assert.IsFalse(remaining.Any(torrent => torrent.Id == added.Id));
    }

    [TestMethod]
    [DataRow(ProtocolPreference.Automatic)]
    [DataRow(ProtocolPreference.Legacy)]
    public async Task SessionSettingsRoundTrip(ProtocolPreference preference)
    {
        using var client = MakeClient(preference);
        await client.ConnectAsync(Cancellation);
        var original = await client.GetSessionSettingsAsync(Cancellation);
        Assert.IsFalse(string.IsNullOrEmpty(original.DownloadDir));
        Assert.IsNotNull(original.Encryption);

        var edited = original with
        {
            SpeedLimitDown = 4321,
            SpeedLimitDownEnabled = true,
            SeedRatioLimit = 3.25, // camelCase on legacy servers, snake_case on JSON-RPC
            Encryption = EncryptionMode.Allowed, // "tolerated" on legacy servers
        };
        await client.UpdateSessionAsync(edited.ChangesFrom(original), Cancellation);
        var saved = await client.GetSessionSettingsAsync(Cancellation);
        await client.UpdateSessionAsync(original.ChangesFrom(saved), Cancellation);
        var restored = await client.GetSessionSettingsAsync(Cancellation);

        Assert.AreEqual(4321, saved.SpeedLimitDown);
        Assert.IsTrue(saved.SpeedLimitDownEnabled);
        Assert.AreEqual(3.25, saved.SeedRatioLimit);
        Assert.AreEqual(EncryptionMode.Allowed, saved.Encryption);
        Assert.AreEqual(original.SpeedLimitDown, restored.SpeedLimitDown);
        Assert.AreEqual(original.Encryption, restored.Encryption);

        var stats = await client.GetSessionStatsAsync(Cancellation);
        Assert.IsGreaterThanOrEqualTo(0, stats.TorrentCount);
        var freeSpace = await client.GetFreeSpaceAsync(original.DownloadDir!, Cancellation);
        Assert.IsGreaterThan(0L, freeSpace.SizeBytes);
    }

    [TestMethod]
    public async Task AWrongPasswordIsReportedAsUnauthorized()
    {
        var environment = RequireEnvironment();
        var options = new TransmissionClientOptions(environment.Endpoint)
        {
            Credentials = new Credentials(environment.Username ?? "nobody", "definitely-wrong"),
        };
        using var client = new TransmissionClient(options);

        var error = await Assert.ThrowsExactlyAsync<TransmissionException>(() => client.ConnectAsync(Cancellation));

        Assert.AreEqual(TransmissionErrorKind.Unauthorized, error.Kind);
    }

    private CancellationToken Cancellation => TestContext.CancellationToken;

    private async Task CheckLabelsAndOptionsAsync(TransmissionClient client, int id)
    {
        await client.ApplyAsync(
            new TorrentMutation([id]) { Labels = ["integration", "test"], BandwidthPriority = Priority.Low },
            Cancellation);
        var relabeled = (await client.GetAllTorrentsAsync(Cancellation)).Single(torrent => torrent.Id == id);
        CollectionAssert.AreEquivalent(new[] { "integration", "test" }, relabeled.Labels.ToArray());
        Assert.AreEqual(Priority.Low, relabeled.BandwidthPriority);

        await client.ApplyAsync(
            new TorrentMutation([id])
            {
                DownloadLimited = true,
                DownloadLimit = 321,
                SeedRatioMode = LimitMode.PerTorrent,
                SeedRatioLimit = 1.5,
            },
            Cancellation);
        var options = await client.GetDetailsAsync(id, TorrentDetails.OptionFields, Cancellation);
        Assert.IsNotNull(options);
        Assert.IsTrue(options.DownloadLimited);
        Assert.AreEqual(321, options.DownloadLimit);
        Assert.AreEqual(LimitMode.PerTorrent, options.SeedRatioMode);
        Assert.AreEqual(1.5, options.SeedRatioLimit);
    }

    /// <summary>Transmission merges trackers on one host into one tier, so the tiers use different addresses.</summary>
    private async Task CheckTrackerReplacementAsync(TransmissionClient client, int id)
    {
        var original = (await client.GetDetailsAsync(id, TorrentDetails.TrackerFields, Cancellation))!.TrackerStats;
        CollectionAssert.AreEqual(
            new[] { "http://127.0.0.1:6969/announce" }, original.Select(tracker => tracker.Announce).ToArray());

        await client.ReplaceTrackersAsync(
            id, original, [["http://127.0.0.2:6970/announce"], ["http://127.0.0.3:6971/announce"]], Cancellation);
        var replaced = (await client.GetDetailsAsync(id, TorrentDetails.TrackerFields, Cancellation))!.TrackerStats;

        CollectionAssert.AreEqual(
            new[] { "http://127.0.0.2:6970/announce", "http://127.0.0.3:6971/announce" },
            replaced.Select(tracker => tracker.Announce).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1 }, replaced.Select(tracker => tracker.Tier).ToArray());
    }

    /// <summary>A previous run that failed half-way may have left the test torrent behind.</summary>
    private async Task RemoveLeftoversAsync(TransmissionClient client)
    {
        var leftovers = (await client.GetAllTorrentsAsync(Cancellation))
            .Where(torrent => Magnet.Contains(torrent.HashString, StringComparison.OrdinalIgnoreCase)
                && torrent.HashString.Length > 0)
            .Select(torrent => torrent.Id)
            .ToList();
        if (leftovers.Count > 0)
        {
            await client.RemoveAsync(leftovers, deletingData: false, Cancellation);
        }
    }

    private static TransmissionClient MakeClient(ProtocolPreference preference)
    {
        var environment = RequireEnvironment();
        var options = new TransmissionClientOptions(environment.Endpoint)
        {
            Credentials = environment.Credentials,
            ProtocolPreference = preference,
        };
        return new TransmissionClient(options);
    }

    private static IntegrationEnvironment RequireEnvironment()
    {
        var environment = IntegrationEnvironment.Current;
        if (environment is null)
        {
            Assert.Inconclusive("Set TRANSMISSION_IT_HOST and TRANSMISSION_IT_PORT to run against a test daemon.");
        }
        return environment;
    }

    private sealed record IntegrationEnvironment(ServerEndpoint Endpoint, string? Username, Credentials? Credentials)
    {
        public static IntegrationEnvironment? Current
        {
            get
            {
                var host = Environment.GetEnvironmentVariable("TRANSMISSION_IT_HOST");
                var port = Environment.GetEnvironmentVariable("TRANSMISSION_IT_PORT");
                if (string.IsNullOrEmpty(host) || !int.TryParse(port, out var portNumber))
                {
                    return null;
                }
                var username = Environment.GetEnvironmentVariable("TRANSMISSION_IT_USER");
                var passwordFile = Environment.GetEnvironmentVariable("TRANSMISSION_IT_PASSWORD_FILE");
                var credentials = username is not null && passwordFile is not null
                    ? new Credentials(username, File.ReadAllText(passwordFile).Trim())
                    : null;
                return new IntegrationEnvironment(new ServerEndpoint(host, portNumber), username, credentials);
            }
        }
    }
}
