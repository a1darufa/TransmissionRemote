using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransmissionKit;

/// <summary>
/// A release version such as "1.2" or "v1.2.3", compared number by number, so 1.10 is newer than 1.9.
/// Missing numbers count as zero, so 1.0 and 1.0.0 are the same version.
/// </summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    private ReleaseVersion(IReadOnlyList<int> components) => Components = components;

    public IReadOnlyList<int> Components { get; }

    /// <summary>
    /// Fails for text without a leading number, such as an empty tag. Anything after the numbers is ignored,
    /// so "1.2.0-beta" reads as 1.2.0.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out ReleaseVersion? version)
    {
        version = null;
        var trimmed = text?.Trim() ?? "";
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        var components = new List<int>();
        foreach (var part in trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var digits = part[..LeadingNumberLength(part)];
            if (digits.Length == 0)
            {
                break;
            }
            if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var component))
            {
                components.Add(component);
            }
        }
        if (components.Count == 0)
        {
            return false;
        }
        version = new ReleaseVersion(components);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }
        var length = Math.Max(Components.Count, other.Components.Count);
        for (var index = 0; index < length; index++)
        {
            var difference = ComponentAt(index).CompareTo(other.ComponentAt(index));
            if (difference != 0)
            {
                return difference;
            }
        }
        return 0;
    }

    public bool Equals(ReleaseVersion? other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is ReleaseVersion other && Equals(other);

    public override int GetHashCode()
    {
        // Equal versions must hash alike, and 1.0 equals 1.0.0, so trailing zeros don't count.
        var significant = Components.Count;
        while (significant > 1 && Components[significant - 1] == 0)
        {
            significant--;
        }
        var hash = new HashCode();
        for (var index = 0; index < significant; index++)
        {
            hash.Add(Components[index]);
        }
        return hash.ToHashCode();
    }

    public override string ToString() => string.Join('.', Components);

    public static bool operator ==(ReleaseVersion? left, ReleaseVersion? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ReleaseVersion? left, ReleaseVersion? right) => !(left == right);

    public static bool operator <(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) < 0;

    public static bool operator <=(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) <= 0;

    public static bool operator >(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) > 0;

    public static bool operator >=(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) >= 0;

    private static int Compare(ReleaseVersion? left, ReleaseVersion? right) =>
        Comparer<ReleaseVersion>.Default.Compare(left, right);

    private int ComponentAt(int index) => index < Components.Count ? Components[index] : 0;

    private static int LeadingNumberLength(string text)
    {
        var length = 0;
        while (length < text.Length && char.IsNumber(text[length]))
        {
            length++;
        }
        return length;
    }
}

/// <summary>The newest published release, from GitHub's releases/latest endpoint.</summary>
public sealed record LatestRelease
{
    private LatestRelease(ReleaseVersion version, string tagName, Uri pageUrl)
    {
        Version = version;
        TagName = tagName;
        PageUrl = pageUrl;
    }

    public ReleaseVersion Version { get; }

    public string TagName { get; }

    /// <summary>The release's page on github.com, where the installer can be downloaded.</summary>
    public Uri PageUrl { get; }

    /// <summary>
    /// Reads GitHub's response, refusing anything that doesn't point to a github.com page over HTTPS, so the app
    /// never opens an unexpected address.
    /// </summary>
    /// <exception cref="JsonException">
    /// The response isn't a release, its tag has no version number, or its page isn't on github.com.
    /// </exception>
    public static LatestRelease ParseGitHubResponse(ReadOnlySpan<byte> utf8Json)
    {
        var response = JsonSerializer.Deserialize(utf8Json, LatestReleaseJsonContext.Default.GitHubReleaseResponse)
            ?? throw new JsonException("The release response is empty.");
        if (!ReleaseVersion.TryParse(response.TagName, out var version))
        {
            throw new JsonException($"Unreadable release tag \"{response.TagName}\".");
        }
        if (!Uri.TryCreate(response.HtmlUrl, UriKind.Absolute, out var pageUrl)
            || pageUrl.Scheme != Uri.UriSchemeHttps
            || pageUrl.Host != "github.com")
        {
            throw new JsonException("The release page isn't on github.com.");
        }
        return new LatestRelease(version, response.TagName, pageUrl);
    }
}

/// <summary>The fields used from GitHub's release JSON.</summary>
internal sealed record GitHubReleaseResponse(string TagName, string HtmlUrl);

// Required and non-null so a response without them (GitHub's {"message": "Not Found"}) fails to decode.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(GitHubReleaseResponse))]
internal sealed partial class LatestReleaseJsonContext : JsonSerializerContext;
