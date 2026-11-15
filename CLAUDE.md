# TransmissionRemote

Native clients for Transmission daemons, replacing the abandoned Transmission Remote GUI (transgui).
One repository, two apps:

- `macOS/`: the released SwiftUI app (macOS 26, Swift 6 with default MainActor isolation). Its core
  is the Swift package `macOS/Packages/TransmissionKit`.
- `Windows/`: the WinUI 3 app in development (.NET 10, C#). Its core, `Windows/src/TransmissionKit`,
  is a port of the Swift package. Read `Windows/README.md` first: it has the status, the plan for
  the app and the logging design.

The user-facing overview is `README.md`. transgui's open issues, checked against the Mac app, are in
`docs/transgui-audit.md`.

## Who works where

Two Claude sessions work on this repository, one on each platform. Each stays in its own app:

- **On the Mac:** edit only `macOS/`, and commit on `main`.
- **On Windows:** edit only `Windows/`, and commit on the `windows-ui` branch. That session merges
  `windows-ui` into `main` when its work is ready, and merges `main` into `windows-ui` to pick up the
  Mac side's changes.
- **Changes the other app needs:** don't edit the other app's folder. Write the user a short note for
  the other session to act on instead: a protocol fix to port to the other core, or new Mac strings for
  the Windows side to regenerate with its converter.
- **Shared files** (`README.md`, `CLAUDE.md`, `docs/`, `.github/`): change them only when needed, and say
  in the note what the other session should know.

## Commands

```bash
# Mac app (from macOS/)
xcodebuild -project TransmissionRemote.xcodeproj -scheme TransmissionRemote \
  -destination 'generic/platform=macOS' -skipPackagePluginValidation build   # runs SwiftLint
cd Packages/TransmissionKit && swift test

# Windows (from Windows/). The app needs Windows; on macOS use TransmissionRemote.Core.slnf instead.
dotnet build TransmissionRemote.slnx      # warnings are errors; -p:Platform=ARM64 for the other architecture
dotnet test TransmissionRemote.slnx
dotnet format TransmissionRemote.slnx --verify-no-changes
```

Run the linters and tests before calling work done. Both cores have integration tests against a real
daemon, enabled by `TRANSMISSION_IT_*` environment variables (see `README.md` and `Windows/README.md`).

## Rules

- **Test only against a throwaway daemon with a test password.** Never use, read or print the
  credentials of a real server, including those stored in the Keychain, Credential Manager or a
  `transgui.ini`. On macOS run `transmission-daemon --foreground`: forked into the background, it
  crashes on its first `torrent-add`.
- **Keep the two cores in step.** A protocol fix or quirk found in one (Transmission versions differ
  in key spelling, types and error handling) goes into the other, with a test in both; the session that
  owns the other core ports it (see "Who works where").
- **Log failures so they can be diagnosed from the log alone**: which operation, against what, how
  long it took and the error with its cause. Never log passwords, `Authorization` headers or request
  bodies. Mac: `DiagnosticLog` (unified log plus daily files, Help → Show Log Files). Windows core:
  `[LoggerMessage]` methods in `Logging/Log.cs` with stable event IDs.
- **Translate every new interface string into all 28 languages** in the same change, in String
  Catalogs: the Mac app's `.xcstrings`, plus a Windows-only catalog for strings only Windows shows. The
  Windows `.resw` files are generated from them by `Windows/tools/StringCatalogConverter`: never edit
  the `.resw` files. Reuse transgui's terms for torrent concepts and the platform's own wording for
  system features.
- **Windows: logic goes into `TransmissionRemote.ViewModels` with a test**, not into WinUI code-behind,
  so it builds and tests on any OS. The view models expose states and errors, never sentences.
- **Code**: readable first, small functions, comments that explain why. The latest stable APIs, no
  deprecated ones. Security by default: validate input, no secrets in code or logs.
- **Commits**: small and reviewable, one purpose each, as Aidar Abdullin <a1dar.ufa@gmail.com> (the
  repository's git config). Never put another personal email address in commits or files. Don't push,
  tag or publish a release unless asked.
- **Releases** (Mac): bump `MARKETING_VERSION`, run `macOS/Scripts/build-release.sh`, then
  `gh release create vX.Y <dmg> --target <full commit SHA>` (GitHub rejects short SHAs). Releases are
  signed with the maintainer's self-signed certificate "TransmissionRemote Local Signing" (in the login
  keychain; the script falls back to ad hoc without it) and not notarized, on purpose. Builds installed
  for testing use the same certificate, so the keychain doesn't ask for the login password after updates.
