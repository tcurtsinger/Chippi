# Screen recording: brief

Wave 2. Owner: Herb. For Travis to confirm. Decisions: [README](README.md) (task 487: MP4, silent unless switched on for that recording, cursor shown, one display per recording). Research: [behavior-spec-a.md](behavior-spec-a.md) (Screen recording), [architecture.md](architecture.md) section 3 (Encoders), [surfaces.md](surfaces.md) section 4 (Recording). The shared selector and freeze: [capture-layer.md](capture-layer.md). The panel and Recent: [screen-capture.md](screen-capture.md). Mockup: Lumen's boards `sheet-overlay` 3 and 4, `sheet-bar`, and the recording page (not yet drawn; she draws it before this is built). DESIGN.md wins where it speaks.

## Job

Record a region, a window or a whole display as an MP4 or a GIF with one key, stop with one key, and have the file saved and in Recent, with nothing of Chippi's own in the picture.

## Doors

| Door | What it does |
|---|---|
| **Capture panel: Record and GIF** | Start the selector in record mode. The panel closes first |
| **Capture panel: recording card** | While recording, it takes the modes row: red dot, elapsed time, source, Discard, Pause, Stop |
| **Recording page** | Opens after Stop: poster, play, trim bar, Discard, Export GIF, Save as, Save |
| **Shortcuts** | Record region, Record window, Record screen, Record GIF, Stop recording (below) |
| **Search / Radial menu** | Each starter verb as Run |
| **Actions** | None |
| **Bar** | "0:42 · Recording · Region" as the top activity while it runs. "Recording saved" with length and source after a Stop over a fullscreen app |
| **The pill** | A small window beside the recorded region: time, Pause, Stop |
| **Settings** | Capture › Recording |

## Behavior

### Start

Record region, Record window and Record screen use the shared selector in record mode (capture-layer.md): the selection stays on the display where the drag started, so a region never spans two displays in this build (README, one display per recording). Record window and Record screen record the display the window or screen is on. Record GIF is the same flow with GIF chosen.

After the selection, a bar under the region shows MP4 or GIF, System sound (off), Mic (off), Start and Cancel. Nothing records until Start. A 3-second countdown can be cancelled with Esc. The toggles start off each time: sound is never on unless switched on for that recording (Travis, task 487). The cursor is shown; Settings can hide it, and can show click rings.

### While recording

- **Frame and pill.** A 2 px red frame around the region and the pill just outside it. Both are their own top-level windows with `WDA_EXCLUDEFROMCAPTURE`, so they are not in the video. The pill is needed because the widget can sit on another display and hides while a fullscreen app is in front. The puck is left out of the video only while a recording runs.
- **Pause and Resume.** Frames and audio stop and the video has no gap: the timeline continues from the last frame written.
- **Stop.** Finalizes a playable MP4 and opens the recording page. Over a fullscreen app on the widget's display, nothing opens: the recording is saved to the Save to folder and added to Recent, and the bar says "Recording saved" (surfaces.md, Stopped over a fullscreen app).
- **Discard.** "Discard recording?" with Cancel and Discard. Deletes the temporary file only.
- **One recording at a time.** Starting a second shows "Already recording" on the bar and does nothing else.

### Recording page

Poster frame, a Play button (nothing plays on its own), a trim bar with two handles, and the buttons Discard, Export GIF, Save as, Save. Trim re-encodes, with a progress state and Cancel; a cancelled trim removes only its partial file. Save writes to the Save to folder without asking, Save as asks. Recent keeps the recording with a poster thumbnail and its length.

### GIF

Frames are recorded as a temporary MP4 (memory stays flat; a raw 3440 × 1440 frame is 20 MB) and turned into a GIF on Stop or from Export GIF, at 15 fps, with no audio and a 60 second limit (behavior spec A). A longer recording exports its first 60 seconds, or the trimmed 60, and says so before it starts. The palette is WIC's with frame differencing first, compared against a ScreenToGif GIF of the same clip on fixtures; Chippi's own quantizer is built only if WIC's gradients band visibly (architecture section 3). Progress and Cancel while encoding.

