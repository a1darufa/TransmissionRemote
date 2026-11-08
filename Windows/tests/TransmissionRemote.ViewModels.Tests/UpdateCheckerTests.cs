using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Time.Testing;

namespace TransmissionRemote.ViewModels.Tests;

/// <summary>Checking GitHub for a newer Windows release, with GitHub's answers made up in memory.</summary>
[TestClass]
public sealed class UpdateCheckerTests
{
    private static readonly string Releases = ReleaseList(
        Release("v1.4", assets: ["TransmissionRemote-1.4.dmg"]),
        Release("v1.3", assets: ["TransmissionRemote-1.3.dmg", "TransmissionRemote-1.3-win-x64.exe"]),
        Release("v1.2", assets: ["TransmissionRemote-1.2-win-x64.exe", "TransmissionRemote-1.2-win-arm64.exe"]));

    private string _directory = null!;
    private FakeTimeProvider _time = null!;
    private SettingsStore _settings = null!;

    [TestInitialize]
    public void CreateSettings()
    {
        _directory = Path.Combine(Path.GetTempPath(), "UpdateCheckerTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
        _settings = new SettingsStore(new AppEnvironment(_directory, "1.2"));
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public async Task OffersTheNewestReleaseWithADownloadForThisPC()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Answer(Releases));
        using var checker = Checker(handler);

        var outcome = await checker.CheckAsync(userInitiated: false);

        var available = Assert.IsInstanceOfType<UpdateAvailable>(outcome);
        Assert.AreEqual("v1.3", available.Release.TagName, "the Mac-only 1.4 is skipped, not hiding 1.3");
        Assert.AreEqual(outcome, checker.Outcome);
        Assert.AreEqual(_time.GetUtcNow(), _settings.Current.LastUpdateCheck);
        Assert.AreEqual(UpdateChecker.ReleasesUrl, handler.RequestedUrls.Single());
        Assert.AreEqual("TransmissionRemote/1.2", handler.UserAgents.Single(), "GitHub refuses requests without one");
    }

