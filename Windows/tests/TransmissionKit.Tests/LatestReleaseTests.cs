using System.Text;
using System.Text.Json;

namespace TransmissionKit.Tests;

/// <summary>Checking GitHub for a newer release.</summary>
[TestClass]
public sealed class LatestReleaseTests
{
    [TestMethod]
    [DataRow("1.0", "1.1")]
    [DataRow("1.9", "1.10")]
    [DataRow("v1.0", "1.0.1")]
    [DataRow("1.2", "2.0")]
    [DataRow("1.0.0-beta", "1.0.1")]
    public void ComparesNumberByNumber(string older, string newer)
    {
        var oldVersion = Version(older);
        var newVersion = Version(newer);

        Assert.IsTrue(oldVersion < newVersion);
        Assert.IsFalse(newVersion < oldVersion);
    }

    [TestMethod]
    public void TreatsMissingTrailingZerosAsEqual()
    {
        var shortForm = Version("1.0");
        var longForm = Version("v1.0.0");

        Assert.AreEqual(shortForm, longForm);
        Assert.AreEqual(shortForm.GetHashCode(), longForm.GetHashCode());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("latest")]
    public void RejectsTagsWithoutANumber(string tag)
    {
        Assert.IsFalse(ReleaseVersion.TryParse(tag, out _));
    }

    [TestMethod]
    public void DecodesGitHubsResponse()
    {
        var json = """
            {"tag_name": "v1.1", "name": "Transmission Remote 1.1",
             "html_url": "https://github.com/a1darufa/TransmissionRemote/releases/tag/v1.1", "draft": false}
            """u8;

        var release = LatestRelease.ParseGitHubResponse(json);

        Assert.AreEqual(Version("1.1"), release.Version);
        Assert.AreEqual("v1.1", release.TagName);
        Assert.AreEqual(
            "https://github.com/a1darufa/TransmissionRemote/releases/tag/v1.1", release.PageUrl.AbsoluteUri);
    }

    [TestMethod]
    [DataRow("https://example.com/download")]
    [DataRow("http://github.com/a1darufa/TransmissionRemote/releases/tag/v9.9")]
    [DataRow("https://github.com.example.net/releases/tag/v9.9")]
    [DataRow("/releases/tag/v9.9")]
    public void RefusesPagesOutsideGitHub(string pageUrl)
    {
        var json = Encoding.UTF8.GetBytes($$"""{"tag_name": "v9.9", "html_url": "{{pageUrl}}"}""");

        Assert.Throws<JsonException>(() => LatestRelease.ParseGitHubResponse(json));
    }

    [TestMethod]
    [DataRow("""{"message": "Not Found"}""")]
    [DataRow("""{"tag_name": null, "html_url": "https://github.com/a1darufa/TransmissionRemote"}""")]
    [DataRow("""{"tag_name": "latest", "html_url": "https://github.com/a1darufa/TransmissionRemote"}""")]
    [DataRow("not json")]
    public void RejectsResponsesThatArentReleases(string response)
    {
        var json = Encoding.UTF8.GetBytes(response);

        Assert.Throws<JsonException>(() => LatestRelease.ParseGitHubResponse(json));
    }

    private static ReleaseVersion Version(string text)
    {
        Assert.IsTrue(ReleaseVersion.TryParse(text, out var version), $"\"{text}\" should be a version");
        return version;
    }
}
