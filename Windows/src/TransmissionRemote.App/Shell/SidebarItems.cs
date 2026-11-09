using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Shell;

/// <summary>Picks the sidebar's template: a section header, a status filter, or a folder, label or tracker.</summary>
public sealed partial class SidebarTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }

    public DataTemplate? StatusTemplate { get; set; }

    public DataTemplate? EntryTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        SidebarHeaderItem => HeaderTemplate,
        StatusSidebarItem => StatusTemplate,
        EntrySidebarItem => EntryTemplate,
        _ => base.SelectTemplateCore(item),
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
