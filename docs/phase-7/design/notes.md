# Phase 7 mockups: notes

## Wave 1 (task 493, 2026-11-06)

`wave-1.html` draws wave 1's screens against the approved map ([surfaces.md](../surfaces.md)) and Travis's decisions ([README.md](../README.md)). Sizes are real DIPs. Glyphs are Lucide's own, copied into `wave-1/lucide.js` (ISC); Screens is Chippi's. Every window, file and number is invented, except Travis's two monitors' names, which Settings › Snap already shows.

- **Real renders:** `wave-1/home-12-{dark,light}.png` are the real island (`IslandGrid` in `Chippi.Island.Tests`), with stand-in Capture and Displays tiles carrying the final glyphs: Lucide `scan` for Capture (not in `LucideIcons` yet) and the app's `Sun` for Displays. The Settings boards follow a real `--settings-snapshot` of main `9a978b7`: the row heights, the Edit link and the Record column match it.
- **App icons:** wave 1 lists no apps. Windows on the frozen screen are named in text, and recent captures are thumbnails. The pet on the bar lines stands in for Chippi.
- **Renders:** `render.ps1` writes `shots/w1-<board>-<theme>.png`; `-Only w1-<board>` renders one.

| Board | What it shows |
|---|---|
| `home` | The home at 12: Displays after System, Capture after Clipboard |
| `sheet-capture` | The Capture panel: wave 1 (Text, QR code, Color), first use, Delay picked, a mode pointed at, and with wave 2's Record, GIF and Convert |
| `sheet-overlay` | The frozen screen: hover a window, dragging with the magnifier, released with both bars, at a screen edge, filling a display, Ctrl+drag |
| `sheet-markup` | Each tool in use (Select, Pen, Highlighter, Line, Arrow, Rectangle, Ellipse, Text, Redact, Eraser, Crop) and the color choice |
| `sheet-bar` | Copied, Saved, Copy failed, Save failed, Copied text, No text found, No QR code found, the color line, Capturing in 3 |
| `sheet-results` | Text, QR code (a link, and two codes), Color |
| `sheet-displays` | Brightness with All displays, Contrast, a display that doesn't answer |
| `settings-capture`, `sheet-settings-capture-states` | Settings › Screen capture, and the save folder: default, changed (Reset), gone |
| `settings-text`, `settings-color`, `settings-displays` | Settings › Text and QR, Color picker, Displays |
| `settings-shortcuts` | Settings › Shortcuts with the new rows; Print Screen on Capture region |

### Calls made in the mockups

1. **Tools keep their places.** Wave 1's tools are Text, QR code and Color; Record, GIF and Convert join after them in wave 2, so nothing moves when they're installed (Marge's call: only installed features show).
2. **Delay is a picker in the panel's header** ("No delay", 3, 5 or 10 seconds) and applies to the next mode; it's kept.
3. **Hover takes a window, drag takes a region** in every mode that selects. The pill names the window and its size.
4. **Bars move, never overlap:** at an edge the tools flip to the selection's other side and the bar goes above; when the selection fills a display both sit inside it; the tool column stays on the display.
5. **Ctrl+drag shows the copy glyph in the size pill** while it's held, so you know release will copy.
6. **Each tool has its key in its tooltip** (P, H, L, A, R, E, T, D, X, C from Karl's spec); Select has none until the brief gives it one.
7. **Text has its size beside it while typing; Crop asks with Cancel and Crop.**
8. **Pick and review shows "Copied"** under the color: HEX is copied on the pick, like Pick color.
9. **A display that doesn't answer** reads "Brightness control failed" on the island; its reason and Try again are in Settings › Displays.
10. **Settings › Shortcuts groups rows by feature** (Screen capture, Text and QR, Color picker, Displays). Today it groups by action kind, so every Phase 7 verb would land under "Chippi". This is a change to `ShortcutsPage` in App.

### For Marge (against the README's starter rule)

- **Extract text and Pick and review color** get global slots here: they start a capture. The README's list names Copy text from screen, Scan QR and Pick color only.
- **"Open Capture" and "Open Displays"** are the panel openers the README calls "opening each panel".

### Copy

- **Capture panel:** Capture · No delay · 3 seconds · 5 seconds · 10 seconds · Region · Window · Screen · All displays · Freeform · Last region · Text · QR code · Color · Record · GIF · Convert · Recent · Open folder · No captures yet · Tooltips: "Capture window · Ctrl+Shift+W" (verb · keys)
- **Frozen screen:** "Terminal · 1040 × 800" · "1320, 760 · #F3F3F3" · Save as · Save · Copy · Cancel (tooltip) · Copy text and Scan QR code (tooltips) · Cancel · Crop · Thin · Medium · Thick · Pick from screen
- **Bar:** Copied · Saved · Copy failed · Save failed · Folder not found · Copied text · 31 words · No text found · No QR code found · Text and QR · Copied as HEX · Capturing in 3
- **Results:** Text · 31 words · Reselect · Copy all · QR code · Link · Text · Copy · Open link · "1 2 of 2" · Color · Copied · HEX · RGB · HSL · Pick again
- **Displays:** Displays · Brightness · Contrast · All displays · Main · Brightness control failed
- **Settings:** Shortcuts · Saving · Save folder · Change · Reset · Folder not found · File type · When a capture finishes · Do what I press · Selecting · Ctrl+drag copies on release · Magnifier · Recent captures · Keep recent captures · The last 50 you copied or saved · Clear · Copying · Copy as · Recent colors · Text · Language · Languages installed in Windows · Keep line breaks · Brightness keys · Act on · Display under the pointer · Step · Try again · "Turn on DDC/CI in the monitor's menu, or connect it without a dock" · Not set · Edit · Record · Add shortcut
