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

The overlay windows (one per display: topmost, at that display's DPI, covering the whole display, taskbar included) are made when the first capture feature starts, and again after a display change, then kept hidden: a capture only shows them and swaps their image (a new window cost 45 ms in the spike). Every starter, whatever its door, runs the same steps:

1. **A starter fires** (a shortcut, a Capture panel card, Search, a Radial slice). The layer notes **the return window**, the one that gets the keyboard back when the overlay closes:
   - after a shortcut or a Radial slice, the foreground window at that moment
   - from the island (a panel card, or Search, which is the island's own page), the last window in front that wasn't the island's. `ForegroundTracker` already keeps it, since it ignores the island's windows.
2. **The overlays take the keyboard before anything is read.** They show with nothing drawn (every pixel fully transparent), and the one on the display under the pointer activates, using the right its door gives it (Foreground, below). From the island, this happens while the island is still on screen, so Chippi holds the foreground throughout the handoff (Phase 6 rule: activate the next window, then hide yours). With a delay, this step waits for the count to end instead.
3. **The island closes, if it's open,** and the freeze waits for its close to finish and one `DwmFlush`, so the presented frame no longer has the panel. An overlay already holds the foreground, so the island hiding doesn't let Windows activate another window. The Radial menu and the App switcher are already gone, since firing a verb closes them.
4. **A delay, if one was picked** (3, 5 or 10 seconds): the bar shows "Capturing in 3", counting down, and nothing else happens. Nothing of Chippi's holds the keyboard during the count, so menus can be opened. A click on the bar line cancels it; so does pressing the same starter again. Esc doesn't, because Esc would also close the menu being opened. At the end, step 2 runs.
5. **Read every display once** into memory with GDI `BitBlt` (`SRCCOPY | CAPTUREBLT`) from the screen DC, one call per display (the spike's answer, below). The pointer isn't included. The empty overlays are on screen, but a fully transparent pixel adds nothing to the read. **Inference:** the Sandbox check compares the frozen image with the fixture's known pixels. If an empty overlay shows up in the read, the overlays get `WDA_EXCLUDEFROMCAPTURE` instead, which keeps them out of every capture, Travis's screen shares included.
6. **Read the window list** at the same moment: top-level windows in z-order from `WindowsWindowList`, visible and not cloaked, with frame bounds from `DWMWA_EXTENDED_FRAME_BOUNDS`. Chippi's own puck, the recording pill and the overlays are left out. This list is a snapshot, so it matches the frozen image.
7. **Draw the frozen image,** dimmed, into the overlays already on screen.

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

**The shortcut service.** `KeyShortcut.IsBindable` today needs Ctrl, Alt or Win. It changes to: Print Screen (`0x2C`) alone or with any modifiers is bindable; nothing else bare. `KeyName` reads "Print Screen". The Settings › Shortcuts recorder already reads keys through the input hub, whose hook sees Print Screen go down and up whatever is in front (spike).

**Default.** When Screen capture is installed, Capture region gets Print Screen, unless another Chippi shortcut already uses it. It's the only Phase 7 slot with a default. Travis changes or removes it in Settings › Shortcuts like any other.

**Windows' setting**, "Use the Print screen key to open screen capture" (`HKCU\Control Panel\Keyboard`, `PrintScreenKeyForSnippingEnabled`; unset means on):

- **When Print Screen becomes a Chippi shortcut** (installing Screen capture with the default, or Travis recording it), Chippi reads the value, saves what it found (unset, 0 or 1) in its own settings, and writes 0. That takes effect at the next press: no sign-out, Explorer restart or broadcast needed (spike). It also broadcasts `WM_SETTINGCHANGE`, which costs nothing and is what putting it back uses.
- **When Print Screen stops being a Chippi shortcut** (changed, removed, the feature uninstalled), Chippi puts back what it found, by writing the value back (or deleting it, if it was unset) and broadcasting `WM_SETTINGCHANGE`, which took effect at once in the spike. Chippi's uninstaller does the same, beside removing the Explorer entry (`Program.cs`, `OnBeforeUninstallFastCallback`).
- **When Travis turns the setting back on himself** while Chippi holds Print Screen: Chippi's shortcut then fires only while a Chippi window is in front, and Windows' screen clipping opens everywhere else (spike). Chippi watches the value (`RegNotifyChangeKeyValue`) and doesn't turn it off again: the Capture region row in Settings › Shortcuts shows "Used by Windows", like any clash, until Travis records Print Screen again, which turns it off again. `RegisterHotKey` itself succeeds either way, so the value, not the registration, tells Chippi.
- **Only Chippi.exe in the real profile changes the setting.** Dev profiles and tests get a fake setting store, the way `ExplorerRegistration.CanChange` keeps tests off the real Explorer menu.

## Foreground

The overlay needs the keyboard (Esc, Enter, arrows, the feature's keys). It takes it in the freeze's step 2, before the read and before the island closes. What earns the right depends on the door:

- **A `RegisterHotKey` shortcut** (Print Screen included): `SetForegroundWindow` directly (Phase 6 rule).
- **A press the input hub swallowed** (a Caps Lock combo, a mouse button, a Radial slice), **or the end of a delay** (no input reached Chippi during the count): the marked 0x07 tap and one retry (`WindowsSwitcherPc.TakeForeground`).
- **A Capture panel card or Search:** the click or key went to the island, so Chippi's process may set the foreground, whether or not the island itself had activated (it's `WS_EX_NOACTIVATE` until a press asks). The overlay activates while the island is still on screen, then the island closes (step 3). If Windows refuses, the 0x07 tap and one retry.
- **If every try is refused** (a game took the foreground back), the capture goes on without the keyboard: the mouse still selects, and a click on the overlay earns the keyboard.

**When the overlay closes** (Esc, Copy, Save, a discard, a lock), it hands the keyboard to the return window from step 1: activate that window while the overlay still holds the foreground, then hide the overlays, so Ctrl+V lands where Travis was. From the island, that's the app he was in before he opened the panel, not the island. If the return window is gone or minimized, the overlays just hide and Windows picks.

## Edge cases

- **A starter while the overlay is up:** before a selection exists it's ignored. After release, the feature decides (Screen capture asks "Discard capture?").
- **Lock, Ctrl+Alt+Del, a UAC prompt or a display change** while the overlay is up: it closes, nothing is kept, and the pixels are released.
- **Fullscreen games:** the shortcut service doesn't pause for fullscreen apps, so Print Screen captures a game. The overlay is a topmost window over it; a game in exclusive fullscreen may drop to the desktop while the overlay is up (inference; borderless games don't).
- **Windows that refuse capture** (`SetWindowDisplayAffinity`): they come out black or missing, as Windows gives them. The secure desktop can't be read.
- **Admin windows:** a capture of the screen includes them (the freeze reads the screen, not the window).
- **Mixed DPI:** each overlay runs at its display's DPI; selection math is in physical pixels throughout, so a region across the 3440 and 1920 displays cuts each display's own pixels.
- **Displays with gaps and negative origins** (a display left of or above the main one): the virtual-screen rectangle handles both.
- **HDR:** what `BitBlt` returns on an HDR display isn't documented. If the live check shows washed-out captures on an HDR display, that display is read with WGC instead (kept warm, below). Whether Travis's OMEN runs HDR is unknown.
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
- **Settings › Shortcuts:** Print Screen (key name); Open Capture (row); Used by Windows (Capture region's row, while Windows' setting is back on).
- **Settings › Screen capture:** Magnifier (its switch is Karl's row; the layer reads it).

## Shared code

- **New project:** `Chippi.Capture` (WPF-free: the screen read, `Selection`, region math) and `Chippi.Capture.Tests`. Names to Marge before `Chippi.slnx`.
- **Chippi.UI:** the selector overlay (`Chippi.UI.Capture`: overlay windows, selection, magnifier, size pill, the bars' placement and keys). Lumen builds its look; I build the window, input and placement.
- **Core, shortcut service:** `KeyShortcut.IsBindable` and `KeyName` for Print Screen; the Print Screen setting store (an interface in Core, the registry version in App).
- **Chippi.UI, displays:** `DisplayCatalog.Changed` (shared with [displays.md](displays.md)).
- **App:** the Capture host (tile, panel host, `capture/open` and its slot) and the uninstall hook's restore of Windows' Print Screen setting.
- **Island:** nothing beyond the Capture tile's catalog order (75, after Clipboard).

## Spike results (Windows Sandbox, 2026-11-06)

A throwaway WPF program ran in Windows Sandbox (two runs, 09:13 and 09:20). Clipboard redirection and networking were off, and it never read Travis's screen. The Sandbox ran Windows 26100.9711 on one 2736 × 1111 display at 100%, with the host's RTX 4080 shared in (vGPU) behind the Remote Display Adapter. The program ran elevated, as the Sandbox's logon command does. Every key was a `SendInput` press, not a physical one.

**1. Reading the screen.** Ctrl+Alt+F10 was registered with `RegisterHotKey` and pressed with `SendInput`. On `WM_HOTKEY` the program read the display, then showed it dimmed in a new topmost window. The clock stopped when that window's first frame was rendered and `DwmFlush` returned. Each method ran in three fresh processes of 11 captures. A process's first capture is cold (device and interop set-up included); the other ten are warm.

| Method | Cold, key to shown (3) | Warm, key to shown (30) | Warm read alone | Image |
|---|---|---|---|---|
| `BitBlt` | median 70 ms, worst 126 ms | median 76 ms, p95 105 ms | median 30 ms, p95 35 ms | Right in every run |
| WGC (`CreateForMonitor`, a session per capture) | 663 to 726 ms | median 78 to 80 ms, p95 140 to 267 ms | median 53 to 56 ms, p95 89 to 233 ms | Right in every run |
| Desktop Duplication (a duplication per capture) | 320 ms | 47 ms | 7 ms | **Black.** The first frame of a new duplication held no desktop image (`LastPresentTime` 0). Waiting for one with a desktop image failed with `DXGI_ERROR_ACCESS_LOST` |

- **Answer: `BitBlt`.** It's simplest and fastest cold, and its warm times are as good as WGC's median with a far better tail. WGC's cold start (WinRT activation and a Direct3D device) costs over half a second. Desktop Duplication only has a desktop image once something on the screen changes, so a still on demand needs a duplication running all the time. It also gives one duplication per GPU, and Travis's displays may sit on two.
- **Most of the time is showing, not reading:** 45 ms median for a new WPF window with a 12 MB image. So the overlay windows are made once, when the first capture feature starts, and kept hidden; a capture only shows them and swaps their image (the freeze, above). **Inference:** that saves most of those 45 ms; the final build's Sandbox run measures it.
- **Travis's displays** have 2.3 times the Sandbox's pixels (7.0 million). **Inference:** a `BitBlt` read of both takes about 70 ms, since the read grew with pixels here. His live check judges it against Snipping Tool.
- **WGC is kept for recording** (wave 2) and for an HDR display if `BitBlt` washes it out.
- **WGC's yellow border: not verified off.**
  - **What the spike saw:** setting `IsBorderRequired = false` (through `IGraphicsCaptureSession3`) returned S_OK in all 33 sessions, from an unpackaged program, without calling `RequestAccessAsync`. A `BitBlt` taken mid-session showed no border.
  - **Why that proves nothing:** it's unknown whether `BitBlt` captures the border at all, so that shot looks the same either way. [Microsoft documents](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired) that the setter can succeed and still be ignored without the user's consent to borderless capture.
  - **Before anything relies on it** (the HDR fallback here, the recorder in wave 2): the wave 2 recording spike asks first with `GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless)`, records what an unpackaged Chippi gets (allowed, a prompt or refused), then runs a 10-second session with the border off. Whether a border shows is judged on the displayed output: a shot of the Sandbox window taken on the host (what the guest displays), and Travis's eyes in his live check. A capture taken inside the session doesn't count.
  - **If the border still shows,** Chippi leaves it: Windows draws it around the recorded display or window, and an HDR read shows it for the one frame the session lasts. No workaround hides it. The recording frame and pill are drawn just inside it.

**2. Print Screen.** Two windows: "Capture spike" (Chippi's stand-in) and "Other app" (a second process). Windows' key went through each state below; in each, five cases ran:
- (A) nothing registered, Other in front
- (B) `RegisterHotKey(VK_SNAPSHOT)` with Chippi's window in front
- (C1) registered with Chippi's window in front, pressed with Other in front
- (C2) registered and pressed with Other in front
- (D) nothing registered, a low-level keyboard hook (the input hub's kind) swallowing the key, Other in front

| Windows' setting | `RegisterHotKey` | `WM_HOTKEY` arrives | Windows' screen clipping opens | Hook sees it |
|---|---|---|---|---|
| Unset (on by default) | Succeeds in every case | Only with Chippi's window in front (B) | With Other in front (A, C1, C2) | Down and up, every case |
| 0, registry only (no broadcast, no restart) | Succeeds | Every case: B, C1, C2 | Never | Down and up |
| 0 plus `WM_SETTINGCHANGE` | Succeeds | B, C1, C2 | Never | Down and up |
| 0 plus Explorer restarted | Succeeds | B, C1, C2 | Never | Down and up |
| 1 plus `WM_SETTINGCHANGE` | Succeeds | Only B | A, C1, C2 | Down and up |
| 1 plus Explorer restarted | Succeeds | Only B | A, C1, C2 | Down and up |

- **Answers:**
  - With the setting on, the registration "succeeds" but works only while a Chippi window is in front, as Microsoft's page warns.
  - Writing 0 works at the next press, with no sign-out.
  - Writing 1 back (with the broadcast) gives the key back to Windows at once.
  - The hook always sees the key, and a hook that swallows it stops screen clipping even with the setting on (D, in every state).
- **What Chippi does:** writes the setting as above and binds Print Screen with `RegisterHotKey`, like every shortcut. The hub swallowing Print Screen also works, but the input hub pauses over fullscreen apps unless Always on is set, and a capture over a game is a main use. So it stays a fallback, not the plan.
- **Snipping Tool isn't in the Sandbox image.** The key opened Windows' own `ScreenClippingHost` ("Draw a shape to create a screen snip"). Travis's PC has Snipping Tool, and Microsoft names the same setting for both. **Inference:** it behaves the same there; his live check presses the real key with the setting off.
- **Not answered:** turning the setting off from the Settings app. The program couldn't find the switch through UI Automation, so how the Settings app writes it is unknown. The registry value is what Chippi changes either way.

The spike's program and its raw results (`timings.jsonl`, `prtsc.json`, a screenshot per Print Screen case) are in my session's temp folder and are deleted when this PR merges.

## Tests

- **CI:**
  - region math: selections across displays with negative origins, gaps, portrait and 100/150/200% scale; cutting each display's own pixels; freeform masks; Ctrl+A; arrows and Shift+arrows at display edges
  - window snapping on a fake window list: frontmost under the pointer, cloaked and Chippi windows left out, click versus drag at the drag distance
  - Last region: kept, missing, partly off screen after a display change
  - the bars' placement: every flip, both bars inside a full-display selection, never across two displays, clear of the dragged handle
  - Print Screen: bindable alone and with modifiers, nothing else bare; the default only when no Chippi shortcut uses it; the setting store saves what it found, turns it off, puts it back on change, removal and uninstall, never touches the real registry from a dev profile
  - the delay: countdown, cancel by bar click and by the starter again, with an injected clock
- **Offscreen shots** (`CHIPPI_ISLAND_SHOTS`, both themes) over a synthetic frozen screen, beside Lumen's board 3: hovering a window, dragging with the magnifier and size pill, released with both bars, bars flipped at each edge, a selection filling a display, Ctrl-drag, the countdown on the bar.
- **Sandbox, once, with the finished wave 1 features** (`tests/Chippi.Input.Tests/Sandbox`, a new `capture` check; clipboard redirection and networking off): Print Screen with Windows' setting on, then after Chippi turns it off, with Chippi in front and another app in front; every mode; Esc; the delay and its cancel; the foreground handed back; key to frozen screen and release to bars timed on the final build.
  - **From the island,** with Notepad in front before the panel opens: a Capture panel card, and Search with the starter typed and Enter. Each time, the frozen image has no island or overlay pixels (checked against the fixture's known pixels), the overlay holds the foreground before the read, and both Esc and Copy hand the foreground back to Notepad, not the island or another Chippi window. Once more with a 3-second delay.
  - **From a swallowed press** (a Caps Lock combo): the overlay gets the keyboard through the 0x07 tap, and Esc hands it back. The clipboard checks are Screen capture's, in the same run.

## Done means

The layer ships inside the first wave 1 feature PR that needs it (Screen capture), since an engine without its first screen can't be used the way Travis will. CI green on x64, ARM64 and the test job; Karl's review, with a full review of the shortcut service change; then the Sandbox check above, with these timings:

| What | Target | How |
|---|---|---|
| Key press to frozen screen shown | Warm p95 under 100 ms; the first capture after Chippi starts under 150 ms (the spike: 105 ms and 126 ms with a new window each time, on one 2736 × 1111 display) | Sandbox: `SendInput` time to every overlay's first frame presented (`DwmFlush`), 10 cold, 30 warm. From the island, the panel's close comes first: reported separately, not counted against the target. Travis's two displays: his live check, against Snipping Tool |
| Release to bars shown | One frame: 16.7 ms at 60 Hz, p95 | Sandbox: mouse up to the frame with the bars presented, 30 releases |
| Selecting at 165 Hz on the 3440 display | No skipped frames while dragging; UI-thread work per move under 2 ms p95 | Sandbox at 60 Hz with moves posted once a frame; Travis judges 165 Hz by feel against Snipping Tool |

Screenshots of each mode next to Lumen's board, and the timings report, go to the room. Travis's live check covers both displays and their scales, a region across them, Print Screen from his real keyboard, and a game in front.

## Not verified

- The spike's numbers on Travis's own displays: it ran in the Sandbox, on one display, through the Remote Display Adapter, elevated, with `SendInput` keys.
- Print Screen from a physical key, with Snipping Tool installed, and the Settings app's own switch.
- That reused overlay windows bring key to frozen screen under 100 ms (the spike made a new window each time).
- Mixed DPI, a region across two displays and negative origins on real hardware: the Sandbox has one display. Travis's live check.
- 165 Hz behavior: the Sandbox runs at 60 Hz.
- HDR reads: no HDR display in the Sandbox.
- That an empty overlay on screen adds nothing to the `BitBlt` read, and that it can be activated while empty (the freeze's steps 2 and 5). The Sandbox check proves both; `WDA_EXCLUDEFROMCAPTURE` is the fallback for the first.
- That WGC's yellow border actually turns off: the setter succeeded, but nobody looked at the screen (spike results, item 1).
- Exclusive-fullscreen games under the overlay.
