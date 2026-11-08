# Transmission Remote for Windows

A native Windows client for Transmission daemons, the sibling of the Mac app in [`macOS/`](../macOS/).
It targets **Windows 11** (x64 and ARM64) and still runs on **Windows 10 version 1809** and later, the
oldest version the Windows App SDK supports.

## Status

| Part | State |
|---|---|
| `TransmissionKit`: the UI-free core (RPC client, models, .torrent parsing, helpers) | Done, ported from the Mac app's Swift package, 241 tests |
| `TransmissionRemote.App`: the WinUI 3 app | Not started. Planned below |
| Installers for x64 and ARM64 | Not started |

The core builds and tests on any OS, so it was written on a Mac. The app needs Windows: the XAML
compiler and the Windows App SDK only run there.

## Building and testing

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). For the app, add Visual Studio 2026
with the **WinUI application development** workload.

```bash
cd Windows
dotnet build TransmissionRemote.slnx
dotnet test TransmissionRemote.slnx
dotnet format TransmissionRemote.slnx --verify-no-changes
```

Warnings are errors, with the recommended .NET analyzers on (`Directory.Build.props`). The style
rules are in `.editorconfig`. CI runs the same three commands on x64 and ARM64 Windows runners
(`.github/workflows/windows-core.yml`).

### Integration tests against a real daemon

`IntegrationTests` show as skipped unless they're pointed at a daemon. Always use a throwaway daemon
with a test password, never a real server: the tests add, edit and remove a torrent and change
session settings (restoring them afterwards).

On Windows, the easiest daemon is Docker (`lscr.io/linuxserver/transmission`, with `USER` and `PASS`
set) or `transmission-daemon` in WSL. Put the test password in a file, then:

```powershell
$env:TRANSMISSION_IT_HOST = "127.0.0.1"
$env:TRANSMISSION_IT_PORT = "9091"
$env:TRANSMISSION_IT_USER = "tester"
$env:TRANSMISSION_IT_PASSWORD_FILE = "C:\path\to\test-password.txt"
dotnet test TransmissionRemote.slnx --filter "FullyQualifiedName~IntegrationTests"
```

They pass against Transmission 4.1.4 over both JSON-RPC and the legacy protocol.

## The core: `src/TransmissionKit`

One namespace, `TransmissionKit`. It mirrors the Swift package file by file, so a fix in one is easy
to carry to the other.

```
Transport/   TransmissionClient (HttpClient), dialects and key spelling, envelopes, lenient JSON
             reading, ServerEndpoint and transport security, proxies, client certificates,
             certificate pinning, TransmissionException, RetryBackoff
Models/      Torrent, TorrentDetails (files, peers, trackers), SessionSettings, SessionStats,
             TrackerSummary, BandwidthGroup, TorrentMutation, AddTorrentRequest, PathMapping,
             RemoteFolderTree, SpeedLimitPresets
Api/         the typed calls, as partial classes of TransmissionClient
Metainfo/    bencode, .torrent files (padding files kept in the numbering), magnet links
GeoIP/       the DB-IP country database and gzip unpacking
Import/      transgui.ini
Updates/     the GitHub release check
Logging/     every log message, generated with [LoggerMessage]
```

How it differs from the Swift version, and why:

- **Requests and answers** are `System.Text.Json` nodes rather than `Codable` types. Parameters are
  built with camelCase keys and renamed for the dialect when sent; answers are renamed back to
  camelCase and read with lenient helpers (`LenientJson.cs`), so one odd field never hides a list.
  No reflection, so the core stays trimming- and AOT-safe (`IsAotCompatible`).
- **Errors** are one `TransmissionException` with a `Kind`, plus `NetworkProblem`, `StatusCode`,
  `Detail` and `CertificateFingerprint`. `Message` is English, for logs. The app shows its own
  localized text based on `Kind`. `RequiresUserAction` and `IsTransient` decide whether polling stops
  or retries.
- **Proxies**: .NET answers a proxy's login request itself, so there's no up-front header.
- **Path mappings** have no `localBookmark`: security-scoped bookmarks are macOS-only. Subfolders are
  joined to the local path with `/`, so the app should normalize separators for Windows paths.
- **Country names** use `RegionInfo`, which names a region in the Windows display language.
- **Gzip**: a cut-off file is detected through the gzip trailer's length, so a file with several gzip
  members counts as truncated. The Swift version only reads single-member files too.

## Logging and error handling

Anyone should be able to tell from the log which part of the app failed and why.

**In the core** (done): `TransmissionClient` takes an optional `ILoggerFactory` and logs through
`Microsoft.Extensions.Logging` with stable event IDs:

