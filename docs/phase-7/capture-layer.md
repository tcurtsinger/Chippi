# Capture layer and selector: brief

Wave 1. Owner: Kayla (engine, overlay wiring, shortcut service). Overlay look: Lumen (`design/wave-1.html`, board 3). Reviewer: Karl. Task 490. Status: for Travis's confirmation with the wave 1 mockups. Nothing is built yet.

[Decisions](README.md) override the research. References: [behavior spec A](behavior-spec-a.md#proposed-selector-contract), [architecture §2](architecture.md#2-one-capture-layer), [surfaces §4](surfaces.md#4-capture). The features on top have their own briefs: [screen-capture.md](screen-capture.md) (Karl: markup, Copy and Save, Recent, the Capture panel), `color-picker.md` and `text-and-qr.md` (Herb). Recording (wave 2) uses this layer too.

Decided (Travis, task 479, 2026-11-06): Capture region gets Print Screen when Screen capture is installed. Chippi switches off Windows' "Use the Print screen key to open screen capture", records what it found, and puts it back if the shortcut changes or the feature is removed. Win+Shift+S stays Windows'. The shortcut service makes Print Screen bindable, alone and with modifiers, and nothing else bare.

## Job

One press, and every display freezes. Then select what you want: drag a region, click a window, take a display. The layer hands the selection and its frozen pixels to the feature that asked (Screen capture, Text and QR, Color picker), which shows its own bars beside it. Every capture feature gets the same freeze, the same selection and the same keys.

## Doors

The layer has no doors of its own: each feature's starters reach it (their briefs list them). Two pieces of the doors are this brief's:

- **Print Screen**, the only default key in Phase 7 (below).
- **The Capture host.** The shell owns the Capture tile and panel while any capture feature is installed (Screen capture, Text and QR, Color picker; Recording and Media tools in wave 2). The host owns the command `capture/open` and its shortcut slot, Open Capture, so the slot stays put whichever features come and go. Each feature registers only its own starters. The panel's content is Karl's ([screen-capture.md](screen-capture.md)); the tile and panel look is Lumen's.

## The freeze

1. **A starter fires** (a shortcut, a panel card, Search, a Radial slice). If the island is open, it closes first, and the freeze waits for its close to finish so the panel is never captured. Chippi's other transient windows (the Radial menu, the App switcher) are gone by then, since firing a verb closes them.
2. **A delay, if one was picked** (3, 5 or 10 seconds): the bar shows "Capturing in 3", counting down, and nothing else happens, so menus can be opened. A click on the bar line cancels it; so does pressing the same starter again. Esc doesn't, because Esc would also close the menu being opened.
3. **Read every display once** into memory. **The method is the spike's answer below.** The pointer isn't included.
4. **Read the window list** at the same moment: top-level windows in z-order from `WindowsWindowList`, visible and not cloaked, with frame bounds from `DWMWA_EXTENDED_FRAME_BOUNDS`. Chippi's own puck and the recording pill are left out. This list is a snapshot, so it matches the frozen image.
5. **Show one overlay per display:** topmost, at that display's DPI, covering the whole display (taskbar included), drawing the frozen image dimmed. The overlay takes the keyboard (Foreground, below).

Everything after that reads the frozen image and the snapshot list, never the live screen. Pixels stay in Chippi's memory until a feature saves; the layer releases them when the overlay closes. Travis's two displays are about 28 MB of pixels.

**What a feature gets back:** a `Selection` with the frozen pixels of each display it touches (BGRA, physical pixels), the selected rectangle in virtual-screen pixels, a mask for freeform, the source window (title, process path) when a window was clicked, each display's DPI and `DisplayCatalog` id, the time, and whether Ctrl was held at the start of the drag (Screen capture's fast path).

## Modes

