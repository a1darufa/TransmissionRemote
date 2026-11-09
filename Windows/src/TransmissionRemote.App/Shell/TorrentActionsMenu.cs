using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TransmissionKit;
using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Shell;

/// <summary>
/// The torrent actions (brief 4.4; Mac: TorrentActionsMenu), the same items in the same order in the Torrent menu
/// and the list's context menu. The menu bar's copy declares the keyboard shortcuts; the context menu's copy shows
/// the same keys through accelerators that are switched off, so a key never fires twice.
/// </summary>
internal sealed class TorrentActionsMenu
{
    private readonly MainViewModel _viewModel;
    private readonly IShell _shell;
    private readonly Func<bool, Task> _remove;
    private readonly List<MenuFlyoutItemBase> _selectionItems = [];

    /// <param name="remove">Asks and removes the selection; true deletes the data too.</param>
    public TorrentActionsMenu(MainViewModel viewModel, IShell shell, Func<bool, Task> remove)
    {
        _viewModel = viewModel;
        _shell = shell;
        _remove = remove;
        viewModel.PropertyChanged += OnViewModelChanged;
    }

    /// <summary>Adds the items to a menu.</summary>
    /// <param name="listScope">Where keys that only work in the list apply.</param>
    /// <param name="declareKeys">True for the menu bar, whose items own the shortcuts.</param>
    public void Fill(IList<MenuFlyoutItemBase> items, UIElement listScope, bool declareKeys)
    {
        // Open and Show in folder need path mappings (Settings, M5) and the runnable-file warning (M4).
        items.Add(Item(Strings.Open, "", null, isEnabled: false));
        items.Add(Item(Strings.ShowInFolder, "", AppCommand.ShowInFolder, isEnabled: false));
        items.Add(new MenuFlyoutSeparator());
        items.Add(CommandItem(Strings.Start, "", AppCommand.Start, _viewModel.StartCommand));
        items.Add(CommandItem(Strings.StartNow, "", AppCommand.StartNow, _viewModel.StartNowCommand));
        items.Add(CommandItem(Strings.Stop, "", AppCommand.Stop, _viewModel.StopCommand));
        items.Add(new MenuFlyoutSeparator());
        items.Add(CommandItem(Strings.VerifyLocalData, "", null, _viewModel.VerifyCommand));
        items.Add(CommandItem(Strings.AskTrackersForMorePeers, "", AppCommand.AskTrackersForMorePeers, _viewModel.ReannounceCommand));
        items.Add(new MenuFlyoutSeparator());
        // The dialogs come in milestone M4.
        items.Add(Item(Strings.MoveDataEllipsis, "", AppCommand.MoveData, isEnabled: false));
        items.Add(Item(Strings.RenameEllipsis, "", AppCommand.Rename, isEnabled: false));
        items.Add(Item(Strings.LabelsEllipsis, "", AppCommand.Labels, isEnabled: false));
        items.Add(Item(Strings.TorrentOptionsEllipsis, "", AppCommand.TorrentOptions, isEnabled: false));
        items.Add(SpeedLimitMenu(Strings.DownloadSpeedLimit, "", TransferDirection.Download));
        items.Add(SpeedLimitMenu(Strings.UploadSpeedLimit, "", TransferDirection.Upload));
        items.Add(Item(Strings.EditTrackersEllipsis, "", null, isEnabled: false));
        items.Add(PriorityMenu());
        items.Add(QueueMenu());
        items.Add(new MenuFlyoutSeparator());
        items.Add(ClickItem(Strings.CopyMagnetLink, "", AppCommand.CopyMagnetLink, () => _viewModel.CopyMagnetLinksAsync(_shell)));
        items.Add(new MenuFlyoutSeparator());
        items.Add(ClickItem(Strings.RemoveEllipsis, "", AppCommand.Remove, () => _remove(false)));
        items.Add(ClickItem(Strings.RemoveAndDeleteDataEllipsis, "", AppCommand.RemoveAndDeleteData, () => _remove(true)));

        foreach (var item in WithSubmenus(items))
        {
            if (item.Tag is AppCommand command)
            {
                Shortcuts.Attach(item, command, listScope);
                if (!declareKeys)
                {
                    foreach (var accelerator in item.KeyboardAccelerators)
                    {
                        accelerator.IsEnabled = false;
                    }
                }
            }
        }
        UpdateEnabled();
    }

