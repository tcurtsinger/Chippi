# Screen recording: brief

Wave 2. Travis authorized implementation on 2026-11-09 ("Ok let's build away"). These recording decisions supersede the older region, separate-pill, and silent-system-audio proposals in the research and early boards. Research: [behavior-spec-a.md](behavior-spec-a.md), [architecture.md](architecture.md) section 3, and [surfaces.md](surfaces.md). Shared capture infrastructure: [capture-layer.md](capture-layer.md). Capture panel and Recent: [screen-capture.md](screen-capture.md).

## Confirmed direction (2026-11-09)

- Serve quick clips for Discord and bug reports, narrated walkthroughs, and longer gameplay recordings.
- **Performance priority (Travis, 2026-11-09):** keep the impact on the rest of the computer as low as possible, especially gameplay. Recording quality and animation must fit a measured resource budget; no FPS/CPU claim is verified yet. See the performance requirements below.
- Manual Start and Stop only. No background replay or "save the last moments" buffer.
- Two targets: **Window** and **Screen**. No recording-region mode. One display per screen recording.
- The recording menu has **Include system audio** (on by default) and **Include mic audio** (off by default). No selected-app audio chooser or separate recording-device picker.
- Resolve the current default output and input devices when Start is pressed. The mic toggle captures the current input device; the menu names the current devices. Do not automatically switch devices mid-take initially.
- Use the **expanded Chippi puck** for recording controls. No separate floating control pill. Pause/Resume and Stop are icon-only, with tooltips and accessible names.
- The puck shows a larger red dot and elapsed time on one line, centered vertically in its 50-DIP height. Keep Pause/Stop close to the timer and center the visible dot/time/control group together in the space beside Chippi. No visible "Recording" label or second status line. The current mockup uses a 10-DIP dot and 20-DIP timer; these dimensions are implemented.
- Chippi remains at his usual end and opens the regular panel. Recording controls remain independently clickable, and the layout mirrors when the pet changes sides.
- **Chosen pet animation: handheld camcorder.** Travis selected the camcorder concept over the head-mounted camera on 2026-11-09. Chippi holds the little camcorder beside and below his face so his expression stays visible. The mockup's motion direction is a small eager lift at Start, quiet breathing and a soft camera tally-light pulse while recording, a relaxed pose/amber tally light on Pause, and a satisfied nod as the camera disappears on Stop. The timer's red dot stays steady. Reduced motion uses still poses and state changes. The approved camcorder artwork and animation are implemented; sustained performance remains unverified.
- Stop saves automatically to the recording folder and adds the result to Recent. Open preview normally; a fullscreen app is not interrupted or given a delayed automatic preview.
- MP4 is the primary output. The menu offers Original or 1080p resolution and 30 fps (default) or 60 fps. Cursor inclusion starts on. Sustained performance still needs proof.

Implementation now includes the approved puck, handheld camcorder, Window/Screen menu, system audio and optional mic, countdown, pause/resume, autosave, Recent, explicit-play preview, trim handles, Save as, and GIF export. Default output is up to 1080p at 30 fps. Nothing is installed or merged by this implementation alone. Remaining hardware and gameplay checks are listed below.

## Job

Record a window or one whole display, with optional system and mic audio; stop from the puck, a shortcut, or the Radial menu; save a playable MP4 into Recent with Chippi's controls excluded from the picture. Trim and GIF export use the saved result.

## Doors

| Door | What it does |
|---|---|
| **Capture panel: Record** | Open the recording menu: Window or Screen, source, audio switches, resolution, frame rate, Start |
| **Capture panel: recording card** | While recording, it takes the modes row: red dot, elapsed time, source, Discard, Pause, Stop |
| **Recording page** | Preview the saved file: poster, play, trim bar, Export GIF, Save as, Open folder |
| **Shortcuts** | Record window, Record screen, Pause/Resume recording, Stop recording |
| **Search / Radial menu** | The same shared start, pause/resume, and stop actions, reflecting current session state |
| **Actions** | None |
| **Expanded puck** | Vertically centered red dot and large timer, icon-only Pause/Resume and Stop, Chippi at his usual end. No separate pill |
| **Settings** | Capture › Recording |

## Behavior

### Start

Record window chooses a specific window. Record screen chooses one display; with only one display, omit the redundant picker. Use existing capture infrastructure where it fits, without exposing Region, Freeform, or All displays recording modes. Capture the chosen window itself rather than treating it as an arbitrary rectangle of its monitor.

