---
name: Chippi
description: A Windows tray suite on Hopper's dark studio-rack chassis, with one violet→azure signal accent and a small gradient blob that lives in the island.
colors:
  synth-violet: "#B15CF0"
  synth-azure: "#4D9BFF"
  synth-violet-deep: "#8E3FD0"
  synth-azure-deep: "#2A6AD6"
  signal-green: "#3DDC97"
  signal-amber: "#F5A64B"
  signal-red: "#E23B4E"
  signal-red-deep: "#C42B3E"
  backdrop: "#0A0D18"
  window: "#0B0F1F"
  titlebar: "#0D1224"
  panel: "#0F1426"
  surface: "#131930"
  surface-selected: "#1A2240"
  chip: "#1B2240"
  input: "#11172C"
  track: "#161D38"
  track-remainder: "#22294A"
  switch-off: "#262E4A"
  button: "#151C34"
  button-hover: "#1C2444"
  island: "#05070F"
  ink: "#EEF1F8"
  ink-strong: "#D6DBEC"
  ink-control: "#C3CAE0"
  ink-secondary: "#7E879F"
  ink-tertiary: "#8A93AD"
  ink-muted: "#7C849C"
colors-light:
  backdrop: "#E9EDF4"
  window: "#F7F8FC"
  titlebar: "#EEF1F7"
  panel: "#F1F4FA"
  surface: "#FFFFFF"
  surface-selected: "#EEF2FD"
  chip: "#EEF1F7"
  input: "#FFFFFF"
  track: "#E4E8F1"
  track-remainder: "#DDE2EE"
  switch-off: "#D3D9E6"
  button: "#FFFFFF"
  button-hover: "#F0F3FA"
  island: "#05070F"
  ink: "#1A2036"
  ink-strong: "#2A3147"
  ink-control: "#34405E"
  ink-secondary: "#5C6580"
  ink-tertiary: "#79839B"
  ink-muted: "#6B7488"
  focus-ring: "#8A3FD0"
  segment-active-text: "#1A2036"
  badge-source-text: "#7B2DB8"
  badge-overlap-text: "#8A4B00"
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
  display:
    fontFamily: "Manrope, Segoe UI Variable Display, Segoe UI, system-ui, sans-serif"
    fontSize: "17px"
    fontWeight: 800
    lineHeight: 1.1
  headline:
    fontFamily: "Manrope, Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "15.5px"
    fontWeight: 700
    lineHeight: 1.15
  title:
    fontFamily: "Manrope, Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "13.5px"
    fontWeight: 600
    lineHeight: 1.2
  body:
    fontFamily: "Manrope, Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "12.5px"
    fontWeight: 500
    lineHeight: 1.45
  caption:
    fontFamily: "Manrope, Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "11.5px"
    fontWeight: 500
    lineHeight: 1.35
  label:
    fontFamily: "Manrope, Segoe UI Variable Text, Segoe UI, system-ui, sans-serif"
    fontSize: "10.5px"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "0.13em"
  mono:
    fontFamily: "JetBrains Mono, Cascadia Mono, ui-monospace, monospace"
    fontSize: "11.5px"
    fontWeight: 600
    lineHeight: 1.2
  mono-readout:
    fontFamily: "JetBrains Mono, Cascadia Mono, ui-monospace, monospace"
    fontSize: "19px"
    fontWeight: 600
    lineHeight: 1.1
rounded:
  badge: "6px"
  window-button: "7px"
  tab: "8px"
  control: "9px"
  chip: "10px"
  segment-trough: "11px"
  row: "12px"
  switch: "12px"
  card: "14px"
  window: "13px"
  flyout: "16px"
  island-expanded: "24px"
  island-pill: "half the pill height"
spacing:
  xs: "6px"
  sm: "8px"
  md: "11px"
  lg: "14px"
  xl: "16px"
  "2xl": "18px"
