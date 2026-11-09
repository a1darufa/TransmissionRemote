namespace TransmissionRemote.ViewModels;

/// <summary>Everything that has a keyboard shortcut.</summary>
public enum AppCommand
{
    AddTorrentFile,
    AddTorrentLink,
    Start,
    StartNow,
    Stop,
    StartAll,
    StopAll,
    Remove,
    RemoveAndDeleteData,
    AskTrackersForMorePeers,
    MoveData,
    Rename,
    Labels,
    TorrentOptions,
    MoveToTopOfQueue,
    MoveUpInQueue,
    MoveDownInQueue,
    MoveToBottomOfQueue,
    CopyMagnetLink,
    ShowInFolder,

    /// <summary>Enter on a row: show its info pane.</summary>
    ShowInfoForRow,
    AlternativeSpeedLimits,
    ServerSettings,
    Settings,
    Reconnect,
    ShowInfoPane,
    ShowSidebar,
    FilterByName,

    /// <summary>Paste magnet links or torrent URLs to add them.</summary>
    Paste,
    SelectAllTorrents,
    Exit,
}

/// <summary>Modifier keys. The values are Windows' own (<c>VirtualKeyModifiers</c>).</summary>
[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
}

/// <summary>
/// The keys the shortcuts use, all of them in the same place on every keyboard layout: letters, digits,
/// function keys and the editing keys. The values are Windows' virtual-key codes (<c>VirtualKey</c>).
/// </summary>
public enum ShortcutKey
{
    Enter = 0x0D,
    Insert = 0x2D,
    Delete = 0x2E,
    Number1 = 0x31,
    Number2 = 0x32,
    A = 0x41,
    C = 0x43,
    E = 0x45,
    F = 0x46,
    I = 0x49,
    L = 0x4C,
    M = 0x4D,
    O = 0x4F,
    R = 0x52,
    T = 0x54,
    U = 0x55,
    V = 0x56,
    X = 0x58,
    F2 = 0x71,
    F3 = 0x72,
    F4 = 0x73,
    F6 = 0x75,
    F7 = 0x76,
    F8 = 0x77,
    F9 = 0x78,
    F11 = 0x7A,
    F12 = 0x7B,

    /// <summary>
    /// The only punctuation key used, for Ctrl+, (Settings), the Windows convention. Unlike / or ., the comma
    /// needs no Shift on the common layouts; Ctrl+F9 is there as well.
    /// </summary>
    Comma = 0xBC,
}

/// <summary>A key with its modifiers, such as Ctrl+Shift+M.</summary>
public sealed record KeyChord(ShortcutKey Key, ShortcutModifiers Modifiers = ShortcutModifiers.None)
{
    private static readonly HashSet<KeyChord> ListOnlyChords =
    [
        new(ShortcutKey.Delete),
        new(ShortcutKey.Delete, ShortcutModifiers.Shift),
        new(ShortcutKey.Enter),
        new(ShortcutKey.Enter, ShortcutModifiers.Control),
        new(ShortcutKey.Insert, ShortcutModifiers.Control),
        new(ShortcutKey.Insert, ShortcutModifiers.Shift),
        new(ShortcutKey.A, ShortcutModifiers.Control),
        new(ShortcutKey.C, ShortcutModifiers.Control),
        new(ShortcutKey.V, ShortcutModifiers.Control),
        new(ShortcutKey.X, ShortcutModifiers.Control),
        new(ShortcutKey.F2),
    ];

    /// <summary>
    /// Keys that text fields use themselves (Delete, Enter, Ctrl+C, Ctrl+Insert…), plus F2 for Rename. They only
    /// work while the torrent list has the focus, so typing in the filter box never removes or renames a torrent.
    /// </summary>
    public bool IsListOnly => ListOnlyChords.Contains(this);
}

/// <param name="Shown">What menus and tooltips show; null for a shortcut that is accepted but not shown.</param>
/// <param name="AlsoAccepted">Further keys for the same command, not shown: transgui's or the Mac's.</param>
public sealed record CommandShortcuts(AppCommand Command, KeyChord? Shown, IReadOnlyList<KeyChord> AlsoAccepted)
{
    /// <summary>Every key for the command, the shown one first.</summary>
    public IEnumerable<KeyChord> All => Shown is null ? AlsoAccepted : [Shown, .. AlsoAccepted];
}