The recording menu shows Window/Screen, the selected source, Include system audio (on), Include mic audio (off), Original/1080p resolution, and 30/60 fps. The output is MP4. Audio switches show the current default device names; there is no separate device picker or app-versus-system audio choice. System audio means all applications playing through the selected default output, not every physical output at once. Reset the audio switches to these defaults for each new recording. Nothing records until Start; a 3-second countdown can be cancelled. The cursor is shown; Settings can hide it and optionally show click rings.

### While recording

- **Puck controls.** The expanded puck takes priority over Now Playing and other activities while a recording runs. The larger red dot and elapsed time share one vertically centered row. There is no visible "Recording" text. Pause/Resume and Stop use icons only, with separate hit targets, hover feedback, keyboard focus, tooltips, and screen reader names. Pausing holds the timer, changes Pause to Resume, and uses an amber state indicator. The accessible state still names Recording or Paused.
- **Pet and dragging.** Chippi opens the regular panel using the existing Home/Last page preference. Pressing either recording control must not also open a page or trigger another control. Preserve the puck's drag behavior and mirrored layouts. Opening the regular panel does not stop the session; the Capture recording card and shared shortcuts remain available.
- **Capture exclusion and fullscreen.** Exclude the puck and any recording highlight from the output while the session runs. No additional floating control window is planned. Fullscreen games may hide or cover the normal puck: retain the Stop shortcut and Radial actions, and validate whether the puck can remain reachable without disturbing the game. This is an open technical check, not a promise of an exclusive-fullscreen overlay or an approved change to ordinary widget visibility.
- **Pause and Resume.** Frames and audio stop and the video has no gap: the timeline continues from the last frame written.
- **Stop.** Finalizes a playable MP4, saves to the Save to folder, and adds it to Recent every time. Open preview normally. Over a fullscreen app on the widget's display, save quietly; never open preview automatically later. Brief saved feedback may replace the timer once the puck is visible again.
- **Discard.** "Discard recording?" with Cancel and Discard. Deletes the temporary file only.
- **One recording at a time.** Starting a second shows "Already recording" on the bar and does nothing else.

### Recording page

Poster frame, a Play button (nothing plays on its own), a trim bar with two handles, and Export GIF, Save as, and Open folder. The original recording is already saved before preview opens. Trim re-encodes, with progress and Cancel; cancellation removes only its partial output. Preserve the original when trimming or exporting. Recent keeps the recording with a poster thumbnail and its length. Discard while capturing removes a temporary recording after confirmation; deleting an already saved result is a separate confirmed action.

### GIF

GIF exports from the saved MP4, at 15 fps, with no audio and a 60-second limit (behavior spec A). There is no separate GIF recording mode in the confirmed menu. Keep frames on disk rather than accumulating raw frames in memory. A longer clip needs an explicit range within the limit; do not silently truncate it. Start with WIC's palette and frame differencing and compare fixture output before deciding whether a custom quantizer is needed. Export has progress and Cancel and preserves the MP4.

### Video

Implemented pipeline: Windows Graphics Capture to bounded D3D11 surfaces, Media Foundation sink writer, H.264 plus optional AAC in fragmented MP4. The selected video encoder is inspected for its hardware-transform identity; production refuses an unverified or software encoder. There is no silent CPU fallback. System sound uses WASAPI loopback from the default output chosen at Start; optional mic capture uses the default input chosen at Start. One pause-aware QPC clock aligns video and audio. Native endpoint formats are normalized to stereo 48 kHz PCM, mixed, then encoded as AAC. Interrupted nonempty partial files are retained; long-session recovery still needs validation.

### Edge cases

- **Disk full, device loss, sleep or encoder failure:** keep the playable part where the file allows, show "Recording stopped" with what was kept, and add it to Recent.
- **Mic disappears:** keep video running and show "Microphone disconnected". Do not silently follow another input device mid-take. A default-device change alone does not replace a still-working device selected at Start. Validate output-device loss and speaker/earbud switching explicitly; surface a clear audio status rather than silently accepting missing sound.
- **Window moves or resizes during Record window:** the frame follows the window; the output size is fixed at the start, with the window padded or cropped to it.
- **Window closes:** the recording stops and is kept.
- **Secure desktop (UAC, lock screen) or protected content:** refused or blank by Windows. Chippi shows "Can't record this window" rather than saving a blank file as a success where it can tell, and says what it can't tell in Settings.
- **Display change** (unplugged, resolution): stops and keeps what was written.
- **Elevated window:** a standard-user Chippi may not capture it as a window; Record screen includes it (unverified, README hard limits).
- **A capture started during a recording:** the screenshot verbs are dimmed on the panel, since the frozen-screen overlays would cover the recorded content. Convert stays usable.
- **Quitting Chippi while recording:** asks "Stop recording and quit?" with Cancel and Stop. Stopping finalizes the file.

