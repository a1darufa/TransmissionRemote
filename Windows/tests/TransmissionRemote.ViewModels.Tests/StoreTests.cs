using Microsoft.Extensions.Time.Testing;
using TransmissionKit;

namespace TransmissionRemote.ViewModels.Tests;

[TestClass]
public sealed class ServerStoreTests
{
    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "ServerStoreTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public void SavedServersSurviveARestart()
    {
        var credentials = new FakeCredentialStore();
        var profile = new ServerProfile
        {
            Name = "NAS",
            Endpoint = new ServerEndpoint("192.168.1.20", 9091),
            Username = "admin",
            PathMappings = [new PathMapping("/mnt/data", @"\\nas\data")],
            Proxy = new ProxySettings { IsEnabled = true, Host = "proxy.lan", Port = 3128 },
        };
        var store = new ServerStore(Environment(), credentials);
        store.Save(profile);
        store.SetPassword(profile, "test-password");

        var reloaded = new ServerStore(Environment(), credentials);

        var server = reloaded.Servers.Single();
        Assert.AreEqual("NAS", server.Name);
        Assert.AreEqual(new ServerEndpoint("192.168.1.20", 9091), server.Endpoint);
        Assert.AreEqual(@"\\nas\data", server.PathMappings.Single().LocalPath);
        Assert.AreEqual("proxy.lan", server.Proxy.Host);
        Assert.AreEqual(profile.Id, reloaded.ActiveServerId);
        Assert.AreEqual("test-password", reloaded.Password(profile.Id));
        Assert.DoesNotContain("test-password", File.ReadAllText(Path.Combine(_directory, "servers.json")));
    }

    [TestMethod]
    public void AnUnreadableEntryCostsOnlyThatEntryAndKeepsACopy()
    {
        File.WriteAllText(Path.Combine(_directory, "servers.json"), """
            {"servers": [
              {"name": "Good", "endpoint": {"host": "nas", "port": 9091}},
              {"name": "Broken", "endpoint": {"host": "nas2", "port": "not a number"}}
            ]}
            """);

        var store = new ServerStore(Environment(), new FakeCredentialStore());

        Assert.AreEqual("Good", store.Servers.Single().Name);
        Assert.HasCount(1, Directory.GetFiles(_directory, "servers-unreadable-*.json"));
    }

    [TestMethod]
    public void AFileThatIsNotJsonIsKeptAndNotOverwrittenSilently()
    {
        File.WriteAllText(Path.Combine(_directory, "servers.json"), "garbage");

        var store = new ServerStore(Environment(), new FakeCredentialStore());

        Assert.IsEmpty(store.Servers);
        Assert.AreEqual("garbage", File.ReadAllText(Directory.GetFiles(_directory, "servers-unreadable-*.json").Single()));
    }

    [TestMethod]
    public void MissingSettingsFallBackToDefaults()
    {
        File.WriteAllText(Path.Combine(_directory, "servers.json"), """{"servers": [{"name": "Old"}]}""");

        var server = new ServerStore(Environment(), new FakeCredentialStore()).Servers.Single();

        Assert.AreEqual(ProtocolPreference.Automatic, server.ProtocolPreference);
        Assert.AreEqual(8080, server.Proxy.Port);
        Assert.IsEmpty(server.RecentFolders);
    }

    [TestMethod]
    public void RemovingAServerDeletesItsPasswords()
    {
        var credentials = new FakeCredentialStore();
        var store = new ServerStore(Environment(), credentials);
        var profile = new ServerProfile { Name = "NAS", Endpoint = new ServerEndpoint("nas") };
        store.Save(profile);
        store.SetPassword(profile, "a");
        store.SetProxyPassword(profile, "b");

        store.Remove(profile.Id);

        Assert.IsEmpty(store.Servers);
        Assert.IsNull(credentials.GetPassword(profile.Id, CredentialKind.Server));
        Assert.IsNull(credentials.GetPassword(profile.Id, CredentialKind.Proxy));
    }

    [TestMethod]
    public void FolderHistoryRanksByUseAndStartsWithTheLastOne()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
        var store = new ServerStore(Environment(), new FakeCredentialStore(), time: time);
        var profile = new ServerProfile { Endpoint = new ServerEndpoint("nas") };
        store.Save(profile);

        store.RecordFolderUse("/data/movies/", profile.Id, limit: 50);
        time.Advance(TimeSpan.FromMinutes(1));
        store.RecordFolderUse("/data/movies", profile.Id, limit: 50);
        time.Advance(TimeSpan.FromMinutes(1));
        store.RecordFolderUse("/data/tv", profile.Id, limit: 50);

