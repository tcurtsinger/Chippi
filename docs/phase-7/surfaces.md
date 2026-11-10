# Phase 7 surfaces: where every feature lives

Lumen, 2026-11-06. Task 468, reviewer Kayla (feasibility). Read against `main` at `8c058e2`, after [behavior-spec-a.md](behavior-spec-a.md) (Capture), [behavior-spec-b.md](behavior-spec-b.md) (Apps and System) and [architecture.md](architecture.md).

Status: a proposal for Travis's Step 2 decision 8 (the doors map). Boards are rough: sizes are real, but glyphs, copy and states get finished in the Step 3 mockups. Every app, file and number on them is invented. The home's fit was checked with a real render of the island. Nothing else here ran.

Boards: [design/surfaces.html](design/surfaces.html), with renders in `design/shots/<board>-<theme>.png` (`design/render.ps1` makes them again). Real home renders are in `design/fit/`.

## 1. The rule

Each verb has one home, and every verb is also reachable from everywhere else.

| Door | What goes there | Test |
|---|---|---|
| **Home tile and panel** | A place you go often, with live state or a list to look at | Used most days, and the panel is worth looking at. The home holds 12 tiles (section 2) |
| **Actions menu** | One-shot verbs and short lists you use now and then, grouped by topic (Phase 6: Plan, Power, Quit, Snap, Input) | A feature with a home tile adds no Actions tile: its verbs are already one click from the home |
| **Search, Radial, shortcuts** | Every verb, whatever its home | Search lists it as a Run row; a Radial slice of kind Action can hold it; Settings › Shortcuts lists its slot |
| **Settings page** | Everything the island does for that feature, plus long lists, history and options | Under the feature's catalog group (Capture, Apps, System) |
| **The bar** | Ambient state (a recording, a delay, updates running) and 6-second result lines (Copied, Saved) | A click opens the feature's page, as today |
| **Overlays** | The frozen screen and any recording highlight; recording controls live in the expanded puck | Only while a capture or recording runs. Never a place to go |

Shortcuts are set only in Settings › Shortcuts. A feature's page shows its keys read-only with Edit (Travis, 2026-11-05). The island never shows key captions on screen. A card's tooltip names its verb and key ("Capture window · Ctrl+Shift+W").

## 2. The home at 12

Today's gallery has 10 tiles (Audio, Awake, Organizer, System, Clipboard, Scratchpad, Agents, Shelf, Actions, Search), plus Now Playing in the footer. The plan counts that as 11 tools. Capture makes 11 tiles. Displays (section 5) makes 12, which fills the third row.

Measured with a real render (`IslandPanelShots` with the real `IslandGrid`, stand-in Capture and Displays tiles, dark and light, at 2x):

| Tiles | Rows | Panel | Tile height |
|---|---|---|---|
| 10 (today) | 3 | 520 × 360 | 67 |
| 11 (+ Capture) | 3 | 520 × 360 | 67 |
| 12 (+ Displays) | 3 | 520 × 360 | 67 |
| 13 | 4 | 520 × 360 | 48: glyph and label crowd, and one tile sits alone on row 4 |

The home is already at its 360 cap with 3 rows plus the quick controls, so a fourth row squeezes every tile instead of growing the panel. **12 is the most the 4-column home holds at its approved size.** A 13th tool needs the five-column layout the Phase 6 README held for later, so the Apps trio doesn't get tiles now (section 6).

Shots: `design/fit/p7-home-12-capture-displays-{dark,light}.png` and `design/fit/p7-home-13-overflow-{dark,light}.png`. The Capture tile in the render uses the existing Image glyph as a stand-in; the mockup picks its own glyph.

**Default order:** Displays after System (Order 65) and Capture after Clipboard (Order 75). Audio, Awake, Organizer, System, then Displays, Clipboard, Capture, Scratchpad, then Agents, Shelf, Actions, Search. Travis's saved order in Settings › Island & pet is kept, and a new tile lands by its default order.

## 3. The doors map

"Starter" slots are the shortcut rows every option of task 471 keeps. The result and editor verbs (Copy capture, Trim recording, Copy HEX and the rest) get global slots only if Travis picks Karl's "every verb" rule. Either way, the overlay's keys (Ctrl+C, Ctrl+S, Esc, the tool letters) are contextual and never registered.

