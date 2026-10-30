---
name: Chippi
description: A Windows tray suite that looks like it shipped with Windows 11 — neutral layers, Segoe, the user's accent — with one small blob that lives in a widget on the desktop.
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
  island: "#F528282C"
  island-accent: "#B79CFF"
  island-accent-fill: "linear-gradient(135deg, #B78FFF, #82AEFF)"
  island-on-accent: "#170F2E"
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
  island: "#F5F5F5F6"
  island-accent: "#7444F0"
  island-accent-fill: "linear-gradient(135deg, #8F5CFF, #5B86FF)"
  island-on-accent: "#FFFFFF"
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
  synth: "#B78FFF"
  tongue: "#FF7FA8"
  headphones: "#2A2440"
  headphone-pads: "#D2BEFF"
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
  puck: "50px circle"
  chip: "8px"
  island: "8px"
  island-card: "6px"
spacing:
  flyout-padding: "12px"
  card-row: "12px 16px"
  settings-row: "10px 16px"
  settings-row-min: "60px with a caption, 48px without"
  sub-row-indent: "50px"
  section-gap: "24px above a section header, 8px below"
  group-gap: "8px"
  settings-content: "40px 4px 40px 28px (left, top, right, bottom)"
  settings-content-max: "1000px, aligned left"
  nav-width: "272px"
  nav-row: "32px, 36px apart"
  nav-search: "32px, 12px below"
  nav-rail: "48px, below a 1008px window"
---

# Design System: Chippi