| Mode | Selection | Notes |
|---|---|---|
| Region | Drag a rectangle. Hovering highlights the frontmost window under the pointer; a click (no drag past Windows' drag distance, `SM_CXDRAG`) takes that window | Region covers Window too, as ShareX and Snipaste do |
| Window | Hover and click only; a drag does nothing | The window's frame bounds, clipped to the displays it's on. Parts covered by other windows show what covered them: the pixels are the frozen screen's |
| Screen | The display under the pointer, selected at once | Bars sit inside the selection, since it fills the display |
| All displays | Every display's bounds, selected at once | Bars on the display under the pointer. Gaps between displays of different sizes are transparent in PNG |
| Freeform | Drag a closed outline; release closes it | The feature gets the bounding rectangle and the mask |
| Last region | The previous rectangle, on a fresh freeze | Remembered in physical pixels for the session and in settings. With no last region, Region mode starts. If the displays changed so the rectangle no longer lies on them, Region mode starts with the part still on screen selected |
| Pick a point | Click a pixel (Color picker) | No selection; the magnifier is always on |

Recording (wave 2) asks for Region or Window with the selection kept on the display where the drag started (surfaces §4, one display per recording). Screenshots may span displays.

**After release** the selection keeps its eight handles: drag them to resize, drag inside to move. Arrow keys move it a pixel and Shift+arrows resize from the right and bottom edges (Flameshot). Ctrl+A selects the whole display under the pointer (Lightshot). Each change redraws the bars' place and the size pill.

**The magnifier** follows the pointer while selecting: 8× over the frozen pixels around the pointer, with the pixel grid, the pointer's physical coordinates and the pixel's HEX. It flips to stay on the display. Settings › Screen capture can hide it, except for Pick a point. **The size pill** shows "1040 × 800" in physical pixels at the selection's corner, outside it when there's room.

The look of the dimming, handles, magnifier and pill is Lumen's board; they stay neutral so they read over any screen.

## Keys on the overlay

Fixed, in context only, never registered with `RegisterHotKey` (README, task 482):

| Key | While selecting | After release |
|---|---|---|
| Esc | Cancels; nothing is kept | Passed to the feature (Screen capture: leaves text editing, then cancels) |
| Enter | Takes the hovered window | Passed to the feature (finish) |
| Arrows, Shift+arrows | Move the pointer a pixel (fine aim) | Move or resize the selection a pixel |
| Ctrl+A | Selects the display under the pointer | Same |
| Ctrl held at drag start | Marks the selection as the fast path | |
| Everything else | Ignored | The feature's keys (Ctrl+C, Ctrl+S, tool letters) |

## The bars' placement

The feature supplies its bars' content (Screen capture: the 40-wide tool column and the output bar under it). The layer places them, so every feature's bars behave the same:

- **Tool column:** outside the selection's right edge, 8 apart; if it doesn't fit on that display, outside the left edge; if neither fits, inside the right edge.
- **Output bar:** under the selection, right-aligned with it; if it doesn't fit, above; if neither, inside the bottom edge.
- **On one display:** both bars sit on the display where the drag was released, clamped to it, never across two.
- **A selection that fills its display** (Screen mode, Ctrl+A, edge to edge) has both bars inside, as on Lumen's board 2b.
- They move with the selection and away from the handle being dragged, and hide while a handle or the selection is being dragged.
- They sit on the overlay window itself (no separate windows), so showing them costs one layout pass.

## Print Screen

**The shortcut service.** `KeyShortcut.IsBindable` today needs Ctrl, Alt or Win. It changes to: Print Screen (`0x2C`) alone or with any modifiers is bindable; nothing else bare. `KeyName` reads "Print Screen". The Settings › Shortcuts recorder already reads keys through the input hub, which sees Print Screen go down (Windows sends windows only its key up).

**Default.** When Screen capture is installed, Capture region gets Print Screen, unless another Chippi shortcut already uses it. It's the only Phase 7 slot with a default. Travis changes or removes it in Settings › Shortcuts like any other.

**Windows' setting**, "Use the Print screen key to open screen capture" (`HKCU\Control Panel\Keyboard`, `PrintScreenKeyForSnippingEnabled`; unset means on):

- **When Print Screen becomes a Chippi shortcut** (installing Screen capture with the default, or Travis recording it), Chippi reads the value, saves what it found (unset, 0 or 1) in its own settings, and turns the setting off. **How it turns it off so it takes effect at once is the spike's answer below.**
- **When Print Screen stops being a Chippi shortcut** (changed, removed, the feature uninstalled), Chippi puts back what it found, the same way. Chippi's uninstaller does the same, beside removing the Explorer entry (`Program.cs`, `OnBeforeUninstallFastCallback`).
- **When Travis turns the setting back on himself** while Chippi holds Print Screen: **the spike's answer decides** whether Chippi's shortcut still works. If it doesn't, the row in Settings › Shortcuts shows "Used by Snipping Tool" like any clash.
- **Only Chippi.exe in the real profile changes the setting.** Dev profiles and tests get a fake setting store, the way `ExplorerRegistration.CanChange` keeps tests off the real Explorer menu.

## Foreground

The overlay needs the keyboard (Esc, Enter, arrows, the feature's keys).

- After a `RegisterHotKey` shortcut (Print Screen included), it takes the foreground directly (Phase 6 rule).
- After a press the input hub swallowed (a Caps Lock combo, a mouse button, a Radial slice), it uses the marked 0x07 tap and one retry (`WindowsSwitcherPc.TakeForeground`).
- After an island click or Search, the island already has the foreground; the overlay activates while it still does, then the island hides.
- When the overlay closes, it hands the foreground back to the window that had it before the freeze (activate that window, then hide the overlays), so Ctrl+V lands where Travis was.

## Edge cases

- **A starter while the overlay is up:** before a selection exists it's ignored. After release, the feature decides (Screen capture asks "Discard capture?").
- **Lock, Ctrl+Alt+Del, a UAC prompt or a display change** while the overlay is up: it closes, nothing is kept, and the pixels are released.
- **Fullscreen games:** the shortcut service doesn't pause for fullscreen apps, so Print Screen captures a game. The overlay is a topmost window over it; a game in exclusive fullscreen may drop to the desktop while the overlay is up (inference; borderless games don't).
- **Windows that refuse capture** (`SetWindowDisplayAffinity`): they come out black or missing, as Windows gives them. The secure desktop can't be read.
- **Admin windows:** a capture of the screen includes them (the freeze reads the screen, not the window).
- **Mixed DPI:** each overlay runs at its display's DPI; selection math is in physical pixels throughout, so a region across the 3440 and 1920 displays cuts each display's own pixels.
- **Displays with gaps and negative origins** (a display left of or above the main one): the virtual-screen rectangle handles both.
- **HDR:** **the spike's method decides** how an HDR display is read. Whether Travis's OMEN runs HDR is unknown.
- **The clipboard's pixel limit** (16,588,800 pixels) is the feature's to refuse; the layer selects any size.

## Shortcuts

The layer adds one slot; each feature brief lists its own starters.

| Verb | Slot | Default |
|---|---|---|
| Open Capture | Global, `capture/open`, owned by the Capture host | Unassigned |
| Capture region | Screen capture's slot (Karl's brief) | Print Screen |
| Overlay keys | In context (table above) | Fixed |

