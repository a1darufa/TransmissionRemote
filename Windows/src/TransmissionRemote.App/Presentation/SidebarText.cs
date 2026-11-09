using System.Globalization;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Presentation;

/// <summary>What the sidebar shows and Narrator reads for each status filter, for x:Bind.</summary>
internal static class SidebarText
{
    public static string Label(StatusFilter filter) => filter switch
    {
        StatusFilter.All => Strings.All,
        StatusFilter.Downloading => Strings.Downloading,
        StatusFilter.Seeding => Strings.Seeding,
        StatusFilter.Completed => Strings.Completed,
        StatusFilter.Active => Strings.Active,
        StatusFilter.Inactive => Strings.Inactive,
        StatusFilter.Stopped => Strings.Stopped,
        StatusFilter.Waiting => Strings.Waiting,
        StatusFilter.Verifying => Strings.Verifying_A7D4A9,
        StatusFilter.Error => Strings.Error_54A0E8,
        _ => filter.ToString(),
    };

    /// <summary>Segoe Fluent Icons glyphs matching the Mac's SF Symbols (brief 4.2).</summary>
    public static string Glyph(StatusFilter filter) => filter switch
    {
        StatusFilter.All => "",
        StatusFilter.Downloading => "",
        StatusFilter.Seeding => "",
        StatusFilter.Completed => "",
        StatusFilter.Active => "",
        StatusFilter.Inactive => "",
        StatusFilter.Stopped => "",
        StatusFilter.Waiting => "",
        StatusFilter.Verifying => "",
        StatusFilter.Error => "",
        _ => "",
    };

    public static string SectionTitle(SidebarSection section) => section switch
    {
        SidebarSection.Status => Strings.Status,
        SidebarSection.Folders => Strings.Folders_C4D6BB,
        SidebarSection.Labels => Strings.Labels,
        _ => Strings.Trackers,
    };

    /// <summary>Folder, tag and antenna, as the Mac's folder, tag and antenna symbols.</summary>
    public static string EntryGlyph(SidebarSection section) => section switch
    {
        SidebarSection.Folders => "",
        SidebarSection.Labels => "",
        _ => "",
    };

    /// <summary>The full folder path as the tooltip; labels and trackers show their whole name already.</summary>
    public static string? EntryToolTip(SidebarSection section, string name) => section == SidebarSection.Folders ? name : null;

    public static string EntryAccessibleName(string displayName, int count) =>
        StringResources.Format(Strings.SidebarItemNameCount, displayName, Count(count));

    /// <summary>A count with the user's digit grouping, as the list shows it.</summary>
    public static string Count(int count) => count.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>"Downloading, 3": Narrator reads the count, which the eye takes from the right edge.</summary>
    public static string AccessibleName(StatusFilter filter, int count) =>
        StringResources.Format(Strings.SidebarItemNameCount, Label(filter), Count(count));
}