/// <summary>
/// The app's keyboard shortcuts: the Windows convention first, then transgui's key, which its Windows users know,
/// then the Mac's with ⌘ as Ctrl where it works on every layout. No Ctrl+Alt (that's AltGr on many European
/// layouts) and no punctuation but the comma.
/// </summary>
public static class KeyboardShortcuts
{
    private const ShortcutModifiers Ctrl = ShortcutModifiers.Control;
    private const ShortcutModifiers Shift = ShortcutModifiers.Shift;
    private const ShortcutModifiers Alt = ShortcutModifiers.Alt;
    private const ShortcutModifiers CtrlShift = ShortcutModifiers.Control | ShortcutModifiers.Shift;

    public static IReadOnlyList<CommandShortcuts> All { get; } =
    [
        Shortcut(AppCommand.AddTorrentFile, Key(ShortcutKey.O, Ctrl), Key(ShortcutKey.Insert)),
        Shortcut(AppCommand.AddTorrentLink, Key(ShortcutKey.U, Ctrl), Key(ShortcutKey.Insert, Ctrl)),
        Shortcut(AppCommand.Start, Key(ShortcutKey.F3)),
        Shortcut(AppCommand.StartNow, Key(ShortcutKey.F3, Shift)),
        Shortcut(AppCommand.Stop, Key(ShortcutKey.F4)),
        Shortcut(AppCommand.StartAll, Key(ShortcutKey.F3, Ctrl)),
        Shortcut(AppCommand.StopAll, Key(ShortcutKey.F4, Ctrl)),
        Shortcut(AppCommand.Remove, Key(ShortcutKey.Delete)),
        Shortcut(AppCommand.RemoveAndDeleteData, Key(ShortcutKey.Delete, Shift)),
        Shortcut(AppCommand.AskTrackersForMorePeers, Key(ShortcutKey.R, Ctrl)),
        // F6 is transgui's "Set data location"; Windows uses it to move between panes, which the user gave up.
        Shortcut(AppCommand.MoveData, Key(ShortcutKey.F6), Key(ShortcutKey.M, CtrlShift)),
        Shortcut(AppCommand.Rename, Key(ShortcutKey.F2)),
        Shortcut(AppCommand.Labels, Key(ShortcutKey.F7), Key(ShortcutKey.L, Ctrl)),
        Shortcut(AppCommand.TorrentOptions, Key(ShortcutKey.Enter, Alt)),
        Shortcut(AppCommand.MoveToTopOfQueue, Key(ShortcutKey.Number1, CtrlShift)),
        Shortcut(AppCommand.MoveUpInQueue, Key(ShortcutKey.Number1, Ctrl)),
        Shortcut(AppCommand.MoveDownInQueue, Key(ShortcutKey.Number2, Ctrl)),
        Shortcut(AppCommand.MoveToBottomOfQueue, Key(ShortcutKey.Number2, CtrlShift)),
        Shortcut(AppCommand.CopyMagnetLink, Key(ShortcutKey.C, CtrlShift)),
        Shortcut(AppCommand.ShowInFolder, Key(ShortcutKey.Enter, Ctrl)),
        Shortcut(AppCommand.ShowInfoForRow, Key(ShortcutKey.Enter)),
        Shortcut(AppCommand.AlternativeSpeedLimits, Key(ShortcutKey.F8), Key(ShortcutKey.T, CtrlShift)),
        Shortcut(AppCommand.ServerSettings, Key(ShortcutKey.F9)),
        Shortcut(AppCommand.Settings, Key(ShortcutKey.Comma, Ctrl), Key(ShortcutKey.F9, Ctrl)),
        Shortcut(AppCommand.Reconnect, Key(ShortcutKey.R, CtrlShift)),
        Shortcut(AppCommand.ShowInfoPane, Key(ShortcutKey.F12), Key(ShortcutKey.I, Ctrl)),
        Shortcut(AppCommand.ShowSidebar, Key(ShortcutKey.F11)),
        Shortcut(AppCommand.FilterByName, Key(ShortcutKey.F, Ctrl), Key(ShortcutKey.E, Ctrl)),
        Shortcut(AppCommand.Paste, Key(ShortcutKey.V, Ctrl), Key(ShortcutKey.Insert, Shift)),
        Shortcut(AppCommand.SelectAllTorrents, Key(ShortcutKey.A, Ctrl)),
        // transgui's Alt+X still works; menus show nothing, since Windows users close with Alt+F4.
        new(AppCommand.Exit, Shown: null, [new(ShortcutKey.X, Alt)]),
    ];

    public static CommandShortcuts For(AppCommand command) => All.Single(shortcuts => shortcuts.Command == command);

    private static CommandShortcuts Shortcut(AppCommand command, KeyChord shown, params KeyChord[] alsoAccepted) =>
        new(command, shown, alsoAccepted);

    private static KeyChord Key(ShortcutKey key, ShortcutModifiers modifiers = ShortcutModifiers.None) => new(key, modifiers);
}