### Video

H.264 in MP4 through Media Foundation: WGC frames go to a `MediaStreamSource` and `MediaTranscoder` (Microsoft's SimpleRecorder sample, MIT, is the pattern), 30 fps, a hardware encoder when present and the software one otherwise. A region is cropped on the GPU from the display's frames before encoding. System sound is WASAPI loopback and the mic is WASAPI capture, lined up with the video on the performance counter (WGC `SystemRelativeTime`). They go to AAC as two tracks mixed into one. A Windows N edition without the Media Feature Pack gets "Recording failed" with the cause in Settings.

### Edge cases

- **Disk full, device loss, sleep or encoder failure:** keep the playable part where the file allows, show "Recording stopped" with what was kept, and add it to Recent.
- **Window moves or resizes during Record window:** the frame follows the window; the output size is fixed at the start, with the window padded or cropped to it.
- **Window closes:** the recording stops and is kept.
- **Secure desktop (UAC, lock screen) or protected content:** refused or blank by Windows. Chippi shows "Can't record this window" rather than saving a blank file as a success where it can tell, and says what it can't tell in Settings.
- **Display change** (unplugged, resolution): stops and keeps what was written.
- **Elevated window:** a standard-user Chippi may not capture it as a window; Record screen includes it (unverified, README hard limits).
- **A capture started during a recording:** the screenshot verbs are dimmed on the panel, since a freeze would cover the region. Convert stays usable.
- **Quitting Chippi while recording:** asks "Stop recording and quit?" with Cancel and Stop. Stopping finalizes the file.

## The WGC border check (first, before any recording code)

Kayla left this for wave 2 (capture-layer.md, spike results). The yellow border `IsBorderRequired = false` should remove is not verified off. Nothing below depends on the answer, but the frame and pill are positioned by it.

1. In Sandbox, call `GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless)` from an unpackaged build and record what comes back: allowed, a prompt, or refused.
2. Run a 10-second WGC session on a display with `IsBorderRequired = false`, and another with it left on, in the same Sandbox.
3. Judge by what the guest displays, not by a capture taken inside the session: a screenshot of the Sandbox window taken on the host, and Travis's eyes in his live check. A `BitBlt` inside the session proves nothing (capture-layer.md).
4. **If the border shows,** Chippi leaves it: Windows draws it around the recorded display or window. The frame and pill sit just inside it, and Settings › Recording says Windows draws the border. No workaround.

## Scrolling capture trial (task 480)

A short trial at the start of this wave, in its own spike with no PR, before recording is built. It ships only if the stitching holds up on real pages; otherwise it's left out and the report says why. Per behavior spec A: select one scrollable area, show Start and Cancel, capture with a visible Stop, stop on no progress or a size/time bound (30 seconds, 100 megapixels), then show a stitched preview before Copy or Save, and label a partial result. Never send wheel events to an unconfirmed foreground window.

- **Method:** WGC capture of the target window, wheel input sent to it through the input hub in steps, each step captured, and the frames stitched where they overlap.
- **Targets:** a browser page with a fixed header, Settings (a Windows page), a long document, a virtualized list, and a page with a looping animation.
- **Pass:** on each of the first three, the stitched image has no repeated or missing rows at the seams, fixed headers appear once, and the result is the right height, over five runs each. A virtualized list or an animation that breaks the stitch is detected and refused ("Can't scroll-capture this window") rather than saved wrong.
- **Fail:** any seam error on the first three that the overlap check doesn't catch. Then no scrolling capture ships.
- **If it passes,** two verbs join the inventory (Capture scrolling area, Stop scrolling capture, both global slots, unassigned) and Karl's screen-capture.md gets a section. This brief does not build them.

## Shortcuts

Slots under the starter rule (README, task 482). All unassigned.

| Verb | Slot | Notes |
|---|---|---|
| Record region, Record window, Record screen, Record GIF | Global | Start the selector in record mode |
| Stop recording | Global | With no recording, does nothing and the bar says "Not recording" |
| Pause recording, Resume recording | Global | Same refusal with no recording |
| Discard recording, Trim, Save, Save as, Export GIF | In context | Buttons on the recording page |
| Start (Enter), Cancel (Esc) | In context, fixed | The selector's own keys, never registered with `RegisterHotKey` |

Settings › Recording shows the slots read-only with Edit, which opens Settings › Shortcuts in that row. Nothing is recorded on the feature's page.

## What we took

- **Snipping Tool:** region first, a deliberate Start, and a result preview. Left out: the Clipchamp hand-off; Chippi never launches another editor to save.
- **ScreenToGif:** preview and trim before GIF export. Left out: its frame-by-frame editor.
- **ShareX:** separate Record video and Record GIF verbs, independent of any upload. Left out: its workflow graph.
- **Xbox Game Bar:** a stop shortcut and an unmistakable recording state (the pill). Left out: background replay recording.

## Copy

For Marge's Decide pass.

| Where | Text |
|---|---|
| Panel tools | Record, GIF |
| Start bar | MP4, GIF, System sound, Mic, Start, Cancel |
| Bar while recording | 0:42 · Recording · Region |
| Pill | 0:42, Pause, Resume, Stop |
| Recording card | Discard, Pause, Stop |
| Discard confirm | Discard recording? with Cancel and Discard |
| Quit confirm | Stop recording and quit? with Cancel and Stop |
| Page buttons | Discard, Export GIF, Save as, Save |
| Trim states | Trimming, Cancel |
| GIF over the limit | GIF is limited to 60 seconds |
| After a Stop over a fullscreen app | Recording saved · 0:42 · Region |
| Refusals | Already recording; Not recording; Can't record this window; Recording stopped; Recording failed |
| Settings title | Recording |
| Settings rows | Format, Frame rate, GIF limit, System sound, Mic, Cursor, Click rings, Save to, Shortcuts |

## Shared code

- **New:** `Chippi.Features.Recording` and `Chippi.Features.Recording.Tests`, both in `Chippi.slnx`. The recorder, the encoder pipeline and the GIF path live in a `Chippi.Media` library shared with Media tools (media-tools.md), also in the slnx. Names to Marge first. Module id `screen-recording`, `Capability.ScreenCapture`.
- **Capture layer:** the selector's record mode (display-bound selection) is Kayla's; this brief asks for it and follows her API. Frame and pill windows use the zone overlay pattern (`WindowLayouts/Views/ZoneOverlay.cs`).
- **Core / shell:** the bar's ambient state (a recording is the highest activity) and the puck hiding itself from capture while recording. Both are shared code (island, App), called out in the PR.
- **Settings:** one page (Lumen). The slots go through `ModuleContext.Shortcuts` with `ShortcutEdits`.
- **Package:** none new. Windows' own encoders only (README, task 484).

## Tests

- **CI, unit.** The recorder against fake frame and audio sources and a fake encoder: pause leaves no gap in timestamps, discard removes only the temp file, a second start is refused, a stop with no recording does nothing. GIF: frame differencing on fixture frames, the 60-second cut, 15 fps timing. Trim arithmetic. Timeline mixing of two audio tracks on a fake clock.
- **Offscreen shots** (`CHIPPI_ISLAND_SHOTS`): the Start bar, the pill, the recording card, the recording page (dark and light, with and without a trim), Settings.
- **Sandbox, once at the end of the wave.** A fixture window of known motion and a known tone; record region, window and screen; check the file by decoding it: frame count, dimensions, a known pixel at the start and end, Chippi's frame and pill absent from every sampled frame, audio present only when switched on and the tone found in it. Pause and resume (duration, no frozen gap), Stop over a fullscreen fixture, disk-full on a small volume, the window closing mid-recording, GIF export size and loop count. The border check above. Sandbox only; never the real clipboard or the owner's screen.

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
- HDR display recording.
- Scrolling capture, until its trial reports.