## The WGC border check (first, before any recording code)

Kayla left this for wave 2 (capture-layer.md, spike results). Borderless capture remains unverified for the unpackaged app. The plan uses the expanded puck, not the old frame-and-pill layout.

1. In Sandbox, call `GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless)` from an unpackaged build and record what comes back: allowed, a prompt, or refused.
2. Run a 10-second WGC session on a display with `IsBorderRequired = false`, and another with it left on, in the same Sandbox.
3. Judge by what the guest displays, not by a capture taken inside the session: a screenshot of the Sandbox window taken on the host, and Travis's eyes in his live check. A `BitBlt` inside the session proves nothing (capture-layer.md).
4. **If the border shows,** Chippi leaves it: Windows draws it around the recorded display or window. Place any optional recording highlight inside it; do not add a separate control pill or bypass the Windows border.

## Scrolling capture trial (task 480)

A short trial at the start of this wave, in its own spike with no PR, before recording is built. It ships only if the stitching holds up on real pages; otherwise it's left out and the report says why. Per behavior spec A: select one scrollable area, show Start and Cancel, capture with a visible Stop, stop on no progress or a size/time bound (30 seconds, 100 megapixels), then show a stitched preview before Copy or Save, and label a partial result. Never send wheel events to an unconfirmed foreground window.

- **Method:** WGC capture of the target window, wheel input sent to it through the input hub in steps, each step captured, and the frames stitched where they overlap.
- **Targets:** a browser page with a fixed header, Settings (a Windows page), a long document, a virtualized list, and a page with a looping animation.
- **Pass:** on each of the first three, the stitched image has no repeated or missing rows at the seams, fixed headers appear once, and the result is the right height, over five runs each. A virtualized list or an animation that breaks the stitch is detected and refused ("Can't scroll-capture this window") rather than saved wrong.
- **Fail:** any seam error on the first three that the overlap check doesn't catch. Then no scrolling capture ships.
- **If it passes,** two verbs join the inventory (Capture scrolling area, Stop scrolling capture, both global slots, unassigned) and Karl's screen-capture.md gets a section. This brief does not build them.

## Shortcuts

Shared actions power Shortcuts, Search, and the Radial menu. Recording's Pause/Resume slot is explicitly included by the 2026-11-09 direction; result/editor verbs stay contextual. All global slots start unassigned.

| Verb | Slot | Notes |
|---|---|---|
| Record window, Record screen | Global | Choose the source, then show the recording setup; a single display needs no picker |
| Pause/Resume recording | Global | Acts on the active session; Radial and puck show the appropriate state |
| Stop recording | Global | With no recording, does nothing and the bar says "Not recording" |
| Discard recording, Trim, Save as, Open folder, Export GIF | In context | Recording card and saved-result page |
| Start (Enter), Cancel (Esc) | In context, fixed | The selector's own keys, never registered with `RegisterHotKey` |

Settings › Recording shows the slots read-only with Edit, which opens Settings › Shortcuts in that row. Nothing is recorded on the feature's page.

## What we took

- **Snipping Tool:** a deliberate Start and a result preview. Chippi offers Window and Screen only; it never launches another editor to save.
- **ScreenToGif:** preview and trim before GIF export. Left out: its frame-by-frame editor.
- **ShareX:** local recording and GIF export independent of upload. Left out: its workflow graph.
- **Xbox Game Bar:** a stop shortcut and unmistakable recording state, shown on Chippi's puck. Left out: background replay recording.

## Copy

Current recording copy. Tooltips and accessible names remain descriptive even where visible controls have no text.

