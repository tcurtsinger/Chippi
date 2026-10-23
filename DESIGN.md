---
name: Chippi
description: A Windows tray suite that looks like it shipped with Windows 11 — neutral layers, Segoe, the user's accent — with one small gradient blob that lives in the island.
colors:
  window: "#202020"
  card: "#2B2B2B"
  card-stroke: "rgba(255,255,255,0.03)"
  divider: "rgba(255,255,255,0.06)"
  flyout: "#2C2C2C"
  flyout-card: "rgba(255,255,255,0.045)"
  flyout-card-stroke: "rgba(255,255,255,0.05)"
  flyout-footer: "#202020"
  control: "rgba(255,255,255,0.06)"
  control-hover: "rgba(255,255,255,0.09)"
  control-pressed: "rgba(255,255,255,0.04)"
  control-stroke: "rgba(255,255,255,0.09)"
  subtle-hover: "rgba(255,255,255,0.04)"
  subtle-selected: "rgba(255,255,255,0.06)"
  segment-trough: "rgba(0,0,0,0.22)"
  segment-stroke: "rgba(255,255,255,0.06)"
  segment-selected: "rgba(255,255,255,0.10)"
  input: "rgba(255,255,255,0.06)"
  input-stroke-bottom: "rgba(255,255,255,0.30)"
  input-focus: "#1C1C1C"
  switch-off-stroke: "rgba(255,255,255,0.60)"
  switch-off-knob: "rgba(255,255,255,0.80)"
  text: "#FFFFFF"
  text-secondary: "#C5C5C5"
  text-caption: "#ABABAB"
  text-muted: "#9D9D9D"
  caution: "#FCE100"
  caution-tint: "rgba(252,225,0,0.10)"
  success: "#6CCB5F"
  critical: "#C42B1C"
  island: "#0A0A0A"
colors-light:
  window: "#F3F3F3"
  card: "#FBFBFB"
  card-stroke: "rgba(0,0,0,0.06)"
  divider: "rgba(0,0,0,0.07)"
  flyout: "#F3F3F3"
  flyout-card: "#FFFFFF"
  flyout-card-stroke: "rgba(0,0,0,0.06)"
  flyout-footer: "#EBEBEB"
  control: "#FFFFFF"
  control-hover: "#F6F6F6"
  control-pressed: "#F0F0F0"
  control-stroke: "rgba(0,0,0,0.10)"
  subtle-hover: "rgba(0,0,0,0.04)"
  subtle-selected: "rgba(0,0,0,0.055)"
  segment-trough: "rgba(0,0,0,0.04)"
  segment-stroke: "rgba(0,0,0,0.06)"
  segment-selected: "#FFFFFF"
  input: "#FFFFFF"
  input-stroke-bottom: "rgba(0,0,0,0.45)"
  input-focus: "#FFFFFF"
  switch-off-stroke: "rgba(0,0,0,0.55)"
  switch-off-knob: "rgba(0,0,0,0.60)"
  text: "#1A1A1A"
  text-secondary: "#404040"
  text-caption: "#5D5D5D"
  text-muted: "#616161"
  caution: "#9D5D00"
  caution-tint: "rgba(157,93,0,0.10)"
  success: "#0F7B0F"
  critical: "#C42B1C"
  island: "#0A0A0A"
accent:
  follow-windows: "default. Dark theme uses the Windows AccentLight2 shade, light theme uses AccentDark1"
  blue: { dark: "#4CC2FF", light: "#005FB8" }
  violet: { dark: "#B79CFF", light: "#6B4FBB" }
  green: { dark: "#6CCB5F", light: "#107C10" }
  pink: { dark: "#FF99A4", light: "#B4235A" }
  on-accent: { dark: "#000000", light: "#FFFFFF" }
pet:
  ink: "#0B0F1F"
  blush: "#FF8FB0"
  spark: "#F5C451"
  synth: "linear-gradient(135deg, #B15CF0, #4D9BFF)"
  iris: "#CBB2FF"
  azure: "#8EC0FF"
  signal: "#8EE6C0"
  ember: "#FFC58F"
  frost: "#E8ECF6"
typography:
  family: "Segoe UI Variable Text, Segoe UI, sans-serif"
  display-family: "Segoe UI Variable Display, Segoe UI, sans-serif"
  code-family: "Cascadia Mono, Consolas, monospace"
  page-title: { size: "20px", weight: 600, lineHeight: "28px", family: display }
  section: { size: "14px", weight: 600, lineHeight: "20px" }
  body: { size: "14px", weight: 400, lineHeight: "20px" }
  flyout-section: { size: "13px", weight: 600, lineHeight: "20px" }
  flyout-body: { size: "13px", weight: 400, lineHeight: "20px" }
  caption: { size: "12px", weight: 400, lineHeight: "16px" }
  nav-group: { size: "12px", weight: 600, lineHeight: "16px" }
  badge: { size: "11px", weight: 600 }
  window-title: { size: "12px", weight: 400 }
  numbers: "tabular figures in the UI family"
