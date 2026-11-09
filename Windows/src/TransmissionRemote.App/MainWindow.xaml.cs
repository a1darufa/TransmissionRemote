using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TransmissionKit;
using TransmissionRemote.App.Presentation;
using TransmissionRemote.App.Shell;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App;

/// <summary>
/// The main window: Mica, the title bar with the menu bar, the command row, the sidebar and the content area.
/// The logic is in <see cref="MainViewModel"/>; this class only connects the controls to it.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ILogger _logger;
    private IReadOnlyList<SpeedLimitChoice> _downloadChoices = [];
    private IReadOnlyList<SpeedLimitChoice> _uploadChoices = [];

    public MainWindow(MainViewModel viewModel, ILogger<MainWindow> logger)
    {
        ViewModel = viewModel;
        _logger = logger;
        SidebarItems = [new SidebarHeader(Strings.Status), .. viewModel.StatusItems];
        InitializeComponent();
        ConnectionPages.ViewModel = viewModel;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        // Tall caption buttons match the 48 epx TitleBar control.
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        // The taskbar and Alt+Tab icon; the .exe's own icon only covers File Explorer and shortcuts.
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        UpdateTitle();
        MainWindowPlacement.Restore(AppWindow, viewModel.Settings.Current.MainWindowPlacement);
        Root.Loaded += (_, _) => FollowDisplayScale();

        Sidebar.OpenPaneLength = viewModel.Settings.Current.EffectiveSidebarWidth;
        Sidebar.IsPaneOpen = viewModel.ShowSidebar;
        Sidebar.SelectedItem = viewModel.StatusItems[0];
        ShowSidebarItem.IsChecked = viewModel.ShowSidebar;
        AssignQueueCommands();
        AttachShortcuts();
        UpdateSpeedLimitMenus();
        UpdateServerChoices();

        viewModel.PropertyChanged += OnViewModelChanged;
        viewModel.Connection.PropertyChanged += OnConnectionChanged;
        viewModel.Servers.Changed += (_, _) => UpdateServerChoices();
        viewModel.Settings.Changed += (_, _) => UpdateSpeedLimitMenus();
        Activated += OnActivated;
        Closed += OnClosed;
        ApplyDevelopmentTheme();
    }

    public MainViewModel ViewModel { get; }

    /// <summary>The sidebar's entries: the Status header and the ten status filters.</summary>
    public IReadOnlyList<object> SidebarItems { get; }

    /// <summary>Shows the window in front of others, also when it was minimized or hidden in the tray.</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }
        AppWindow.Show();
        Activate();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ServerName))
        {
            UpdateTitle();
        }
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ServerConnection.Session):
                UpdateSpeedLimitMenus();
                break;
            case nameof(ServerConnection.Profile):
                UpdateServerChoices();
                break;
        }
    }

    /// <summary>"My Server – Transmission Remote" on the taskbar and in Alt+Tab, once a server is set.</summary>
    private void UpdateTitle() => Title = ViewModel.ServerName.Length > 0
        ? StringResources.Format(Strings.TransmissionRemoteWindowTitle, ViewModel.ServerName)
        : Strings.TransmissionRemote;

    // Polling is slower while the window is in the background (20 s instead of 5 s).
    private void OnActivated(object sender, WindowActivatedEventArgs args) =>
        ViewModel.Connection.IsInForeground = args.WindowActivationState != WindowActivationState.Deactivated;

    private void OnClosed(object sender, WindowEventArgs args)
    {
        var width = Sidebar.OpenPaneLength;
        var placement = MainWindowPlacement.Capture(AppWindow) ?? ViewModel.Settings.Current.MainWindowPlacement;
        ViewModel.Settings.Update(settings => settings with { SidebarWidth = width, MainWindowPlacement = placement });
    }

    /// <summary>Keeps the minimum window size right when the window moves to a display with another scale.</summary>
    private void FollowDisplayScale()
    {
        MainWindowPlacement.ApplyMinimumSize(AppWindow, Root.XamlRoot.RasterizationScale);
        Root.XamlRoot.Changed += (root, _) => MainWindowPlacement.ApplyMinimumSize(AppWindow, root.RasterizationScale);
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void OnOpenLogsFolder(object sender, RoutedEventArgs e) => LogsFolder.Open(_logger);

    // The sidebar ------------------------------------------------------------------------------------------------

    private void OnSidebarSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is StatusSidebarItem item)
        {
            ViewModel.SelectedFilter = item.SidebarFilter;
        }
    }

    private void OnPaneToggleRequested(TitleBar sender, object args) => ToggleSidebar();

    private void OnShowSidebarClicked(object sender, RoutedEventArgs e) => ToggleSidebar();

    private void ToggleSidebar()
    {
        Sidebar.IsPaneOpen = !Sidebar.IsPaneOpen;
        // Only the full-width sidebar is remembered: in a narrow window it opens over the list for a moment.
        if (Sidebar.DisplayMode == NavigationViewDisplayMode.Expanded)
        {
            ViewModel.ShowSidebar = Sidebar.IsPaneOpen;
        }
    }

    private void OnSidebarPaneChanged(NavigationView sender, object args) => ShowSidebarItem.IsChecked = sender.IsPaneOpen;

    private void OnSidebarDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        // Widening the window brings the sidebar back the way the user left it.
        if (args.DisplayMode == NavigationViewDisplayMode.Expanded)
        {
            sender.IsPaneOpen = ViewModel.ShowSidebar;
        }
        ShowSidebarItem.IsChecked = sender.IsPaneOpen;
    }

    // Keyboard shortcuts ----------------------------------------------------------------------------------------

    /// <summary>
    /// The menu items carry their keys (brief 3.5). The list's own keys (Enter, Ctrl+A, Ctrl+V) come with the list
    /// in M2; until then, the keys that only work in the list are scoped to the area it will fill.
    /// </summary>
    private void AttachShortcuts()
    {
        (UIElement Item, AppCommand Command)[] menuItems =
        [
            (AddTorrentFileItem, AppCommand.AddTorrentFile),
            (AddTorrentLinkItem, AppCommand.AddTorrentLink),
            (SettingsItem, AppCommand.Settings),
            (ShowInFolderItem, AppCommand.ShowInFolder),
            (StartItem, AppCommand.Start),
            (StartNowItem, AppCommand.StartNow),
            (StopItem, AppCommand.Stop),
            (AskTrackersItem, AppCommand.AskTrackersForMorePeers),
            (MoveDataItem, AppCommand.MoveData),
            (RenameItem, AppCommand.Rename),
            (LabelsItem, AppCommand.Labels),
            (TorrentOptionsItem, AppCommand.TorrentOptions),
            (MoveToTopItem, AppCommand.MoveToTopOfQueue),
            (MoveUpItem, AppCommand.MoveUpInQueue),
            (MoveDownItem, AppCommand.MoveDownInQueue),
            (MoveToBottomItem, AppCommand.MoveToBottomOfQueue),
            (CopyMagnetLinkItem, AppCommand.CopyMagnetLink),
            (RemoveItem, AppCommand.Remove),
            (RemoveAndDeleteDataItem, AppCommand.RemoveAndDeleteData),
            (StartAllItem, AppCommand.StartAll),
            (StopAllItem, AppCommand.StopAll),
            (AlternativeSpeedItem, AppCommand.AlternativeSpeedLimits),
            (ServerSettingsItem, AppCommand.ServerSettings),
            (ReconnectItem, AppCommand.Reconnect),
            (ShowSidebarItem, AppCommand.ShowSidebar),
            (ShowInfoPaneItem, AppCommand.ShowInfoPane),
        ];
        foreach (var (item, command) in menuItems)
        {
            Shortcuts.Attach(item, command, TorrentListHost);
        }
        Shortcuts.AttachHidden(Root, AppCommand.FilterByName, () => FilterBox.Focus(FocusState.Keyboard));
        Shortcuts.AttachHidden(Root, AppCommand.Exit, Close);
    }

    // Menus built from data -------------------------------------------------------------------------------------

    private void AssignQueueCommands()
    {
        (MenuFlyoutItem Item, QueueMove Move)[] items =
        [
            (MoveToTopItem, QueueMove.Top), (MoveUpItem, QueueMove.Up),
            (MoveDownItem, QueueMove.Down), (MoveToBottomItem, QueueMove.Bottom),
        ];
        foreach (var (item, move) in items)
        {
            item.Command = ViewModel.MoveInQueueCommand;
            item.CommandParameter = move;
        }
    }

    /// <summary>
    /// The Server menu's speed limit submenus. Rebuilt only when their items change, so a refresh doesn't replace
    /// them under the pointer.
    /// </summary>
    private void UpdateSpeedLimitMenus()
    {
        _downloadChoices = Refill(DownloadLimitMenu, TransferDirection.Download, _downloadChoices);
        _uploadChoices = Refill(UploadLimitMenu, TransferDirection.Upload, _uploadChoices);
    }

    private IReadOnlyList<SpeedLimitChoice> Refill(
        MenuFlyoutSubItem menu, TransferDirection direction, IReadOnlyList<SpeedLimitChoice> shown)
    {
        var choices = ViewModel.GlobalSpeedLimitChoices(direction);
        if (choices.SequenceEqual(shown))
        {
            return shown;
        }
        menu.Items.Clear();
        foreach (var choice in choices)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = SpeedLimitText(choice.Limit),
                GroupName = $"GlobalSpeedLimit{direction}",
                IsChecked = choice.IsCurrent,
            };
            item.Click += (_, _) => _ = ViewModel.Connection.SetGlobalSpeedLimitAsync(choice.Limit, direction);
            menu.Items.Add(item);
            if (choice.Limit is null)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
            }
        }
        return choices;
    }

    /// <summary>"Unlimited" or "250 kB/s", the number as typed in the presets, grouped for the user's region.</summary>
    private static string SpeedLimitText(int? limit) => limit is { } speed
        ? StringResources.Format(Strings.KBS_22B85B, speed.ToString("N0", CultureInfo.CurrentCulture))
        : Strings.Unlimited;

    /// <summary>Server → Server: a radio list of the servers, only when there's more than one to pick from.</summary>
    private void UpdateServerChoices()
    {
        var servers = ViewModel.Servers.Servers;
        ServerChoiceMenu.Visibility = Presentation.Visible.When(servers.Count > 1);
        ServerChoiceMenu.Items.Clear();
        foreach (var server in servers)
        {
            ServerChoiceMenu.Items.Add(ServerChoice(server, "ServerMenu", ViewModel.Servers.ActiveServer?.Id));
        }
    }

    /// <summary>The command row's Server button: the servers with the connected one checked, then Reconnect.</summary>
    private void OnServerFlyoutOpening(object? sender, object e)
    {
        ServerFlyout.Items.Clear();
        foreach (var server in ViewModel.Servers.Servers)
        {
            ServerFlyout.Items.Add(ServerChoice(server, "ServerButton", ViewModel.Connection.Profile?.Id));
        }
        if (ViewModel.Servers.Servers.Count > 0)
        {
            ServerFlyout.Items.Add(new MenuFlyoutSeparator());
        }
        ServerFlyout.Items.Add(new MenuFlyoutItem { Text = Strings.Reconnect, Command = ViewModel.ReconnectCommand });
        // Settings arrives in milestone M5.
        ServerFlyout.Items.Add(new MenuFlyoutItem { Text = Strings.ManageServersEllipsis, IsEnabled = false });
    }

    private RadioMenuFlyoutItem ServerChoice(ServerProfile server, string group, Guid? checkedId) => new()
    {
        Text = server.DisplayName,
        GroupName = group,
        IsChecked = server.Id == checkedId,
        Command = ViewModel.SwitchToServerCommand,
        CommandParameter = server.Id,
    };

    /// <summary>
    /// Debug builds only: TRANSMISSIONREMOTE_THEME=Light or Dark overrides the Windows theme, to take review
    /// screenshots in both without changing the PC's settings. The app itself always follows Windows.
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    private void ApplyDevelopmentTheme()
    {
        if (!Enum.TryParse<ElementTheme>(Environment.GetEnvironmentVariable("TRANSMISSIONREMOTE_THEME"), out var theme)
            || theme == ElementTheme.Default)
        {
            return;
        }
        Root.RequestedTheme = theme;
        AppWindow.TitleBar.PreferredTheme = theme == ElementTheme.Light ? TitleBarTheme.Light : TitleBarTheme.Dark;
    }
}
