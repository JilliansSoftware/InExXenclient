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
## C. Shard panels
## D. Atmosphere
## E. Keep-out list
(not reviewed yet)