rounded:
  control: "4px"
  icon-chip: "6px"
  segment-trough: "6px"
  card: "8px"
  flyout: "8px"
  window: "system (8px)"
  switch: "10px"
  badge: "4px"
  island-tab: "half-oval: fully round on the side away from the edge"
  island: "8px"
spacing:
  flyout-padding: "12px"
  card-row: "12px 16px"
  settings-row: "10px 16px"
  settings-row-min: "60px with a caption, 48px without"
  sub-row-indent: "50px"
  section-gap: "24px above a section header, 8px below"
  group-gap: "8px"
  settings-content: "4px 40px 40px 28px"
  nav-width: "272px"
---

# Design System: Chippi

Approved mockup (2026-10-23): [Chippi Native Look](https://claude.ai/artifact/5M1W8XSCiDPvCpc5Hrd2Bq), with the Settings window and the Keep awake and Tidy flyouts in dark and light. It is the reference for every pixel below. The audit that led to it is [docs/phase-1-2-ui-ux-audit.md](docs/phase-1-2-ui-ux-audit.md).

This replaces Direction A "Studio Rack" (navy chassis, Manrope, the violet→azure Synth Gradient, JetBrains Mono readouts), approved 2026-10-21 and retired 2026-10-23 because it read as heavy and unpolished next to vorssaint. The [design canvas](https://claude.ai/artifact/CMBT4oFV3oGZRUei1d7K17) keeps Studio Rack for history only.

## 1. Overview

**Creative North Star: "Native Windows 11, with a resident."**

Chippi should look like it shipped with Windows 11. It uses Windows' own font, its neutral layered surfaces, its control shapes and the user's accent color, so it sits next to Settings, Quick Settings and File Explorer without looking like a guest. vorssaint looks polished because it looks native to macOS; Chippi gets there by looking native to Windows.

The personality comes from one place: **Gumdrop**, a gradient blob with a face who lives in the island, a small tab the user docks on a screen edge. It naps when nothing is happening and reacts when Chippi does something for you. Everything else stays quiet.

**Key characteristics**
- Neutral gray layers: window, card, control. No tinted chassis.
- One flat accent, the user's Windows accent by default, used only for what is on, selected or primary.
- Segoe at Regular for everything you read. Semibold only marks structure.
- Rows grouped in one card per section, divided by hairlines.
- The tray flyout is where you act. Settings is where you configure.
- Dark and light are equal. Follow Windows is the default.

**Boundaries.** vorssaint-utils is the feature reference, not a visual one: its name, icon, trade dress and branding are covered by its trademark policy. Chippi follows Windows 11 conventions, keeps its own name, icon and pet, and never copies vorssaint's screens or assets.

## 2. Colors

### Surfaces
Dark: Window `#202020` · Card `#2B2B2B` · Flyout `#2C2C2C` · Flyout footer `#202020`. Light: Window `#F3F3F3` · Card `#FBFBFB` · Flyout `#F3F3F3` · Flyout cards `#FFFFFF` · Flyout footer `#EBEBEB`. Controls, hovers, strokes and dividers are white (dark) or black (light) at low alpha; the exact values are in the front matter.

**Island** `#0A0A0A` is darker than any window, so the pill reads as a hole in the screen. It stays dark in both themes.

### Text
Dark: Text `#FFFFFF` · Secondary `#C5C5C5` (values, inactive tab and segment labels, icon buttons) · Caption `#ABABAB` · Muted `#9D9D9D` (nav group labels, placeholders, glyphs of features that are off). Light: `#1A1A1A` · `#404040` · `#5D5D5D` · `#616161`.

### Accent
A setting in General with five choices: **Follow Windows** (the default), **Blue**, **Violet**, **Green** and **Pink**. Each choice has two shades, because one color can't carry text on both a dark and a light surface:

| Choice | Dark theme | Light theme |
|---|---|---|
| Follow Windows | Windows AccentLight2 | Windows AccentDark1 |
| Blue | `#4CC2FF` | `#005FB8` |
| Violet | `#B79CFF` | `#6B4FBB` |
| Green | `#6CCB5F` | `#107C10` |
| Pink | `#FF99A4` | `#B4235A` |

Text and glyphs on an accent fill are black in dark and white in light (**On-accent**). Every pair clears 4.5:1.

### Signals
- **Caution** `#FCE100` dark / `#9D5D00` light: held files, warnings, validation messages, Overlap badges. On a tint of itself at 10% for badges.
- **Success** `#6CCB5F` / `#0F7B0F`: a moved file's check, a live status dot.
- **Critical** `#C42B1C`: destructive only, such as the Clear history button and the titlebar Close hover, always with white text.

### Named rules
**The Accent Rule.** The accent marks what is on, selected or primary: a switch that is on, a checked box, the selected nav item's pill, the selected tab's bar, the selected duration chip, the one primary button per view, and the glyph of an installed or watched feature. It is never decoration, a background wash, a divider or body text.

**The One Gradient Rule.** Gumdrop's body is the only gradient in the app.

**The Muted Floor Rule.** `#9D9D9D` (dark) or `#616161` (light) is the quietest text allowed. Both clear 4.5:1 on their surfaces.

## 3. Typography

**UI font: Segoe UI Variable** (Text for the UI, Display for page titles), the Windows 11 system font, with Segoe UI as the fallback. Nothing is bundled. See §9 for the WPF caveat on variable fonts.
**Code font: Cascadia Mono**, falling back to Consolas, only for regex, rename templates and other text the user types as code.

| Role | Size / weight | Used for |
|---|---|---|
| Page title | 20 / 600 Display | Settings page titles |
| Section | 14 / 600 | Settings section headers ("Watched folders") |
| Body | 14 / 400 | Settings row labels, buttons, nav items, values |
| Flyout section | 13 / 600 | The panel title in the flyout ("Keep awake") |
| Flyout body | 13 / 400 | Flyout rows, chips, buttons |
| Caption | 12 / 400 | The line under a row label, status lines, the flyout header status |
| Nav group | 12 / 600 | Sidebar group labels ("Your features", "Chippi") |
| Badge | 11 / 600 | Overlap and neutral badges |
| Window title | 12 / 400 | "Chippi Settings" in the titlebar |

The flyout's "Chippi" name is 14 / 600.

**The Weight Rule.** Regular (400) for everything you read. Semibold (600) only for page titles, section headers, the selected nav item, the selected SelectorBar tab and the flyout's name. Never Bold or heavier. Most of the clean look comes from this rule.

**The Tabular Rule.** Every number (times, counts, sizes, durations, versions) uses the UI font with tabular figures, so digits line up and don't shift as they change. Numbers never switch to a monospace font.

Copy is sentence case, plain and short. State comes first and detail second: "On · 1 hr left", "Silent · moves after 30s", "Held · a program named like a PDF". No exclamation marks, no emoji.

**The Bare Label Rule** (Travis, 2026-10-22). A label names the action and nothing else. Never repeat the app's name where its icon already says it, and never pad a label with explanation. The Explorer entry is "Organize", not "Organize with Chippi". When a label seems to need a sentence, the design is wrong; fix the design, not the copy.

**No page intros** (2026-10-23). A Settings page title has no description under it. A row caption may explain that row, in one short line.

## 4. Elevation

Flat, like Windows 11. Depth comes from the surface steps (window → card → control), not shadows.

- **Settings window:** the system frame draws the corners and shadow.
- **Flyout:** `0 8px 32px rgba(0,0,0,.28)` (light `rgba(0,0,0,.14)`), the shadow Windows uses on its own flyouts.
- **Island:** `0 16px 40px rgba(0,0,0,.45)` on the open island and `0 8px 24px rgba(0,0,0,.45)` on the drag bubble. The docked tab has none.
- **Selected segment, light theme only:** `0 1px 2px rgba(0,0,0,.12)`.

**The No-Glow Rule.** Nothing glows: no colored shadows under chips, thumbs, cards or buttons.

## 5. Surfaces

### Tray flyout
- **Placement:** anchored to the tray icon, 12px above the taskbar, right edge 16px from the screen edge. It closes on Esc, on click-away and when the tray icon is clicked again.
- **Frame:** 380 wide. Its height fits the open panel, up to 640, and a taller panel scrolls inside. Flyout color, 1px stroke (`rgba(255,255,255,.08)` / `rgba(0,0,0,.12)`), 8px radius, flyout shadow.
- **Header:** Gumdrop at 24px, idle, beside "Chippi" (14 / 600) over a caption status line ("2 features on", "Keep awake is on"). There is no settings button in the header; Settings lives in the footer.
- **Tab strip:** one icon tab per installed feature that has a flyout panel, in sidebar order, left-aligned under the header with a hairline below.
  - Tabs are 40×36 with a 2px gap and a 16px glyph. Eight fit at 380 wide; any more go into a final "More" tab that lists the rest.
  - Selected: subtle-selected fill, the glyph in the accent, and a 14×3 accent bar 2px from the bottom.
  - Each tab's tooltip and accessible name is the feature's short name. The flyout reopens on the last tab used.
- **Panel:** 12px padding. Status comes first, the main actions are visible without scrolling, and a "More options" row (chevron right) opens that feature's Settings page.
- **Footer:** 48px of flyout-footer color with a hairline above. Settings (gear + label) on the left, Quit (power + label) on the right, both subtle 32px buttons.
- **Keep awake panel:**
  - Flyout section title "Keep awake".
  - One flyout card. The status row reads "Off" over "PC sleeps as usual", or "On · 1 hr left" over "Until 11:42 AM", with a switch on the right. The switch starts the default length.
  - Under it, a 3-column grid of 32px duration chips: 30 min · 1 hr · 2 hr · 4 hr · No limit · Until…. Clicking a chip starts that length and selects it (accent fill); clicking the selected chip stops. "Until…" opens a time picker and then reads "Until 6:00 PM".
  - Then a "Keep screen on" switch row and the "More options" row.
- **Tidy panel:**
  - A flyout card with the folder row: a 32px icon chip (accent glyph while watched, muted when Paused), the folder name as a picker (name + small chevron; it lists every folder with its mode, plus Add folder), and the mode line under it ("Silent · moves after 30s", "Manual · 3 to review", "Paused · not watching"). Under that, the full-width Silent / Manual / Paused segment.
  - Below the card, always visible: "Organize now" (accent, fills the row) and "Rules" (96px).
  - A "Recent" header with a "History ›" link on the right, then one flyout card of up to 4 rows. Held rows come first: a caution triangle, the file name, the reason in caution ("Held · a program named like a PDF") and a Review button. Moved rows: a muted file glyph, the name, "Documents · 23m", and an undo icon button. After undo the row reads "Restored to Downloads" and loses its button. Waiting rows: a clock glyph and "Waiting · reason", no action.
  - Empty state: "No folders yet" with the caption "New folders start in Manual", an accent "Add Downloads" and a standard "Choose folder…".
- **Empty flyout:** with nothing installed, the tab strip is hidden and the panel shows "No features on yet" with an accent "Add features" button that opens Settings › Add features.
- **Tray menu (right-click):** flyout styling, 8px radius, 36px items with a 16px glyph and a 13 / 400 label: Open settings, Quit. Its bottom edge sits at the click point, kept 12px off the taskbar.

### Island (movable, approved 2026-10-23)
Mockup: the "Movable island" row. A small dark tab docked on a screen edge. Hovering it opens the island. It never hides at rest, because hover-to-open is the reason to have it; it only hides while a fullscreen app is in front on its display.
- **Position:** the user drags the tab to the **Top** (the default, centered), **Left** or **Right** edge, and it snaps there on release.
  - It never docks on the taskbar's edge or in a corner. It stays at least 64px from the ends of a side edge, and 160px from the ends of the top edge, so it never covers window buttons.
  - Its edge and its place along that edge (0–1) are saved, so resolution changes don't move it.
  - Which display it lives on is still a setting: **Main display** (the default), **Follow active window**, or **One monitor** by name. Dragging it onto another display switches the setting to One monitor for that display. If that display disconnects, it falls back to the main display on the same edge.
- **Tab:** a half-oval with its flat side against the edge, in Island color with a 1px `rgba(255,255,255,.14)` stroke on every side but the flat one.
  - Side edges: 18×64 at rest, 26×84 when something is live or the pointer is on it. Top edge: 64×18 and 84×26. It grows around its center, so it never slides out from under the pointer.
  - Contents: Gumdrop's small form, 14px and sleepy at rest, 18px and idle when live or hovered. Beside it (top) or under it (sides) is one 12px live glyph in the island accent: a 3-bar equalizer for media, a progress ring for timers and agents, a folder for downloads and stashes, a dot for status activities such as Keep awake.
  - When several activities are live, the tab shows the highest: media, then timer, then download or stash, then agent, then status. On a stash Gumdrop celebrates; held and failed stashes show a caution dot.
- **Dragging:** pressing and moving 4px starts a drag and cancels hover-open. While dragging, a 44px bubble with Gumdrop follows the pointer, and a 3px accent line on the target edge shows where it will land. On release it slides into place over 180ms. A press without movement is a click and toggles the island.
- **Opening:** hovering opens it after 150ms. It closes 250ms after the pointer leaves both the tab and the island. Esc closes it. With the tab focused, Enter or Space toggles it.
- **The island:** 328 wide, opening beside the tab with an 8px gap toward the screen center: right of a left tab, left of a right tab, below a top tab. It is centered on the tab and kept 8px inside the work area.
  - 8px radius, Island color, 1px `rgba(255,255,255,.10)` stroke, island shadow, 12px padding, 8px gaps. It slides 12px in from the tab's side over 180ms, with no slide under reduced motion.
  - Contents, top to bottom:
    1. A header: Gumdrop at 22px, "Chippi" (13 / 600) and a clock.
    2. The live activity card. Media: a 40px art tile, title and source, previous / accent play-pause / next, and a 4px accent progress bar with times. Timer: a "Focus timer" caption, the time at 20px, and Stop.
    3. The installed features' island panels, in sidebar order. Keep awake: its status and four chips (30 min · 1 hr · 2 hr · No limit). Downloads: the folder, its mode line and Organize now.

    Inner cards are `rgba(255,255,255,.06)` with an 8px radius. Sections without data are left out. While Keep awake is on, the header pet is wide awake.
- **Colors:** the island always uses the dark accent shade and dark text tokens, whatever the app theme.

### Settings window
- **Frame:** 1120×720 by default and resizable, on the Windows 11 frame: system corners and shadow, so snap layouts, maximize and resize stay native.
- **Titlebar:** 40px in window color. Gumdrop's small form at 16px, "Chippi Settings" (12 / 400), and caption buttons 46×40. Close hovers to Critical with a white glyph.
- **Sidebar:** 272 wide on the window color, no divider.
  - A 34px search field, "Find a setting". Typing replaces the content with matching rows from every page, grouped by page; clicking one opens its page.
  - **Your features:** each installed feature with a Settings page, in catalog order, then **Add features**.
  - **Chippi:** General, Island & pet, Notifications, About. Shortcuts and Permissions join this group only once a feature uses them. Appearance is gone.
  - Group labels are Nav group type with 12px of space above. Items are 36px with a 16px glyph and a 14 / 400 label. Hover is subtle-hover. The selected item gets subtle-selected, Semibold and a 3×16 accent pill at its left edge.
- **Opening:** Settings opens on the page its caller asks for (the flyout's "More options" opens that feature; a notice opens its run in History). With no target it opens the first sidebar item. Sub-tabs open on their first tab.
- **Content:** padding 4 / 40 / 40 / 28. The page title (20 / 600 Display) sits 14px above the first section, with no description.
- **Page template:** title, then an optional SelectorBar, then sections. A section is a Section header (with an optional right-aligned button) over one card of rows. Two cards in a row of the same section sit 8px apart.
- **Rows:** at least 60px with a caption, 48px without; 10 / 16 padding; 14px gap. Label over caption on the left, the control on the right. A switch shows "On" or "Off" in body text just left of it. An expander row ends in a chevron and its sub-rows are indented 50px.
- **Downloads organizer:** a SelectorBar of Settings · Rules · History (§5 Downloads organizer).
- **Keep awake:** a card with the status row (accent coffee glyph, "Keep awake", caption "Off" or "On · no time limit", switch). Options: Default length (combo, "Used when you start it without picking one"), Keep screen on (switch), After a restart (value "Starts off", caption "Sessions don't carry over").
- **Add features** (replaces the Features hub): a search field with the "2 on" count beside it. **Available** lists the features that work, each a row with a 32px icon chip (accent glyph when on, muted when off), name, one-line description and a switch; Overlap badges sit after the name. One collapsed **Coming later** row ("39 features on the way") expands into one row per group listing the names. Presets are hidden until enough features exist for them to matter. Toggling a feature installs or uninstalls it, and an installed feature appears in the sidebar at once.
- **General:** Theme (Dark · Light · Follow Windows segment, default Follow Windows), Accent color (a "Windows" pill and four 24px swatches; the caption reads "Follows your Windows accent" or the color's name), Run at startup (switch). A Settings file section with Export… and Import… beside the path.
- **Island & pet:** Island (switch, "Shows what's live from a screen edge"), Position (Top · Left edge · Right edge combo plus a Reset button, captioned "Or drag the island to any screen edge"), Display (combo). A Gumdrop section: a Color row with a 48px Gumdrop preview, the color's name as caption and six swatches; Reduce motion (combo: Follow Windows · On · Off).
- **Notifications:** see below.
- **About:** Chippi with a 32px Gumdrop and "Version 0.1.0"; License (GPL-3.0-or-later, View); Privacy ("No telemetry. Your settings stay on this PC."); Third-party notices (View). Notices open as rendered text, never raw markdown.
- **Swatch selection:** a selected swatch or pill gets a 2px gap in the card color and a 2px accent ring.

### Downloads organizer
Behavior approved 2026-10-22 (`docs/phase-2/design/phase-2-mockups.html`); visuals restyled 2026-10-23. The Tidy panel is under Tray flyout above.
- **Settings tab:**
  - Watched folders, with Add folder on the header's right. Each folder is an expander row: folder glyph (accent while watched, muted when Paused), name over path, the mode as a value, and a chevron. Its sub-rows: Mode (Silent · Manual · Paused segment, caption "Moves files on its own", "Waits for your review" or "Not watching"), Wait before moving (combo, "Gives downloads time to finish"), Ignore list (Edit, "Files and patterns to leave alone") and Stop watching this folder (Remove).
  - Options: Keep history (combo), Organize in Explorer (switch, "Adds Organize to the right-click menu"), Import archive (Choose file…, "zip, tgz or tar.gz").
- **Rules tab:**
  - A card holding the test field: a search glyph, "Test a file name, like notes.txt", and the result on the right ("Txt Files → Documents" in Secondary, or "No rule matches · stays in Downloads" in Caption). It runs the whole rule set.
  - "Rules" with a "First match wins" caption, and Add rule on the right.
  - One card of rule rows: drag grip, number, name over a summary (".txt → Documents"), switch, and a chevron that opens the rule. One rule is open at a time. Edits save when valid, with no Save button. Drag, or Alt+Up/Down, reorders; the drop target is a 2px accent line.
  - Regex and templates use the code font. Placeholder tokens are 22px chips in control fill, insertable by click. Validation messages are caution text under the field.
- **History tab:**
  - A toolbar: search field, All folders combo, Clear history.
  - Grouped by day under Section headers. One card per run: a header row ("Silent · Downloads" over "10:07 AM · 1 moved") with Undo run, then indented file rows (name over "Downloads › Documents") with an undo icon button. Runs over 10 files collapse.
  - Undo per row and per run goes through an inline confirm bar ("Undo 4"). Clear history uses a popover with a Critical "Clear" button.
- **Preview** is a view inside Settings with a back button, not a dialog.
  - Each row has a checkbox, the file name with its size, the rule and the destination. Warnings are a caution line under the name, and a move to another drive gets a neutral "Other drive" badge.
  - Held rows start unchecked. Files no rule matches show a disabled checkbox. The header checkbox shows mixed when some rows are selected.
  - The footer shows the selected count and total size, with Cancel and Approve (accent).
  - After apply: a success check and Undo on each moved row, a caution failure line on each failed one, then "Preview again" and "Done".
- **Notices:** event kinds are Organized, Ready to review, Held, Failed, Archive imported. Which events reach Windows notifications and the island is set on the app-wide Notifications page.

### Notifications page
Behavior approved 2026-10-22 (`docs/phase-2/design/notifications/`); visuals restyled 2026-10-23.
- **Destinations:** one card with two switch rows, Windows notifications and Island.
- **Turning a destination off** keeps its choices. Its caption reads "Off · choices kept", and that checkbox column dims to 60% in every group while staying editable.
- **When the Windows backend is unavailable,** one line sits under the card: a 7px caution dot and "Windows notifications unavailable" in caution caption. No button, and no claims about Focus or Do not disturb.
- **Feature groups** follow the sidebar order. Each is a Section header with "Windows" and "Island" caption labels over 96px columns, then a card of 48px rows with one 20px checkbox centered in each column. A feature with no events gets no group.
- **Downloads defaults:** Organized and Ready to review: Island only. Held and Failed: both. Archive imported: Windows only.
- **Accessible names** read "Feature, Event, Destination", for example "Downloads organizer, Held, Windows".
- **Quiet hours and sound** stay with Windows. Chippi has no setting for them.
- **Copy and clicks:** a notification states what happened first ("4 files organized", "1 file held", "1 file not moved", "photos-2025.zip imported"), one per run.
  - Organized and Archive imported: clicking opens that run in History. The buttons are Undo (opens the run's inline Undo confirmation, never undoes on its own; only when something is undoable) and History.
  - Failed: clicking opens the run in History; the button is History.
  - Held and Ready to review: clicking, or the Review button, opens the review.
- **Island tab on a stash:** Gumdrop celebrates, then goes idle, and the folder glyph shows. Held and failed stashes add a caution dot.

## 6. Components

Every interactive control is custom-templated with the full state set: rest, hover, pressed, keyboard focus and disabled. Focus is a 2px outline in the Text color at 1px offset, and it is non-negotiable. Disabled is 45% opacity with no hover.

- **Button:** 32px, 12px side padding, 4px radius, body label (13px in the flyout). Control fill with a 1px control stroke; hover control-hover; pressed control-pressed with secondary text.
- **Accent button:** the same shape in the accent with on-accent text; hover at 90% opacity. One per view.
- **Critical button:** Critical fill, white text. Only inside a confirm popover.
- **Icon button:** 32×32, 4px radius, transparent, a 16px glyph in Secondary. Hover: control fill and Text. Always has an accessible name.
- **Subtle button:** the flyout footer and link buttons. Transparent until hover.
- **Switch:** 40×20, 10px radius. Off: transparent with a 1px switch-off stroke and a 12px switch-off knob, 4px from the left. On: accent fill and stroke with a 14px on-accent knob at 22px. The knob slides in 150ms. Settings rows show On/Off text to its left; flyout rows don't.
- **Segmented control:** a 2px-padded trough (segment-trough fill, segment stroke, 6px radius). Items are 28px with 13px labels in Secondary, 4px radius; the selected item gets segment-selected fill and Text (plus the light-theme shadow). In Settings items size to their labels with 14px padding; in the flyout they split the width equally.
- **SelectorBar:** text tabs under a page title, 40px tall, 12px padding, body text in Secondary. The selected tab is Text, Semibold, with a 16×3 accent bar 3px from the bottom.
- **Nav item:** see Settings window. The pill is 3×16, 2px radius.
- **Flyout tab:** see Tray flyout.
- **Text field and search field:** 32–34px, 10px padding, 4px radius, input fill, 1px stroke with a stronger bottom stroke (input-stroke-bottom). Focus: input-focus fill and a bottom stroke in Text. Placeholders are Muted. Search fields lead with a 16px search glyph.
- **Combo box:** a Button with its value on the left and a chevron on the right, at least 150 wide.
- **Checkbox:** 20px, 4px radius. Off: a 1px switch-off stroke on `rgba(0,0,0,.1)` (light `#FFFFFF`). On and mixed: accent fill and stroke with an on-accent glyph (2.2 stroke). Use it for multi-select lists and grids; a single setting uses a switch.
- **Card:** card fill, 1px card stroke, 8px radius, clipping its rows. Rows inside are divided by 1px dividers; the first row has none. Never a card inside a card.
- **Flyout card:** flyout-card fill and stroke, 8px radius. Rows are 12 / 16 padded; divided rows are 48px; file rows are 52px.
- **Expander row:** a row that is a button, with a chevron down (closed) or up (open) at its right. Sub-rows sit in the same card, indented 50px.
- **Icon chip:** 32×32, 6px radius, control fill, a 16px glyph in the accent when on and Muted when off. For feature rows, the folder row and the Tidy card.
- **Duration chip:** 32px, control fill and stroke, 4px radius, 13px label with tabular figures, in a 3-column grid with 8px gaps. Selected: accent fill and stroke, on-accent text.
- **Swatch:** a 24px circle. The "Windows" accent choice is a 24px pill with a 12px label. Selection: §5 Settings window.
- **Badges:** 18px, 6px side padding, 4px radius, 11 / 600.
  - **Overlap:** caution text on caution tint, for example "Overlaps PowerToys", for features Windows or PowerToys already cover.
  - **Neutral:** Secondary text on control fill, for example "Other drive" or "Needs VB-CABLE".
- **Status dot:** 7px. Success for active, caution for attention, Muted for offline. No glow.
- **Drag handle:** a 16px six-dot grip in Muted. Alt+Up/Down moves the row.
- **Icons:** Lucide on a 24px grid at 1.6 stroke, round caps and joins, drawn at 16px (20px for a Settings row lead glyph). Each feature has its own glyph; no glyph is reused for two features. Brand logos are never redrawn: an app shows its real icon, or a monogram tile when no icon is available.

## 7. The pet: Gumdrop

### Character
Gumdrop is a rounded drop of the Synth gradient with two dark eyes, a white shine and a tiny mouth. It has no limbs, no accessories and no text. It is calm by default, quietly pleased when Chippi finishes something for you, and asleep when nothing is happening. It never interrupts, nags or needs feeding. It is a status light with a face.

It is deliberately different from vorssaint's companion: a drop silhouette rather than a ball, a gradient rather than flat pastels, a shine highlight, and a mouth that changes with mood. Keep those four traits, and never reduce it to a plain colored ball with two dots.

### Geometry
Drawn on a 120×120 viewBox. These are SVG path strings, and WPF `Path.Data` accepts the same syntax.

| Part | Spec |
|---|---|
| Body | `M60 16 C80 30 100 52 100 77 C100 98 82 108 60 108 C38 108 20 98 20 77 C20 52 40 30 60 16 Z`, filled with the Synth gradient at 135° (WPF `StartPoint="0,0" EndPoint="1,1"`) |
| Shine | Ellipse centered (46, 46), radii 9 × 5.5, rotated −28°, white at 38% |
| Eyes (idle) | Ellipses at (47, 74) and (73, 74), radii 4.8 × 6.2, pet ink `#0B0F1F`, each with a white highlight (r 1.7) at (+1.7, −2.3) |
| Mouth (idle) | `M56 88 Q60 91.5 64 88`, a 2.8 ink stroke with round caps |
| Ground shadow | Ellipse (60, 113), 30 × 4, black at 35%, at sizes of 64px and up only |

### Moods

| Mood | Eyes | Mouth | Extras | When |
|---|---|---|---|---|
| **Idle** | Dots, as in the geometry table | Small smile | None | The default when the island shows activity, in the flyout header and in Settings |
| **Celebrating** | Happy arcs `M42 75 Q47 68 52 75 M68 75 Q73 68 78 75` (4.2 stroke) | Open smile `M54 87 Q60 96 66 87 Z` (filled ink) | Blush ellipses at (38, 85) and (82, 85), 5.5 × 3.2, `#FF8FB0` at 70%. Spark strokes `M14 30 L6 24 M12 44 H3 M24 18 L20 9 M106 30 L114 24 M108 44 H117 M96 18 L100 9` (3.5, `#F5C451`). The body squishes to scale (1.06, 0.94) from the bottom center (60, 108) | The organizer stashed files, a timer finished, an agent task finished, a download completed. Holds about 2.5s, then returns to idle |
| **Sleepy** | Closed arcs `M42 74 Q47 78.5 52 74 M68 74 Q73 78.5 78 74` | Flat line `M57 89 H63` | A "z" in 20px Segoe UI Variable Display Bold at (94, 30) and another in 13px at (106, 15), in Muted | The island tab at rest, with nothing live |
| **Wide awake** | Larger dots, radii 6.4 × 7.4, highlights r 2.4 | Small "o", an ellipse at (60, 89), 3 × 3.6, filled ink | None | While Keep awake is holding the PC up |

### Sizes
- **Small form, for the tray icon (16–20px) and the Settings titlebar (16px):** body plus eyes only, with eyes enlarged to 6 × 7.4. Drop the shine, mouth, highlights and extras, which are illegible at that size.
- **Island tab:** 14–18px in the small form (sleepy uses closed arcs instead of dots). **Island header and flyout header:** 22–24px, full detail. **Settings › Island & pet:** 48px beside the color swatches. **About:** 32px.

### Color option
Synth is the default and the brand. Settings › Island & pet can switch the body to a solid **Iris** `#CBB2FF`, **Azure** `#8EC0FF`, **Signal** `#8EE6C0`, **Ember** `#FFC58F` or **Frost** `#E8ECF6`. The eyes and mouth stay pet ink on every color, which keeps contrast at 5:1 or better. In the light theme the Frost swatch gets a 1px `rgba(0,0,0,.15)` stroke so it doesn't vanish.

### Motion
- **Blink:** every 4–7s at random, the eyes scale to 10% height for 120ms. This is the only ambient loop in the whole app.
- **Mood change:** eyes and mouth cross-fade in 150ms. Celebrating springs into the squish and back (about 400ms) while the sparks fade out over 1.2s.
- **Falling asleep:** the eyes ease shut over 600ms before the z's fade in.
- **Reduced motion** (Windows "Animation effects" off, or the setting): no blink and no squish. Moods swap instantly with no tween.

### Accessibility
In the island and flyout header, the pet is decorative (`AutomationProperties.AccessibilityView="Raw"`), because the text beside it carries the state. Where it stands alone, as in the Settings preview, it gets a name such as "Gumdrop pet, idle". It must never be the only signal of anything.

## 8. Do's and Don'ts

### Do
- **Do** act in the flyout and configure in Settings. If a common action needs Settings, move it to the feature's panel.
- **Do** keep each section's rows in one card with dividers: one shape per section, not per row.
- **Do** follow the Accent Rule, the Weight Rule and the Tabular Rule.
- **Do** check every screen in dark and light, and with at least one non-blue accent.
- **Do** give every custom control a visible focus ring and an accessible name.
- **Do** keep status visible at rest: folder mode, held files and Keep awake time show without hovering.
- **Do** flag overlaps with Windows or PowerToys with an Overlap badge rather than hiding the feature.
- **Do** list only features that work. Coming soon stays folded away.

### Don't
- **Don't** use gradients anywhere but Gumdrop, and never gradient text.
- **Don't** use Bold, colored glows, glassmorphism or hero metrics.
- **Don't** put a description under a page title, or a sentence where a label will do.
- **Don't** nest cards, or give each row its own card.
- **Don't** ship stock WPF chrome: no default checkboxes, group boxes or gray dialogs.
- **Don't** use a colored side stripe (a left or right border wider than 1px) to mark a row or card. The nav pill is the one exception.
- **Don't** animate anything for its own sake. Only switches, segment and tab changes, the island and the pet move. The pet's blink and the island's live equalizer are the only loops.
- **Don't** use emoji anywhere in the UI.
- **Don't** redraw other companies' logos. Use the real app icon or a monogram.
- **Don't** copy vorssaint's screens, icon or assets.
- **Don't** let the pet talk, show speech bubbles, or block content.

## 9. Implementation notes (WPF)

- **Variable fonts:** WPF draws only a variable font's default instance and can't select weights from it. That is why Manrope once rendered ExtraLight, and Segoe UI Variable is a variable font. Check that 400 and 600 render as true weights before building on it. If they don't, use static **Segoe UI** (Regular `segoeui.ttf`, Semibold `seguisb.ttf`, present on every Windows 11 install) for both roles. The design holds either way.
- **Tabular figures:** `Typography.NumeralAlignment="Tabular"` on any element showing numbers, or on the base text style.
- **Accent:** read the Windows accent shades with `Windows.UI.ViewManagement.UISettings.GetColorValue` (`AccentLight2` for dark, `AccentDark1` for light) and listen to `ColorValuesChanged`, which fires off the UI thread. Swap one `AccentBrush` and one `OnAccentBrush` when the accent setting, the theme or the Windows accent changes.
- **Theme:** Follow Windows is the default and reads `AppsUseLightTheme`. Both themes define every key; the island keeps its own dark keys.
- **Pet control:** a `UserControl` with a `Mood` dependency property, the size forms above and one `Path` per part. Moods switch through visual states.
- **Island window:** borderless, `AllowsTransparency`, topmost, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, so it never steals focus or shows in Alt+Tab. It sits on the display the island display setting picks and hides while a fullscreen app or game is in front on that display. Because it never activates, dragging uses mouse capture and `SetWindowPos`, not `DragMove`. Keep the window sized to the tab while closed, so the rest of the edge stays clickable.
- **Flyout window:** borderless and topmost, placed from `Shell_NotifyIconGetRect`. It hides when it loses activation.
- **Reduced motion:** read `SystemParameters.ClientAreaAnimation` and listen for changes.