    [TestMethod]
    public async Task OnlyCountsDownloadsForThisPCsArchitecture()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Answer(Releases));
        using var checker = Checker(handler, Architecture.Arm64);

        Assert.IsInstanceOfType<UpToDate>(await checker.CheckAsync(userInitiated: true));
    }

    [TestMethod]
    public async Task IgnoresDraftsPreReleasesAndUnreadableEntries()
    {
        var releases = ReleaseList(
            Release("v2.0", draft: true, assets: ["TransmissionRemote-2.0-win-x64.exe"]),
            Release("v1.9", prerelease: true, assets: ["TransmissionRemote-1.9-win-x64.exe"]),
            Release("latest", assets: ["TransmissionRemote-latest-win-x64.exe"]),
            """{"tag_name": "v1.8", "html_url": "https://example.com/v1.8", "assets": [{"name": "x-win-x64.exe"}]}""",
            """{"tag_name": "v1.7", "assets": [{"name": 7}, "odd"]}""",
            Release("v1.3", assets: ["TransmissionRemote-1.3-win-x64.zip"]));
        using var checker = Checker(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Answer(releases)));

        var outcome = await checker.CheckAsync(userInitiated: true);

        Assert.AreEqual("v1.3", Assert.IsInstanceOfType<UpdateAvailable>(outcome).Release.TagName);
    }

    [TestMethod]
    [DataRow("TransmissionRemote-1.3-win-x64.exe", Architecture.X64, true)]
    [DataRow("TransmissionRemote-1.3-WIN-X64.zip", Architecture.X64, true)]
    [DataRow("TransmissionRemote-1.3-win-arm64.exe", Architecture.Arm64, true)]
    [DataRow("TransmissionRemote-1.3-win-arm64.exe", Architecture.X64, false)]
    [DataRow("TransmissionRemote-1.3-win-x64.exe", Architecture.Arm64, false)]
    [DataRow("TransmissionRemote-1.3-win-x64.exe.sha256", Architecture.X64, false)]
    [DataRow("TransmissionRemote-1.3-win-x64", Architecture.X64, false)]
    [DataRow("TransmissionRemote-1.3.dmg", Architecture.X64, false)]
    [DataRow("TransmissionRemote-1.3-win-x86.exe", Architecture.X86, false)]
    public void RecognizesWindowsDownloadsByName(string assetName, Architecture architecture, bool expected)
    {
        Assert.AreEqual(expected, UpdateChecker.IsWindowsAsset(assetName, architecture));
    }

    [TestMethod]
    public async Task AutomaticChecksStayQuietAboutASkippedReleaseButTheUserStillHearsOfIt()
    {
        using var checker = Checker(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Answer(Releases)));
        var offered = Assert.IsInstanceOfType<UpdateAvailable>(await checker.CheckAsync(userInitiated: true));
        checker.Skip(offered.Release);
        checker.Dismiss();

        Assert.IsNull(await checker.CheckAsync(userInitiated: false));
        Assert.IsNull(checker.Outcome);
        Assert.IsInstanceOfType<UpdateAvailable>(await checker.CheckAsync(userInitiated: true));
        Assert.AreEqual("v1.3", _settings.Current.SkippedUpdateVersion);
    }

    [TestMethod]
    public async Task OnlyTheUserHearsThatTheAppIsUpToDate()
    {
        using var checker = Checker(
            new StubHttpMessageHandler(_ => StubHttpMessageHandler.Answer(Releases)), appVersion: "1.3.0");

        Assert.IsNull(await checker.CheckAsync(userInitiated: false));
        Assert.IsInstanceOfType<UpToDate>(await checker.CheckAsync(userInitiated: true));
    }

    [TestMethod]
    public async Task ReportsGitHubErrorsOnlyToTheUser()
    {
        var handler = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Answer("""{"message": "API rate limit exceeded"}""", HttpStatusCode.Forbidden));
        using var checker = Checker(handler);

        Assert.IsNull(await checker.CheckAsync(userInitiated: false));
        Assert.AreEqual(
            new UpdateCheckFailed(UpdateCheckFailure.ServerError, 403), await checker.CheckAsync(userInitiated: true));
        Assert.IsNull(_settings.Current.LastUpdateCheck, "a failed check is retried");
    }

    [TestMethod]
    public async Task ReportsNetworkFailuresAndUnreadableAnswers()
    {
        using var offline = Checker(new StubHttpMessageHandler(_ => throw new HttpRequestException("No route")));
        using var odd = Checker(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Answer("""{"message": "?"}""")));

        Assert.AreEqual(
            new UpdateCheckFailed(UpdateCheckFailure.Network), await offline.CheckAsync(userInitiated: true));
        Assert.AreEqual(
            new UpdateCheckFailed(UpdateCheckFailure.InvalidResponse), await odd.CheckAsync(userInitiated: true));
    }

    [TestMethod]
    public async Task GivesUpWhenGitHubDoesNotAnswer()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });
        using var checker = Checker(handler);

        var check = checker.CheckAsync(userInitiated: true);
        Assert.IsTrue(checker.IsChecking);
        _time.Advance(TimeSpan.FromSeconds(21));

        Assert.AreEqual(new UpdateCheckFailed(UpdateCheckFailure.TimedOut), await check);
        Assert.IsFalse(checker.IsChecking);
    }

    [TestMethod]
    public async Task ChecksAutomaticallyOnlyWhenOnAndADayHasPassed()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Answer(Releases));
        using var checker = Checker(handler);

        _settings.Update(settings => settings with { CheckForUpdates = false });
        await checker.CheckIfDueAsync();
        Assert.IsEmpty(handler.RequestedUrls);

        _settings.Update(settings => settings with
        {
            CheckForUpdates = true,
            LastUpdateCheck = _time.GetUtcNow() - TimeSpan.FromHours(23),
        });
        await checker.CheckIfDueAsync();
        Assert.IsEmpty(handler.RequestedUrls);

        _time.Advance(TimeSpan.FromHours(1));
        await checker.CheckIfDueAsync();
        Assert.HasCount(1, handler.RequestedUrls);
    }

    [TestMethod]
    public async Task AutomaticChecksRunAtLaunchAndThenDaily()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Answer(Releases));
        using var checker = Checker(handler);

        checker.StartAutomaticChecks();
        await Eventually.AssertAsync(() => handler.RequestedUrls.Count == 1, "checks at launch");

        _time.Advance(TimeSpan.FromHours(6));
        await Task.Delay(50);
        Assert.HasCount(1, handler.RequestedUrls, "six hours after a check, the next isn't due");

        _time.Advance(TimeSpan.FromHours(18));
        await Eventually.AssertAsync(() => handler.RequestedUrls.Count == 2, "checks again a day later");

        checker.StopAutomaticChecks();
        _time.Advance(TimeSpan.FromDays(2));
        await Task.Delay(50);
        Assert.HasCount(2, handler.RequestedUrls);
    }

    private UpdateChecker Checker(
        StubHttpMessageHandler handler, Architecture architecture = Architecture.X64, string appVersion = "1.2") =>
        new(_settings, new AppEnvironment(_directory, appVersion), new HttpClient(handler), time: _time)
        {
            ProcessArchitecture = architecture,
        };

    private static string ReleaseList(params string[] releases) => $"[{string.Join(", ", releases)}]";

    private static string Release(string tag, bool draft = false, bool prerelease = false, string[]? assets = null)
    {
        var assetList = string.Join(", ", (assets ?? []).Select(name => $$"""{"name": "{{name}}"}"""));
        return $$"""
            {"tag_name": "{{tag}}", "html_url": "https://github.com/a1darufa/TransmissionRemote/releases/tag/{{tag}}",
             "draft": {{(draft ? "true" : "false")}}, "prerelease": {{(prerelease ? "true" : "false")}},
             "assets": [{{assetList}}]}
            """;
    }
}
