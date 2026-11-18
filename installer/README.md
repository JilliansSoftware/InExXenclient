# In Ex Xen installer

All-in-one Windows installer for the In Ex Xen shard (`play.inexxen.com:2593`): this client,
Razor Community Edition and the shard's UO data. Players need nothing else.

## Build
Needs: .NET 10 SDK, Visual Studio C++ build tools (NativeAOT), Inno Setup 6
(`winget install JRSoftware.InnoSetup`), and a reference UO install
(default `C:\Program Files\Ultima Online 2D`, only read).

```powershell
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

It compiles the client the same way upstream's release workflow does, adds Razor CE x64
1.9.77.0, copies the UO data (client 5.0.6.5, digital music), and writes
`installer\output\InExXen-Setup.exe`. Game data is never committed; the installer is
attached to GitHub releases.

`InExXen.iss` installs per user to `%LOCALAPPDATA%\InExXen` (no admin prompt) and writes
`settings.json` on first install only, so reinstalling keeps players' settings.

## Linux and macOS
`build.ps1` ends by running `build-unix.ps1`, which writes `output\InExXen-linux-x64.tar.gz`,
`InExXen-osx-arm64.tar.gz` and `InExXen-osx-x64.tar.gz`: the client cross-compiled as a
self-contained .NET build (NativeAOT can't cross-compile), its native libraries, the staged UO
data, a launcher (`InExXen.sh` / `InExXen.command`) and `settings.default.json`, which the
launcher copies to `settings.json` on first run. No Razor, no auto-update. `release.ps1`
refuses to run if these are missing or older than the Windows build, and attaches them to
every release. The macOS builds are unsigned, so the first start is right-click > Open.

## Release
1. Bump `AppVersion` in `InExXen.iss`.
2. `build.ps1 -Version 1.2.0.0` (the client version players and the server see).
3. `release.ps1 -Tag v1.2` prepares `output\release\` (manifest + only the changed files, gzipped);
   add `-Publish` to create the GitHub release (draft, upload, then publish).

Installed clients check `releases/latest/download/manifest.txt` at every start and download
what changed, including the launcher itself. Set `IEX_UPDATE_MANIFEST` to a local URL to test
against `release.ps1 -BaseUrl http://127.0.0.1:8765`; create a `noupdate` file next to
ClassicUO.exe or pass `-noupdate` to skip updating.
