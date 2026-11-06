# Phase 7 mockups: notes

## Wave 1 (task 493, 2026-11-06)

The wave 1 mockups are SerenityOS designs in [`designs/`](../../../designs/) at the repo root (task 511, 2026-11-06): each has `now.html` for a screen that exists today, then `v1.html` (the board first drawn here in `wave-1.html`, now removed) and two versions that really differ. Each page links `designs/tokens.css` (the XAML themes' colors) and names its parts with `data-id`s for review notes. `designs/system.html` shows the tokens, type and real parts. They're drawn against the approved map ([surfaces.md](../surfaces.md)) and Travis's decisions ([README.md](../README.md)). Sizes are real DIPs, glyphs are Lucide's own or Chippi's `LucideIcons`, and every window, file and number is invented, except Travis's two monitors' names, which Settings › Snap already shows.

- **Real renders:** `designs/home/now.html` follows the real island (`IslandPanelShots.RenderToolGallery`); the Settings designs follow a real `--settings-snapshot` of main `f633523`, and `designs/settings-shortcuts/now.html` is that snapshot's Shortcuts page.
- **App icons:** wave 1 lists no apps. Windows on the frozen screen are named in text, and recent captures are thumbnails. The pet on the bar lines stands in for Chippi.
- **Content colors:** the frozen screen's dimming, white selection and ink colors, the invented wallpaper and the sampled color are content, not Chippi's theme, so each page names them once as variables (`--ink-*`, `--wall-*`, `--sample`).

| Design | Versions |
|---|---|
| `home` | now (10 tiles); v1 the 12 tiles; v2 five columns; v3 Capture and Displays lead as wide tiles |
| `capture-panel` | v1 modes and tools as tiles; v2 Region leads; v3 a list of modes with their keys |
| `capture-overlay` | v1 two bars beside the selection (Lightshot); v2 one floating toolbar; v3 a dock at the bottom of the display. Each with hover, drag, released, edge, full display, Ctrl+drag and every markup tool |
| `capture-results` | v1 an island page for each; v2 results on the frozen screen; v3 the capture beside its result. Each with the bar's lines |
| `displays` | v1 a slider row per display; v2 a mixer; v3 the displays as Windows arranges them |
| `settings-capture` | v1 four pages, shortcuts first; v2 one Screen capture page; v3 four pages, shortcuts last. Each with the save folder's states |
| `settings-shortcuts` | now; v1 grouped by feature; v2 grouped as the sidebar is; v3 set shortcuts first |

### Calls made in the mockups (v1)

1. **Tools keep their places.** Wave 1's tools are Text, QR code and Color; Record, GIF and Convert join after them in wave 2, so nothing moves when they're installed (Marge's call: only installed features show).
2. **Delay is a picker in the panel's header** ("No delay", 3, 5 or 10 seconds) and applies to the next mode; it's kept.
3. **Hover takes a window, drag takes a region** in every mode that selects. The pill names the window and its size.
4. **Bars move, never overlap:** at an edge the tools flip to the selection's other side and the bar goes above; when the selection fills a display both sit inside it; the tool column stays on the display.
5. **Ctrl+drag shows the copy glyph in the size pill** while it's held, so you know release will copy.
6. **Each tool has its key in its tooltip** (P, H, L, A, R, E, T, D, X, C from Karl's spec); Select has none until the brief gives it one.
7. **Text has its size beside it while typing; Crop asks with Cancel and Crop.**
8. **Pick and review shows "Copied"** under the color: HEX is copied on the pick, like Pick color.
9. **A display that doesn't answer** reads "Brightness control failed" on the island; its reason and Try again are in Settings › Displays.
10. **Settings › Shortcuts groups rows by feature, one rule for the page** (Marge approved). Every feature's rows sit under its own name, A to Z, existing ones too (App switcher, Command bar, Radial menu). The bindings you make with Add shortcut come last, under "Your shortcuts", next to Add shortcut. Today feature rows all sit under "Chippi" and bindings under their action kind, so this is a change to `ShortcutsPage` in App.
11. **No default keys except Print Screen** (Travis, decision 5): every other Phase 7 row shows Record on Settings › Shortcuts and "Not set" with Edit on its feature page. Existing defaults stay as shipped (Command bar's Ctrl+Alt+Space).

### For Marge (against the README's starter rule)

- **Extract text and Pick and review color** get global slots here: they start a capture. The README's list names Copy text from screen, Scan QR and Pick color only.
- **"Open Capture" and "Open Displays"** are the panel openers the README calls "opening each panel".

### Copy

- **Capture panel:** Capture · No delay · 3 seconds · 5 seconds · 10 seconds · Region · Window · Screen · All displays · Freeform · Last region · Text · QR code · Color · Record · GIF · Convert · Recent · Open folder · No captures yet · Tooltips: the verb, then its keys once set ("Capture region · PrtSc", "Capture window")
- **Frozen screen:** "Terminal · 1040 × 800" · "1320, 760 · #F3F3F3" · Save as · Save · Copy · Cancel (tooltip) · Copy text and Scan QR code (tooltips) · Cancel · Crop · Thin · Medium · Thick · Pick from screen
- **Bar:** Copied · Saved · Copy failed · Save failed · Folder not found · Copied text · 31 words · No text found · No QR code found · Text and QR · Copied as HEX · Capturing in 3
- **Results:** Text · 31 words · Reselect · Copy all · QR code · Link · Text · Copy · Open link · "1 2 of 2" · Color · Copied · HEX · RGB · HSL · HSV · CMYK · Clear · Pick again (all five formats and Clear, as color-picker.md says; the first board had three)
- **Displays:** Displays · Brightness · Contrast · All displays · Main · Brightness control failed
- **Settings:** Shortcuts · Saving · Save folder · Change · Reset · Folder not found · File type · When a capture finishes · Do what I press · Selecting · Ctrl+drag copies on release · Magnifier · Recent captures · Keep recent captures · The last 50 you copied or saved · Clear · Copying · Copy as · Recent colors · Text · Language · Languages installed in Windows · Keep line breaks · Brightness keys · Act on · Display under the pointer · Step · Try again · "Turn on DDC/CI in the monitor's menu, or connect it without a dock" · Not set · Edit · Record · Add shortcut
