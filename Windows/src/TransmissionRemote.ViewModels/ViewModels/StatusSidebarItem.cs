using CommunityToolkit.Mvvm.ComponentModel;

namespace TransmissionRemote.ViewModels;

/// <summary>One entry of the sidebar's Status section with its live count.</summary>
public sealed partial class StatusSidebarItem(StatusFilter filter) : ObservableObject
{
    public StatusFilter Filter { get; } = filter;

    /// <summary>What to set as <see cref="MainViewModel.SelectedFilter"/> when the item is selected.</summary>
    public SidebarFilter SidebarFilter { get; } = new StatusSidebarFilter(filter);

    [ObservableProperty]
    public partial int Count { get; internal set; }
}
