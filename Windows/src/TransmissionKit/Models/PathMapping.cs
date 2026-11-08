namespace TransmissionKit;

/// <summary>
/// A folder on the server and the same folder as this computer sees it, for example a NAS share mounted over SMB:
/// /mnt/HD/HD_a2 on the server is /Volumes/HD_a2 here. Subfolders are appended to the local path with "/".
/// </summary>
/// <param name="RemotePath">The folder as the daemon reports it.</param>
/// <param name="LocalPath">The same folder on this computer.</param>
public sealed record PathMapping(string RemotePath = "", string LocalPath = "")
{
    /// <summary>Tells mappings apart while they're edited, even when two have the same paths.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// <paramref name="remotePath"/> on this computer, or null when it isn't inside <see cref="RemotePath"/>.
    /// Only whole folder names match, so a mapping for /data doesn't claim /database.
    /// Windows daemons' paths (D:\Torrents\TV) work too, compared without regard to case.
    /// </summary>
    public string? LocalPathFor(string remotePath)
    {
        var isWindows = IsWindowsPath(RemotePath) || IsWindowsPath(remotePath);
        var prefix = Normalized(RemotePath, isWindows);
        var local = Normalized(LocalPath);
        var path = Normalized(remotePath, isWindows);
        if (prefix.Length == 0 || local.Length == 0)
        {
            return null;
        }

        var comparison = isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string remainder;
        if (string.Equals(path, prefix, comparison))
        {
            remainder = "";
        }
        else if (prefix == "/" && path.StartsWith('/'))
        {
            remainder = path[1..];
        }
        else if (path.StartsWith(prefix + "/", comparison))
        {
            remainder = path[(prefix.Length + 1)..];
        }
        else
        {
            return null;
        }

        if (remainder.Length == 0)
        {
            return local;
        }
        return local == "/" ? "/" + remainder : local + "/" + remainder;
    }

    /// <summary>
    /// Trims spaces and trailing slashes, keeping a lone "/" for the root. For Windows paths, backslashes become
    /// slashes first, so D:\Torrents\ is D:/Torrents.
    /// </summary>
    internal static string Normalized(string path, bool windows = false)
    {
        path = path.Trim();
        if (windows)
        {
            path = path.Replace('\\', '/');
        }
        while (path.Length > 1 && path.EndsWith('/'))
        {
            path = path[..^1];
        }
        return path;
    }

    /// <summary>A drive letter (C:) or a backslash marks a path on a Windows daemon.</summary>
    internal static bool IsWindowsPath(string path)
    {
        var trimmed = path.Trim();
        var hasDriveLetter = trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':';
        return hasDriveLetter || trimmed.Contains('\\', StringComparison.Ordinal);
    }
}

/// <summary>Picks the right mapping when several are set up.</summary>
public static class PathMappingExtensions
{
    extension(IEnumerable<PathMapping> mappings)
    {
        /// <summary>
        /// The mapping that covers <paramref name="remotePath"/>, preferring the deepest server folder.
        /// </summary>
        public PathMapping? MappingFor(string remotePath) =>
            mappings
                .Where(mapping => mapping.LocalPathFor(remotePath) is not null)
                .MaxBy(mapping => PathMapping.Normalized(mapping.RemotePath).Length);

        /// <summary><paramref name="remotePath"/> on this computer, or null when no mapping covers it.</summary>
        public string? LocalPathFor(string remotePath) => mappings.MappingFor(remotePath)?.LocalPathFor(remotePath);
    }
}