Approved mockup (2026-10-23): [Chippi Native Look](https://claude.ai/artifact/5M1W8XSCiDPvCpc5Hrd2Bq), with the Settings window and the Keep awake and Tidy flyouts in dark and light. It is the reference for every pixel below. The audit that led to it is [docs/phase-1-2-ui-ux-audit.md](docs/phase-1-2-ui-ux-audit.md).

This replaces Direction A "Studio Rack" (navy chassis, Manrope, the violet→azure Synth Gradient, JetBrains Mono readouts), approved 2026-10-21 and retired 2026-10-23 because it read as heavy and unpolished next to vorssaint. The [design canvas](https://claude.ai/artifact/CMBT4oFV3oGZRUei1d7K17) keeps Studio Rack for history only.

## 1. Overview

**Creative North Star: "Native Windows 11, with a resident."**

Chippi should look like it shipped with Windows 11. It uses Windows' own font, its neutral layered surfaces, its control shapes and the user's accent color, so it sits next to Settings, Quick Settings and File Explorer without looking like a guest. vorssaint looks polished because it looks native to macOS; Chippi gets there by looking native to Windows.

The personality comes from one place: **Chippi**, a soft blob with a face who lives in the widget, a puck the user places anywhere on the desktop. It naps when nothing is happening, listens along with your music and reacts when Chippi does something for you. Everything else stays quiet.

**Key characteristics**
- Neutral gray layers: window, card, control. No tinted chassis.
- One flat accent, the user's Windows accent by default, used only for what is on, selected or primary.
- Segoe at Regular for everything you read. Semibold only marks structure.
- Rows grouped in one card per section, divided by hairlines.
- The island's pages are where you act. Settings is where you configure.
- Dark and light are equal. Follow Windows is the default.

**Boundaries.** vorssaint-utils is the feature reference, not a visual one: its name, icon, trade dress and branding are covered by its trademark policy. Chippi follows Windows 11 conventions, keeps its own name, icon and pet, and never copies vorssaint's screens or assets.

## 2. Colors

### Surfaces
Dark: Window `#202020` · Card `#2B2B2B` · Flyout `#2C2C2C` · Flyout footer `#202020`. Light: Window `#F3F3F3` · Card `#FBFBFB` · Flyout `#F3F3F3` · Flyout cards `#FFFFFF` · Flyout footer `#EBEBEB`. Controls, hovers, strokes and dividers are white (dark) or black (light) at low alpha; the exact values are in the front matter.

**Island** (revised 2026-10-29): the widget and its panel follow the app theme on a solid surface, `#28282C` in dark and `#F5F5F6` in light, both at 96% (Windows shows its own acrylic this way on this PC, with transparency effects off). Its accent is the pet's violet, not the Accent setting: `#B79CFF` dark, `#7444F0` light, and what's on or primary takes the accent fill, a violet-to-blue gradient (`#B78FFF` → `#82AEFF` dark with `#170F2E` text, `#8F5CFF` → `#5B86FF` light with white text). The exact keys are the `Island*` brushes in `Dark.xaml` and `Light.xaml`.

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

**The Gradient Rule** (revised 2026-10-29). Two gradients exist: the island's accent fill, on what's on or primary in the island, and the pet's soft shading. Nothing else is a gradient.

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

**The Weight Rule.** Regular (400) for everything you read. Semibold (600) only for page titles, section headers, island tile names, the selected nav item, the selected SelectorBar tab and the flyout's name. Never Bold or heavier. Most of the clean look comes from this rule.

**The Tabular Rule.** Every number (times, counts, sizes, durations, versions) uses the UI font with tabular figures, so digits line up and don't shift as they change. Numbers never switch to a monospace font.

Copy is sentence case, plain and short. State comes first and detail second: "On · 1 hr left", "Auto · moves after 30s", "Held · a program named like a PDF". No exclamation marks, no emoji.

**The Bare Label Rule** (Travis, 2026-10-22). A label names the action and nothing else. Never repeat the app's name where its icon already says it, and never pad a label with explanation. The Explorer entry is "Organize", not "Organize with Chippi". When a label seems to need a sentence, the design is wrong; fix the design, not the copy. This is a technical tool (Travis, 2026-10-24): **buttons name only the verb** ("Undo", not "Undo run" or "Undo 4"; "Refresh", not "Preview again"), and **a confirmation is the question plus Cancel and the action** ("Clear history?" · Cancel · Clear), with no line explaining what happens. Statuses, error reasons and section labels stay, because they say something you can't see ("Failed · in use").

**Setting labels** (Travis, 2026-10-25). The Bare Label Rule covers every setting too. A setting's label is a noun phrase in Windows' own terms ("Communications device", "Sound feedback", "Input device"), never a sentence or an instruction. No "my", "your", "it" or "too". No row that only explains fixed behavior ("New devices · Add to the end"): if the user can't change it, it isn't a row. Tooltips follow the same rule and never explain.

**No page intros** (2026-10-23). A Settings page title has no description under it. A row caption shows state or data (a path, a count, the chosen accent), never an explanation of the row.

## 4. Elevation

Flat, like Windows 11. Depth comes from the surface steps (window → card → control), not shadows.

- **Settings window:** the system frame draws the corners and shadow.
- **Flyout:** `0 8px 32px rgba(0,0,0,.28)` (light `rgba(0,0,0,.14)`), the shadow Windows uses on its own flyouts.
- **Island (revised 2026-10-29):** the panel: blur 40, 16 down, black at 50% (dark) or 20% (light). The puck: blur 14, 4 down, 18%; the chip the same at 16%; the lifted puck blur 30, 14 down, 28%. Now Playing's play disc: blur 16, 6 down, 35%.
- **Selected segment, light theme only:** `0 1px 2px rgba(0,0,0,.12)`.

**The No-Glow Rule.** Nothing glows: no colored shadows under chips, thumbs, cards or buttons.

## 5. Surfaces

### Tray icon (surfaces model, approved 2026-10-26; revised 2026-10-28)
Mockup: [Chippi Surfaces](https://claude.ai/artifact/RJAX7v2QT6MLpPRgZQVXeS). Chippi has two surfaces, the island and the Settings window (Travis, 2026-10-28). There is no tray flyout and no docked panel.
- **Left click** (or Enter on the focused icon) opens Settings on the last page used, whether or not the island shows (revised 2026-10-28). With the island off, or hidden for a fullscreen app, that's the way in.
- **Right click:** the tray menu, in flyout colors: 8px radius, 36px items with a 16px glyph and a 13 / 400 label: Open settings, Quit. Its bottom edge sits at the click point, kept 12px off the taskbar.
- **Hide tray icon** (Settings › Island & pet, off by default): the icon leaves the notification area while the island shows, so the island is the only door. It comes back while the island is off or hidden for a fullscreen app. Right-clicking the widget opens the same menu (Open settings, Quit), so Quit is always reachable.
- **A second launch** (Chippi started again from Start) opens the island and gives it the keyboard; with the island off or hidden, it opens Settings. A notice click opens its island page the same way, or Settings (revised 2026-10-28).

### Island pages (approved 2026-10-26)
- **Every page** but Now Playing starts with a 24px header row, 8 above the body (revised 2026-10-28): the tile's name (13 / 600) and, only where the page's controls don't already show it, the tile's status on the right in Caption (Keep awake's "1 hr left"). Now Playing keeps its own header, with the app playing on the right.
- **Now Playing (revised 2026-10-29, after Apple Music's widget):** the header names the app playing on the right in Caption. Only when two or more apps have sessions does it get a chevron and open a menu: each app over its track, the one in use semibold, the one playing marked with the accent Audio lines glyph. A pick holds until another app starts playing or the picked one goes. The body centers a 168 × 168 artwork row: 16 to its right the title (20 / 600, up to 2 lines) and the artist (13, tertiary, 1 line, never the app's name), with previous / play-pause / next as solid 28 / 36 / 28 glyphs spread under them in 48 / 52 / 48 round buttons. The column is optically level with the art: the title's capitals on its top edge, the solid glyphs' bottoms on its bottom edge. A cover has a 1px hairline (IslandBorderBrush), so a dark one keeps its edge. The progress runs the full width 22 below, elapsed and remaining at its ends, its fill tertiary while paused, its space kept when there's no timeline. A cut-off title or artist shows in full in a tooltip. No artwork shows Chippi (listening, 76) on the panel fill in the art's place; nothing playing shows Chippi asleep (72) over "Nothing playing", centered, with no controls. No volume or output picker.
- **Sound** (Audio routing, Mic mute shortcut and Mic processing share it; revised 2026-10-28): two columns 244 wide and 8 apart, both from the top, each under a 12 / 600 tertiary label. Every card is one 44 row: a 16 glyph, the middle, then the control; the cards' text buttons share MinWidth 64.
  - **Output:** a card with the speaker glyph (Caution VolumeMuted while muted), the system volume slider (IslandSliderStyle: 4 track, 16 thumb) and its percentage (12, tabular, 34 wide; "Muted" in Caution while muted), and Mute / Unmute. 8 below, a one-line radio row (30, radio 14 under the card's glyph) per *connected* output; the one in use is filled and highlighted. Picking one uses it at once and moves it to the top of its priority list, whether or not priority switching is on. With no output: the glyph and "No output" in tertiary, no slider, Mute disabled, no rows.
  - **Microphone:** a card with the mic glyph (Caution mic-off while muted), "Live" or "Muted" in Caution, and Mute / Unmute. Then a one-line radio row per connected mic, then the Mic processing card (AudioWaveform in the accent, "Mic processing", while it runs a 4 tall meter of the processed mic, then the switch, 34 × 18). A warning shows below it only when Mic processing failed or stopped. With no microphone: mic-off and "No microphone" in tertiary, Mute disabled, no rows; the Mic processing card stays.
  - **Fit:** the Output column shows 7 rows and the Microphone column 5, one fewer for each warning line showing in that column, so no row is cut off. The rest are in Settings › Output and Settings › Microphone.
  - The tile's status is the output in use, "Mic muted" (Caution, with the mic-off glyph and dot) while muted, or "Mic stopped" (Caution, with the dot) when Mic processing stopped.
- **App volume (revised 2026-10-28):** centered vertical app strips, 8 apart, filling the 304 body height. Up to 7 apps use 56-wide strips with a 32 bar, 20 app icon (also Mute), percentage, two-line name and output menu. At 8 to 12 use 34 strips and 24 bars without names; at 13 or 14 use 28 strips without percentages. Past 14, active apps come first; the rest are in Settings. Use the program file icon, System sounds Volume glyph or two-letter name tile. Muted replaces the icon with Caution VolumeMuted, the bar with white 28%, and the percentage with Caution "Muted". Empty centers Sliders (20) and "No apps playing". The system volume stays on Sound.
- **Keep awake (revised 2026-10-28):** six cards in 3 × 2, 104 tall, with 8 gaps and 14 labels, centered vertically. Choices remain 30 min · 1 hr · 2 hr · 4 hr · No limit · Until…. The running choice takes the accent fill with on-accent text, semibold; hovering it says "Stop". Until… opens the existing time picker. The header keeps the remaining time.
- **File organizer (revised 2026-10-28):** one 44-high control card with a bare accent folder glyph, folder picker, then Auto / Manual / Paused at the right. Recent files use whole 30-high rows from the top, with existing Review/Undo actions. Empty centers File (20) and "No recent files". History › and Organize now sit at the bottom; History starts 10 in. Overflow stays in History. Auto is a display label; the stored mode remains `silent`.
- Quick pages show live state and controls only: no captions that restate a setting, and no unplugged devices (Settings lists those).

### Island (the widget and its panel, approved 2026-10-29)
Brief: [docs/shell-redesign-handoff.md](docs/shell-redesign-handoff.md). Mock: [final.html](docs/shell-redesign/design/final.html), shots [final-light.png](docs/shell-redesign/design/shots/final-light.png) and [final-dark.png](docs/shell-redesign/design/shots/final-dark.png). This replaces the movable tab, the tile grid and the round (orbit) buttons of 2026-10-25. The island is the widget and the panel it opens. It follows the app theme, and its accent is the pet's violet (§2 Island).

- **The widget:** a 50px round puck with Chippi (§7) at 36, Island fill, a 1px Island border and a soft shadow (blur 14, 4 down, black at 18%). It sits anywhere on any display and never hides at rest, except while a fullscreen app is in front on its display.
  - **Where it starts:** 24 in from the bottom-right corner of the main display's work area, for new and existing installs.
  - **Place:** saved as two fractions (0 to 1) of the room the puck has in its display's work area, so a resolution or taskbar change moves it with the work area and never off screen. Position â€º Reset puts it back at the start.
  - **Display** (Settings › Island & pet): Main display (default) or One monitor by name. Dropping the widget on another display picks that one monitor. While the chosen monitor is disconnected, the widget sits at the start on the main display. Follow active window is gone: a widget you place yourself doesn't jump between screens (a saved one reads as Main display).
- **Chippi's mood on the puck:** asleep at rest, idle while hovered or open, wide awake while Keep awake runs, idle for any other activity, listening (headphones) while a song plays, and celebrating when Chippi finishes something for you.
- **The chip:** 36 tall, 8 corners, Island fill and border, a lighter shadow (blur 14, 4 down, 16%), 6 from the puck on the side toward the screen's middle, centered on it. It shows the first that applies:
  - a drag from another app over the puck: the target tile's glyph and name;
  - Plain paste feedback for 6 seconds: ClipboardType, accent or Caution (§5 Clipboard and text);
  - a song playing: a 24 cover (4 corners; a Music glyph without art), the title in 12 Text and " · artist" in tertiary, then the equalizer: three 3-wide bars in the accent fill that follow the default output's real level, resting at 8, 14 and 10;
  - the highest activity: its tile's glyph (or the progress ring, a folder or a dot) and its status, such as "1 hr left". The glyph turns Caution while the activity needs attention.
  - The line is at most 150 wide. Longer, it scrolls 30 DIPs a second after a 2-second hold, a copy 40 behind so it loops, fading over 8 at its ends; it holds under reduced motion and stops while the panel is open. With nothing to show there's no chip.
  - Clicking the chip opens its page: the song's opens Now Playing, an activity's its feature's page. It replaces the live round button.
- **Muted mic:** a red badge on the puck's lower right: an 18 `#C42B1C` circle with a white 11 MicOff, ringed 2 in the Island fill. It shows whatever else is live, and the puck's help text says "Microphone muted".
- **Hover:** the puck grows 4px (50 to 54) over 120ms ease-out and Chippi wakes. Hover never opens the panel.
- **Clicks:** a click opens the panel on the last page used, or home if none is remembered or its feature is gone; another click, Esc or a click outside closes it. Outside clicks still reach the app underneath. Right click opens the tray menu (Open settings, Quit) above the puck, so Quit stays reachable with the tray icon hidden.
- **Dragging:** a press that moves 4px starts a drag and closes the panel at once. The puck lifts (scale 1.12, tilted −6°, a deeper shadow: blur 30, 14 down, 28%), the chip goes, and it follows the pointer onto any display. On release it slides the least distance that keeps it whole inside that display's work area (180ms, ease-out), then the place is saved. The chip comes back on the side toward the middle.
- **Shelf drop target:** a drag from another app held 250ms over the puck opens the Shelf without taking focus; leaving for 500ms closes it again.
- **Reduced motion:** hover, lift and the drop's slide snap; the song holds and the bars rest.
- **The panel:** 520 × 360 with 8 corners, Island fill, a 1px Island border and the panel shadow (§4). It opens beside the puck toward the screen's middle (left or right and up or down by the puck's center), overlapping the puck's far side by 20, and stays 8 inside the work area. The puck stays above it.
  - **Morph:** the shape grows out of the puck (from its circle to the panel, corners 25 to 8) over 220ms ease-out, the content fading in over the second half. Closing shrinks it back into the puck over 160ms, the content fading out over the first half. Page to page the shape doesn't change: the old page goes at once and the new one fades in over 150ms. A drag or a fullscreen app closes it at once. Reduced motion snaps every transition.
  - **Known cost (2026-10-29):** at 165 Hz the open skips about 2 frames (up to 11 on the first open after start), the close about 6 and a page switch about 3; the panel's shadow is 1 or 2 of them. Travis kept this open over a cheaper fade and scale (task 167).
  - **Keyboard:** only Esc is watched before the user clicks into the panel. After that click, Tab, arrows and Enter move through its controls; no global hook takes those keys from the app in front. A page may use Esc first (clear a search, leave Clean URL); otherwise Esc closes the panel.
- **Home** (like Windows Quick Settings), top to bottom in the 496 wide body:
  - **Now Playing card:** 68 tall: the art at 52 (4 corners; without art Chippi at 28, listening while a track plays, asleep otherwise), the app in 11 tertiary, the title in 14 / 600 and the artist in 12 secondary ("Nothing playing" with no track), then previous and next (32, glyph 14) around a 36 play disc in the accent fill. Clicking the card opens Now Playing.
  - **Feature buttons:** up to 8, in 4 columns and 2 rows, in Settings › Island & pet's order and Show choices; fewer features means fewer buttons, and nothing moves. Each is a 44 tall button with a 16 glyph (6 corners, button fill, a 1px control border), with the name in 12 and the status in 11 tertiary under the button. A cut-off name or status shows in full in a tooltip.
  - **Toggle buttons** split in two: the main part toggles, and a 28 wide arrow (ChevronRight 13, named "Open {feature}") opens the page. On, the button takes the accent fill with an on-accent glyph; the name and status under it keep their colors. Keep awake starts the last length picked on its page (1 hr the first time, kept across restarts) and stops when on; Microphone mutes and unmutes.
  - **Attention:** a muted mic or a feature that needs attention turns its glyph (MicOff for the mic) and status Caution. There are no live dots.
  - **Volume:** shown only with Sound installed: a 16 speaker, the system volume slider, its percentage (12, tabular) and an arrow that opens Sound.
  - **Footer:** 46 tall across the panel's full width, a hairline above it and a faint fill: Chippi at 20 and "Chippi" in 12 on the left, a 32 Settings button on the right. Settings closes the panel, then opens Settings on its last page.
- **Pages:** the body is 496 × 304, as before; see Island pages above. Every page starts with the shell header: a Back arrow (ChevronLeft 16, an icon button named "Back") to home, then the page's own header row. The page header and the fit rules below are unchanged.
  - **Fit rules (revised 2026-10-28):** nothing on the island scrolls except the text inside a Scratchpad note. Pages adapt to fit the body: when content grows, a page adds columns, shortens rows, narrows strips and shrinks tiles. Lists show whole rows only and leave out a row that doesn't fully fit. Lists start at the top; content that never grows (Keep awake's choices, empty states) sits in the vertical middle. Content never grows the island, and no page has a ScrollViewer. Whatever still can't fit is in that feature's Settings page, which may hold information for this, not just configuration.
  - **Compact sizes (revised 2026-10-29):** text 12, captions and secondary lines 11, page titles 13 / 600. One 8 gap between every card, tile and column, and below the header. Header row 24. Text buttons 28 tall, 10 padding, 12 text, at least 64 wide. Icon buttons 28 × 24 with a 14 glyph. Glyphs are 12, 14, 16 or 20, all drawn with the same 1.25px line. Cards, tiles and expanded entries have 6 corners; controls (buttons, fields, search) 4. Content inside every row and card starts 10 in. Control cards 44 tall with 10 padding; list rows 30, one line; table rows 28; switches 34 × 18; slider thumbs 18 with a ring. These are shared island resources in `IslandStyles.xaml`.
- **Pages restyled to the mock (2026-10-29):** the accent fill marks what's on or primary (Now Playing's play disc, a running Keep awake card, the selected segment, App volume's bars); Clipboard's open entry sits on the accent tint; Scratchpad's selected tab gets a 3px accent bar over a hairline under the tabs; Shelf's tiles have no fill until selected, with an accent pin; System monitor's rows are divided by hairlines with none above the first.

### Settings window
- **Frame (revised 2026-10-28):** 1120×720 by default, centered on the main display, and resizable down to 860 wide, on the Windows 11 frame: system corners and shadow, so snap layouts, maximize and resize stay native. Pointing at maximize shows Windows' snap layouts. It reopens at the size, place and maximized state it closed in; if that place is on no monitor now, it centers on the main display. Esc does nothing (it still clears the search box).
- **Titlebar:** 40px in window color with no line under it. Chippi's small form at 16px, "Chippi Settings" (12 / 400), and caption buttons 46×40. Close hovers to Critical with a white glyph.
- **Sidebar (revised 2026-10-28):** 272 wide on the window color, no divider. One list for every group: one scroller (the mouse wheel works anywhere on it), one Tab stop, and arrow keys that cross groups. Opening a page scrolls its row into view.
  - A 32px search field, "Find a setting", 12px above the list. Typing replaces the content with matching rows from every page, grouped by page; clicking one opens its page.
  - **Feature groups** (2026-10-26): the installed features' pages under the catalog's group labels, in Add features' order (Files, Sound, System, Clipboard & text, Windows, Capture, Apps). Only groups with an installed feature show. Within them: File organizer, Shelf; Now Playing, Output, Microphone, App volume; Keep awake, System monitor; Clipboard history, Clean URL, Paste as plain text, Scratchpad (2026-10-28).
  - **Sound is organized by task (revised 2026-10-28):** **Now Playing** (the shell's, always shown), **Output**, **Microphone** and **App volume**, each with the island's controls first. Output: Volume (slider, percentage, Mute), **Output in use** (a radio row per connected output), then **Priority** (+ Add device: Priority switching, Communications device, the ordered list). Microphone holds three features, shown while each is installed: the **Microphone** row (Mic mute: Live or Muted, Mute / Unmute), **Mic in use** and **Priority** (Audio routing), **Mic processing**, and **Mute shortcut** (Shortcut, removable; Sound feedback). The mic is picked once there. App volume: a row per app, every app, then **Options**. A feature's deep link opens the page that holds it. Features still install one by one in Add features.
  - **Chippi:** General, Island & pet, Notifications, Add features, About. Shortcuts and Permissions join this group only once a feature uses them. Appearance is gone.
  - Group labels are Nav group type with 12px of space above (none above the first) and 4 below. Items are 32px, 36 apart, with a 16px glyph and a 14 / 400 label that trims with "…". Hover is subtle-hover. The selected item gets subtle-selected, Semibold and a 3×16 accent pill at its left edge.
  - **Rail (2026-10-28):** below a 1008px window the sidebar becomes a 48px icon rail: a menu button, search as an icon, then the page icons with a hairline between groups and the pill on the open page. The menu button (or search) opens the full sidebar over the page with a shadow; it closes on a pick, on Esc, or on a click outside it.
- **Scrollbars (2026-10-28):** they float over the content as in Windows 11: a 2px line at rest, 6px over a faint 10px track while the pointer is over the scroller. Content keeps its full width, so cards are 768 wide on every page at 1120.
- **Opening:** Settings opens on the page its caller asks for (the island's Settings button opens the last page; a notice opens its run in History). With no target (the tray icon) it opens the page showing when Settings last closed, also after a restart, or the first sidebar item if that page has gone (2026-10-28). Sub-tabs open on their first tab.
- **Content (revised 2026-10-28):** padding 40 / 4 / 40 / 28 (left, top, right, bottom), at most 1000 wide and aligned left, so a maximized window doesn't stretch it. The page title (20 / 600 Display) sits 14px above the first section, with no description.
- **Page template:** title, then an optional SelectorBar, then sections. A section is a Section header (with an optional right-aligned button) over one card of rows. Two cards in a row of the same section sit 8px apart.
- **Rows (revised 2026-10-28):** at least 60px with a caption, 48px without; 16,8 padding; 12px gap. Label over caption on the left, the control on the right. A switch shows "On" or "Off" in body text just left of it. An expander row ends in a chevron and its sub-rows are indented 50px.
- **File organizer (revised 2026-10-28):** each watched folder shows its glyph, name/path, mode, Organize now and a chevron for Mode, Wait before moving, Ignore list and Stop watching. Unmatched preview files fold into one count row; with nothing actionable the footer says Nothing to organize with Cancel and Add rule, which opens a new unsaved draft. A SelectorBar of Settings · Rules · History (§5 File organizer).
- **Island & pet** (2026-10-26): one card of Island, Hide tray icon (dimmed while the island is off), Opens on (Last page used / All features), Position with Reset (back to the start), and Display (Main display or One monitor, with "Monitor not connected · using main display" while the chosen one is gone). Then **Tiles**: a row per tile with a grip (drag, or Up and Down on the focused grip), its glyph, its name and a Show switch, beside a card with a small preview. The home shows the first 8 shown tiles. Then **Chippi**: Color and Reduce motion (revised 2026-10-29: the round buttons are gone).
- **Two surfaces (revised 2026-10-28, replaces "Settings never repeats a daily control").** Settings can do everything the island can: a feature's page starts with its island controls, which call the same module method as their island twin, then its configuration, and lists every item the fixed island leaves out. The island is unchanged: its pages show live state and controls only. No page intros and no info buttons; subtext appears only when a label cannot stand alone.
- **Keep awake (revised 2026-10-28):** a Keep awake row with the state caption and Stop while on, then Length with the six island choices (30 min, 1 hr, 2 hr, 4 hr, No limit, Until...), with the active choice lit. Until opens the time picker. Keep screen on follows. There is no session switch, Default length or After a restart row.
- **System monitor Settings (revised 2026-10-28):** the live readings first, including every thermal zone, then Disks with every drive, General (Update speed, Tile status), Readings, Units and Alerts. Alert switches and current thresholds sit in expandable headers; their tuning folds inside at the shared 50px inset. Repeat interval uses hour-style values.

- **Add features** (replaces the Features hub): a search field with the "2 on" count beside it. **Available** lists the features that work, each a row with a 32px icon chip (accent glyph when on, muted when off), name, one-line description and a switch. One collapsed **Coming later** row ("39 features") expands into one row per group listing the names. Presets are hidden until enough features exist for them to matter. Toggling a feature installs or uninstalls it, and an installed feature appears in the sidebar at once.
- **General:** Theme (Dark · Light · Follow Windows segment, default Follow Windows), Accent color (a "Windows" pill and four 24px swatches; the caption reads "Follow Windows" or the color's name), Run at startup (switch). A Settings file section with one row, "Location", captioned with the path, and Export… and Import… on the right.
- **Island & pet:** see above. The Chippi section: a Color row with a 48px Chippi preview, the color's name as caption and six swatches; Reduce motion (combo: Follow Windows · On · Off).
- **Notifications:** see below.
- **About:** Chippi with a 32px Chippi and "Version 0.1.0" on the right; License (GPL-3.0-or-later, View); Privacy ("No telemetry"); Third-party notices (View). The sidebar has no version footer. Notices open as rendered text, never raw markdown.
- **Swatch selection:** a selected swatch or pill gets a 2px gap in the card color and a 2px accent ring.

### File organizer
Behavior approved 2026-10-22 (`docs/phase-2/design/phase-2-mockups.html`); visuals restyled 2026-10-23. Its island page is under Island pages above.
- **Settings tab:**
  - Watched folders, with Add folder on the header's right. Each folder is an expander row: folder glyph (accent while watched, muted when Paused), name over path, the mode as a value, and a chevron. Its sub-rows: Mode (Auto · Manual · Paused segment), Wait before moving (combo), Ignore list (Edit) and Stop watching this folder (Remove).
  - Options: Keep history (combo), Organize in Explorer (switch), Import archive (Choose file…; the picker filters to zip, tgz and tar.gz).
- **Rules tab:**
  - A card holding the test field, borderless across the whole card: a search glyph, "Test a file name", and the result on the right ("Txt Files → Documents" in Secondary, or "No rule matches · stays in Downloads" in Caption). It runs the whole rule set.
  - "Rules" with a "First match wins" caption, and Add rule on the right.
  - One card of rule rows: drag grip, number, name over a summary (".txt → Documents"), switch, and a chevron that opens the rule. One rule is open at a time. Drag, or Alt+Up/Down, reorders; the drop target is a 2px accent line.
  - **Saving.** Everything inside the editor (name, conditions, action, destination) is a draft until Save. The editor ends with Save (accent, enabled only when the draft is valid and changed) and Cancel on the left, Duplicate and Delete on the right. Save keeps the rule and closes the editor; Cancel puts it back, and removes a rule from Add rule that was never saved. The switch, reorder, Duplicate and Delete save at once, like Windows switches. Collapsing a rule keeps its draft and shows an amber "Unsaved changes" badge on the row. Leaving the page (another tab, another Settings page, a deep link, closing Settings) with a draft shows a card at the top of Rules instead: "Discard changes?" with Discard (accent, then goes on) and Keep editing (opens the first draft).
  - Action is a Move to folder · Rename segment. **Move** shows the destination as a read-only path with Browse… beside it, which opens the Windows folder picker (it has its own New folder); the rule saves the full path, shown in the summary by the folder's name with the full path on hover. A new rule starts at Documents. A relative destination from before the picker stays as it is: it means "inside the watched folder the file is in", reads as "Downloads\Documents" with the caption "Inside each watched folder", and picking a folder replaces it. Move takes no placeholders. **Rename** is a typed template whose placeholders are 22px chips in control fill, insertable by click.
  - Regex and rename templates use the code font and fill their column up to 460px. Validation messages are caution text under the field.
  - **Files already there.** Adding a folder or changing its mode never moves the files already in it. Saving a rule, or switching one on, sorts them: only the top-level files that rule wins under first match. Files Chippi just moved or put back with Undo, partial downloads, the ignore list and misleading names stay out. Auto moves them as one run with history, Undo and a notice; Manual lists them in Ready to review; Paused does nothing. An invalid or Off rule never runs.
- **History tab:**
  - A toolbar: search field ("Search history"), All folders combo, Clear history (text only, no glyph).
  - Grouped by day under Section headers. One card per run: a header row ("Auto · Downloads" over "10:07 AM · 1 moved") with Undo, then indented file rows (name over "Downloads › Documents") with an undo icon button. Every run starts collapsed behind a chevron, except the run a notice or link opened; clicking anywhere on the header (but a button) opens or closes it, like a rule's header.
  - Undo per row and per run goes through an inline confirm bar: "Undo 4 moves?" (or "Undo this move?") with Cancel and an accent Undo. Clear history uses a popover: "Clear history?" with Cancel and a Critical Clear.
- **Preview** is a view inside Settings with a back button, not a dialog.
  - Each row has a checkbox, the file name with its size, the rule and the destination. Warnings are a caution line under the name, and a move to another drive gets a neutral "Other drive" badge.
  - Held rows start unchecked. Files no rule matches show a disabled checkbox. The header checkbox shows mixed when some rows are selected.
  - The footer shows the selected count and total size, with Cancel and Approve (accent).
  - After apply: a success check and Undo on each moved row, a caution failure line on each failed one, then "Refresh" and "Done".
- **Notices:** event kinds are Organized, Ready to review, Held, Failed, Archive imported. Which events reach Windows notifications and the island is set on the app-wide Notifications page.

### System monitor (approved 2026-10-26)

Screen and copy specification: [approved notes](docs/phase-4/design/notes.md), [boards](docs/phase-4/design/index.html), and [renders](docs/phase-4/design/shots/). The notes decide the approved exceptions to this section; the [Phase 4 brief](docs/phase-4-handoff.md) decides behavior.

- **Tile:** Activity glyph, name "System monitor", sixth after File organizer. One configurable reading as status, CPU by default (for example "CPU 12%"). No normal dot. Enabled alerts beyond their thresholds show their reading with Caution and its dot; the first alert in Settings order wins. Standard tile hiding, ordering and round-button choices apply.
- **Page:** one 520 x 360 island, with a 496 x 304 body. Two full-width cards: readings, an 8 DIP gap, then Disks. Both use 10,2 padding. Rows are 28 DIP with hairline dividers. Lists show whole rows only; overflow drives appear in Settings (revised 2026-10-28).
- **Typography:** Name 80 DIP, 12, tertiary; detail 11, tertiary, trimmed; value 12 / 600, tabular, right-aligned. Values turn Caution while their alert holds. No page note (revised 2026-10-28).
- **Graphs:** removed from the page, along with the logical-processor strip, committed/shared memory, GPU fan, disk activity and transfer rates, and the network scale/legend. The sampler, tile and alerts keep their behavior (revised 2026-10-28).
- **Details:** CPU, GPU, Memory, Network, then Battery and Thermal zone only where supported. CPU shows clock and load; GPU shows temperature and dedicated memory with load; Memory shows used of total and percent; Network shows down/up arrows and rates, or Not connected. GPU and adapter pickers show only with multiple choices; the GPU name is in its picker only. Battery shows state, rate, time left and percent; zones are numbered when several exist. Failed reads show Unavailable with no value; restarting rates clear old values (revised 2026-10-28).
- **Disks:** letter, Used (Total minus Free), Free and Total, sorted by letter. Three equal numeric columns follow the 80 DIP name column; only Free turns Caution while its alert holds. Header 24 and rows 28 normally; tight mode uses 22 for both header and rows, fitting seven drives with four readings. Extra readings leave fewer drive rows. A failed disk read shows Unavailable; Settings lists every drive (revised 2026-10-28).
- **Units:** Celsius by default with Fahrenheit optional; binary sizes through TiB, KiB/s then MiB/s, or Kbps then Mbps. Temperature values include a space before the unit; RPM uses thousands separators. Caution applies to reading values and disk free amounts only while their enabled alert holds.
- No tray readouts, extra tray icons, page settings link, pause or time-range control.
- **Settings:** System group after Keep awake, all drives in a Disks section at the top, followed by configuration (revised 2026-10-28). First configuration card: Update speed (1/2/5 seconds, default 2), Tile status (CPU/Memory/GPU/Network, enabled readings only). Readings switches default on; Battery/Thermal zone options appear only where present. Tile status falls back to the first enabled reading. Units and Alerts follow, with no feature switch or page intro. Settings can scroll.
- **Alerts:** five off-by-default kinds: CPU load, Memory in use, Low disk space, Low battery and GPU temperature. Threshold sub-rows contain sliders and values and dim while off. GPU limit fields remain editable while off, one per reporting GPU, 40 to 110 Celsius with "Not set" and no default. The GPU switch requires at least one limit. Repeat interval choices: 2/5/15/30/60 minutes, default 15. Exact threshold and sustain behavior is in the brief.
- **Notices:** Windows and Island destinations default on for each opt-in alert. One short reading (for example "Memory in use 93%"), no buttons; clicking opens the System monitor page in the island, or Settings while the island can't show. The widget's chip shows the alert's reading with its glyph in Caution, after media/timer/stash/agent priority, until the alert clears. Chippi stays idle.

### Clipboard and text (approved 2026-10-28)

Screen and copy specification: [approved notes](docs/phase-5/design/notes.md), [boards](docs/phase-5/design/index.html), and [renders](docs/phase-5/design/shots/). The notes decide the approved exceptions to this section. [Scope](docs/phase-5/scope.md) and the [design input](docs/phase-5/design-input.md) decide behavior, except the screens the notes replaced: there is no Clean URL page, and no Plain paste tile or page. The tile set follows Vorssaint's island, which gives these five features three tiles.

- **Tiles:** Clipboard (ClipboardList), Scratchpad (NotebookPen) and Shelf (Inbox), after System monitor, so nine tiles make three rows. Clipboard appears while Clipboard history or Clean URL is installed, and they share its page like Sound's features share Sound. Paste as plain text adds no tile. Statuses: Clipboard "Off", "Paused", "No items", "23 items" (Clean URL alone: "Manual" or "Automatic"); Scratchpad "4 notes"; Shelf "5 items". Never the latest copy's text, and no dots.
- **Clipboard page** (revised 2026-10-28): the header holds Clean URL (link), Pause or Resume, and More (Clear history, Erase all) as icon buttons; with recording off, only Clean URL. Then a 28 search box and the list: Pinned and Recent groups (22 labels, 11 / 600) of 30 rows (glyph or 32×20 thumbnail, one line, time), as many whole rows as fit. The selected row opens in place and counts at its full height: at most 3 lines of preview, the last trimmed, then Paste (accent) and Copy 8 apart, and Pin and Delete as icon buttons 2 apart. Rows above leave so the open entry stays in view. A search shows its best matches that fit, newest first. Nothing scrolls; the full history goes to Settings › Clipboard history (Part F).
  - Recording is off until Turn on (on the page) or Recording (Settings). With recording off and nothing saved, the ClipboardList glyph (20), "Recording off" and Turn on sit in the middle of the body. A private or skipped copy leaves no row, gap, count change or status.
  - A missing file shows FileX and "Unavailable", and disables Paste and Copy ("1 file unavailable"). A copy over a limit shows one Caution line above the list until the next copy.
- **Clean URL:** the link button (shown pressed) swaps the list for a full-width link field that grows to three padded lines. Longer links keep their start and end with a middle ellipsis while unfocused; focus selects the whole link. The clean link follows as you type, trimmed the same way, with Copy for the whole link. Nothing scrolls (revised 2026-10-28). No list of what was removed (Travis, 2026-10-28). The button again, or Esc, goes back. Without Clipboard history the page is Clean URL alone, with the tile status in the header. The current copy fills the field when it's one web link without a private marker. States: "Nothing to remove", "Not a web link" (Caution), "Copied".
- **Paste as plain text:** a shortcut only, with no default; it does nothing until one is set in Settings. It pastes into the app in front. When it can't (an admin app, or one whose level can't be read), the plain text becomes the copy. That case, pictures or files only, and a busy clipboard each play the Windows default beep and show the ClipboardType glyph on the widget's chip for 6 seconds: accent for "Plain text copied", Caution for "No text copied" and "Clipboard busy" (screen-reader names). No Windows notification, Notifications group or "Pasted" state.
- **Scratchpad** (revised 2026-10-28): header status "Saved", "Save failed" or "Export failed" (Caution). A 30 tabs row of up to 12 named notes (the ones that don't fit under "+N ⌄") with New note, the editor or preview filling the body, then at the bottom edge a 28 Edit · Preview segment and ⋯ (Rename, Export…, Delete). Only the note's text scrolls, inside the editor, and notes have no length cap. Preview renders local Markdown and fetches nothing. A failed load shows "Load failed" (Caution) with Retry and no editor.
- **Shelf** (revised 2026-10-28): tiles fill the body above Copy, Pin and Remove (bottom edge, right), newest first, and shrink as items pile up: up to 18 at 6 × 3 (76 × 66, the glyph or 36 × 20 picture in a 20 slot 7 from the top, then the name on up to 2 lines, or 1 above "Unavailable" or "Online only"), up to 28 at 7 × 4 (the name on up to 2 lines), up to 40 at 8 × 5 (the glyph alone, centered). Names wrap at spaces and hyphens; one long word trims. Size and type are in the tooltip with the full name. At 40 items the Shelf is full: "Shelf full" (Caution) at the left of the action row, and drops are refused until something is removed; nothing is thrown away. Empty, the dashed drop zone fills the body with Inbox (20) and "No items". The whole page takes drops, one drop per item. Out is copy-only; an accepted drop removes an unpinned item. Remove has no confirmation and never touches the source file. Kept text and pictures are capped at 128 MiB.
  - A drag that stays 250 ms over the puck shows the Shelf on its chip and opens the Shelf without taking focus. The tray icon never takes a drop.
- **Keyboard:** opening puts the keyboard in Search (the link field in Clean URL or without History), at the end of the note, or on the first Shelf item. On Clipboard the first Esc clears a search or leaves Clean URL, and the next closes. Tab leaves the Scratchpad note and never types a tab.
- **Settings (revised 2026-10-28):** Clipboard & text holds Clipboard history, Clean URL, Paste as plain text and Scratchpad; Shelf is under Files, after File organizer.
  - **Clipboard history:** Recording, Apps to skip, then History: Pause or Resume in its header, a search box with the item count, and every copy on a 48 row, pinned first, each with a ⋯ of Paste, Copy, Pin or Unpin, and Delete. Paste goes to the window Settings was switched from; with none, it copies and shows "Copied". The list is as long as the history; the page scrolls.
  - **Clean URL:** the island's cleaner at the top (the link field, then the clean link and Copy in the accent; no "Removed" line), then Automatic cleaning and Sites to skip.
  - **Paste as plain text:** Shortcut: "Not set" in secondary text and Set; the keys with Change and Remove; "Shortcut in use".
  - **Scratchpad:** "Saved" (or "Save failed" in the warning color) beside the title, the note tabs and +, the note, then Edit / Preview, Export… and Delete note. It edits the same notes as the island page, so an edit shows in both.
  - **Shelf:** every item on a 60 row: glyph, name, a kind caption ("PDF · 2.1 MB", "Online only", "Unavailable"), the pin when pinned, Copy, and ⋯ (Pin or Unpin, Remove). Unavailable items are dimmed. Rows drag out like the island's tiles. Empty: "No items".
  - Uninstalling Clipboard history while history is saved asks "Uninstall and erase history?".
- **Confirmations:** "Clear unpinned items?" with Clear, "Erase all items and pins?" with Erase, "Delete “Standup”?" with Delete, each with Cancel.

### Notifications page
Behavior approved 2026-10-22 (`docs/phase-2/design/notifications/`); visuals restyled 2026-10-23.
- **Destinations:** one card with two switch rows, Windows notifications and Island, with no icons.
- **Turning a destination off** keeps its choices. Its row has no caption; its checkbox column dims to 60% in every group while staying editable.
- **When the Windows backend is unavailable,** one line sits under the card: a 7px caution dot and "Windows notifications unavailable" in caution caption. No button, and no claims about Focus or Do not disturb.
- **Feature groups** follow the sidebar order. Each is a Section header with "Windows" and "Island" caption labels over 96px columns, then a card of 48px rows with one 20px checkbox centered in each column. A feature with no events gets no group.
- **Downloads defaults:** Organized and Ready to review: Island only. Held and Failed: both. Archive imported: Windows only.
- **Accessible names** read "Feature, Event, Destination", for example "File organizer, Held, Windows".
- **Quiet hours and sound** stay with Windows. Chippi has no setting for them.
- **Copy and clicks:** a notification states what happened first ("4 files organized", "1 file held", "1 file not moved", "photos-2025.zip imported"), one per run.
  - Organized and Archive imported: clicking opens that run in History. The buttons are Undo (opens the run's inline Undo confirmation, never undoes on its own; only when something is undoable) and History.
  - Failed: clicking opens the run in History; the button is History.
  - Held and Ready to review: clicking, or the Review button, opens the review.
- **The widget on a stash:** Chippi celebrates, then goes idle, and the chip shows the folder glyph. Held and failed stashes turn it Caution.

## 6. Components

Every interactive control is custom-templated with the full state set: rest, hover, pressed, keyboard focus and disabled. Focus is a 2px outline in the Text color at 1px offset, and it is non-negotiable. Disabled is 45% opacity with no hover.

- **Button:** 32px, 12px side padding, 4px radius, body label (13px in the flyout). Control fill with a 1px control stroke; hover control-hover; pressed control-pressed with secondary text.
- **Accent button:** the same shape in the accent with on-accent text; hover at 90% opacity. One per view.
- **Critical button:** Critical fill, white text. Only inside a confirm popover.
- **Icon button:** 32×32, 4px radius, transparent, a 16px glyph in Secondary. Hover: control fill and Text. Always has an accessible name.
- **Subtle button:** the flyout footer and link buttons. Transparent until hover.
- **Switch:** 40×20, 10px radius. Off: transparent with a 1px switch-off stroke and a 12px switch-off knob, 4px from the left. On: accent fill and stroke with a 14px on-accent knob at 22px. The knob slides in 150ms. Settings rows show On/Off text to its left; flyout rows don't.
- **Segmented control (revised 2026-10-28):** a 2px-padded trough (segment-trough fill, segment stroke, 6px radius). Items are 26px (32px for the whole control) with 13px labels in Secondary, 4px radius; the selected item gets segment-selected fill and Text (plus the light-theme shadow). In Settings items size to their labels with 14px padding; in the flyout they split the width equally.
- **SelectorBar:** text tabs under a page title, 40px tall, 12px padding, body text in Secondary. The selected tab is Text, Semibold, with a 16×3 accent bar 3px from the bottom.
- **Nav item:** see Settings window. The pill is 3×16, 2px radius.
- **Text field and search field:** 32–34px, 10px padding, 4px radius, input fill, 1px stroke with a stronger bottom stroke (input-stroke-bottom). Focus: input-focus fill and a bottom stroke in Text. Placeholders are Muted. Search fields lead with a 16px search glyph.
- **Combo box:** a Button with its value on the left and a chevron on the right, 180 wide in Settings (revised 2026-10-28).
- **Checkbox:** 20px, 4px radius. Off: a 1px switch-off stroke on `rgba(0,0,0,.1)` (light `#FFFFFF`). On and mixed: accent fill and stroke with an on-accent glyph (2.2 stroke). Use it for multi-select lists and grids; a single setting uses a switch.
- **Card:** card fill, 1px card stroke, 8px radius, clipping its rows. Rows inside are divided by 1px dividers; the first row has none. Never a card inside a card.
- **Flyout card:** flyout-card fill and stroke, 8px radius. Rows are 12 / 16 padded; divided rows are 48px; file rows are 52px.
- **Settings layout (revised 2026-10-28):** one shared toolkit in `Chippi.UI.Controls.SettingsLayout`: sections have 24 above their 20-high header and 8 below, so cards sit 52 apart. A header action does not add height. Rows use 16,8 padding and are 48 high, or 60 with a caption. Empty states are one caption-color row inside the card. Add buttons use the Plus glyph and "Add <thing>".
- **Expander row (revised 2026-10-28):** the header holds the name, current value in secondary text, On/Off switch and chevron. Sub-rows sit in the same card, indented 50px; folding retains their values.
- **Icon chip:** 32×32, 6px radius, control fill, a 16px glyph in the accent when on and Muted when off. For feature rows, the folder row and the Tidy card.
- **Duration chip:** 32px, control fill and stroke, 4px radius, 13px label with tabular figures, in a 3-column grid with 8px gaps. Selected: accent fill and stroke, on-accent text.
- **Swatch:** a 24px circle. The "Windows" accent choice is a 24px pill with a 12px label. Selection: §5 Settings window.
- **Vertical volume bar** (Sound island mixer; `Island.dc.html`): a 40x136px track, 12px radius, `rgba(255,255,255,.12)` fill (`.16` hover), filling upward in accent with no separate thumb. A 56px strip holds a 28px app/mute button, bar, 12px tabular percentage and 12px Caption app name, with 6px gaps. Strips have 4px gaps; a 1px `.10` white divider with 8px side margins separates System from apps.
  - Normal range 0 to 100%. With validated Volume boost, app range 0 to 200% in 5-point steps, with a 1px `.45` white line at 100% (halfway, inset 8px). Above 100%, fill and percentage are Caution; System remains 0 to 100%. The gate failing disables the above-100 range, not ordinary volume/mute.
  - Pointer drag changes volume; keyboard arrows adjust a step, Home/End select bounds. Accessible name identifies app/output, and RangeValue exposes the actual percentage. Focus is a 2px white outline with 2px gap on the island. Mute is a separate named button and does not discard the stored level.
- **Badges:** 18px, 6px side padding, 4px radius, 11 / 600.
  - **Caution:** caution text on caution tint, for example "Unsaved changes" in the rule editor. Feature rows omit overlap badges.
  - **Neutral:** Secondary text on control fill, for example "Other drive" or "Needs VB-CABLE".
- **Status dot:** 7px. Success for active, caution for attention, Muted for offline. No glow.
- **Drag handle:** a 16px six-dot grip in Muted. Alt+Up/Down moves the row.
- **Sound glyphs:** the Sound tile uses `AudioLines` (six vertical audio lines); Mic processing uses `Mic`; Mic mute shortcut uses `MicOff` (slashed microphone). Use the matching mic-off glyph for muted state in the flyout, island tab and Microphone tile/page, in Caution. These are distinct feature glyphs, not two features sharing Mic.
- **Icons:** Lucide on a 24px grid, round caps and joins, drawn at 16px (20px for a Settings row lead glyph). The stroke is 30 / size in the grid, so every size draws the same 1.25px line (revised 2026-10-28). Each feature has its own glyph; no glyph is reused for two features. Brand logos are never redrawn: an app shows its real icon, or a monogram tile when no icon is available.

## 7. The pet: Chippi

### Character
Chippi (the pet shares the app's name, Travis 2026-10-29) is a soft rice-cake blob with big shiny eyes and an open grin, no blush, no limbs and no text. It replaced Gumdrop. It is calm by default, pleased when Chippi finishes something for you, asleep when nothing is happening, and wears headphones while music plays. It never interrupts, nags or needs feeding. It is a status light with a face. The control is `ChippiPet` (`src/Chippi.App/Pet/ChippiPet.cs`).

### Geometry
Drawn on a 120×120 viewBox; WPF `Path.Data` takes the same syntax.

| Part | Spec |
|---|---|
| Body | `M17 76 C17 45 36 31 60 31 C84 31 103 45 103 76 C103 98 87 106 60 106 C33 106 17 98 17 76 Z`, one pet color with soft shading: a vertical gradient from the color to 88% of it |
| Eyes (idle) | White eye whites 10.5 × 12.5 at (47, 66) and (73, 66); ink pupils r 6 at (49, 68) and (75, 68); white highlights r 2 at (50.6, 65.6) and (76.6, 65.6) |
| Mouth (idle) | An open grin in ink with a `#FF7FA8` tongue |
| Ground shadow | At sizes of 64 and up only |

### Moods

| Mood | Face | Extras | When |
|---|---|---|---|
| **Idle** | The eyes and grin above | Blinks | Any activity; the puck while hovered or open |
| **Wide awake** | Happy-arc eyes (3.2 stroke) | Blinks | While Keep awake holds the PC up |
| **Celebrating** | Happy arcs (4.2 stroke) | Sparks (`#F5C451`, 3.5) fading over 1.2s; a squish to (1.06, 0.94), then (0.985, 1.02), then back, at 110, 260 and 400ms from the bottom center | Chippi finished something for you (stashed files, a timer, a download). Holds 2.5s, then goes back |
| **Asleep** (`Sleepy`) | Shut arcs (3.4 stroke) and a small "o" mouth | Two z's, 20 and 13px bold, `#9D9D9D` | Nothing live: the puck at rest, Nothing playing |
| **Listening** | The celebrating face, no sparks | Headphones: a `#2A2440` band (5 stroke) and cups at the sides with pads in `#D2BEFF` | A song playing: the puck (not while lifted), Now Playing and the home card without art |

### Sizes
- **Small form (22 and under):** body and eyes only (6 × 7.4 at y 74); no mouth, sparks or z's.
- **Puck:** 36 in the 50 puck. **Home footer:** 20. **Home media card without art:** 28. **Now Playing without art:** 76, listening; **Nothing playing:** 72, asleep. **Settings titlebar:** 16. **Settings › Island & pet:** 48 beside the color swatches. **About:** 32.

### Color option
Settings › Island & pet › Chippi › Color: **Synth** `#B78FFF` (the default, the accent's violet), **Iris** `#CBB2FF`, **Azure** `#8EC0FF`, **Signal** `#8EE6C0`, **Ember** `#FFC58F` or **Frost** `#E8ECF6`. Every color draws the same way (one color, soft shading), and the face stays pet ink and white on all of them.

### Motion
- **Blink:** every 4 to 7s at random, the eyes scale to 10% height for 120ms (idle and wide awake).
- **Mood change:** the face cross-fades in 150ms. **Falling asleep** from idle or wide awake: the eyes ease shut over 600ms, then the z's fade in over 400ms.
- **Reduced motion:** no blink, squish or tween; moods swap at once, and a running tween snaps when the setting changes.

### Accessibility
Where text beside it carries the state (the puck, the home, Now Playing), the pet is decorative and left out of the accessibility tree. Where it stands alone, as in the Settings preview, its name is "Chippi, {mood}": idle, wide awake, celebrating, asleep or listening. It is never the only signal of anything.

## 8. Do's and Don'ts

### Do
- **Do** act on the island's pages and configure in Settings. If a common action needs Settings, move it to the feature's page.
- **Do** keep each section's rows in one card with dividers: one shape per section, not per row.
- **Do** follow the Accent Rule, the Weight Rule and the Tabular Rule.
- **Do** check every screen in dark and light, and with at least one non-blue accent.
- **Do** give every custom control a visible focus ring and an accessible name.
- **Do** keep status visible at rest: folder mode, held files and Keep awake time show without hovering.
- **Do** list only features that work. Coming soon stays folded away.

### Don't
- **Don't** use gradients beyond the Gradient Rule's two, and never gradient text.
- **Don't** use Bold, colored glows, glassmorphism or hero metrics.
- **Don't** put a description under a page title, or a sentence where a label will do.
- **Don't** nest cards, or give each row its own card.
- **Don't** ship stock WPF chrome: no default checkboxes, group boxes or gray dialogs.
- **Don't** use a colored side stripe (a left or right border wider than 1px) to mark a row or card. The nav pill is the one exception.
- **Don't** animate anything for its own sake. Only switches, segment and tab changes, the island and the pet move. The pet's blink, the chip's live equalizer and its scrolling song are the only loops.
- **Don't** use emoji anywhere in the UI.
- **Don't** redraw other companies' logos. Use the real app icon or a monogram.
- **Don't** copy vorssaint's screens, icon or assets.
- **Don't** let the pet talk, show speech bubbles, or block content.

## 9. Implementation notes (WPF)

- **Variable fonts:** WPF draws only a variable font's default instance and can't select weights from it. That is why Manrope once rendered ExtraLight, and Segoe UI Variable is a variable font. Check that 400 and 600 render as true weights before building on it. If they don't, use static **Segoe UI** (Regular `segoeui.ttf`, Semibold `seguisb.ttf`, present on every Windows 11 install) for both roles. The design holds either way.
- **Tabular figures:** `Typography.NumeralAlignment="Tabular"` on any element showing numbers, or on the base text style.
- **Accent:** read the Windows accent shades with `Windows.UI.ViewManagement.UISettings.GetColorValue` (`AccentLight2` for dark, `AccentDark1` for light) and listen to `ColorValuesChanged`, which fires off the UI thread. Swap one `AccentBrush` and one `OnAccentBrush` when the accent setting, the theme or the Windows accent changes.
- **Theme:** Follow Windows is the default and reads `AppsUseLightTheme`. Both themes define every key, the island's `Island*` keys included; the island's accent keys stay violet whatever the Accent setting.
- **Pet control:** `ChippiPet`, with a `Mood` property (`PetMood`: Idle, Celebrating, Sleepy, WideAwake, Listening), `Size`, `PetColor` and `IsDecorative`, drawn from paths on the 120 viewBox.
- **Island window:** borderless, `AllowsTransparency`, topmost, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, so it never steals focus or shows in Alt+Tab. The widget's window is 340 × 124 around the puck with room for its lift and shadow; fully transparent pixels click through. It sits on the display the Display setting picks and hides while a fullscreen app or game is in front on that display. Because it never activates, dragging uses mouse capture and `SetWindowPos`, not `DragMove`; a drag's moves change only the position (no resize, no z-order), which keeps each under 1 ms. The panel is its own window, sized to the panel and the puck with room for the shadow. While the island is open, low-level keyboard and mouse hooks watch for Esc and for a click elsewhere; they are installed only while it is open and always pass the input on.
- **Reduced motion:** read `SystemParameters.ClientAreaAnimation` and listen for changes.