| ID | Level | Event |
|---|---|---|
| 1000 | Information | Connecting: address, transport security, protocol preference |
| 1001 | Information | Connected: Transmission version, RPC version, protocol |
| 1002 | Warning | The server is older than Transmission 2.40 |
| 1100 | Debug | A call succeeded: method, protocol, HTTP status, size, duration |
| 1101 | Warning | A call failed: method, protocol, duration, error kind, detail, exception |
| 1102 | Debug | A new CSRF session ID; the call is repeated |
| 1103 | Information | The connection dropped; retrying once |
| 1104 | Warning | A redirect was refused |
| 1200 | Warning | An untrusted TLS certificate was rejected (with its fingerprint) |
| 1201 | Debug | A pinned certificate was accepted |

Never log passwords, the `Authorization` header or request bodies (they can hold whole .torrent files).
`Credentials`, `TransmissionClientOptions` and `AddTorrentRequest` hide those when printed, and
`LoggingTests` check it.

**In the app** (to do), following common practice for desktop .NET apps:

- Serilog behind `ILoggerFactory`: `Serilog.Extensions.Logging`, `Serilog.Sinks.File` and
  `Serilog.Sinks.Async`. Daily files `TransmissionRemote-<date>.log` in
  `%LOCALAPPDATA%\TransmissionRemote\Logs`, 14 days kept, 10 MB per file, the same as the Mac app.
- Information level by default; a **Detailed logging** switch in Settings turns on Debug, which adds
  every call (event 1100).
- At startup, log the app version, Windows version and build, architecture and .NET version.
- Catch what nothing else catches, log it as Critical with the exception, then show a dialog that
  offers to open the logs folder: `Application.UnhandledException` (UI thread),
  `AppDomain.CurrentDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`. Call
  `Log.CloseAndFlush()` on exit.
- **Help → Open Logs Folder**, like the Mac app's Help → Show Log Files.
- Write folder paths with `%LOCALAPPDATA%` or `~` rather than the full user folder, so shared logs
  don't reveal the user's name.

## The app: plan

Same layout and features as the Mac app, drawn the Windows 11 way: Fluent 2 controls, Mica, Segoe
Fluent Icons, light and dark themes, and keyboard and Narrator support. Mica falls back to the
default background on Windows 10.

| Mac app | Windows app |
|---|---|
| Sidebar filters (status, folders, labels, trackers) | `NavigationView`, left pane |
| Torrent list with sortable, reorderable columns | [WinUI.TableView](https://github.com/w-ahmad/WinUI.TableView) (community package, evaluate first) or a `ListView` with a header row |
| Info pane tabs: General, Trackers, Peers, Files, Speed | `SelectorBar` or `TabView` under the list, in a resizable split |
| Toolbar | `CommandBar` in the custom title bar |
| Status bar: speeds, limits, free space | A bottom bar in the main window |
| Settings window | A Settings page built from `SettingsCard` and `SettingsExpander` (CommunityToolkit.WinUI.Controls.SettingsControls) |
| Sheets (Add torrent, Options, Trackers) | `ContentDialog`; Add torrent may need its own window |
| Menu bar extra | Tray icon with a menu ([H.NotifyIcon.WinUI](https://github.com/HavenDV/H.NotifyIcon)) |
| Notifications when a download finishes | `AppNotificationManager` (Windows App SDK) |
| Keychain | Windows Credential Manager through `Windows.Security.Credentials.PasswordVault` |
| .torrent and magnet handling | File and `magnet:` protocol registration by the installer, single instance with `AppInstance.FindOrRegisterForKey` and `RedirectActivationToAsync` |
| String Catalogs, 28 languages | `.resw` files per language under `Strings/`, `x:Uid` in XAML. Convert the Mac app's `.xcstrings` with a script so both apps share the translations |
| Peer country flags | Flag images: Windows' emoji font shows regional-indicator flags as letters |

Suggested structure: `src/TransmissionRemote.App` (WinUI 3, MVVM with CommunityToolkit.Mvvm),
referencing `TransmissionKit`. Port the Mac app's `ServerConnection` (polling, backoff, coalesced
refreshes, completion notifications), `ServerStore` (servers as JSON, kept safe when unreadable) and
`AddTorrentFlow` as view models and services. They're in `macOS/TransmissionRemote/Model/`.

### Packaging

- Unpackaged (`WindowsPackageType=None`) and self-contained (`WindowsAppSDKSelfContained=true`), built
  once per architecture: `win-x64` and `win-arm64`.
- An installer per architecture (Inno Setup handles ARM64) that registers `.torrent` and `magnet:`,
  plus a zip for people who don't want an installer.
- Unsigned at first, so SmartScreen warns on first run (**More info → Run anyway**), the same trade-off
  as the unnotarized Mac app. Signing (for example Azure Trusted Signing) can come later.