## What we took

| Tool | Taken | Left out |
|---|---|---|
| Lightshot | One key, then drag; Ctrl held at the drag's start copies on release; Ctrl+A takes the display; bars beside the selection | Upload and search buttons |
| Snipping Tool | The mode set (region, window, screen, freeform) and delay; the screen frozen at the press | A separate editor window; its own key (it keeps Win+Shift+S) |
| ShareX | Hover a window to take it, drag for a region, in one mode; the magnifier; last region | Workflow builders, screen-color picking from the live screen |
| Snipaste | Window snapping while selecting | Snapping to controls inside a window (UI Automation would slow the freeze) |
| Flameshot | Arrow keys move the selection a pixel, Shift resizes | |
| Greenshot | Print Screen taken over by turning Windows' setting off, as its FAQ tells users to do by hand | A destination dialog after every capture |
| CleanShot X | The bars' look as part of the app, not a separate editor | The floating thumbnail after every capture |

## Copy

Every string the layer shows. Marge runs them through Decide before the installer.

- **Bar:** Capturing in <seconds>; Capture failed (the read failed, for example on the secure desktop).
- **Overlay:** "<width> × <height>" (size pill); "<x>, <y>" and "#RRGGBB" (magnifier).
- **Settings › Shortcuts:** Print Screen (key name); Open Capture (row); Used by Snipping Tool (clash, only if the spike shows it's needed).
- **Settings › Screen capture:** Magnifier (its switch is Karl's row; the layer reads it).

## Shared code

- **New project:** `Chippi.Capture` (WPF-free: the screen read, `Selection`, region math) and `Chippi.Capture.Tests`. Names to Marge before `Chippi.slnx`.
- **Chippi.UI:** the selector overlay (`Chippi.UI.Capture`: overlay windows, selection, magnifier, size pill, the bars' placement and keys). Lumen builds its look; I build the window, input and placement.
- **Core, shortcut service:** `KeyShortcut.IsBindable` and `KeyName` for Print Screen; the Print Screen setting store (an interface in Core, the registry version in App).
- **Chippi.UI, displays:** `DisplayCatalog.Changed` (shared with [displays.md](displays.md)).
- **App:** the Capture host (tile, panel host, `capture/open` and its slot) and the uninstall hook's restore of Windows' Print Screen setting.
- **Island:** nothing beyond the Capture tile's catalog order (75, after Clipboard).

## Spike results (Windows Sandbox, 2026-11-06)

PENDING: the spike runs once Travis's fullscreen game is closed.

## Tests

- **CI:**
  - region math: selections across displays with negative origins, gaps, portrait and 100/150/200% scale; cutting each display's own pixels; freeform masks; Ctrl+A; arrows and Shift+arrows at display edges
  - window snapping on a fake window list: frontmost under the pointer, cloaked and Chippi windows left out, click versus drag at the drag distance
  - Last region: kept, missing, partly off screen after a display change
  - the bars' placement: every flip, both bars inside a full-display selection, never across two displays, clear of the dragged handle
  - Print Screen: bindable alone and with modifiers, nothing else bare; the default only when no Chippi shortcut uses it; the setting store saves what it found, turns it off, puts it back on change, removal and uninstall, never touches the real registry from a dev profile
  - the delay: countdown, cancel by bar click and by the starter again, with an injected clock
- **Offscreen shots** (`CHIPPI_ISLAND_SHOTS`, both themes) over a synthetic frozen screen, beside Lumen's board 3: hovering a window, dragging with the magnifier and size pill, released with both bars, bars flipped at each edge, a selection filling a display, Ctrl-drag, the countdown on the bar.
- **Sandbox, once, with the finished wave 1 features** (`tests/Chippi.Input.Tests/Sandbox`, a new `capture` check; clipboard redirection and networking off): Print Screen with Windows' setting on, then after Chippi turns it off, with Chippi in front and another app in front; every mode; Esc; the delay and its cancel; the foreground handed back; key to frozen screen and release to bars timed on the final build. The clipboard checks are Screen capture's, in the same run.

## Done means

The layer ships inside the first wave 1 feature PR that needs it (Screen capture), since an engine without its first screen can't be used the way Travis will. CI green on x64, ARM64 and the test job; Karl's review, with a full review of the shortcut service change; then the Sandbox check above, with these timings:

| What | Target | How |
|---|---|---|
| Key press to frozen screen shown | PENDING from the spike (warm and cold, two displays) | Sandbox: `SendInput` time to every overlay's first frame presented (`DwmFlush`), 10 cold, 30 warm |
| Release to bars shown | One frame: 16.7 ms at 60 Hz, p95 | Sandbox: mouse up to the frame with the bars presented, 30 releases |
| Selecting at 165 Hz on the 3440 display | No skipped frames while dragging; UI-thread work per move under 2 ms p95 | Sandbox at 60 Hz with moves posted once a frame; Travis judges 165 Hz by feel against Snipping Tool |

Screenshots of each mode next to Lumen's board, and the timings report, go to the room. Travis's live check covers both displays and their scales, a region across them, Print Screen from his real keyboard, and a game in front.

## Not verified

- Everything under the spike's PENDING section until it runs.
- Mixed DPI, a region across two displays and negative origins on real hardware: the Sandbox has one display. Travis's live check.
- 165 Hz behavior: the Sandbox runs at 60 Hz.
- HDR reads: no HDR display in the Sandbox.
- Exclusive-fullscreen games under the overlay.
