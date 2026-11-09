# In Ex Xen client backlog

Our changes live in `src/*/IEX/` folders; upstream files only get small hooks marked `// IEX:`.
`main` mirrors upstream ClassicUO; work happens on `inexxen`.

## A. Foundation
- [x] 1. Auto-update (bootstrap `IEX/Updater.cs`; manifest + gzip assets on GitHub releases, `installer/release.ps1`)
- [x] 2. Game files kept in sync with the server (same updater: every installed file is in the manifest)
- [ ] 3. News on the login screen - tabled 2026-11-09
- [ ] 4. Server-controlled feature switches - tabled 2026-11-09 (build when the first feature needs it)
- [x] 5. In Ex Xen branding (window title, login version line, "Powered by ClassicUO"). Open: own icon art, website and Discord links (`IEX/IexClient.cs`, empty = hidden)
- [x] 6. Client reports its version (0xBF sub 0xEE); server turns away outdated clients when
  `iex.clientCheck.enabled` is true (ModernUO `Custom/IEX/ClientVersionCheck.cs`, off by default)

## B. Better defaults
- [x] 1. Old-client hotkeys (IMY macros.txt) on new characters (`IEX/IexDefaults.cs`)
- [x] 2. Old-client settings on new characters (gray out-of-range objects; the rest already matched)
- [x] 3a. Circle of transparency on (original-client style)
- [ ] 3b. Health bars over damaged mobiles - cut 2026-11-09
- [x] 3c. Larger default game window (800x600)
- [ ] 3d. Grid-style containers - cut 2026-11-09
- [x] 3e. Journal keeps 500 lines; system/object/client messages shown
- [x] 4. Razor starting profile: reagent and bandage counters in the title bar (installer only, never overwritten by updates)
- [x] 5. "Save current settings as defaults for new users" mentioned in release notes
## C. Shard panels
## D. Atmosphere
## E. Keep-out list
(C-E not reviewed yet)
