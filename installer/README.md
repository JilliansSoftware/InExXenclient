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

## Release
1. Bump `AppVersion` in `InExXen.iss` (and `-Version` for the client build).
2. Run `build.ps1`.
3. Tag `vX.Y` on the `inexxen` branch, push, attach `output\InExXen-Setup.exe` to the release.