| Feature | Home | Actions | Search | Radial | Starter shortcut slots | Bar | Settings |
|---|---|---|---|---|---|---|---|
| Screen capture | **Capture** tile: modes row, Delay, Recent | none | Each verb as Run; "Capture" opens the panel; "Recent captures" | Any verb | Capture region (Print Screen if Travis opts in), Capture window, full screen, all displays, freeform, last region, after delay; Open recent captures | Copied, Saved, Copy failed, "Capturing in 2" | Capture › Screen capture |
| Screen recording | Capture panel: Record; a recording card while it runs; saved-result preview | none | Record window, Record screen, Pause/Resume, Stop recording | The same shared recording actions | Record window, Record screen, Pause/Resume recording, Stop recording | Expanded puck: large red dot/time, icon-only Pause/Resume and Stop, pet at his usual end | Capture › Recording |
| Text and QR | Capture panel: Text, QR code; the result pages | none | Copy text from screen, Extract text, Scan QR | Any verb | Copy text from screen, Extract text, Scan QR | Copied text, No text found | Capture › Text and QR |
| Color picker | Capture panel: Color; the review page | none | Pick color, Pick and review color | Any verb | Pick color, Pick and review color | "#7C5CFF · Copied as HEX" with the swatch | Capture › Color picker |
| Media tools | Capture panel: Convert page (no tile) | none | Convert media, Resize images, Compress media | Any verb | Convert media, Resize images, Compress media (each opens Convert with a file picker) | "3 of 4 converted" while running | Capture › Media tools |
| Displays and brightness | **Displays** tile: a slider per display, All displays, Contrast | none | Brightness up, down; "brightness 40" as a value row | Any verb | Brightness up, Brightness down | none (the change shows on the monitor) | System › Displays |
| App updates | none | **Apps** menu: Update apps (list) | Check app updates, Update all apps; "update firefox" | Any verb | Check app updates, Update all apps | "2 of 5 updated" while running | Apps › App updates |
| Uninstaller | none | Apps menu: Uninstall (opens Settings) | "uninstall notepad" finds the app, Run asks first | Any verb | Uninstall app (opens Settings › Uninstaller, search focused) | "Uninstalling Notepad++" while running | Apps › Uninstaller |
| Cleaner | none | Apps menu: Clean junk files (list, then confirm) | Clean junk files | Any verb | Scan for junk files, Clean junk files | "Cleaned 1.0 GB" | Apps › Cleaner |
| Port manager | none | none | "port 3000" is a value row naming the process, with Free, which asks first | Any verb | Find port (opens Search with "port "), Free port (same, then the confirm) | none | System › Ports |

Every feature has at least three doors, and each verb has exactly one home.

## 4. Capture

### The Capture panel (board `sheet-capture`)

520 × 360 with no scrolling. The body uses the island's six 76-wide columns (`IslandPage.Columns`), so both card rows line up.

