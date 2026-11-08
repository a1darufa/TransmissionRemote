using System.Diagnostics.CodeAnalysis;

namespace TransmissionKit;

/// <summary>A validated magnet: link.</summary>
public sealed class MagnetLink
{
    private const string Prefix = "magnet:?";
    private const string Version1Topic = "urn:btih:";
    private const string Version2Topic = "urn:btmh:";

    private MagnetLink(Uri url, string? infoHash, string? displayName, IReadOnlyList<string> trackers)
    {
        Url = url;
        InfoHash = infoHash;
        DisplayName = displayName;
        Trackers = trackers;
    }

    public Uri Url { get; }

    /// <summary>The lowercased info hash from "xt=urn:btih:" (hex or base32), if the link has one.</summary>
    public string? InfoHash { get; }

    public string? DisplayName { get; }

    public IReadOnlyList<string> Trackers { get; }

    /// <summary>Succeeds only for a magnet link with a BitTorrent exact topic.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out MagnetLink? link)
    {
        link = null;
        var trimmed = text?.Trim();
        if (trimmed is null
            || !trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var url))
        {
            return false;
        }

        var items = QueryItems(trimmed[Prefix.Length..]);
        var topics = ValuesNamed("xt", items).ToList();
        // BEP 9 (v1 "btih") and BEP 52 (v2 "btmh") topics.
        if (!topics.Any(topic => IsTopic(topic, Version1Topic) || IsTopic(topic, Version2Topic)))
        {
            return false;
        }

        var infoHash = topics.FirstOrDefault(topic => IsTopic(topic, Version1Topic))?[Version1Topic.Length..]
            .ToLowerInvariant();
        var displayName = items.FirstOrDefault(item => item.Name == "dn").Value;
        link = new MagnetLink(url, infoHash, displayName, [.. ValuesNamed("tr", items)]);
        return true;
    }

    private static bool IsTopic(string topic, string prefix) =>
        topic.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ValuesNamed(string name, IEnumerable<(string Name, string? Value)> items) =>
        items.Where(item => item.Name == name).Select(item => item.Value).OfType<string>();

    /// <summary>
    /// Splits a query into percent-decoded name/value pairs; an item without "=" has no value.
    /// "+" stays a plus, as in magnet links it isn't an encoded space.
    /// </summary>
    private static List<(string Name, string? Value)> QueryItems(string query)
    {
        var fragmentStart = query.IndexOf('#', StringComparison.Ordinal);
        if (fragmentStart >= 0)
        {
            query = query[..fragmentStart];
        }

        var items = new List<(string Name, string? Value)>();
        foreach (var item in query.Split('&'))
        {
            var separator = item.IndexOf('=', StringComparison.Ordinal);
            items.Add(separator < 0
                ? (Uri.UnescapeDataString(item), null)
                : (Uri.UnescapeDataString(item[..separator]), Uri.UnescapeDataString(item[(separator + 1)..])));
        }
        return items;
    }
}

/// <summary>Recognizes the kinds of links the daemon can add torrents from.</summary>
public static class TorrentLinkExtensions
{
    extension(string text)
    {
        /// <summary>True for http(s) URLs, which the daemon can download .torrent files from.</summary>
        public bool IsTorrentDownloadUrl =>
            Uri.TryCreate(text.Trim(), UriKind.Absolute, out var url)
            && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
            && url.Host.Length > 0;
    }
}
