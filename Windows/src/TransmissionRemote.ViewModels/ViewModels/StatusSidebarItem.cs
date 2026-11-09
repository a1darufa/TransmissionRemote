using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TransmissionRemote.ViewModels;

/// <summary>The sidebar's sections, in order. Status is always there; the others only when they have entries.</summary>
public enum SidebarSection
{
    Status,
    Folders,
    Labels,
    Trackers,
}

/// <summary>A section's title in the sidebar. Not selectable.</summary>
public sealed record SidebarHeaderItem(SidebarSection Section);

/// <summary>One entry of the sidebar's Status section with its live count.</summary>
public sealed partial class StatusSidebarItem(StatusFilter filter) : ObservableObject
{
    public StatusFilter Filter { get; } = filter;

    /// <summary>What to set as <see cref="MainViewModel.SelectedFilter"/> when the item is selected.</summary>
    public SidebarFilter SidebarFilter { get; } = new StatusSidebarFilter(filter);

    [ObservableProperty]
    public partial int Count { get; internal set; }
}

/// <summary>A folder, label or tracker in the sidebar, with its live count.</summary>
public sealed partial class EntrySidebarItem : ObservableObject
{
    internal EntrySidebarItem(SidebarSection section, string name)
    {
        Section = section;
        Name = name;
        SidebarFilter = section switch
        {
            SidebarSection.Folders => new FolderSidebarFilter(name),
            SidebarSection.Labels => new LabelSidebarFilter(name),
            _ => new TrackerSidebarFilter(name),
        };
    }

    public SidebarSection Section { get; }

    /// <summary>The full value: the folder path, label or tracker host. Folders show it as their tooltip.</summary>
    public string Name { get; }

    public SidebarFilter SidebarFilter { get; }

    /// <summary>What the sidebar shows; for folders a short form of the path, which can change with other folders.</summary>
    [ObservableProperty]
    public partial string DisplayName { get; internal set; } = "";

    [ObservableProperty]
    public partial int Count { get; internal set; }
}

/// <summary>
/// The sidebar's entries as one list for the navigation pane: section headers, the status filters, then the folders,
/// labels and trackers. Entries are updated in place, never rebuilt, so the selection and Narrator's place stay put.
/// </summary>
internal static class SidebarItemsSync
{
    /// <param name="selected">
    /// The selected filter. A selected folder, label or tracker that no torrent has any more stays, with a count of
    /// 0, as the Mac app keeps it selected and shows an empty list.
    /// </param>
    public static void Apply(ObservableCollection<object> items, SidebarSummary summary, SidebarFilter selected)
    {
        SyncSection(items, SidebarSection.Folders, Entries(summary.Folders, SidebarSection.Folders, selected));
        SyncSection(items, SidebarSection.Labels, Entries(summary.Labels, SidebarSection.Labels, selected));
        SyncSection(items, SidebarSection.Trackers, Entries(summary.Trackers, SidebarSection.Trackers, selected));
    }

    private static List<SidebarEntry> Entries(IReadOnlyList<SidebarEntry> entries, SidebarSection section, SidebarFilter selected)
    {
        var list = entries.ToList();
        var selectedName = (section, selected) switch
        {
            (SidebarSection.Folders, FolderSidebarFilter folder) => folder.Path,
            (SidebarSection.Labels, LabelSidebarFilter label) => label.Label,
            (SidebarSection.Trackers, TrackerSidebarFilter tracker) => tracker.Host,
            _ => null,
        };
        if (selectedName is not null && list.TrueForAll(entry => entry.Name != selectedName))
        {
            var displayName = section == SidebarSection.Folders ? RemotePaths.LastComponent(selectedName) : selectedName;
            list.Add(new SidebarEntry(selectedName, 0, displayName));
        }
        return list;
    }

    private static void SyncSection(ObservableCollection<object> items, SidebarSection section, List<SidebarEntry> desired)
    {
        if (desired.Count == 0)
        {
            RemoveSection(items, section);
            return;
        }
        var headerIndex = IndexOfHeader(items, section);
        if (headerIndex < 0)
        {
            headerIndex = InsertionPoint(items, section);
            items.Insert(headerIndex, new SidebarHeaderItem(section));
        }

        var existing = items.OfType<EntrySidebarItem>().Where(item => item.Section == section)
            .ToDictionary(item => item.Name, StringComparer.Ordinal);
        foreach (var gone in existing.Values.Where(item => desired.TrueForAll(entry => entry.Name != item.Name)).ToList())
        {
            items.Remove(gone);
            existing.Remove(gone.Name);
        }
        for (var position = 0; position < desired.Count; position++)
        {
            var entry = desired[position];
            var index = headerIndex + 1 + position;
            if (!existing.TryGetValue(entry.Name, out var item))
            {
                item = new EntrySidebarItem(section, entry.Name);
                items.Insert(index, item);
            }
            else if (items.IndexOf(item) != index)
            {
                items.Move(items.IndexOf(item), index);
            }
            item.DisplayName = entry.DisplayName;
            item.Count = entry.Count;
        }
    }

    private static void RemoveSection(ObservableCollection<object> items, SidebarSection section)
    {
        var inSection = items.Where(item => item is SidebarHeaderItem header && header.Section == section
            || item is EntrySidebarItem entry && entry.Section == section).ToList();
        foreach (var item in inSection)
        {
            items.Remove(item);
        }
    }

    private static int IndexOfHeader(ObservableCollection<object> items, SidebarSection section)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is SidebarHeaderItem header && header.Section == section)
            {
                return index;
            }
        }
        return -1;
    }

    /// <summary>Where a missing section goes: before the next section that's there, or at the end.</summary>
    private static int InsertionPoint(ObservableCollection<object> items, SidebarSection section)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is SidebarHeaderItem header && header.Section > section)
            {
                return index;
            }
        }
        return items.Count;
    }
}
