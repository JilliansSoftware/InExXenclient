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
## C. Shard panels - skipped for now (2026-11-09)
## D. Style
- [x] Icon from the UO logo (exe, game window, installer)
- [x] Text size slider (Options > Fonts, 100-200%): overhead text, journal, chat lines. Gumps stay on the zoom slider.
- [x] Dark windows (Options > Fonts, on for new characters): 20 stock window backgrounds redrawn in code as black stone + gold trim (`IEX/IexDarkArt.cs`), dark text in those windows turns parchment (`IEX/IexDarkUi.cs`). Built and art checked offline; not yet tried in game. Title font (ASCII 4) only applies where a window has a title.
  Also: gold-framed dark buttons (OKAY/CANCEL/APPLY/DEFAULT + paperdoll), scroll bars, checkboxes, dark journal/skills scrolls with gold title plates, gold ASCII 4 titles on server windows, paperdoll frame from Midjourney art (`branding/art/make_paperdoll.py` -> `IEX/Art/*.rgba.gz`).
- [x] Skills window redesign (`IEX/IexSkillsGump.cs`, Options > Fonts "Modern skills window", needs dark windows): resizable, search, Real/Base/Cap, groups sidebar (dropdown when narrow), use + lock per row, drag to group / out for a skill button. No bars or gain counts on purpose. Not yet tried in game.
- [x] Dark status window and bars (with dark windows): status 0x2A6C, own bar 0x0803/0x0807 (war = red copy), others' bar 0x0804 grey plus pre-coloured copies per notoriety (0xFE40-0xFE44, drawn untinted so the gold stays gold). Art from Midjourney (`branding/art/status-mj.webp`, `statusbar-mj.webp`) fitted by `branding/art/make_status.py`. Status numbers parchment, name gold. Art checked offline; not yet tried in game. Only for MUL gump files (UOP status art has another layout).
- Fonts: keep UO defaults (decided 2026-11-09)
## E. Keep-out list
(C-E not reviewed yet)
