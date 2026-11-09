using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Shell;

/// <summary>A section title in the sidebar, such as "Status". Not selectable.</summary>
public sealed record SidebarHeader(string Title);

/// <summary>Picks the sidebar's template: a section header or a status filter with its count.</summary>
public sealed partial class SidebarTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }

    public DataTemplate? StatusTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        SidebarHeader => HeaderTemplate,
        StatusSidebarItem => StatusTemplate,
        _ => base.SelectTemplateCore(item),
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
