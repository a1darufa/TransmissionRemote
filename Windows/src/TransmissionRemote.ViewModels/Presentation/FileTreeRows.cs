namespace TransmissionRemote.ViewModels;

/// <summary>A row of the Files tab: a node of the file tree, how deep it sits and whether its folder is open.</summary>
public readonly record struct VisibleFileNode(FileNode Node, int Depth, bool IsExpanded);

/// <summary>
/// The file tree as the rows a list shows: each node followed by its children while its folder is expanded. The
/// Files tab draws these in a <c>ListView</c>, which gives it real columns and multi-selection that a
/// <c>TreeView</c> can't combine with the download checkboxes.
/// </summary>
public static class FileTreeRows
{
    public static IReadOnlyList<VisibleFileNode> Visible(IReadOnlyList<FileNode> roots, IReadOnlySet<string> expandedIds)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(expandedIds);
        List<VisibleFileNode> rows = [];
        Add(roots, depth: 0);
        return rows;

        void Add(IReadOnlyList<FileNode> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                var isExpanded = node.Children is not null && expandedIds.Contains(node.Id);
                rows.Add(new VisibleFileNode(node, depth, isExpanded));
                if (isExpanded)
                {
                    Add(node.Children!, depth + 1);
                }
            }
        }
    }
}
