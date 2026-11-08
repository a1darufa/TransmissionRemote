# Transmission Remote GUI issues and pull requests, checked against this app

On 8 November 2026 every open issue (441) and open pull request (25) of
[Transmission Remote GUI](https://github.com/transmission-remote-gui/transgui) was read and compared
with this app's code. Suspected problems were then verified against the code and, where it mattered,
against Transmission's own source for versions 3.00, 4.0 and 4.1.

| | Issues | Pull requests |
|---|---:|---:|
| Already handled by this app | 182 | 9 |
| Not applicable (Windows, Linux, Lazarus, installers, transgui's own files) | 125 | 11 |
| Out of scope for a remote client | 24 | – |
| Possible problems to verify | 33 | 4 |
| Features this app doesn't have | 77 | 1 |

## Fixed

Each fix is its own commit.

| Problem | transgui reference |
|---|---|
| Editing trackers failed on Transmission before 4.0 (empty add/remove lists are rejected, and a failed add skips the remove) | #1276 |
| Unchecking files when adding chose the wrong files in torrents with padding files: Transmission 3.00–4.1 count them, the app skipped them | #766 |
| Typed numbers (port, speed limits, ratio, peers) weren't saved when Save or Apply was clicked with the mouse | #1326, #1269, #1493 |
| An unreadable `servers.json` (for example after going back to an older version) was overwritten, losing every server | #1344 |
| Open, Show in Finder and Copy Path missed unfinished torrents kept in the daemon's incomplete folder | #304, #1367 |
| Moving or deleting large torrents showed a false timeout while the daemon was still working | #1158 |
| A proxy closing idle connections made actions fail at random | #827 |
| A wrong port or RPC path gave a cryptic "not valid JSON" error instead of saying it reached a web page | #870 |
| Path mappings didn't work for Transmission daemons on Windows | #1240, #1058 |
| A malformed gzip header could crash the country database loader | PR #1609 |
| Opening an app or script from a torrent on a network share ran it with no warning | PR #1583 |
| The Add sheet kept proposing an old folder, and folder history couldn't be cleared | #807, #738, #310 |
| Folder paths with trailing slashes or spaces were sent and remembered as typed | #1143 |
| Adding hundreds of torrents at once timed out | #804 |
| Seeds and Peers didn't sort by swarm size | #1180 |
| Rename failed on Transmission 2.40–2.7x, which can't rename | #1023 |
| Sheets hung or showed empty data when the torrent was removed meanwhile | PR #1593, #1591 |
| Sidebar folders with the same name looked identical and appeared unsorted | #979 |
| No hint when macOS's Local Network permission blocks the connection | #1473, PR #1614 |
| Opening a .torrent from Finder at launch showed the Add sheet without the server's defaults | #790 |
| Every refresh fetched all torrents, slow with very large libraries: refreshes now fetch only torrents changed in the last minute, with a full list once a minute | #708, #1163, #1422 |

Verified as not affecting this app, among others: magnet links with spaces or non-ASCII names
(#1485), the table response format (PR #1594), clipboard self-import (PR #1610), duplicate instances
(PR #1573).

## Not fixed yet

- **Proxies** (#769, #1469): HTTP proxy login on plain-HTTP servers and SOCKS 5 are only unit-tested
  and need a check against a real proxy.
- Smaller: renaming a single-file torrent can drop its extension (#758, #1010); names with leading or
  trailing spaces may not rename (#1317); a Freebox daemon reporting non-standard speed units (#781);
  `open -g` bringing the app forward (#688).

## Features people asked for

### Quick wins

- Totals for selected torrents, and total size / ETA in the status bar (#610, #348, #795, #563, #299)
- "Largest file" button in Add Torrent (#852, #750)
- Restore the last sidebar filter and info tab at launch (#643)
- A "Started" filter for everything not stopped (#484)
- Keyboard shortcut for Verify, and a customizable toolbar (#1286, #1106, #1409)
- Optional columns: Created On, Comment, Availability, Seed/Peer ratio, average seed speed,
  per-torrent limits (#1336, #1316, #837, #615, #932, #986)
- Right-click menu on sidebar folders and labels: act on the whole group, Show in Finder (#797, #834)
- Folders as a tree in the sidebar (#1133, the most discussed request), and pinned folders (#942)
- Option to skip the remove confirmation (#1382)
- Speeds on the Dock icon (#1343) and speeds in bits per second (#564)
- Add by pasting a bare info hash (#873); new torrents at the top of the queue (#934)
- Copy torrent details as CSV (#512); folder autocomplete (#1061); show the .torrent file's path (#1388)
- Files tab search and sorting (#622, #497); scheduled blocklist update (#635)
- Unfinished torrents first when sorting Completed (#1376); turn off alternating rows (#1200)

### Medium

- Choose files of a magnet link once its metadata arrives (#1436)
- Local watch folder for .torrent files (#1148, #651, #1223)
- Edit trackers while adding, for example http → https (#861, #782)
- Pick the server in Add Torrent (#1033, #716); two addresses per server, LAN and internet (#863)
- Wake-on-LAN before connecting (#1221)
- Combine filters, for example folder and status (#1337, #621)
- Pieces bars per file and swarm availability (#1454, #770)
- Folder chosen automatically by file type, name or tracker (#791, #715, PR #1580 templates)
- Batch rename and batch location change (#767, #980); drag torrents onto a sidebar folder to move them (#631)
- Export and import the app's settings (#752); keep magnet links while the server is unreachable (#705)
- "Apply to all" for many torrents in Add (#793); default per-torrent limits on add (#810)
- Digest authentication (#221); keep copies of added .torrent files (#1073)

### Large or low priority

- Create torrents (#1046), RSS downloads (#182), run scripts on torrents (#447), tracker favicons
  (#458, which would mean contacting tracker sites), several servers in one list (#799)
- Developer ID signing and notarization (#1499): left out on purpose for now
