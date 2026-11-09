# Transmission Remote for Windows

A native Windows client for Transmission daemons, the sibling of the Mac app in [`macOS/`](../macOS/).
It targets **Windows 11** (x64 and ARM64) and still runs on **Windows 10 version 1809** and later, the
oldest version the Windows App SDK supports.

## Status

| Part | State |
|---|---|
| `TransmissionKit`: the UI-free core (RPC client, models, .torrent parsing, helpers) | Done, ported from the Mac app's Swift package, 288 tests |
| `TransmissionRemote.ViewModels`: the app's logic without its interface | Done, ported from the Mac app's models, 195 tests. See [below](#the-apps-logic-srctransmissionremoteviewmodels) |
| `tools/StringCatalogConverter`: the Mac app's translations as `.resw` files | Done, 109 tests. See [Strings](#strings) |
| App icon: `assets/AppIcon/` (`AppIcon.ico` with 9 sizes, and PNGs) | Done, exported from the Mac build |
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
Localization/ CLDR plural rules for the app's 28 languages (not in the Swift package: macOS has its own)
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

## The app's logic: `src/TransmissionRemote.ViewModels`

Everything the Windows app does except drawing it: saved servers and settings, the live connection,
the sidebar filters, the torrent list, adding torrents, and a view model for each window and dialog.
It's platform-neutral (CommunityToolkit.Mvvm with partial properties), so it builds and is tested on
any OS, against a fake Transmission. The WinUI app binds to it and adds only the interface.

```
Abstractions/   interfaces the app implements with Windows APIs: ICredentialStore (Credential
                Manager), IClientCertificateProvider (certificate store), INotificationService
                (app notifications), IShell (open, show in File Explorer, clipboard)
Models/         ServerProfile, ProxySettings, AppSettings
Services/       ServerStore and SettingsStore (JSON files in %LOCALAPPDATA%\TransmissionRemote),
                ServerConnection (connect, retry, poll, actions), AddTorrentFlow, TransguiImporter
                (transgui.ini from %LOCALAPPDATA%\Transmission Remote GUI or a portable copy),
                UpdateChecker, PeerInfoService (countries and host names), LocalFiles and
                RunnableFile (open downloaded files through path mappings, asking first for
                programs and scripts)
Presentation/   sidebar filters and counts, the file tree, list columns, natural sorting,
                DisplayFormatter (sizes, speeds, durations, ratios, dates with the app's own
                localized unit labels) and the torrent status as an enum
ViewModels/     MainViewModel, AddTorrentViewModel, SessionSettingsViewModel,
                TorrentOptionsViewModel, EditTrackersViewModel, ServerEditorViewModel, torrent rows
```

Ground rules:

- **Use it from the UI thread.** Awaits resume on WinUI's synchronization context, so properties
  change on the UI thread and bindings update safely.
- **No sentences.** It exposes states as enums and errors as exceptions (`TransmissionException.Kind`,
  `ConnectionFailure.Remedy`, `AddProblemKind`…); the app picks the localized text. That keeps all
  wording in the `.resw` files.
- **New logic goes here, with a test**, not into code-behind, so it keeps building and testing on a Mac
  and in CI.
- Same behavior as the Mac app, including its fixes for transgui issues; comments name them.
- Saved files are read leniently: an unreadable server costs only that entry, settings missing from an
  older file keep their defaults, and an unreadable file is copied aside before the next save.

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
| 2000s | | `TransmissionRemote.ViewModels`, saved data: unreadable or unsaved servers and settings |
| 2100s | | The connection: connecting, server unreachable (with the retry delay), stopped, refresh and action failures |
| 2200s | | Adding torrents: unreadable files, refused torrents |
| 2300s | | Other services: update check, country database, opening local files |

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

## Strings

The Windows app uses the Mac app's translations, all 28 languages. `tools/StringCatalogConverter` turns
the Mac String Catalogs into `.resw` files. Run it from `Windows/` whenever the Mac strings change:

```bash
dotnet run --project tools/StringCatalogConverter -- \
  --catalog ../macOS/TransmissionRemote/Localizable.xcstrings \
  --catalog ../macOS/Packages/TransmissionKit/Sources/TransmissionKit/Resources/Localizable.xcstrings \
  --output src/TransmissionRemote.App/Strings
```

It writes `Strings/<language>/Resources.resw`: `en-US`, the app's default language, and the catalogs'
own codes for the rest (`ru`, `pt-BR`, `zh-Hans`, ...), which MRT accepts as language folders.
`InfoPlist.xcstrings` is Mac-only. Entries are sorted by name, so a rerun shows only real changes. The
files are generated: a rerun overwrites them, so change the catalogs instead of the `.resw` files.

- **Names.** A `.resw` name can't be a sentence (MRT gives `.` and `/` a meaning), so it's the English
  text's words in PascalCase: `Show Log Files` is `ShowLogFiles`, and
  `“%@” couldn't be opened as a torrent file.` is `CouldNotBeOpenedAsATorrentFile`. Placeholders and
  punctuation are dropped, contractions spelled out, `↓` and `↑` named Down and Up, a trailing `…` kept as `Ellipsis` (`Settings…` is
  `SettingsEllipsis`), and long texts cut after the last whole word that fits in 40 characters. Names
  that would clash, including ones that differ only in case, which MRT treats as equal, all get six hex
  digits of their key's SHA-256: `Total Size` is `TotalSize_9AC7DE` and `Total size` is
  `TotalSize_51CDF6`. Adding a string whose name clashes with an existing one renames both. Every entry's
  comment holds the Mac key, so search the `.resw` for the Mac wording to find a name.
- **Placeholders.** `%@`, `%lld`, `%d` and the like become `{0}`, `{1}`, ... in argument order (`%2$@`
  is `{1}`) and `%%` becomes `%`. A string with placeholders is a composite format string in every
  language: pass it to `string.Format(CultureInfo.CurrentCulture, ...)`. Its literal braces are doubled;
  strings without placeholders are left as they are. Numbers carry no formatting, so format counts, sizes
  and speeds before inserting them, as the Mac app does. Only fixed decimals keep theirs: `%.1f` is
  `{0:F1}`.
- **Plurals.** `.resw` has no plurals, so each form is a resource named `<name>_<category>`: `%lld files`
  is `Files_One` and `Files_Other` in English, and `Files_One`, `Files_Few`, `Files_Many` and
  `Files_Other` in Russian. `PluralRules` in TransmissionKit picks the form. If that comes back empty,
  which happens while a language's translation is missing, use the `_Other` form, which English always
  has:

  ```csharp
  string Plural(ResourceLoader loader, string name, string uiLanguage, long count)
  {
      var category = PluralRules.CategoryFor(uiLanguage, count);
      var form = loader.GetString(PluralRules.ResourceName(name, category));
      if (form.Length == 0)
      {
          form = loader.GetString(PluralRules.ResourceName(name, PluralCategory.Other));
      }
      return string.Format(CultureInfo.CurrentCulture, form, count.ToString("N0", CultureInfo.CurrentCulture));
  }
  ```

- **Missing translations.** Only finished translations (`"state": "translated"`) are written. A string a
  language lacks is left out of that language's file, and MRT falls back to `en-US` for that string
  alone, the way macOS falls back to English. A plural string is written only once all its forms are
  finished. Strings marked `shouldTranslate: false`, such as `%@ %@`, are only in `en-US`.
- **Code, not `x:Uid`.** The names have no property part (`ShowLogFiles.Text`), so load them from code
  with `ResourceLoader.GetString` or through a small markup extension.
- **Markdown.** SwiftUI renders Markdown in strings, and one string has a link to DB-IP in Markdown:
  `RightClickThePeerListToTurnHostNamesAnd`. It's kept as it is, so show the link with a `Hyperlink`.

Anything the converter can't carry over faithfully stops it with the catalog, key and language: plural
substitutions, device variations, specifier widths and flags, or a translation that uses an argument the
English text doesn't. `MacCatalogTests` convert the real catalogs, so CI catches these, and check that
every language has each plural form `PluralRules` can ask for.

**New strings** need all 28 languages, on Windows as on the Mac. A string both apps show goes into the
Mac catalog and then through the converter. Strings only the Windows app needs go into a Windows-only
String Catalog, for example `src/TransmissionRemote.App/Strings/Windows.xcstrings`, passed as one more
`--catalog`, so that one tool and one format keep every translation.

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
| Keychain | Passwords in Credential Manager (`PasswordVault`, which holds at most 2,560 bytes per entry); client certificates in the user's certificate store, found by thumbprint (`ClientIdentity.FromCertificate`) |
| .torrent and magnet handling | File and `magnet:` protocol registration by the installer, single instance with `AppInstance.FindOrRegisterForKey` and `RedirectActivationToAsync` |
| String Catalogs, 28 languages | `.resw` files per language under `Strings/`, generated from the Mac app's `.xcstrings` (see [Strings](#strings)) |
| Peer country flags | Flag images: Windows' emoji font shows regional-indicator flags as letters |

Structure: `src/TransmissionRemote.App` (WinUI 3), referencing `TransmissionRemote.ViewModels` and
`TransmissionKit`, with `assets/AppIcon/AppIcon.ico` as its icon. Its own code is the XAML, the
implementations of the interfaces in `Abstractions/`, startup (dependency injection, logging,
single instance, activation) and the tray icon.

### Packaging

- Unpackaged (`WindowsPackageType=None`) and self-contained (`WindowsAppSDKSelfContained=true`), built
  once per architecture: `win-x64` and `win-arm64`.
- An installer per architecture (Inno Setup handles ARM64) that registers `.torrent` and `magnet:`,
  plus a zip for people who don't want an installer.
- Unsigned at first, so SmartScreen warns on first run (**More info → Run anyway**), the same trade-off
  as the unnotarized Mac app. Signing (for example Azure Trusted Signing) can come later.
- Name the downloads so the update check finds them: the file name without its extension ends in
  `-win-x64` or `-win-arm64`, such as `TransmissionRemote-1.3-win-x64.exe` and
  `TransmissionRemote-1.3-win-arm64.zip`. The Mac and Windows apps share this repository's releases;
  `UpdateChecker` offers the newest release with a download for the PC's architecture, so a Mac-only
  release never shows up on Windows.