| Where | Text |
|---|---|
| Panel tool | Record |
| Recording menu | Window, Screen, Include system audio, Include mic audio, Resolution, Frame rate, Start recording |
| Expanded puck | Red dot and large elapsed time only; no visible Recording label |
| Puck controls | Pause/Resume and Stop icons; tooltips Pause recording, Resume recording, Stop and save |
| Recording card | Discard, Pause, Stop |
| Discard confirm | Discard recording? with Cancel and Discard |
| Quit confirm | Stop recording and quit? with Cancel and Stop |
| Page buttons | Play, Trim, Export GIF, Save as, Open folder |
| Trim states | Trimming, Cancel |
| GIF over the limit | GIF is limited to 60 seconds |
| Saved feedback | Saved; duration available in Recent and result preview |
| Mic loss | Microphone disconnected |
| Refusals | Already recording; Not recording; Can't record this window; Recording stopped; Recording failed |
| Settings title | Recording |
| Settings rows | Save to, Cursor, Click rings, Shortcuts; recording menu holds the audio switches and quality controls |

## Shared code

- **New:** `Chippi.Features.Recording` and `Chippi.Features.Recording.Tests`, both in `Chippi.slnx`. The recorder, the encoder pipeline and the GIF path live in a `Chippi.Media` library shared with Media tools (media-tools.md), also in the slnx. Names to Marge first. Module id `screen-recording`, `Capability.ScreenCapture`.
- **Capture layer:** reuse source selection and capture infrastructure for Window and Screen. No recorder-specific region selector or separate floating pill.
- **Core / shell:** extend the existing puck with recording controls, priority, mirrored layout, capture exclusion, and accessible state. Preserve the pet's normal panel destination and existing drag behavior. Fullscreen reachability is a spike/check item.
- **Settings:** one page (Lumen). The slots go through `ModuleContext.Shortcuts` with `ShortcutEdits`.
- **Package:** none new. Windows' own encoders only (README, task 484).

## Tests

- **CI, unit.** Fake frame/audio sources and encoder: pause leaves no timestamp gap; discard touches only the temporary session; refuse a second start; stop without a session is harmless. Check both audio defaults, default-device selection at Start, mic loss while video continues, and no silent mid-take device replacement. Test puck controls independently from pet clicks and dragging, both mirrored layouts, and shared shortcut/Radial actions. GIF range limits, timing, trim arithmetic, and mixing use fixtures and a fake clock.
- **Offscreen shots:** recording menu, expanded recording/paused/saved puck (large centered dot/time and icon-only controls), recording card, preview/trim, and Settings, in both themes. No separate pill.
- **Sandbox and owned fixtures:** record a known window and display with known motion and tone; decode output to verify dimensions, duration, frame timing, audio toggles/sync, and exclusion of Chippi's controls. Cover pause/resume, Stop over fullscreen, display/window loss, disk-full, forced interruption/recovery, GIF export, and the border check. Use test-owned content, not the owner's desktop or microphone. Hardware checks separately cover longer gameplay, actual GPU encoders, and speakers/earbuds with Travis.

## Performance requirements (2026-11-09)

Travis wants the least practical impact on his computer. Recording still needs GPU work, some CPU/audio work, memory, and disk bandwidth. Hardware encoder availability alone does not prove a cheap end-to-end path, particularly when a game is already GPU-bound.

- **Verify hardware encoding actually runs.** Prefer the GPU's H.264 encoder through a compatible Windows pipeline. Record the active encoder/adapter in diagnostics. A hardware-acceleration request is not proof that hardware was selected. Do not silently switch to CPU encoding, including mid-recording; decide the fallback behavior before implementation.
- **Keep frames on the GPU where possible.** Reuse capture surfaces and buffers, perform necessary scaling/color conversion on the GPU, and avoid per-frame CPU readback, screenshot/PNG conversion, full-frame managed allocations, or unnecessary transfers between adapters. Validate the actual pipeline rather than claiming zero-copy in advance.
- **Pace capture to the chosen output.** Use capture-side rate limiting where available and skip unnecessary frame work otherwise. A 165 Hz display does not require encoding 165 frames for a 30 fps file. Use bounded queues; release stale frames rather than accumulating latency and memory. Under overload, dropping a recording frame is preferable to growing an unbounded backlog. Preserve audio/timeline synchronization and report dropped frames.
- **Write encoded output incrementally.** Keep memory bounded as recording duration grows, batch ordinary file writes, and validate that recovery/finalization does not introduce sustained stalls. No raw-frame history, background replay buffer, live video preview, concurrent trim, or GIF conversion while capturing.
- **Keep the puck cheap.** Reuse small cached camcorder artwork and animate transforms/opacity rather than generating new bitmap frames. Do not tie pet animation to capture frame arrival or the display's refresh rate. Avoid rendering work while hidden and stop continuous recording motion when paused. Measure animation enabled versus disabled.
- **Recommended low-impact setting:** 1080p at 30 fps with hardware H.264. Original resolution and 60 fps remain options. This is the implemented default following authorization to build. Lower encoded resolution reduces downstream work; capturing the source at native size may still carry a cost.
- **No invented performance promise.** Do not claim zero impact, a fixed CPU percentage, or parity with another recorder until measured. Record CPU, GPU 3D/copy/video-encode utilization, RAM/VRAM, disk writes, output frame loss, and audio drift.