components:
  button-primary:
    background: "linear-gradient(135deg, #8E3FD0, #2A6AD6)"
    textColor: "#FFFFFF"
    rounded: "{rounded.control}"
    height: "36px"
    padding: "0 16px"
    typography: "{typography.title}"
  button-secondary:
    backgroundColor: "{colors.button}"
    textColor: "{colors.ink-control}"
    border: "1px rgba(255,255,255,0.08)"
    rounded: "{rounded.control}"
    height: "33-34px"
    padding: "0 13px"
    typography: "{typography.body}"
  switch:
    size: "40x23"
    rounded: "{rounded.switch}"
    off: "{colors.switch-off}"
    on: "linear-gradient(135deg, #B15CF0, #4D9BFF)"
    knob: "17px white, slides 17px"
  segmented:
    trough: "{colors.input}"
    active: "linear-gradient(135deg, rgba(177,92,240,0.22), rgba(77,155,255,0.22)), 1px rgba(177,92,240,0.55)"
    activeText: "#FFFFFF"
    inactiveText: "{colors.ink-tertiary}"
  badge-source:
    backgroundColor: "rgba(177,92,240,0.16)"
    textColor: "#D3A6FA"
    border: "rgba(177,92,240,0.35)"
  badge-overlap:
    backgroundColor: "rgba(245,166,75,0.12)"
    textColor: "#F5B866"
    border: "rgba(245,166,75,0.30)"
  badge-needs:
    backgroundColor: "{colors.chip}"
    textColor: "{colors.ink-control}"
    border: "rgba(255,255,255,0.08)"
---

# Design System: Chippi