    /// <summary>Every item, including those in submenus such as Queue.</summary>
    private static IEnumerable<MenuFlyoutItem> WithSubmenus(IEnumerable<MenuFlyoutItemBase> items)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case MenuFlyoutItem single:
                    yield return single;
                    break;
                case MenuFlyoutSubItem submenu:
                    foreach (var inner in WithSubmenus(submenu.Items))
                    {
                        yield return inner;
                    }
                    break;
            }
        }
    }

    private static MenuFlyoutItem Item(string text, string glyph, AppCommand? command, bool isEnabled) => new()
    {
        Text = text,
        Icon = new FontIcon { Glyph = glyph },
        Tag = command,
        IsEnabled = isEnabled,
    };

    private static MenuFlyoutItem CommandItem(string text, string glyph, AppCommand? command, System.Windows.Input.ICommand action)
    {
        var item = Item(text, glyph, command, isEnabled: true);
        item.Command = action;
        return item;
    }

    /// <summary>An item that acts on the selection through a method rather than a command, kept in step by hand.</summary>
    private MenuFlyoutItem ClickItem(string text, string glyph, AppCommand? command, Func<Task> action)
    {
        var item = Item(text, glyph, command, isEnabled: true);
        item.Click += async (_, _) => await action();
        _selectionItems.Add(item);
        return item;
    }

    /// <summary>Unlimited, then the presets; no check mark, since the selected torrents may differ (brief 4.4.2).</summary>
    private MenuFlyoutSubItem SpeedLimitMenu(string text, string glyph, TransferDirection direction)
    {
        var menu = new MenuFlyoutSubItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        var presets = direction == TransferDirection.Download
            ? _viewModel.Settings.Current.DownloadSpeedPresets
            : _viewModel.Settings.Current.UploadSpeedPresets;
        menu.Items.Add(LimitItem(Strings.Unlimited, null, direction));
        menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var speed in SpeedLimitChoice.Speeds(presets))
        {
            menu.Items.Add(LimitItem(
                StringResources.Format(Strings.KBS_22B85B, speed.ToString("N0", CultureInfo.CurrentCulture)), speed, direction));
        }
        _selectionItems.Add(menu);
        return menu;
    }

    private MenuFlyoutItem LimitItem(string text, int? limit, TransferDirection direction)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += async (_, _) =>
            await _viewModel.Connection.SetSpeedLimitAsync(limit, direction, _viewModel.Connection.Selection);
        return item;
    }

    private MenuFlyoutSubItem PriorityMenu()
    {
        var menu = new MenuFlyoutSubItem { Text = Strings.BandwidthPriority_5262E0, Icon = new FontIcon { Glyph = "" } };
        foreach (var (text, priority) in new[] { (Strings.High, Priority.High), (Strings.Normal, Priority.Normal), (Strings.Low, Priority.Low) })
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += async (_, _) =>
                await _viewModel.Connection.SetBandwidthPriorityAsync(priority, _viewModel.Connection.Selection);
            menu.Items.Add(item);
        }
        _selectionItems.Add(menu);
        return menu;
    }

    private MenuFlyoutSubItem QueueMenu()
    {
        var menu = new MenuFlyoutSubItem { Text = Strings.Queue, Icon = new FontIcon { Glyph = "" } };
        (string Text, QueueMove Move, AppCommand Command)[] moves =
        [
            (Strings.MoveToTop, QueueMove.Top, AppCommand.MoveToTopOfQueue),
            (Strings.MoveUp, QueueMove.Up, AppCommand.MoveUpInQueue),
            (Strings.MoveDown, QueueMove.Down, AppCommand.MoveDownInQueue),
            (Strings.MoveToBottom, QueueMove.Bottom, AppCommand.MoveToBottomOfQueue),
        ];
        foreach (var (text, move, command) in moves)
        {
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = text,
                Tag = command,
                Command = _viewModel.MoveInQueueCommand,
                CommandParameter = move,
            });
        }
        _selectionItems.Add(menu);
        return menu;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CanActOnSelection))
        {
            UpdateEnabled();
        }
    }

    private void UpdateEnabled()
    {
        foreach (var item in _selectionItems)
        {
            item.IsEnabled = _viewModel.CanActOnSelection;
        }
    }
}