- **Header:** Back, "Capture", and on the right a Delay picker ("No delay", 3, 5 or 10 seconds). The delay applies to the next mode picked. This is how the spec's "Capture after delay" sits on the panel without a seventh mode.
- **Modes row,** 64-tall cards: Region (the accent fill, because it's the main task), Window, Screen, All displays, Freeform, Last region. A click closes the island first, so the panel is never in the capture, then freezes the screen.
- **Tools row:** Record, Text, QR code, Color, Convert. The revised recording menu offers Window/Screen; GIF is exported from a saved recording.
- **Recent:** the last four captures as 118 × 66 thumbnails with type, size and age, plus "Open folder". A recording shows its length. A click opens it on its result page with Copy, Save as, Annotate and Open folder. "No captures yet" when there are none. The full history is in Settings.
- **While recording:** a recording card takes the modes row (red dot, elapsed time, the source, Discard, Pause, Stop). Record shows as on, and the screenshot tools are dimmed until Stop because their frozen-screen overlays would cover the recorded content. Convert stays usable. The expanded puck provides the primary icon-only controls.

### The frozen screen (board `sheet-overlay`)

This is Lightshot's flow on Snipping Tool's feature list.

1. **Select:** one key, and every display freezes and dims. Hovering lights the frontmost window, and a click takes it. A drag draws a region (ShareX and Snipaste work this way, so Region mode covers Window too). A magnifier shows the pixel, its coordinates and color, and can be hidden in Settings. A size pill shows "1040 × 800" at the selection's corner.
2. **Mark up in place:** on release the selection keeps its handles, and arrow keys move it by a pixel (Shift resizes, as Flameshot does). The tools run down the selection's side in one 40-wide column: Select, Pen, Highlighter, Line, Arrow, Rectangle, Ellipse, Text, Redact, Eraser, Crop, then color, then Undo and Redo. Copy and Save sit in a bar under it: Copy text, Scan QR, Save as, Save, Copy (the accent) and Cancel. This is Lightshot's two-bar layout, so a small selection still gets every tool. Each bar flips to the other side when the selection reaches a screen edge. The bars live on the display where the drag was released, clamped to it, so they never span two displays. A selection that fills the whole display (Ctrl+A, or edge to edge) has no outside, so both bars sit inside it, as in Lightshot (board `sheet-overlay` 2b).
3. **Fast path:** hold Ctrl as the drag starts, and release copies and closes with no bars (Lightshot). Ctrl+C, Ctrl+S, Esc and Enter work with no click.
4. **Text and QR from a capture:** the same selection's Copy text and Scan QR buttons, so a region you already drew doesn't need drawing again.

The bars use island surfaces (fill, rim, shadow, 4-radius controls), so they read as Chippi in both themes. The dimming and handles stay neutral so they work over any screen.

### Recording (implementation authorized by Travis, 2026-11-09)

- **Targets:** Window and Screen only; no region recording or background replay. Screen chooses one display; skip the picker when only one exists. Screenshot modes remain unchanged.
- **Before Start:** the recording menu shows source, Include system audio (on), Include mic audio (off), Original/1080p, and 30/60 fps. Audio uses the current default output/input resolved at Start, without app-audio or recording-device pickers. Start begins a cancellable 3-second countdown. Output is MP4; GIF remains a result export.
- **While recording:** use the expanded Chippi puck, not a separate pill. A larger red dot and timer share one vertically centered row; omit the visible "Recording" label. Pause/Resume and Stop are icon-only with hover labels, accessible names, and independent hit targets. The pet still opens the regular panel; preserve mirrored layouts and dragging. Recording takes priority over ordinary activity and Now Playing. Exclude the puck from the output only during recording.
- **Chosen animation (2026-11-09):** Chippi holds a small camcorder beside/below his face, with gentle motion and a pulsing camera tally light; the timer's dot is steady. Pause and finish have distinct poses. Center the tightly spaced dot/time/control group together in the space beside Chippi. See the recording brief for the chosen motion direction; the production puck now uses the approved camcorder artwork.
- **Fullscreen:** validate puck reachability without disturbing the game. Do not promise an exclusive-fullscreen overlay or silently change the widget's ordinary fullscreen visibility. Stop shortcuts and Radial actions provide alternate controls.
- **Stopped:** always save to the recording folder and add Recent before opening preview. The result page offers Play, Trim, Export GIF, Save as, and Open folder; nothing plays on its own. Trim/export preserve the original.
- **Stopped over a fullscreen app:** save quietly and show brief Saved feedback when the puck is visible again. No automatic preview opens later. See [screen-recording.md](screen-recording.md) for the current plan; the early region/pill boards are superseded for recording.

### Results (board `sheet-results`)

The rule: a result that copies says so on the bar for 6 seconds. A result you need to read opens a page on the Capture panel.

- **Copy text from screen:** "Copied text · 31 words" on the bar. Nothing found shows "No text found" in Caution, and the clipboard is left alone.
- **Extract text:** the Text page with the text editable, its language and word count, Reselect and Copy all. Ctrl+C copies a selection.
- **Scan QR:** the QR code page with the payload in full, its kind and where a link goes. Copy, and Open link only for web links. Several codes switch in the header.
- **Pick color:** "#7C5CFF · Copied as HEX" with the swatch on the bar. **Pick and review:** the Color page with HEX, RGB and HSL, each with Copy, a recent palette and Pick again.
- **Convert:** the Convert page. The header switches between Convert, Resize and Compress. Files are listed with their sizes and the estimated sizes. Options sit in two dropdowns, plus Add files and Run. Empty, it's a drop zone with Add files. More than five files page like Actions lists. Originals are kept, and new files save beside them.

## 5. Displays gets a tile

**Recommend a tile** (board `sheet-displays`).

- **Frequency:** brightness is a several-times-a-day control (evening, a bright room), the way volume is. Twinkle Tray's and Monitorian's whole value is that the slider is one click away.
- **Live state:** the panel shows each display's level, so it's a place to look as well as a control.
- **Essentials:** Displays is in the Essentials preset, the last one not built. A fresh Essentials install gets it on the home next to Audio and System.
- **Fit:** it's the 12th tile and fills row 3 (section 2).

The panel's header switches between Brightness and Contrast (Contrast only for external monitors), the way Audio switches views. There's an All displays row (left out with one display), then a 44-tall card per display: its name, "· Main", a slider and the percentage. Up to five rows fit, so four displays plus All displays. A display that doesn't answer shows "Brightness control failed" (architecture's copy). Chippi can't tell DDC/CI being off in the monitor's menu from a dock or adapter that doesn't pass it on, so Settings › Displays lists the likely causes rather than one reason. Extend and Turn off displays stay in Actions, since they're Windows display modes and power, not brightness.

