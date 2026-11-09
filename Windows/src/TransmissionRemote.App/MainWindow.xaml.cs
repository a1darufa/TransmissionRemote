using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TransmissionKit;
using TransmissionRemote.App.Controls;
using TransmissionRemote.App.Presentation;
using TransmissionRemote.App.Shell;
using TransmissionRemote.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace TransmissionRemote.App;

/// <summary>
/// The main window: Mica, the title bar with the menu bar, the command row, the sidebar and the content area.
/// The logic is in <see cref="MainViewModel"/>; this class only connects the controls to it.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ILogger _logger;
    private readonly TorrentActionsMenu _torrentActions;
    private IReadOnlyList<SpeedLimitChoice> _downloadChoices = [];
    private IReadOnlyList<SpeedLimitChoice> _uploadChoices = [];

    public MainWindow(
        MainViewModel viewModel, TorrentCellText cellText, DisplayFormatter format, IShell shell, ILogger<MainWindow> logger)
    {
        ViewModel = viewModel;
        _logger = logger;
        InitializeComponent();
        ConnectionPages.ViewModel = viewModel;
        Torrents.Bind(viewModel, cellText);
        StatusBar.Bind(viewModel, format);
        _torrentActions = new TorrentActionsMenu(viewModel, shell, RemoveSelectionAsync);
        _torrentActions.Fill(TorrentMenu.Items, Torrents.List, declareKeys: true);
        var rowMenu = new MenuFlyout();
        _torrentActions.Fill(rowMenu.Items, Torrents.List, declareKeys: false);
        Torrents.RowMenu = rowMenu;

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
        AttachShortcuts();
        UpdateSpeedLimitMenus();
        UpdateServerChoices();

        viewModel.PropertyChanged += OnViewModelChanged;
        viewModel.Connection.PropertyChanged += OnConnectionChanged;
        viewModel.Servers.Changed += (_, _) => UpdateServerChoices();
        viewModel.Settings.Changed += (_, _) => UpdateSpeedLimitMenus();
        viewModel.AddQueue.PropertyChanged += OnAddQueueChanged;
        Activated += OnActivated;
        Closed += OnClosed;
        ApplyDevelopmentTheme();
    }

    public MainViewModel ViewModel { get; }

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
            case nameof(ServerConnection.ActionError) when ViewModel.Connection.ActionError is { } error:
                ShowProblem(Strings.ActionFailed, ErrorMessages.For(error));
                break;
        }
    }

    private void OnAddQueueChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AddTorrentFlow.Problem) && ViewModel.AddQueue.Problem is { } problem)
        {
            ShowProblem(Strings.CouldNotAddTorrent, ErrorMessages.For(problem));
        }
    }

    // Errors, adding and removing --------------------------------------------------------------------------------

    /// <summary>
    /// A failed action or add in the bar above the list (brief 4.7.10): non-modal, so it never collides with a dialog.
    /// The newest problem replaces an older one.
    /// </summary>
    private void ShowProblem(string title, string message)
    {
        ProblemBar.Title = title;
        ProblemBar.Message = message;
        ProblemBar.IsOpen = true;
    }

    /// <summary>Closing the bar clears what it showed, so the same problem can be reported again.</summary>
    private void OnProblemBarClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        ViewModel.Connection.ActionError = null;
        ViewModel.AddQueue.Problem = null;
    }

    private async void OnRemoveClicked(object sender, RoutedEventArgs e) => await RemoveSelectionAsync(deletingData: false);

    /// <summary>Remove… and Remove and Delete Data…: ask first, then remove what's selected.</summary>
    private async Task RemoveSelectionAsync(bool deletingData)
    {
        var count = ViewModel.Connection.Selection.Count;
        if (count > 0 && await RemoveConfirmation.AskAsync(Root.XamlRoot, count, deletingData))
        {
            await ViewModel.RemoveSelectionAsync(deletingData);
        }
    }

    /// <summary>
    /// Ctrl+V or Shift+Insert in the list: magnet links and torrent URLs from the clipboard's text, one per line, or
    /// .torrent files copied in File Explorer. The clipboard is read only now, never watched.
    /// </summary>
    private async Task PasteAsync()
    {
        if (!ViewModel.IsConnected)
        {
            return;
        }
        var content = Clipboard.GetContent();
        if (content.Contains(StandardDataFormats.StorageItems))
        {
            var items = await content.GetStorageItemsAsync();
            ViewModel.AddQueue.Open(items.Select(item => item.Path).Where(path => path.Length > 0));
        }
        else if (content.Contains(StandardDataFormats.Text))
        {
            ViewModel.AddQueue.OpenLinks(await content.GetTextAsync());
        }
    }

    /// <summary>Files, links and text dragged over the list area can be dropped there while connected.</summary>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        var data = e.DataView;
        var canAdd = ViewModel.IsConnected && (data.Contains(StandardDataFormats.StorageItems)
            || data.Contains(StandardDataFormats.Text) || data.Contains(StandardDataFormats.WebLink));
        e.AcceptedOperation = canAdd ? DataPackageOperation.Copy : DataPackageOperation.None;
        if (canAdd)
        {
            e.DragUIOverride.Caption = Strings.Add;
        }
    }

    /// <summary>Dropped .torrent files and links go to the add flow; other files come back as "couldn't be opened".</summary>
    private async void OnDrop(object sender, DragEventArgs e)
    {
        var data = e.DataView;
        var deferral = e.GetDeferral();
        try
        {
            if (data.Contains(StandardDataFormats.StorageItems))
            {
                var items = await data.GetStorageItemsAsync();
                ViewModel.AddQueue.Open(items.Select(item => item.Path).Where(path => path.Length > 0));
            }
            else if (data.Contains(StandardDataFormats.WebLink))
            {
                ViewModel.AddQueue.OpenLinks((await data.GetWebLinkAsync()).ToString());
            }
            else if (data.Contains(StandardDataFormats.Text))
            {
                ViewModel.AddQueue.OpenLinks(await data.GetTextAsync());
            }
        }
        finally
        {
            deferral.Complete();
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
        var filter = args.SelectedItem switch
        {
            StatusSidebarItem status => status.SidebarFilter,
            EntrySidebarItem entry => entry.SidebarFilter,
            _ => null,
        };
        if (filter is not null)
        {
            ViewModel.SelectedFilter = filter;
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
    /// The menu items carry their keys (brief 3.5); the Torrent menu's are attached where it's built. The list has
    /// a few keys of its own with no menu item: Enter, Ctrl+A and paste.
    /// </summary>
    private void AttachShortcuts()
    {
        (UIElement Item, AppCommand Command)[] menuItems =
        [
            (AddTorrentFileItem, AppCommand.AddTorrentFile),
            (AddTorrentLinkItem, AppCommand.AddTorrentLink),
            (SettingsItem, AppCommand.Settings),
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
            Shortcuts.Attach(item, command, Torrents.List);
        }
        Shortcuts.AttachHidden(Root, AppCommand.FilterByName, () => FilterBox.Focus(FocusState.Keyboard), Torrents.List);
        Shortcuts.AttachHidden(Root, AppCommand.Exit, Close, Torrents.List);
        Shortcuts.AttachHidden(Torrents.List, AppCommand.ShowInfoForRow, ViewModel.ShowInfoForSelection, Torrents.List);
        Shortcuts.AttachHidden(Torrents.List, AppCommand.SelectAllTorrents, Torrents.List.SelectAll, Torrents.List);
        Shortcuts.AttachHidden(Torrents.List, AppCommand.Paste, () => _ = PasteAsync(), Torrents.List);
    }

    // Menus built from data -------------------------------------------------------------------------------------

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
        // The app's own theme is set on the Application (DevelopmentOverrides); the caption buttons follow here.
        if (Startup.DevelopmentOverrides.Theme is { } theme)
        {
            AppWindow.TitleBar.PreferredTheme = theme == ApplicationTheme.Light ? TitleBarTheme.Light : TitleBarTheme.Dark;
        }
    }
}