Before release, compare repeatable baseline runs with recording off against 1080p30, 1080p60, and native-resolution recording at the same game settings. Include a GPU-heavy workload, a CPU-heavy workload, ordinary desktop motion, audio switch combinations, longer sessions, and the puck animation on/off. Use repeated paired runs after warm-up; report average FPS, frame-time distribution and 1% lows as well as recorder resource use and output quality. A healthy average FPS must not hide new frame-time spikes. The initial fixture checks establish the pipeline; real-game/hardware measurements happen later with Travis, not as part of this planning work. Choose a numerical release budget from those measurements before declaring performance acceptable.

Implementation references: [Windows GPU capture frames](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture), [Media Foundation hardware transforms](https://learn.microsoft.com/en-us/windows/win32/medfound/hardware-mfts), and [NVIDIA's dedicated encoder architecture](https://developer.nvidia.com/video-codec-sdk). These document capabilities, not Chippi benchmark results.

## Build order

1. Prove capture, audio synchronization, hardware encoding, recoverable storage, border behavior, and fullscreen control access with owned fixtures.
2. Build Window/Screen setup, countdown, manual recording, pause/resume, stop/autosave, Recent, and shared actions.
3. Add the expanded puck controls and recording menu, preserving pet clicks and dragging.
4. Add result preview, trim, and GIF export without replacing the saved original.
5. Validate longer sessions, memory growth, dropped frames, audio drift, device changes, x64/ARM64, and installer behavior before release.

## Done means, and timings to set

Targets, none measured yet.

- **Key to recording started** (after the countdown): under 300 ms cold, 150 ms warm.
- **Frames:** 30 fps held on the 3440 × 1440 display with none dropped over 60 seconds, counted by the encoder's own timestamps. CPU read over 45 to 60 s three times, never a stress test.
- **Stop to page open:** under 1 s for a 60-second recording.
- **Trim and GIF export:** time and size recorded for a 30-second clip at 1080p and at 3440 × 1440.
- **Page open:** under 100 ms, already filled.
- **Proof:** the Sandbox results above with decoded frames, shots dark and light next to the mockup, and the timings.

## Not verified

- Whether the WGC border turns off for an unpackaged Chippi (the check above).
- A physical 165 Hz display and a second monitor; the Sandbox has one.
- Hardware encoder behavior on Travis's NVIDIA and AMD adapters, and the ARM64 path.
- Sustained 60 fps, longer-session stability, and recovery after an interrupted write.
- Puck access over real fullscreen games, and speaker/earbud/default-device changes during a recording.
- HDR display recording.
- Scrolling capture, until its trial reports.

## Implementation evidence (2026-11-09)

- Native NVIDIA H.264 hardware transform verified with synthetic GPU surfaces and synthesized PCM written only to a file. The file reopened through Windows Media APIs with video dimensions and a poster.
- End-to-end recording of an owned offscreen window on the host passed: native WGC, hardware encoding, pause/resume, Stop, playable MP4. No desktop or microphone capture and no playback.
- Unpackaged Sandbox WGC returned the expected blue/red owned-window pixels. Borderless access returned Allowed. This establishes the API result, not visual proof that the border disappears.
- Native trim produced a playable one-second copy; GIF export decoded to six frames for a 0.4-second, 15-fps range. Original MP4 remained intact.
- Unit coverage includes bounded audio buffers, pause timestamps, surround mapping, size calculation, original-file protection, countdown cancellation, single-session ownership, Stop, Discard, and partial-file retention. Puck tests cover both sides, distinct controls, and priority over Now Playing.
- Still required with real use: longer sessions, game frame-time/resource measurements, actual input/output disconnects, exclusive fullscreen, multi-monitor/HDR, and ARM64 hardware execution. Click rings are deferred; cursor inclusion is implemented.