        var server = store.Find(profile.Id)!;
        CollectionAssert.AreEqual(new[] { "/data/movies", "/data/tv" }, server.SuggestedFolders.ToArray());
        Assert.AreEqual("/data/tv", server.LastUsedFolder);
    }

    [TestMethod]
    public void FolderHistoryKeepsTheMostRecentWithinTheLimit()
    {
        var time = new FakeTimeProvider();
        var profile = new ServerProfile();
        foreach (var folder in new[] { "/a", "/b", "/c" })
        {
            profile = profile.WithFolderUse(folder, limit: 2, time.GetUtcNow());
            time.Advance(TimeSpan.FromSeconds(1));
        }

        CollectionAssert.AreEquivalent(new[] { "/b", "/c" }, profile.RecentFolders.Select(folder => folder.Path).ToArray());
    }

    private AppEnvironment Environment() => new(_directory, "1.0");
}

[TestClass]
public sealed class SettingsStoreTests
{
    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "SettingsStoreTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public void StartsWithTheMacAppsDefaults()
    {
        var settings = new SettingsStore(new AppEnvironment(_directory, "1.0")).Current;

        Assert.AreEqual(5, settings.RefreshIntervalSeconds);
        Assert.AreEqual(20, settings.BackgroundRefreshIntervalSeconds);
        Assert.AreEqual(50, settings.FolderHistoryLimit);
        Assert.IsTrue(settings.ShowAddTorrentDialog);
        Assert.AreEqual(TimeSpan.FromSeconds(20), settings.RefreshInterval(isInForeground: false));
    }

    [TestMethod]
    public void ChangesAreSavedAndAnnounced()
    {
        var store = new SettingsStore(new AppEnvironment(_directory, "1.0"));
        var announced = 0;
        store.Changed += (_, _) => announced++;

        store.Update(settings => settings with { CloseToTray = true, RefreshIntervalSeconds = 3 });

        Assert.AreEqual(1, announced);
        var reloaded = new SettingsStore(new AppEnvironment(_directory, "1.0")).Current;
        Assert.IsTrue(reloaded.CloseToTray);
        Assert.AreEqual(3, reloaded.RefreshIntervalSeconds);
    }

    [TestMethod]
    public void TheWindowLayoutIsRemembered()
    {
        var store = new SettingsStore(new AppEnvironment(_directory, "1.0"));
        Assert.IsTrue(store.Current.ShowSidebar);
        Assert.AreEqual(220, store.Current.EffectiveSidebarWidth);
        Assert.IsNull(store.Current.MainWindowPlacement);

        var placement = new WindowPlacement(-1700, 100, -300, 980, IsMaximized: true);
        store.Update(settings => settings with { ShowSidebar = false, SidebarWidth = 260, MainWindowPlacement = placement });

        var reloaded = new SettingsStore(new AppEnvironment(_directory, "1.0")).Current;
        Assert.IsFalse(reloaded.ShowSidebar);
        Assert.AreEqual(260, reloaded.EffectiveSidebarWidth);
        Assert.AreEqual(placement, reloaded.MainWindowPlacement, "a monitor left of the main one has negative coordinates");
    }

    [TestMethod]
    public void TheSidebarWidthStaysWithinTheMacAppsRange()
    {
        Assert.AreEqual(180, new AppSettings { SidebarWidth = 20 }.EffectiveSidebarWidth);
        Assert.AreEqual(340, new AppSettings { SidebarWidth = 5000 }.EffectiveSidebarWidth);
        Assert.AreEqual(220, new AppSettings { SidebarWidth = double.NaN }.EffectiveSidebarWidth);
    }

    [TestMethod]
    public void AnEmptyWindowPlacementIsNotUsed()
    {
        Assert.IsTrue(new WindowPlacement(0, 0, 1280, 820, IsMaximized: false).IsUsable);
        Assert.IsFalse(new WindowPlacement(100, 100, 100, 900, IsMaximized: false).IsUsable);
    }

    [TestMethod]
    public void UnreadableSettingsResetAndKeepACopy()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{ not json");

        var settings = new SettingsStore(new AppEnvironment(_directory, "1.0")).Current;

        Assert.AreEqual(new AppSettings(), settings);
        Assert.HasCount(1, Directory.GetFiles(_directory, "settings-unreadable-*.json"));
    }
}

/// <summary>An in-memory credential store; the real one is Windows Credential Manager.</summary>
internal sealed class FakeCredentialStore : ICredentialStore
{
    private readonly Dictionary<(Guid, CredentialKind), string> _passwords = [];

    public string? GetPassword(Guid serverId, CredentialKind kind) =>
        _passwords.GetValueOrDefault((serverId, kind));

    public void SetPassword(Guid serverId, CredentialKind kind, string password, string label) =>
        _passwords[(serverId, kind)] = password;

    public void DeletePassword(Guid serverId, CredentialKind kind) => _passwords.Remove((serverId, kind));
}

[TestClass]
public sealed class SettingsCompatibilityTests
{
    [TestMethod]
    public void SettingsMissingFromAnOlderFileKeepTheirDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SettingsCompatibilityTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "settings.json"), """{"closeToTray": true}""");

            var settings = new SettingsStore(new AppEnvironment(directory, "1.0")).Current;

            Assert.IsTrue(settings.CloseToTray);
            Assert.IsTrue(settings.ShowAddTorrentDialog, "a true default must not turn false");
            Assert.AreEqual(5, settings.RefreshIntervalSeconds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
