using TransmissionKit;

namespace TransmissionRemote.ViewModels;

public enum WantedState
{
    All,
    None,
    Mixed,
}

/// <summary>A folder or file in a torrent's file tree. Folders aggregate their contents.</summary>
/// <param name="Id">The node's path inside the torrent, unique within one tree. Folder IDs end in "/".</param>
/// <param name="FileIds">Every file index in this subtree, for applying a change to a whole folder.</param>
/// <param name="Priority">Null when the files inside have different priorities.</param>
/// <param name="Children">Null for files.</param>
public sealed record FileNode(
    string Id,
    string Name,
    IReadOnlyList<int> FileIds,
    long Size,
    long BytesCompleted,
    WantedState WantedState,
    Priority? Priority,
    IReadOnlyList<FileNode>? Children)
{
    public bool IsFolder => Children is not null;

    public double Progress => Size > 0 ? (double)BytesCompleted / Size : 0;

    /// <summary>The path relative to the torrent's download folder, as Transmission names it.</summary>
    public string RelativePath => IsFolder ? Id[..^1] : Id;
}

public static class FileTree
{
    /// <summary>
    /// Builds a tree from flat <c>a/b/c.ext</c> paths. A single top-level folder (the torrent's own folder)
    /// is skipped, so its contents appear directly. Padding files only align pieces, so they're left out.
    /// </summary>
    public static IReadOnlyList<FileNode> Build(IEnumerable<FileEntry> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var items = files.Where(file => !file.IsPadding)
            .Select(file => new Item(file.Path.Split('/', StringSplitOptions.RemoveEmptyEntries), file))
            .ToList();
        var nodes = MakeNodes(items, parentPath: "");
        return nodes is [{ Children: { } onlyChildren }] ? onlyChildren : nodes;
    }

    private sealed record Item(string[] Components, FileEntry File);

    private static List<FileNode> MakeNodes(IReadOnlyList<Item> items, string parentPath)
    {
        var nodes = new List<FileNode>();
        foreach (var group in items.GroupBy(item => item.Components.Length > 0 ? item.Components[0] : ""))
        {
            var path = parentPath.Length == 0 ? group.Key : $"{parentPath}/{group.Key}";
            nodes.AddRange(group.Where(item => item.Components.Length <= 1).Select(item => FileNodeFor(item.File, group.Key, path)));
            var nested = group.Where(item => item.Components.Length > 1)
                .Select(item => item with { Components = item.Components[1..] })
                .ToList();
            if (nested.Count > 0)
            {
                nodes.Add(FolderNode(group.Key, path, MakeNodes(nested, path)));
            }
        }
        // Folders first, then names in File Explorer order.
        return nodes.OrderByDescending(node => node.IsFolder)
            .ThenBy(node => node.Name, NaturalStringComparer.Instance)
            .ToList();
    }

    private static FileNode FileNodeFor(FileEntry file, string name, string path) => new(
        path, name, [file.Id], file.Length, file.BytesCompleted,
        file.Wanted ? WantedState.All : WantedState.None, file.Priority, Children: null);

    private static FileNode FolderNode(string name, string path, List<FileNode> children)
    {
        var wantedStates = children.Select(child => child.WantedState).Distinct().ToList();
        // A child folder with mixed priorities reports null, which makes this folder mixed too.
        var priorities = children.Select(child => child.Priority).Distinct().ToList();
        return new FileNode(
            path + "/",
            name,
            children.SelectMany(child => child.FileIds).ToList(),
            children.Sum(child => child.Size),
            children.Sum(child => child.BytesCompleted),
            wantedStates.Count == 1 ? wantedStates[0] : WantedState.Mixed,
            priorities.Count == 1 ? priorities[0] : null,
            children);
    }
}