The alternative, if Travis would rather keep the 12th place free: an Actions menu "Displays" whose view holds the same sliders, like Snap's custom view. That puts it two clicks away.

## 6. Apps and System without tiles

**Recommend no home tiles** for App updates, Uninstaller, Cleaner and Port manager.

- **Frequency:** they're weekly or rarer jobs.
- **Room:** the uninstaller's list runs to hundreds of apps and needs search and sorting, and its leftover review is long. Those belong in Settings, where the island's no-scroll rule doesn't hold.
- **Fit:** a 13th tile breaks the home's fit (section 2).

**Actions gains one menu, Apps** (board `sheet-actions`), which takes Actions to 19 of its 20 places:

- **Update apps:** a list like Quit apps, with each app's real icon, its versions and Update, plus Update all in the header. Running apps ask "Close Firefox and update?". "Everything is up to date" and Check again when there's nothing. Skip and history live in Settings.
- **Clean junk files:** the categories, measured on open, with sizes and the last ticks remembered. "Check first" and "Needs admin" tags. Clean in the header asks "Clean 1.0 GB for good?" with Cancel and Clean.
- **Uninstall:** opens Settings › Apps › Uninstaller with its search focused (the open glyph, like Arrangements' Manage).

**Port manager lives in Search and Settings** (board `sheet-search`). The task is "who holds port 3000?", a lookup by number, so typing "port 3000" in Search gives the row "Port 3000 · node.exe" with its PID and state. "Ports" opens Settings › System › Ports, the live list. The row's verb depends on what holds the port (architecture §9):

| Holder | Row's verb | Confirm |
|---|---|---|
| Your own process | Free | "End node.exe to free port 3000?" with Cancel and End |
| A service (its row says "Service") | Stop, with the UAC shield | "Stop service Print Spooler to free port 9100?" with Cancel and Stop. Only the service stops, never its `svchost` host, which may run others. |
| An admin, SYSTEM or other user's process | Free, with the UAC shield | "End postgres.exe to free port 5432?" with Cancel and End, the shield on End. It goes through the admin helper (wave 3), one UAC prompt. |
| System (PID 4) or a closing connection (PID 0, TIME_WAIT) | none | Enter opens Settings › Ports at that row. |

Before Free or Stop runs, the row is read again (PID and start time). If the process has changed or gone, nothing is ended, and the row updates. The shield is Windows' own stock shield icon. Settings › Ports uses the same verbs and confirms.

The Search value rows "brightness 40" and "port 3000" follow the existing "volume 40" pattern.

## 7. Settings pages

Each feature gets one page in its catalog group's section of the sidebar (board `settings`).

| Group | Page | What's there beyond the island |
|---|---|---|
| Capture | Screen capture | After capture (Copy, Save, or both), Save to, file type, Ctrl+drag, magnifier, recent captures and how many to keep, Print Screen, Shortcuts (read-only with Edit) |
| Capture | Recording | Save to, cursor and click rings, Shortcuts; audio switches and quality are in the recording menu |
| Capture | Text and QR | OCR language (installed ones only), line breaks, Shortcuts |
| Capture | Color picker | Copy format, palette, Clear, Shortcuts |
| Capture | Media tools | Presets, output folder, metadata, Shortcuts |
| Apps | App updates | The full list, Skipped with Restore, history, "Show apps with an unknown version", Shortcuts |
| Apps | Uninstaller | Search, sort and filter over every app, the leftover review, Shortcuts |
| Apps | Cleaner | Categories with descriptions, what each never touches, Shortcuts |
| System | Displays | The likely causes when a display doesn't answer, step size, which display the keys act on, Reset, Shortcuts |
| System | Ports | The live list (protocol, ports, state, process with icon and path), all connections, Shortcuts |

## 8. What we took from each competitor's screens

| Tool | Taken for ease of use | Left out |
|---|---|---|
| Lightshot | One key, then drag; tools beside the selection, Copy and Save under it; Ctrl-drag copies; Ctrl+A takes the display | Upload, share and search buttons |
| Snipping Tool | The mode set (region, window, screen, freeform), delay, text actions on a capture, recording's Start step | A separate editor window, Clipchamp hand-off |
| ShareX, Snipaste | Hover a window to take it, drag for a region, in one mode; magnifier; last region | Workflow builders |
| Flameshot | Arrow keys nudge and Shift resizes; tool letters | |
| CleanShot X | One Capture home with the recent results beside the tools | The floating thumbnail after every capture (the bar line does that job) |
| PowerToys Color Picker, Text Extractor | Copy straight away, or review with formats | |
| Twinkle Tray, Monitorian | One click to the sliders, All displays together, plain likely causes for a display that doesn't answer | Time-of-day schedules |
| UniGetUI, Patch My PC | List, Update, Update all; close the app first | Several package managers, schedules |
| BleachBit | Measure first, then a confirm with the size | Shredding |
| TCPView, CurrPorts | Process name, icon and path on every port | Closing single connections in the first build |

## 9. For Travis (Step 2, decision 8)

1. **Displays:** a home tile (recommended), or an Actions menu with the same sliders.
2. **Apps menu in Actions:** Update apps, Clean junk files and Uninstall in one new menu (recommended), taking Actions to 19 of 20 places.
3. **Port manager without a tile or menu:** Search's "port 3000" and Settings › Ports (recommended).
4. **Media tools inside Capture:** the Convert page on the Capture panel rather than a tile (recommended).
5. **Recording controls (revised 2026-11-09):** the expanded puck owns icon-only Pause/Resume and Stop; the separate-pill proposal is superseded. Fullscreen control access remains a technical check.
6. **Recording targets (revised 2026-11-09):** Window or one Screen. Region and combined multi-display recording are not in this plan.

## 10. Feasibility (Kayla's review of #280)

- **Recording controls out of the video:** the capture-exclusion approach must be checked on the expanded puck. There is no separate pill in the revised plan. The puck is excluded only while a recording runs.
- **Click takes a window, drag draws a region:** still applies to screenshots. Recording now offers Window/Screen only; exclude Chippi's own windows from source selection.
- **Bars never span two displays:** each display has its own overlay, so a bar is clamped to the display where the drag was released.
- **Recent thumbnails:** small thumbnails are saved into the history index when a capture is kept (a poster frame for a recording), so the panel's open decodes nothing. Kayla adds this to the capture brief.
- **"port N" per keystroke:** the port table reads in about 2 ms; process names and services are read once per query, and the row is read again before Free.
- **Verbs the capture brief adds:** Pause and Resume, Trim, playback on the recording page, and Record window's frame following the window as it moves. All feasible.

## Not verified

- Only the home's fit was rendered from the real island (section 2). Every other board is HTML at real sizes, not WPF.
- The glyphs for Capture, Displays and the tools are Lucide stand-ins; the app doesn't have most of them yet.
- No timings: none of these surfaces exist. The briefs set the targets (key to frozen screen, release to toolbar, the panel's open).
