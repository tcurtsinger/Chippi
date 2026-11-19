# Scrolling capture: brief

Phase 7, task 480. For Travis to confirm. It replaces the trial in [screen-recording.md](screen-recording.md#scrolling-capture-trial-task-480), which never ran. Decisions: [README](README.md) (scope 478, shortcuts 471 and 482). The layer it sits on: [capture-layer.md](capture-layer.md) and [screen-capture.md](screen-capture.md). Mockup: [design/scrolling-capture.html](design/scrolling-capture.html). DESIGN.md wins where it speaks.

## Job

Capture an area taller than the screen (a long page, a chat, a settings list) as one picture, with no repeated or missing rows.

## The approach, and why

Research, 2026-11-18 (sources at the end). Scrolling capture is done two ways:

- **The tool scrolls** (ShareX, Snagit's Automatic, ScrollShot): capture, scroll a step, wait, repeat until nothing moves.
  - It fails where the app ignores the scroll input. Chromium ignores posted wheel messages and stops drawing when hidden. Elevated windows refuse input from a standard process.
  - It fails where focus can't be taken (ShareX's open bug #8837).
- **You scroll** (Snagit's Scrolling capture, formerly Panoramic; Capso): the tool watches the area and stitches the frames as they come.
  - It works in every app, because the tool sends nothing.
  - Its one failure is scrolling faster than a screen between frames, which can be detected.

Chippi does both, in that order:
- **By hand first**, because it works everywhere and sends no input.
- **Auto as an option** where it's reliable.

One stitcher serves both.

## Doors

| Door | What it does |
|---|---|
| **Finish bar** | Scroll joins Copy text and Scan QR code (lead group) after a region or window is released. Off for a freeform shape, and for an area that crosses displays ("One display, rectangle") |
| **Shortcut** | Capture scrolling area: opens the selector; release goes straight to scrolling (no finish bar) |
| **Search** | Capture scrolling area, as a Run row with the other capture modes |
| **Radial menu** | Through the shortcut, as every capture mode |
| **Capture panel** | Unchanged: its two rows are full. Recent shows scrolling captures ("Scrolling · 700 × 4,920") |
| **Settings** | Screen capture › Shortcuts: one new row, unassigned. No other settings |

## Behavior

### Starting

1. **Scroll pressed.** The overlay drops the frozen picture and the markup tools. The screen is live again.
   - The area keeps a 2 px accent frame just outside it.
   - Outside the area, a light dim (30%) shows what's left out.
   - The overlay is click-through everywhere except its bar and preview, so the page under the area takes the wheel, trackpad, keys and scrollbar as usual.
2. **The overlay is never captured.** The frame, dim, bar and preview windows set `WDA_EXCLUDEFROMCAPTURE`.
3. **Frames come from Windows Graphics Capture** on the area's display, through `Chippi.Media`'s capture interop: the same code as Screen recording, borderless as Recording requests it.
   - Frames arrive only when the screen changes.
   - Each one is cropped to the area in physical pixels.
4. **The first frame is the top of the result.** Chippi doesn't scroll to the page's top first. Scroll up before pressing Scroll for the whole page.

### By hand

- **The bar:**
  - the status ("Scroll" until the first movement, then nothing)
  - the result's size in physical pixels ("700 × 2,180")
  - Auto, Done (accent) and Cancel
- **The preview** sits beside the area: the result so far at a fixed 96 DIP width, growing downward.
  - The part the area shows now is framed.
  - Seams are dashed, so a bad one can be spotted before saving.
  - When it's taller than the area, it scrolls with the newest part in view.
- **Scrolling down** appends new rows. Scrolling up, or holding still, adds nothing.
- **Too fast:** if a frame no longer overlaps the last one used, the bar says "Too fast", and the preview marks the gap in Caution. Nothing is stitched over the gap.
  - Scrolling back until the area overlaps again catches up, and the status clears.
  - Done during "Too fast" keeps what was stitched before the gap.

### Auto

- **Offered when all of these hold:**
  - The area lies inside one top-level window.
  - The window isn't elevated (the input hub's integrity check) and isn't a browser render host: Chromium's `Chrome_RenderWidgetHostHWND`, Firefox's `MozillaWindowClass`.
  - UI Automation finds a vertically scrollable pane under the area's center (`ScrollPattern`, read only).
- **Otherwise** Auto is off, with the tooltip "Auto-scroll not available", and scrolling by hand still works.
- **How it scrolls** (ScrollShot's method):
  - Chippi posts `WM_MOUSEWHEEL` to the deepest child window under the area's center. It never moves the pointer or takes the keyboard, so Travis can keep working in another window.
  - The post goes through the shell's one action PC: a new `IActionPc.PostWheel(window, point, notches)`, marked as Chippi's own. No feature posts or sends input itself.
  - UI Automation is only read: `VerticalScrollPercent` gives the position, which predicts each step's shift and says when the end is reached. Its scroll methods are never called (they bring XAML apps to the front).
- **Steps:**
  - The first step is 3 notches. After that the step is sized so each new frame overlaps the last by about 40% of the area's height, 1 to 40 notches.
  - After each step, Chippi waits until two frames in a row match: at most 1.5 s, polled every 60 ms.
  - Smooth scrolling and a page that loads as you scroll both settle that way.
- **Ends:**
  - **End reached:** at 99.999% scrolled, or when two steps in a row move nothing. Done then takes focus.
  - **Stop:** keeps what was stitched and returns to scrolling by hand.
  - **Nothing moved:** a first step that moves nothing turns Auto off for this capture ("Auto-scroll not available") and leaves scrolling by hand.

### Limits

- **Size:** capture stops at 30,000 physical pixels tall or 80 megapixels, whichever comes first ("Limit reached"). Done keeps what was stitched.
- **Memory:** at 80 megapixels the stitched pixels take 320 MB. They're kept as strips and encoded to PNG in bands, so the whole image is never held twice.
- **Area changes:** the area's display changing (resolution, removal), a lock, or the secure desktop ends the capture and keeps nothing, as a frozen capture does.

### Done

- **The result** shows on the area's display, scaled to fit its height, with the size pill ("700 × 4,920") and the bar: Save as, Save, Copy (focused) and Cancel.
- **Copy and Save** follow Screen capture's rules: the save folder, the file type, Recent, and "When a capture finishes".
- **Esc or Cancel** keeps nothing.
- **Markup:** the tools are left out for a scrolling capture in this build, and so is Annotate on its Recent entry. Drawing on a picture many screens tall needs its own design.

## The stitcher

A pure function from frames to a result, with no Windows calls, so it runs in CI on synthetic frames. Rules, from ScrollShot, Capso and the open-source stitchers, which agree:

1. **Fixed parts by what doesn't move.**
   - On the first pair of frames that differ, rows that are equal at the top and bottom are the sticky header and footer.
   - Columns that match in place rather than shifted (each column votes) are fixed sidebars and the scrollbar.
   - The header goes in once, from the first frame. The footer goes in once, from the last frame. Fixed columns below the first screen take their own background color.
   - Shifts are searched only in the moving band, so a sticky bar can't pull the shift toward zero.
2. **Signed shift, two passes.**
   - **Candidates:** each row of the moving band becomes a signature: gray, 32 to 64 column bins. A row counts as informative when its range is over 24 levels.
   - **Confirm:** the mean absolute difference over the whole overlap strip, at half scale.
   - **Accept** only when all hold: at least 8 informative rows; overlap of at least a fifth of the band; the best error under 0.8 of the median candidate's; and no other candidate, other than its neighbors, within 15%.
   - **Narrowed when the position is known:** when Auto knows the scroll percentage, the search is ±12 px around the predicted shift.
3. **Never guess.**
   - An ambiguous or non-overlapping frame is rejected, and the reference frame doesn't advance.
   - By hand: "Too fast".
   - In Auto: one retry with half the step, then Stop with "Auto-scroll not available".
   - **Blank stretches:** one taller than the overlap is crossed only on Auto's predicted shift. By hand, the stitcher waits for a frame with something in it.
4. **Settle before matching.** A frame mid-animation (a smooth-scroll glide) is skipped until two frames match.

## Edge cases

- **Infinite feeds** never end; Done or the limit stops them.
- **Pages that move on their own:** a video, an animation or parallax leaves visible seams. The preview's seams show them before saving.
- **Pages that load as you scroll** are captured as they look when each part is reached.
- **A popup or menu opening** over the area while you scroll is stitched in like any content. Cancel and start again.
- **A pointer** over the area is left out of frames: WGC's cursor capture is off.
- **A window that's minimized, closed or resized** during Auto: the capture stops ("Done" keeps what was stitched).
- **HDR:** the area is read in WGC's SDR format, as Recording does.

## Shortcuts

| Slot | Default | What it does |
|---|---|---|
| Capture scrolling area | Unassigned | The selector; release goes straight to scrolling |

In context only: Enter is Done, Esc is Cancel, and the bar's buttons are reachable with Tab. Auto and Stop have no global slot.

## What we took

| Tool | Took | Left |
|---|---|---|
| Snagit | Scrolling by hand with a live result (its renamed Panoramic capture); Automatic as the second way | Its arrows over the window |
| ShareX | Stop when nothing moves; a size cap | Sending keys or wheel to the foreground window; scrolling to the top first |
| ScrollShot | Posted wheel to the deepest child; UI Automation read only for position and end; wait for two matching frames; column votes for fixed parts | `PrintWindow` capture (WGC instead); filling the whole viewport on a failed match |
| Capso | Sticky footer once; signed shifts; the reference frame not advanced on a failed match | Apple Vision |
| delorfin/stitch-screenshots | Confirming on the overlap strip with an acceptance margin | Its trackpad-gesture scrolling |

## Copy

- **Finish bar:** Scroll. Tooltips: "Capture scrolling area", with its keys once set; "One display, rectangle".
- **Scrolling bar:** Scroll · Too fast · Scrolling · End reached · Limit reached · "700 × 2,180" · Auto · Stop · Done. Tooltip: "Auto-scroll not available".
- **Result:** "700 × 4,920" · Save as · Save · Copy · Cancel.
- **Recent:** "Scrolling · 700 × 4,920".
- **Search:** Capture scrolling area · Screen capture.
- **Errors:** "Scrolling capture failed" (WGC refused or lost).

## Shared code (call out in the PR)

- **`Chippi.UI.Capture`:** the finish bar's Scroll button and the shortcut's straight-to-scrolling release. A `ScrollingSession` owns the live overlay, the bar and the preview.
- **`Chippi.Capture`:** the stitcher (`ScrollStitcher`, pure).
- **`Chippi.Media`:** a frame source for a display area on the existing WGC interop, beside the recording backend.
- **`Chippi.Core` / `Chippi.UI.Actions`:** `IActionPc.PostWheel` on `WindowsActionPc`. It posts to the given window only, and is refused for an elevated window. A posted message never passes the input hub's hooks, so the hub sees nothing and the pointer stays where it is.
- **Screen capture:** the Recent entry kind and its label; Annotate hidden for it.

## Tests

- **The stitcher, in CI, windowless (`Chippi.Capture.Tests`):** synthetic tall pages scrolled by known offsets. Each case stitches row for row against the source or is refused, never wrong. The cases:
  - a sticky header and footer
  - a fixed sidebar and scrollbar
  - repeating lines (line numbers)
  - a blank stretch taller than the overlap
  - frames caught mid-glide
  - scrolling back up
  - a fast scroll that leaves a gap
  - a frame identical to the last
  - a page that grows while scrolled
  - the limit
- **Timing reports:** `CHIPPI_SCROLL_TIMINGS` names a report of the stitch time per frame at 1440p and the final encode at 20,000 px.
- **Offscreen shots (`CHIPPI_ISLAND_SHOTS`, both themes):** each board of the mockup, over a synthetic screen, including the bar's states and the preview with a gap.
- **The overlay's exclusion from capture:** checked in the Sandbox. WGC frames must hold no frame, dim, bar or preview pixels.
- **Sandbox, once, at the end (`run.ps1 -Checks scrolling`):**
  - **Fixtures:**
    - a local HTML page in Edge with a sticky header and footer, a fixed sidebar, and 2,000 rows each carrying a colored index stripe, so the verifier can check that every row appears exactly once, in order
    - Notepad with 1,000 numbered lines
    - Windows Settings
  - **By hand:** the probe's `SendInput` wheel at several speeds, including one too fast.
  - **Auto:** on Notepad and Settings. Edge must show Auto as unavailable.
  - Every saved result is verified row for row.
- **Travis's live check:** a long web page and a chat, by hand and with Auto, on his displays.

## Done means, and timings to set

| What | Target |
|---|---|
| Scroll pressed to the first live frame | Under 800 ms cold (WGC's start, spike: 663 to 726 ms), under 200 ms warm |
| Stitch per frame, 1440p area | Under 10 ms median, so the preview keeps up with a frame every 33 ms |
| Auto step | Under 400 ms per step on Notepad (scroll, settle, stitch) |
| Done to the result shown, 20,000 px tall | Under 500 ms |
| Correctness | Zero repeated or missing rows on every Sandbox fixture, over five runs each |

## Not verified

- **Posted wheel messages:** whether every app Travis uses honors them. Each Sandbox fixture and his live check decide this per app. Auto turns itself off where nothing moves.
- **Chromium:** that it ignores posted wheel input comes from third-party reports (ScrollShot's README, a PowerToys-Run-Snip PR). It is checked on Edge in the Sandbox before the class list is fixed.
- **`WDA_EXCLUDEFROMCAPTURE`:** whether it hides the overlay from WGC monitor capture. Documented to; checked in the Sandbox.

## Sources

- [ShareX](https://github.com/sharex/sharex), [issue #8837](https://github.com/ShareX/ShareX/issues/8837), [issue #7063](https://github.com/ShareX/ShareX/issues/7063)
- [Snagit scrolling vs panoramic](https://snagitpro.com/snagit-scrolling-capture/)
- [crutkas/ScrollShot](https://github.com/crutkas/ScrollShot) (MIT)
- [Capso PR #309](https://github.com/lzhgus/Capso/pull/309)
- [delorfin/stitch-screenshots](https://github.com/delorfin/stitch-screenshots)
- [Row-signature stitching](https://dev.to/nan_nan_d86f644d31091c00f/scrolling-capture-implementation-row-signature-stitching-35a0)
- [PowerToys-Run-Snip PR #89](https://github.com/Jotaro2025/PowerToys-Run-Snip/pull/89)
- [UI Automation scroll pattern](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/implementing-the-ui-automation-scroll-control-pattern)
