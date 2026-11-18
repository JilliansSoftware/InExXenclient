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
- [x] Window theme, three-way (Options > Fonts "Window theme": Original / Dark / Light; replaces the dark windows checkbox, old profiles keep their choice through `IexDarkUi`, new characters start on Dark). Light is sandstone with bronze trim and brown ink: same code-drawn pieces as Dark from a second palette plus a layered sandstone fill (`IexDarkArt.Use`, `Sand`), status window and bars recolored by `branding/art/make_light.py`, the paperdoll from its own Midjourney art (sandstone arch, griffins, pentagram: `branding/art/paperdoll-light-mj.webp`, scaled whole by `make_paperdoll.py`; the virtue button is the piece of the frame under it) -> `IEX/Art/*-light.rgba.gz`. On Light, light text that sits on sandstone is darkened (also `color=#...` in server HTML); text on see-through or black panels and black-edged text is left alone. Previews: `branding/theme-compare.png`, `branding/light-ingame-art.png`. Dark art checked pixel-identical to before; Light art checked offline; not yet tried in game.
- [x] Dark paperdoll frame redone from a Midjourney retexture of the light art (black stone, gold trim, winged gargoyles: `branding/art/paperdoll-dark-mj.webp`), so both themes share one shape; `make_paperdoll.py` now builds both (the earlier `paperdoll-mj.webp` is no longer used). Checked offline; not yet tried in game.
- [x] Skills window redesign (`IEX/IexSkillsGump.cs`, Options > Fonts "Modern skills window", needs the Dark or Light theme): resizable, search, Real/Base/Cap, groups sidebar (dropdown when narrow), use + lock per row, drag to group / out for a skill button. No bars or gain counts on purpose. Not yet tried in game.
- [x] Dark status window and bars (with dark windows): status 0x2A6C, own bar 0x0803/0x0807 (war = red copy), others' bar 0x0804 grey plus pre-coloured copies per notoriety (0xFE40-0xFE44, drawn untinted so the gold stays gold). Art from Midjourney (`branding/art/status-mj.webp`, `statusbar-mj.webp`) fitted by `branding/art/make_status.py`. Status numbers parchment, name gold. Art checked offline; not yet tried in game. Only for MUL gump files (UOP status art has another layout).
- Fonts: keep UO defaults (decided 2026-11-09)
## E. Keep-out list
(C-E not reviewed yet)
## F. Graphics
- [x] Enhanced lighting, phase 1 (Options > Video, "Enhanced lighting", on for new characters): tinted darkness (warm at dusk, blue at night, colder underground; same brightness as stock), warm fire light with a slight flicker, softened light edges and glow. `IEX/IexLighting.cs`, `ClassicUO.Renderer/IEX/IexLightingPass.cs`, `shaders/IexLighting.fx` (recompile with `compile_shaders.bat`); hooks in `RenderTargets.cs`, `GameScene.cs`, `LightColors.cs`. Falls back to stock lighting if the shader does not load. Tried in game 2026-11-10.
- [x] Atmosphere, phase 2 (same "Enhanced lighting" option): colour grade by the hour (golden low sun, cooler and a little drained at night), darker screen edges underground and at deep night, light cast by spells, explosions and lightning bolts (`IexLighting.Grade`, `EffectLight`; hooks in `GameEffectView.cs`, `LightningEffectView.cs`). Shader render-tested offline (Vulkan); not yet tried in game.
- [x] Weather and water (Options > Video, "Enhanced weather and water", on for new characters): rain as thin streaks with splashes, snow of mixed sizes, glints on open water (`IEX/IexWeather.cs`, `IEX/IexWater.cs`; hooks in `Weather.cs`, `GameScene.cs`). With enhanced lighting also on: rain and storms dim and grey the picture, storms flash with lightning and thunder. Not yet tried in game.
- [x] Light as a commodity, phase 3 (same "Enhanced lighting" option, 2026-11-17): round lights (stock sprites 1, 2, 29, 49) drawn by the shader as a bright core with a long smooth tail instead of the 32-step sprite, reaching as far as the server says per item (`IEX/IexLightRadius.cs`, packet 0xBF/0xEF; default twice the stock sprite); light map at half strength so light can go past the art's own brightness, eased by a tone curve; bloom on the brightest parts of the lit picture (quarter size); haze in the air around lights over dark ground. Haze and bloom scale with darkness (`IexLighting.Atmosphere`), none by day. Not yet tried in game.
- [x] Light spell and 4th circle Night Sight (2026-11-17): slot 6 of the magery book reads "Light"; a Light spell's glow on a mobile is drawn as a steady white round light from the server's reach (`IEX/IexLightRadius.cs`, mobiles forgotten when they leave view); Night Sight (spell 65) listed at the end of the 4th circle and given its own page when the server says the book holds it (`IEX/IexSpellbook.cs`, 0xBF/0xF0; `SpellbookGump.cs`, `SpellsMagery.cs`). Not yet tried in game.
- [x] Circle of transparency no longer hides trees and rocks from tools (2026-11-17): while a neutral target cursor is up (axe, pickaxe; not spells), trees and rocks inside the circle can still be picked, including fully faded ones at its centre (`IexCotSelectable` in `GameSceneDrawingSorting.cs`). Not yet tried in game.
- [ ] Phase 3 polish (soft shadows, sharp scaling, CRT filter) - proposed 2026-11-10
## G. TazUO-style features (reviewed 2026-11-17)
Kept:
- [x] 1. Health bars under creatures and players, plus name plates you can toggle (Options; off by default). B.3b brought back as an option. Already in stock ClassicUO: Options > General "Show HP" (percentage / line / both; always / when damaged) and name plates with "Show HP bar". No changes (2026-11-17).
- [x] 2. Modern journal: stock has one (Options "Use alternate journal", off by default), replaced by `IEX/IexJournal.cs` so it follows the shard options: same tabs (Profile.JournalTabs; right-click a tab for its message types or to delete it, + to add), a search box that filters the current tab, the Text size option, Dark/Light frame with readable colours, resize grip, one window at a time. Hooks in `GameActions.OpenJournal`, `Profile` gump restore, `MacroManager` (close journal), `OptionsGump` (text size). Tried in game 2026-11-17. The option is on Options > Speech, "Use alternative journal".
- [x] 3. Modern shop window (`IEX/IexShopGump.cs`, Options > Fonts "Modern shop window", needs the Dark or Light theme, on for new characters): one resizable window for buying and selling. Vendor list with picture, name, price and count, an amount box per line (double-click +1, Shift + double-click all), search, sort (vendor order / name / price), "Add all" on the sell window; cart with total, your gold, Clear and Accept. A ShopGump subclass made by `ShopGump.Create`; hooks in `ShopGump` (virtual AddItem/SetNameTo, bare constructor, Update), the three `PacketHandlers` creation sites, `Profile`, `OptionsGump`, `IexDefaults`. Built; not yet tried in game.
- [x] 4. Compact health bars you can resize, with mana and stamina. Built 2026-11-17 as the style of "Use custom health bars" (Options > General, "Compact style"; both on for new characters): IEX/IexHealthBar.cs. Name in notoriety colour; hits for anyone (green poisoned, yellow can't be hurt, grey out of range or dead); mana and stamina for you and your party; numbers inside the bars; red edge in war mode or on your last target. Drag the corner to resize; the size becomes the default for that kind of bar (yours / others). Tried in game 2026-11-17: works.
- [ ] 5. Spell bar, plus a range circle or area preview while you cast or target.
- [ ] 6. Lock or anchor windows in place; a key that hides the whole interface.
- [ ] 7. Damage numbers you can style (colour, size, how they float).
- [x] 8. Follow mode: already in stock ClassicUO (Alt + click a mobile; right-click stops it; stays within 3 tiles). No changes (2026-11-17).
- [x] 9. Zoom the game view with the mouse wheel: stock Ctrl + wheel, now on for new characters (`IexDefaults`), and it glides instead of stepping (`Camera.CalculateLerpZoom`, `ShownZoom`, `SnapZoom`; hooks in `GameScene.Load`, `GameSceneDrawingSorting`). Built; not yet tried in game.
- [ ] 10. Built-in scripting, limited: the server enforces the limits (no unattended loops); Razor stays. Decide the rules before building.
- [ ] 11. Launcher window before the game (patch notes, links, picking a profile), built on the existing updater.
- [ ] 12. Discord integration (shows status in Discord; maybe a chat bridge).
- [ ] 13. Copy a profile (settings, layout, macros) from one character to another.
Cut: grid containers (cut again), paperdoll durability readout and hidden layers, cooldown bars, auto-loot, durability tracker, tooltip overrides and item comparison (little to show on T2A), world map extras, login news (still tabled).
