/// RPC methods we use. Raw values are the JSON-RPC (snake_case) names; the legacy
/// protocol uses the same words joined with hyphens.
public enum RPCMethod: String, Sendable {
    case torrentStart = "torrent_start"
    case torrentStartNow = "torrent_start_now"
    case torrentStop = "torrent_stop"
    case torrentVerify = "torrent_verify"
    case torrentReannounce = "torrent_reannounce"
    case torrentSet = "torrent_set"
    case torrentGet = "torrent_get"
    case torrentAdd = "torrent_add"
    case torrentRemove = "torrent_remove"
    case torrentSetLocation = "torrent_set_location"
    case torrentRenamePath = "torrent_rename_path"

    case sessionGet = "session_get"
    case sessionSet = "session_set"
    case sessionStats = "session_stats"

    case blocklistUpdate = "blocklist_update"
    case portTest = "port_test"
    case freeSpace = "free_space"

    case queueMoveTop = "queue_move_top"
    case queueMoveUp = "queue_move_up"
    case queueMoveDown = "queue_move_down"
    case queueMoveBottom = "queue_move_bottom"

    /// Bandwidth groups, Transmission 4.0+.
    case groupGet = "group_get"
    case groupSet = "group_set"
}
