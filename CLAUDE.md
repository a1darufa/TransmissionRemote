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

## Commands

```bash
# Mac app (from macOS/)
xcodebuild -project TransmissionRemote.xcodeproj -scheme TransmissionRemote \
  -destination 'generic/platform=macOS' -skipPackagePluginValidation build   # runs SwiftLint
cd Packages/TransmissionKit && swift test

# Windows core (from Windows/)
dotnet build TransmissionRemote.slnx      # warnings are errors
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
  in key spelling, types and error handling) goes into the other, with a test in both.
- **Log failures so they can be diagnosed from the log alone**: which operation, against what, how
  long it took and the error with its cause. Never log passwords, `Authorization` headers or request
  bodies. Mac: `DiagnosticLog` (unified log plus daily files, Help → Show Log Files). Windows core:
  `[LoggerMessage]` methods in `Logging/Log.cs` with stable event IDs.
- **Translate every new interface string into all 28 languages** in the same change: the Mac app's
  `.xcstrings` String Catalogs, and the Windows app's `.resw` files once they exist. Reuse transgui's
  terms for torrent concepts and the platform's own wording for system features.
- **Code**: readable first, small functions, comments that explain why. The latest stable APIs, no
  deprecated ones. Security by default: validate input, no secrets in code or logs.
- **Commits**: small and reviewable, one purpose each, as Aidar Abdullin <a1dar.ufa@gmail.com> (the
  repository's git config). Never put another personal email address in commits or files. Don't push,
  tag or publish a release unless asked.
- **Releases** (Mac): bump `MARKETING_VERSION`, run `macOS/Scripts/build-release.sh`, then
  `gh release create vX.Y <dmg> --target <full commit SHA>` (GitHub rejects short SHAs). Releases are
  ad-hoc signed and not notarized, on purpose.