Mockups: [Chippi design directions canvas](https://claude.ai/artifact/CMBT4oFV3oGZRUei1d7K17). Direction **A · Studio Rack** and the **Gumdrop** blob pet are the chosen design. Directions B (Graphite Lab) and C (Burrow), and the earlier animal pets, stay on the canvas for reference only.

## 1. Overview

**Creative North Star: "The Studio Rack, with a resident."**

Chippi grows Hopper's design system into a whole suite. Hopper was one well-made unit in an audio rack. Chippi is the full rack: a dark chassis, precise monospace readouts, and one signal-colored accent that lights up only where something is live. The suite adds one thing the rack never had, a small resident. Gumdrop is a gradient blob with a face who lives in the island at the top of the screen. It naps when nothing is happening and reacts when Chippi does something for you.

The aesthetic stays **refined and restrained**. Depth comes from stacked tonal planes (backdrop → window → panel → surface → chip) and soft accent glows, never from gray drop shadows. The **Synth Gradient** (violet `#B15CF0` → azure `#4D9BFF`) is rationed to live, active and primary states. Gumdrop wears the gradient too, because the pet is the brand's face.

Chippi still rejects what Hopper rejected: **bloated vendor suites** (busy multi-pane dashboards, RGB everywhere), **stock Win32/WPF chrome** (gray dialogs, default checkboxes, group boxes) and **over-decorated SaaS** (gradient text, glassmorphism for its own sake, hero-metric panels). A suite with dozens of features makes the first trap easier to fall into. The tray panel is the guard against it: four tabs, one screen each, everything else in Settings.

**Key characteristics**
- Dark studio-chassis surfaces, layered tonally, with soft accent glows.
- One accent, the Synth Gradient, used only for live, active or primary states, plus the pet.
- Compact, fixed-width surfaces, dense but calm, built to be glanced at.
- Monospace numerals for every measurement.
- A single character, Gumdrop, carries the personality, so the chrome never has to.
- Dark is the identity. Light mirrors every token, and the island stays dark in both.

**Boundaries.** vorssaint-utils is the feature reference, not a visual one: its name, icon and overall look are covered by its trademark policy. Chippi's look descends from Hopper, and Gumdrop is drawn to be clearly its own (see §7).

## 2. Colors

### Accent
- **Synth Violet** `#B15CF0` and **Synth Azure** `#4D9BFF` live together as the **Synth Gradient**, `linear-gradient(135deg, #B15CF0, #4D9BFF)`. It is used for switch-on tracks, slider fills, the active tab glow, the icon chip of a live or installed item, live meters, the island's progress bars and Gumdrop's body.
- **Deep Synth** `#8E3FD0` → `#2A6AD6` is the same gradient darkened for fills that carry text or a glyph meant to be read: primary buttons and the island's play/pause button. White on it clears 4.5:1. White on the regular gradient only reaches about 3:1, which is why it never carries text.

### Signals
- **Signal Green** `#3DDC97`: active, online, routing, and done (a moved file's check, a valid rule). Status dots such as "Pinned · noise suppression on", with a soft glow.
- **Signal Amber** `#F5A64B`: attention. Files the organizer held back, "Overlaps Windows/PowerToys" badges and warning rows.
- **Signal Red** `#E23B4E`: destructive only, such as the close-button hover. Where it fills behind white text (the muted-mic button), use **Signal Red Deep** `#C42B3E`.

### Chassis (dark)
Backdrop `#0A0D18` · Window `#0B0F1F` · Titlebar `#0D1224` · Panel `#0F1426` · Surface `#131930` · Surface Selected `#1A2240` · Chip `#1B2240` · Input and segment trough `#11172C` · Track `#161D38` · Track Remainder `#22294A` · Switch Off `#262E4A` · Button `#151C34` (hover `#1C2444`). Hairlines are white at 5–8% alpha (`rgba(255,255,255,.05–.08)`), and up to 16% for strong borders.

**Island** `#05070F` is darker than the window, so the pill reads as a hole in the screen, like hardware. It does not change with the theme.

### Ink (dark)
Ink `#EEF1F8` for primary text · Ink Strong `#D6DBEC` for readout values · Ink Control `#C3CAE0` for button labels and icon buttons · Ink Secondary `#7E879F` · Ink Tertiary `#8A93AD` for captions, eyebrow labels, the output device under an app name and timestamps · Ink Muted `#7C849C`, the floor.

### Light theme
Every chassis and ink token has a light mirror (see `colors-light` in the front matter, taken from Hopper's `Light.xaml`). The Synth Gradient, signals and pet colors are identical in both themes. The focus ring darkens to `#8A3FD0` in light, and the island stays `#05070F`.

Three text colors change in light because the dark values fail contrast on light surfaces (approved 2026-10-21):
- **Active segment text** is Ink `#1A2036` (12.7:1), not white.
- **Source badge text** is `#7B2DB8` (5.5:1 or better on Surface and Panel).
- **Overlap badge text** is `#8A4B00` (5.7:1 or better). The same color is used for any amber **text** in light (held and failed reasons, validation messages); amber borders, dots and glyphs stay Signal Amber.
Badge tints and borders are unchanged.

### Named rules
**The Signal Rule.** The gradient marks what is genuinely live, active or primary: a switch that is on, a playing output, an installed feature's icon chip, the one primary action per view, and meters. It is never decoration, a divider or a background wash. **One exception:** Gumdrop's body, because the pet is the brand.

**The Deep Gradient Rule.** If the gradient carries text or a glyph meant to be read, use the deep stops (`#8E3FD0 → #2A6AD6`). This applies to buttons and play/pause, never to readable labels set *in* gradient (no gradient text, ever).

**The Muted Floor Rule.** `#7C849C` (dark) or `#6B7488` (light) is the quietest text allowed, and it still clears 4.5:1.

## 3. Typography

**UI font: Manrope** (OFL), bundled as static TTFs for weights 500, 600, 700 and 800, with Segoe UI Variable as the fallback. WPF can't select weights from a variable font and renders its default instance instead. Manrope's default is 200 (ExtraLight), which is why Hopper's copy only rendered ExtraLight.
**Mono font: JetBrains Mono** (OFL), bundled as static TTFs for weights 500 and 600, falling back to Cascadia Mono.

| Role | Size / weight | Used for |
|---|---|---|
| Display | 17 / 800 | Settings page titles ("Features") |
| Headline | 15.5 / 700 | The "Chippi" wordmark in the flyout header, current device on the status card |
| Title | 13.5 / 600 | Row and card names, setting titles, primary button labels |
| Body | 12.5 / 500 | Descriptions, secondary button labels, captions under island states |
| Caption | 11.5 / 500 | Output device under an app, status lines, folder-mode line |
| Label | 10.5 / 700, +0.13em, UPPERCASE | Eyebrows: NOW PLAYING TO, RECENTLY STASHED, FOCUS TIMER |
| Mono | 11.5 / 600 | Every number: volume %, timestamps, counts, speeds |
| Mono readout | 19 / 600 | The big value in a System tile (CPU 23%) |

**The Readout Rule.** Every measurement (percent, °C, GB, MB/s, mm:ss, counts) is set in JetBrains Mono. Prose never borrows the mono, and numbers never borrow the sans.

**The Quiet Ceiling Rule.** 17px / 800 is the largest text in the app. The only thing allowed above it is a numeric System readout at 19px mono. There is no hero type and no clamp-scaled heading.

Copy is sentence case, plain and short. State comes first and detail second: "Pinned · noise suppression on", "Held · remote-access installer", "9 features on · watching Downloads". No exclamation marks, no emoji.

**The Bare Label Rule** (Travis, 2026-10-22). A label names the action and nothing else. Never repeat the app's name where its icon already says it, and never pad a label with explanation. The Explorer entry is "Organize", not "Organize with Chippi". Buttons, menu items, toggles and notices follow the same rule. When a label seems to need a sentence, the design is wrong; fix the design, not the copy.

## 4. Elevation

Mostly flat, layered tonally, with colored glows for emphasis.

- **Window lift** `0 10px 26px rgba(0,0,0,.5)` (light `rgba(40,50,90,.22)`): the flyout and the Settings window.
- **Island lift** `0 8px 24px rgba(0,0,0,.45)` for the pills, and `0 16px 40px rgba(0,0,0,.55)` for the expanded island.
- **Card glow** `0 0 30px rgba(77,155,255,.16–.35)`: under the now-playing card.
- **Icon glow** `0 0 16px rgba(177,92,240,.5–.65)`: under a gradient icon chip and the primary island button.
- **Thumb glow** `0 0 10px rgba(177,92,240,.7)`: the slider thumb.

**The Glow-Not-Drop Rule.** Emphasis is a colored glow. A gray, downward shadow belongs only to whole windows and the island.

## 5. Surfaces

### Tray flyout
- **Placement:** anchored to the tray icon, 12px above the taskbar, right edge 16px from the screen edge. It closes on Esc, on click-away and when the tray icon is clicked again.
- **Size and frame:** 380 wide, up to 612 tall. Window color, 1px hairline, 16px radius, window lift, 14px padding, 12px gap between sections.
- **Header:** a 34px chip-colored square (10px radius) holding Gumdrop at 28px, idle. Beside it, "Chippi" in Headline over a Caption status line, and a settings icon button (32×30) on the right.
- **Tabs:** Sound · System · Tidy · Tools, in a 4-column segmented trough (4px padding, 4px gap, 11px radius). Each tab is 46px tall with a 17px icon over an 11px / 700 label, and the active tab uses the segmented-active glow. Tabs show only for installed features. Up to four fit, and more collapse into Tools.
- **Footer:** two secondary buttons, Settings and Quit.
- **Sound tab:** the NOW PLAYING TO card, then app volume rows, then the mic row.
- **System tab:** a 2×2 grid of stat tiles, a network row with a Speed test button, then the top processes by CPU.
- **Tidy tab:** the folder card with its mode segment (Silent / Manual / Paused) and mode line, RECENTLY STASHED rows with undo, then Organize now (primary) and Rules (secondary).
- **Tools tab:** a 3-column grid of tool tiles.
- **Empty state:** with nothing installed, a chip with an icon, "No features on yet", a Caption line, and one primary "Browse features" button that opens Settings › Features.
- **Tray menu (right-click):** a small flyout-style card, not the stock Windows menu. 12px radius, 36px items with a 16px icon and a 13 / 600 label, 8px hover radius. Items: Open settings, Quit. Its bottom edge sits at the click point, kept 12px off the taskbar.

### Island
- **Placement:** top-center of a display, floating 8px below the top of that display's work area (the top edge, unless a taskbar or appbar sits there). It is never attached to the edge and never covers a fullscreen app on its display. Which display is a setting in Settings › Island & pet: **Main display** (the default), **Follow active window**, or **One monitor** chosen by name. If the chosen monitor is disconnected, the island falls back to the main display (approved 2026-10-22).
- **Resting pill:** 84×34, holding Gumdrop at 28px, sleepy.
- **Live pill:** up to 292×38, with 6px left and 14px right padding. Gumdrop at 30px idle, then a Body-weight-700 title and a tertiary source ("Weekend Mix · Spotify"), then a 5-bar gradient equalizer. It shows one activity at a time in priority order: media, then timer, then download or stash, then agent, then status. **Status** is the lowest kind, for background state such as Keep awake; a timed session shows its countdown in mono.
- **Expanded:** 328 wide, 24px radius, padding 12 / 14 / 14, 10px gaps. It opens on hover (after a short delay) or click, and contains:
  1. A header: Gumdrop at 28px celebrating or idle, "Chippi" at 13 / 800 and a mono clock.
  2. Media: a 46px art tile (11px radius), title and source, a 4px gradient progress bar and mono times.
  3. Transport: previous and next at 32px (transparent) around a 38px deep-gradient play/pause with icon glow.
  4. A 2-column tile row (Panel color, 12px radius, label eyebrow plus value): FOCUS TIMER in mono, JUST STASHED in title text.
  5. An agent usage row: name, mono "62% of 5h · resets 4:10 PM" and a 5px gradient bar.
  6. Status rows (Panel color, 12px radius): one per Status activity, such as Keep awake, with the title left and its detail in tertiary right.

  Sections without data are left out. With nothing live at all, the expanded island shows one Body line in tertiary: "Nothing live right now". While Keep awake is on, the header pet is wide awake instead of idle.
- **Frame:** Island color, 1px `rgba(255,255,255,.08)` hairline, island lift.

### Settings window
- **Size and frame:** 1160×720 by default (resizable), custom titlebar on the Windows 11 frame. Corners and shadow are the system's (8px), not the 13px radius and window lift, so snap layouts, maximize and resize stay native.
- **Titlebar:** 44px in Titlebar color. Gumdrop at 22px in its small form, "Chippi Settings" at 13 / 700, and window buttons (32×30, 7px radius, Close hovers to Signal Red).
- **Sidebar:** 220px wide: General, Features, Island & pet, Shortcuts, Permissions, Appearance, About. Items are 36px tall with a 16px icon and a 13 / 600 label, the active one in the segmented-active style. The version and license sit in mono at the bottom.
- **Features hub:** a Display title and a Body description. A mono "N of M installed" count sits right. Below them are a preset segment (Essentials · Audio · Power user · Everything · Custom) and a 36px search field. The groups follow, each a Label eyebrow over a 3-column grid of feature cards (12px gap). Toggling any card switches the preset to Custom. The count includes Coming-soon features in M. Clicking an installed card, or pressing Enter on it, opens that feature's settings page.
- **General and Appearance:** the theme setting lives in General. Appearance links to General and to Island & pet rather than repeating their controls.

### Downloads organizer (approved 2026-10-22)
The approved mockups are `docs/phase-2/design/phase-2-mockups.html`, with one PNG per screen. They are the reference for every pixel below.
- **Tidy tab:**
  - The folder card is Surface, 14px radius and 13px padding. Its 32px chip is the gradient while the folder is watched and Chip color when Paused.
  - The folder name is a picker listing every folder with its mode, plus Add folder. RECENTLY STASHED shows all folders together.
  - The mode segment in the flyout is 34px tall with 12 / 700 labels.
  - Mode lines: "Manual · 3 to review", "Silent · moves after 30s", "Paused · not watching".
  - Empty state: "No folders yet", with the caption "New folders start in Manual", a primary "Add Downloads" and a secondary "Choose folder…".
- **Stash rows:**
  - Held: amber 45% border and a Review button, which opens preview.
  - Waiting: "Waiting · reason" in tertiary with a clock glyph and no action.
  - Failed: the reason in amber with a retry button, but no border, so the amber border always means held.
- **Preview** is a view inside Settings with a back button, not a dialog.
  - Each row has a checkbox, the file name with its size in mono, the rule and the destination. Warnings are an amber line under the name, and a move to another drive gets a neutral "Other drive" badge.
  - Held rows start unchecked. Files no rule matches show a disabled checkbox. The header checkbox shows mixed when some rows are selected.
  - The footer shows the selected count and total size, with Cancel and Approve.
  - After apply: a green check and Undo on each moved row, an amber failure line on each failed one, then "Preview again" and "Done".
- **Downloads organizer page:** a Settings · Rules · History segment at the top right. There is no description under the title, because the Features card already says what it does.
- **Rules editor:**
  - An accordion with one rule open at a time. Edits save when valid, with no Save button.
  - Drag handles to reorder, or Alt+Up/Down from the keyboard. The drop target is a 2px gradient line.
  - Regex and templates are set in mono, with placeholder tokens tinted like the source badge and insertable from chips.
  - Validation messages are amber, under the field.
  - A "test a file name" box runs the whole rule set and tags the winning rule with a "Match" badge.
- **History:**
  - Grouped by day, one Panel container per run, with runs over 10 files collapsed.
  - Undo per row and per run goes through an inline confirm bar ("Undo 4").
  - Clear history uses a popover with a Signal Red Deep "Clear" button.
- **Notices:**
  - Event kinds: Organized, Ready to review, Held, Failed, Archive imported.
  - Which events reach Windows notifications and the island is set on the app-wide **Notifications** settings page, not inside the feature (design pending, Travis 2026-10-22).
  - Notification copy states what happened first: "4 files organized", "1 file held", "1 file not moved", "photos-2025.zip imported".
  - Island stash pill: Gumdrop celebrates, then goes idle, and a folder glyph replaces the equalizer. Held and failed pills use amber source text and an amber dot.

## 6. Components

All interactive controls are custom-templated with the full state set: default, hover, keyboard focus (a 2px violet ring at 2px offset, which is non-negotiable) and disabled (opacity .45).

- **Primary button:** deep gradient, white Title label, 36px tall, 9px radius. One per view. Hover drops the fill to 92% opacity.
- **Secondary button:** Button color, hairline border, Ink Control text, 33–34px tall.
- **Icon button:** 30–32px square, 7px radius, transparent until hover. Always has an accessible name.
- **Switch:** 40×23 with a 12px radius. Off is `#262E4A`, on is the gradient, and the 17px white knob slides 17px over 0.18s. Hover lays a light wash over the track.
- **Segmented control:** an Input-colored trough with transparent inactive segments in Ink Tertiary. The active segment gets a soft 22% gradient fill, a 1px violet border at 55% and white text. Used for flyout tabs, folder modes and presets. Outside the flyout (presets, settings rows), segments are 36px tall with 13 / 700 labels.
- **Slider hover:** the 15px thumb grows to 18px around its center; the layout does not move.
- **Icon chips** (tool tiles, feature cards, flyout header) use the 10px chip radius.
- **App volume row:**
  - Grid of `32px | 1fr | 40px | 30px` with 18px and 14px rows. Surface color, 12px radius, 9 8 9 10 padding.
  - A 32px chip-colored monogram or app icon, then the name (Title, 13px) beside the output device (Caption, tertiary).
  - A mono value and a mute icon button span both rows.
  - The slider underneath has a 5px track: gradient up to the value, Track Remainder after, and a 15px white thumb with the thumb glow.
- **Now-playing card:** gradient `#171F40 → #121831`, 14px radius, 13px padding, card glow. Holds a 38px gradient icon chip with icon glow, device and detail text, and the equalizer.
- **Stat tile:** Surface color, 12px radius. A Label eyebrow, a mono readout with a mono caption, and a 2px azure sparkline 30px tall.
- **Stash row:** Surface color, 12px radius. The file name (Body 600, ellipsized) over its destination (Caption, tertiary), then a mono age and an undo icon button. A held file gets an amber 45% border and its reason in Signal Amber.
- **Tool tile:** 92px tall, 12px radius, 10px padding, with a 30px icon chip over the name and its state or shortcut. When on, the border is violet at 50%, the fill is a 16% gradient wash and the chip is the full gradient. When off, it sits on Surface with a hairline border. Names stay on one line and end in an ellipsis when too long (about 10 characters in three columns); the full name is the tooltip and the accessible name, so keep feature names short.
- **Feature card:** at least 128px tall, 14px radius, 13px padding.
  - A 34px icon chip (gradient when installed) with the switch top-right.
  - The name (Title) over a description (12px, tertiary), then badges.
  - Installed cards sit on Surface with a violet border at 40%. Uninstalled cards sit on Panel with a 5% hairline.
  - Coming-soon cards look uninstalled, with a disabled switch and a neutral "Coming soon" badge first in the badge row.
- **Badges:** 20px tall, 6px radius, 10.5 / 700.
  - **Source** (violet tint), e.g. "From Hopper".
  - **Overlap** (amber tint), e.g. "Overlaps Win+V", for features Windows or PowerToys already cover.
  - **Needs** (neutral), e.g. "Admin for temps" or "Needs VB-CABLE".
- **Checkbox** (approved 2026-10-22):
  - An 18px box with a 5px radius.
  - Off: Input fill with a 1.5px Ink Muted border.
  - On and mixed: the deep gradient with a white glyph.
  - Hover: an Ink Control border and Button Hover fill when off, 92% opacity when on. Focus and disabled states follow the rules above.
  - Use it for multi-select lists and grids. A single setting still uses the switch.
- **Text field:** the search-box chrome without the icon. Templates and regex use mono.
- **Drag handle:** Hopper's grip glyph, 16px. Alt+Up/Down moves the row.
- **Status dot:** 7px with a glow when live. Green for active, amber for attention, Ink Muted for offline.
- **Icons:** Lucide-style on a 24px grid, 1.8 stroke, round caps and joins, drawn at 15–17px (Hopper already ships `LucideIcons.cs`). Brand logos are never redrawn: an app shows its real icon, or a monogram tile when no icon is available.

## 7. The pet: Gumdrop

### Character
Gumdrop is a rounded drop of the Synth Gradient with two dark eyes, a white shine and a tiny mouth. It has no limbs, no accessories and no text. It is calm by default, quietly pleased when Chippi finishes something for you, and asleep when nothing is happening. It never interrupts, nags or needs feeding. It is a status light with a face.

It is deliberately different from vorssaint's companion. Gumdrop has a drop silhouette rather than a ball, the brand gradient rather than flat pastels, a shine highlight, and a mouth that changes with mood. Keep those four traits, and never reduce it to a plain colored ball with two dots.

### Geometry
Drawn on a 120×120 viewBox. These are SVG path strings, and WPF `Path.Data` accepts the same syntax.

| Part | Spec |
|---|---|
| Body | `M60 16 C80 30 100 52 100 77 C100 98 82 108 60 108 C38 108 20 98 20 77 C20 52 40 30 60 16 Z`, filled with the Synth Gradient at 135° (WPF `StartPoint="0,0" EndPoint="1,1"`) |
| Shine | Ellipse centered (46, 46), radii 9 × 5.5, rotated −28°, white at 38% |
| Eyes (idle) | Ellipses at (47, 74) and (73, 74), radii 4.8 × 6.2, pet ink `#0B0F1F`, each with a white highlight (r 1.7) at (+1.7, −2.3) |
| Mouth (idle) | `M56 88 Q60 91.5 64 88`, a 2.8 ink stroke with round caps |
| Ground shadow | Ellipse (60, 113), 30 × 4, black at 35%, at sizes of 64px and up only |

### Moods

| Mood | Eyes | Mouth | Extras | When |
|---|---|---|---|---|
| **Idle** | Dots, as in the geometry table | Small smile | None | The default when the island shows activity, in the flyout header and in Settings |
| **Celebrating** | Happy arcs `M42 75 Q47 68 52 75 M68 75 Q73 68 78 75` (4.2 stroke) | Open smile `M54 87 Q60 96 66 87 Z` (filled ink) | Blush ellipses at (38, 85) and (82, 85), 5.5 × 3.2, `#FF8FB0` at 70%. Spark strokes `M14 30 L6 24 M12 44 H3 M24 18 L20 9 M106 30 L114 24 M108 44 H117 M96 18 L100 9` (3.5, `#F5C451`). The body squishes to scale (1.06, 0.94) from the bottom center (60, 108) | The organizer stashed files, a timer finished, an agent task finished, a download completed. Holds about 2.5s, then returns to idle |
| **Sleepy** | Closed arcs `M42 74 Q47 78.5 52 74 M68 74 Q73 78.5 78 74` | Flat line `M57 89 H63` | A "z" in 20px Manrope 800 at (94, 30) and another in 13px at (106, 15), in Ink Tertiary | The resting island pill, after a quiet stretch with nothing live |
| **Wide awake** | Larger dots, radii 6.4 × 7.4, highlights r 2.4 | Small "o", an ellipse at (60, 89), 3 × 3.6, filled ink | None | While Keep awake is holding the PC up |

### Sizes
- **Small form, for the tray icon (16–20px) and the Settings titlebar (22px):** body plus eyes only, with eyes enlarged to 6 × 7.4. Drop the shine, mouth, highlights and extras, which are illegible at that size.
- **Island pill:** 28–30px, full detail. **Flyout header:** 28px. **Settings › Island & pet preview:** 112px with the ground shadow.

### Color option
Synth is the default and the brand. Settings › Island & pet can switch the body to a solid **Iris** `#CBB2FF`, **Azure** `#8EC0FF`, **Signal** `#8EE6C0`, **Ember** `#FFC58F` or **Frost** `#E8ECF6`. The eyes and mouth stay pet ink on every color, which keeps contrast at 5:1 or better.

### Motion
- **Blink:** every 4–7s at random, the eyes scale to 10% height for 120ms. This is the only ambient loop in the whole app.
- **Mood change:** eyes and mouth cross-fade in 150ms. Celebrating springs into the squish and back (about 400ms) while the sparks fade out over 1.2s.
- **Falling asleep:** the eyes ease shut over 600ms before the z's fade in.
- **Reduced motion** (Windows "Animation effects" off): no blink and no squish. Moods swap instantly with no tween.

### Accessibility
In the island and flyout header, the pet is decorative (`aria-hidden` / `AutomationProperties.AccessibilityView="Raw"`), because the text beside it carries the state. Where it stands alone, as in the Settings preview, it gets a name such as "Gumdrop pet, celebrating". It must never be the only signal of anything.

## 8. Do's and Don'ts

### Do
- **Do** ration the gradient to live, active and primary states and the pet: the Signal Rule.
- **Do** use the deep gradient stops under any text or glyph: the Deep Gradient Rule.
- **Do** set every measurement in JetBrains Mono: the Readout Rule.
- **Do** give every custom control a visible focus ring.
- **Do** keep status visible at rest: default device, mic state, folder mode and held files show without hovering.
- **Do** flag overlaps with Windows or PowerToys with an Overlap badge rather than hiding the feature.
- **Do** keep the tray flyout to four tabs, with one screen each.

### Don't
- **Don't** build a dashboard. If the flyout or a Settings page starts to feel like one, cut it back.
- **Don't** ship stock WPF chrome: no default checkboxes, group boxes or gray dialogs.
- **Don't** use gradient text, glassmorphism for its own sake, or hero metrics.
- **Don't** use a colored side stripe (a left or right border wider than 1px) to mark a row or card.
- **Don't** animate anything for its own sake. Only meters, switches, island transitions and the pet move, and the pet's blink is the only loop.
- **Don't** use emoji anywhere in the UI.
- **Don't** redraw other companies' logos. Use the real app icon or a monogram.
- **Don't** let the pet talk, show speech bubbles, or block content.

## 9. Implementation notes (WPF)

- **Theme keys:** keep Hopper's `ResourceDictionary` key names (`WindowBackgroundBrush`, `SurfaceBrush`, `AccentGradientBrush`, `TextTertiaryBrush`, ...) so Hopper's views move over unchanged. Add `AccentGradientDeepBrush`, `DangerDeepBrush`, `IslandBrush` and a `Pet*` set.
- **Pet control:** a `UserControl` with a `Mood` dependency property, the size forms above and one `Path` per part. Moods switch through visual states.
- **Island window:** borderless, `AllowsTransparency`, topmost, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, so it never steals focus or shows in Alt+Tab. It sits on the display the island display setting picks (main display by default; §5), and hides while a fullscreen app or game is in front on that display.
- **Flyout window:** borderless and topmost, placed from `Shell_NotifyIconGetRect`. It hides when it loses activation.
- **Reduced motion:** read `SystemParameters.ClientAreaAnimation` and listen for changes.
