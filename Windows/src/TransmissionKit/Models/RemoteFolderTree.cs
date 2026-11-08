using System.Globalization;

namespace TransmissionKit;

/// <summary>
/// A folder on the server, as a node in a browsable tree. Transmission's RPC can't list directories, so the tree
/// holds only folders the daemon already reported (where torrents are saved, the default download folder) and the
/// user's folder history, plus their parent folders so the tree can be walked.
/// </summary>
/// <param name="Path">The full path on the server.</param>
/// <param name="Name">What to show: the last path component, or the full path for a top-level node.</param>
/// <param name="TorrentCount">Torrents saved directly in this folder, not in its subfolders.</param>
/// <param name="Children">Null for folders without known subfolders, so tree views show no expander.</param>
public sealed record RemoteFolder(string Path, string Name, int TorrentCount, IReadOnlyList<RemoteFolder>? Children)
{
    // Records compare lists by reference; two trees with the same folders are the same tree.
    public bool Equals(RemoteFolder? other) =>
        other is not null
        && (Path, Name, TorrentCount) == (other.Path, other.Name, other.TorrentCount)
        && (Children is null || other.Children is null
            ? Children is null && other.Children is null
            : Children.SequenceEqual(other.Children));

    public override int GetHashCode() => HashCode.Combine(Path, Name, TorrentCount, Children?.Count);
}

/// <summary>Builds the known-folders tree for the folder browser.</summary>
public static class RemoteFolderTree
{
    // Like Finder and Explorer: case-insensitive, and "Season 2" before "Season 10".
    private static readonly StringComparer NameOrder = StringComparer.Create(
        CultureInfo.CurrentCulture,
        CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.NumericOrdering);

    /// <summary>
    /// Builds the tree from known folder paths and how many torrents each holds. A chain of single folders at the
    /// top (/mnt → HD → HD_a2) is shown as one node, so the first thing the user sees is where their downloads are.
    /// </summary>
    public static IReadOnlyList<RemoteFolder> Build(IReadOnlyDictionary<string, int> torrentCounts)
    {
        var entries = new List<Entry>();
        foreach (var (path, count) in torrentCounts)
        {
            if (RemotePath.Parse(path) is { } remotePath)
            {
                entries.Add(new Entry(remotePath, count));
            }
        }
        return [.. MakeNodes(entries, depth: 0).Select(CollapsingSingleChildChain)];
    }

    private sealed record Entry(RemotePath Root, int Count)
    {
        public IReadOnlyList<string> Components => Root.Components;
    }

    private static List<RemoteFolder> MakeNodes(IEnumerable<Entry> entries, int depth) =>
        [.. entries
            .Where(entry => entry.Components.Count > depth)
            .GroupBy(entry => entry.Components[depth], StringComparer.Ordinal)
            .Select(group =>
            {
                var sample = group.First();
                var children = MakeNodes(group, depth + 1);
                return new RemoteFolder(
                    Path: sample.Root.Joined(depth + 1),
                    Name: sample.Components[depth],
                    TorrentCount: group.Where(entry => entry.Components.Count == depth + 1).Sum(entry => entry.Count),
                    Children: children.Count == 0 ? null : children);
            })
            .OrderBy(folder => folder.Name, NameOrder)
            // Names that differ only in case still get a fixed order.
            .ThenBy(folder => folder.Name, StringComparer.Ordinal)];

    /// <summary>
    /// Merges a top-level folder into its only child while the folder itself holds nothing, and names the result
    /// by its full path.
    /// </summary>
    private static RemoteFolder CollapsingSingleChildChain(RemoteFolder node)
    {
        while (node.TorrentCount == 0 && node.Children is [var onlyChild])
        {
            node = onlyChild;
        }
        return node with { Name = node.Path };
    }

    /// <summary>
    /// A server path split into folders. Daemons on Linux and macOS use "/"; on Windows the path may use "\" and
    /// start with a drive letter, so the separator is taken from the path.
    /// </summary>
    private sealed class RemotePath
    {
        private readonly char _separator;
        private readonly bool _isAbsolute;

        private RemotePath(IReadOnlyList<string> components, char separator, bool isAbsolute)
        {
            Components = components;
            _separator = separator;
            _isAbsolute = isAbsolute;
        }

        public IReadOnlyList<string> Components { get; }

        /// <summary>Null for paths with no folder names, such as "" or "/".</summary>
        public static RemotePath? Parse(string path)
        {
            path = path.Trim();
            var usesBackslashes = !path.Contains('/', StringComparison.Ordinal)
                && path.Contains('\\', StringComparison.Ordinal);
            var separator = usesBackslashes ? '\\' : '/';
            var components = path.Split(separator, StringSplitOptions.RemoveEmptyEntries);
            return components.Length == 0 ? null : new RemotePath(components, separator, path.StartsWith(separator));
        }

        /// <summary>The path made of the first <paramref name="count"/> folder names.</summary>
        public string Joined(int count)
        {
            var body = string.Join(_separator, Components.Take(count));
            if (_isAbsolute)
            {
                return _separator + body;
            }
            // A bare drive ("C:") means the current folder on that drive, not its root.
            return count == 1 && body.EndsWith(':') ? body + _separator : body;
        }
    }
}
