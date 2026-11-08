namespace TransmissionKit;

/// <summary>The RPC methods this app uses.</summary>
public enum RpcMethod
{
    TorrentStart,
    TorrentStartNow,
    TorrentStop,
    TorrentVerify,
    TorrentReannounce,
    TorrentSet,
    TorrentGet,
    TorrentAdd,
    TorrentRemove,
    TorrentSetLocation,
    TorrentRenamePath,

    SessionGet,
    SessionSet,
    SessionStats,

    BlocklistUpdate,
    PortTest,
    FreeSpace,

    QueueMoveTop,
    QueueMoveUp,
    QueueMoveDown,
    QueueMoveBottom,

    /// <summary>Bandwidth groups, Transmission 4.0 and newer.</summary>
    GroupGet,
    /// <summary>Bandwidth groups, Transmission 4.0 and newer.</summary>
    GroupSet,
}

internal static class RpcMethodNames
{
    /// <summary>
    /// The JSON-RPC name, such as <c>torrent_get</c>. The legacy protocol joins the same words
    /// with hyphens instead.
    /// </summary>
    public static string SnakeCaseName(this RpcMethod method) => method switch
    {
        RpcMethod.TorrentStart => "torrent_start",
        RpcMethod.TorrentStartNow => "torrent_start_now",
        RpcMethod.TorrentStop => "torrent_stop",
        RpcMethod.TorrentVerify => "torrent_verify",
        RpcMethod.TorrentReannounce => "torrent_reannounce",
        RpcMethod.TorrentSet => "torrent_set",
        RpcMethod.TorrentGet => "torrent_get",
        RpcMethod.TorrentAdd => "torrent_add",
        RpcMethod.TorrentRemove => "torrent_remove",
        RpcMethod.TorrentSetLocation => "torrent_set_location",
        RpcMethod.TorrentRenamePath => "torrent_rename_path",
        RpcMethod.SessionGet => "session_get",
        RpcMethod.SessionSet => "session_set",
        RpcMethod.SessionStats => "session_stats",
        RpcMethod.BlocklistUpdate => "blocklist_update",
        RpcMethod.PortTest => "port_test",
        RpcMethod.FreeSpace => "free_space",
        RpcMethod.QueueMoveTop => "queue_move_top",
        RpcMethod.QueueMoveUp => "queue_move_up",
        RpcMethod.QueueMoveDown => "queue_move_down",
        RpcMethod.QueueMoveBottom => "queue_move_bottom",
        RpcMethod.GroupGet => "group_get",
        RpcMethod.GroupSet => "group_set",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown RPC method."),
    };
}
