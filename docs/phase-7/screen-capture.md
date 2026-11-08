# Screen capture: brief

Wave 1. Owner: Karl. Reviewer: Kayla. Task: 491. Status: proposed implementation contract, awaiting review and Travis's confirmation with Lumen's mockups. No feature code is authorized by this document alone.

[Phase decisions](README.md) (PR #281 while pending) override the research. References: [Capture research](behavior-spec-a.md), [architecture](architecture.md), [doors map](surfaces.md). The shared freeze, selector, magnifier, selection geometry and Print Screen ownership belong to Kayla's `capture-layer.md`. Lumen owns screens and glyphs in `design/wave-1.html`; developers own engines and wiring.

## Job and doors

Press Print Screen, select, mark up beside the selection, then Copy or Save. No separate editor window and no upload.

- Home: Capture after Clipboard, default order 75. Preserve the owner's saved order. With Displays the home has 12 tiles.
- Capture panel: 520 by 360 without scrolling. Region, Window, Screen, All displays, Freeform and Last region; header Delay (No delay, 3, 5 or 10 seconds); four Recent thumbnails; Open folder. Delay applies to the next mode. Capture after delay from another door first opens the mode/delay picker.
- Tools: Text, QR code and Color only when their providers are installed. Record, GIF and Convert appear only when wave 2 installs those providers. Missing providers leave no disabled placeholders. Capture is available when any capture-group provider is installed; screenshot modes and screenshot Settings require Screen capture. Removing the last provider removes the tile and panel command.
- Search: Capture opens the panel; each starter runs its mode; Recent captures opens Settings at the full list. Radial and the shared action picker expose the same starters. No Capture entries are added to Actions.
- Bar: success or failure line for six seconds, using the existing bar lifecycle. It never opens a result page automatically over a fullscreen app.
- Settings > Capture > Screen capture: output preference, Save to, file type, Ctrl-drag, magnifier, Recent, and read-only shortcut rows with Edit. Edit opens Settings > Shortcuts in that row. Full history lives here; Open recent captures lands on it.

Capture panel registration is shared by the installed providers, with one stable page identity and one panel command. Opening Text, QR code or Color delegates to that provider. Copy text and Scan QR in the screenshot toolbar pass the current selection to Herb's implementation without capturing again. Their result pages and strings belong to his briefs. Removing a provider cancels its pending work and removes its controls.

## What we took

These choices come from the research tables in behavior-spec-a.md and surfaces.md, not a new competitor trial.

- Lightshot: one key, drag, tools beside the selection, Copy/Save underneath, Ctrl-drag and Ctrl+A.
- Snipping Tool: the capture modes, delay, markup and text actions within the captured selection.
- ShareX: magnifier and repeat-last-region access; scrolling remains the separately approved wave 2 trial.
- Snipaste: hover a window or drag a region within one selector, as recorded in the doors map.
- Flameshot: arrow-key precision, Shift to resize and fixed tool letters.
- CleanShot X: one Capture home with recent results alongside its tools.
- Greenshot: configurable destinations and naming, annotation and opaque redaction.

## Selection and draft lifecycle

Close transient Chippi panels before the shared layer samples. Consume its frozen physical pixels and selection mask. Do not recapture while annotating. The shared layer owns click versus drag, window selection, last-region validation, cross-display geometry, Ctrl+A, keyboard nudging, countdown and toolbar placement. Window mode uses the agreed shared-layer path, including its elevated/protected-content limitations.

On release show the markup and output bars on the release display, clamped and flipped as in the doors map. All-display selections keep physical pixel geometry, including negative origins. PNG preserves freeform transparency and gaps. The draft remains memory-only until Copy or Save finishes. Escape, Cancel, lock or secure-desktop transition cancels and releases buffers without adding Recent. A second starter while a draft exists asks "Discard capture?" with Cancel and Discard before replacing it. A cancelled Save as dialog returns to the same draft.

## Markup

All edits use image-pixel coordinates independent of WPF DPI. Zoom and display scale affect presentation only. Changing selection bounds preserves objects in source coordinates and clips at export. Crop is undoable and cannot reveal pixels already removed by an earlier exported redaction.

| Tool | Behavior | Fixed key in context |
|---|---|---|
| Select | Select, move and resize an object; Delete removes the selected object | V; Delete |
| Pen | Freehand opaque stroke, selected color and width | P |
| Highlighter | Translucent stroke with stable opacity, including self-overlap within one stroke | H |
| Line | Straight line with adjustable endpoints | L |
| Arrow | Line with an arrowhead, adjustable endpoints | A |
| Rectangle | Outline or filled rectangle | R |
| Ellipse | Outline or filled ellipse | O |
| Text | Editable text box, color and size; commit before export | T |
| Redact | Fully opaque filled rectangle; never blur or translucent paint | B |
| Eraser | Remove a whole markup object under the pointer; never erase source pixels or reveal a redacted export | E |
| Crop | Adjust output bounds inside the captured source | C |
| Color and width | Palette and size controls for the active tool; redaction opacity stays opaque | Pointer or keyboard focus |
| Undo / Redo | One completed stroke, object edit, text edit or crop per step | Ctrl+Z / Ctrl+Y; Ctrl+Shift+Z also redoes |

Tool letters and selection arrows are suspended while editing text. Ctrl+A there selects text. Escape first leaves active text editing; the next Escape cancels the draft. Ctrl+C inside selected text copies that selection; outside text editing it copies the composed capture. Output buttons always commit text and export the image. Enter commits text while editing, otherwise finishes using the output preference. Initial preference is Copy. Each completed edit clears the redo branch. No emoji, stickers, blur or pixelation in this wave.

Flatten at source resolution before every clipboard, file, thumbnail and retained-image output. Redaction must cover the underlying pixels in every output, with no embedded source image, vector layer, undo history or recoverable metadata. Reopening a finished result starts from its flattened pixels. Editor undo can reveal the original only while that unsaved draft is open.

## Copy and files

Copy, Ctrl+C and Ctrl-drag copy the composed image. Save and Ctrl+S save directly to Save to. Save as and Ctrl+Shift+S ask for a path and type. Ctrl-drag bypasses the bars; Copy failure returns to the draft. Explicit output verbs perform only their named operation. Enter uses Settings' Copy, Save or Copy and save preference. This keeps fast-path Copy predictable even when Enter is set to save.

Save to defaults to the Windows Pictures known folder plus Screenshots, respecting folder redirection. Settings' Choose opens a folder picker; Cancel keeps the old path. Validate on use as well as selection. Create a missing folder only beneath the configured destination when access permits. A disconnected drive, denied folder or full disk reports Save failed and preserves the draft; no silent fallback to another location. Changing Save to affects future quick saves, never moves old files. Save as does not change Save to.

PNG is the initial type. JPEG is explicit, with a preview of the white background used to flatten transparency. Quick saves use `Screenshot-yyyy-MM-dd-HHmmss-fff.png` (or `.jpg`) with exclusive file creation and numeric suffixes on collisions. Save as uses the standard overwrite confirmation. Encode to a sibling temporary file, finish and close it, then publish atomically where supported. Remove only Chippi's partial file on failure. Never truncate an existing output before encoding succeeds.

Copy uses the shell's clipboard adapter with a composed bitmap and PNG representation where supported. Never export source layers. Clipboard contention uses bounded asynchronous retry, then Copy failed; keep the draft and Retry. No clipboard reads for validation. For Copy and save, execute each once and report partial success accurately: "Copied. Save failed" or "Saved. Copy failed". Retry reruns only the failed output. A successful output permits a single Recent entry even when the other fails; finishing a retry updates that entry.

## Recent: 50 finished captures

Keep the last 50 successfully copied or saved captures, newest first. Four appear in the panel; Settings shows all 50 with thumbnail, dimensions, time and saved-file status. Cancelled drafts and failed outputs add nothing. Repeated Copy/Save of one draft updates its entry rather than duplicating it.

Store a flattened retained PNG and precomputed thumbnail in the feature data folder, including for copy-only captures. Seal the index and thumbnail payloads with the established per-user sealing mechanism used by Clipboard history. Retained PNGs and user-saved files are ordinary local image files, not encrypted archives. Record that privacy boundary in the implementation report; it adds no Settings explanation row. Do not introduce a runtime dependency on the Clipboard feature: extract/reuse the sealing primitive after review, with a distinct capture key/store. Logs contain counts, durations and error codes, never pixels, OCR text, window titles or full saved paths.

Commit the retained image before its index entry. Recovery removes only orphan files inside the capture-owned store. A retention failure after successful Copy/Save reports "Recent capture failed" and does not falsely report the output failed. Index corruption is isolated and reported; never reset silently or delete user files. Panel open uses the already prepared thumbnails, with no full-image decode on the UI thread.

The 51st entry evicts the oldest index entry and its Chippi-owned retained image and thumbnail. Never delete an external saved file through retention. Reopening offers Copy, Save as, Annotate and Open folder. Annotate creates a new draft from flattened retained pixels. An externally deleted file shows "File missing"; the retained copy stays usable. Open folder uses the selected saved file's folder when available, otherwise Save to, and reports failure without launching a guessed path.

Remove recent capture removes the entry and its owned cache only. Clear recent captures asks "Clear recent captures?" with Cancel and Clear. Delete capture file is a separate context action for an existing saved file: "Delete <filename>?" with Cancel and Delete, sent to the Recycle Bin; failure leaves the file and entry. Revalidate file identity before deletion; if the path has been replaced, refuse and refresh. A deleted saved file becomes missing while its retained result remains available. No global delete binding.

## Shortcuts and verb inventory

Each starter is a shared command and a global slot, registered while its provider runs even when unassigned. Print Screen is the sole default; the shared-layer brief owns its Windows-setting lifecycle and clash handling. Fixed contextual keys never go through RegisterHotKey.

| Starter | Proposed command id | Default |
|---|---|---|
| Open Capture | `capture/open` | Unassigned |
| Capture region | `screen-capture/region` | Print Screen |
| Capture window | `screen-capture/window` | Unassigned |
| Capture full screen | `screen-capture/screen` | Unassigned |
| Capture all displays | `screen-capture/all-screens` | Unassigned |
| Capture freeform | `screen-capture/freeform` | Unassigned |
| Capture last region | `screen-capture/repeat` | Unassigned |
| Capture after delay | `screen-capture/delay` | Unassigned |
| Open recent captures | `screen-capture/recent` | Unassigned |

The shell Capture host owns `capture/open` and its shortcut slot. Providers register only their own starters. Open Capture remains available if Screen capture is removed but another provider remains.

In context only: all markup verbs in the table; Copy, Save, Save as, Finish (Enter), Cancel (Esc), Retry (focused button), Delay (focused picker), Choose (folder picker), Open folder, Annotate, Remove recent capture, Clear recent captures and Delete capture file (focused controls/context menu). Selection move is arrows; resize is Shift+arrows; Ctrl+A expands to the current display outside text. Tab and Shift+Tab reach every bar control, with accessible names matching the labels. Copy text and Scan QR use Herb's context actions. No row/result verb gets a global slot.

## Copy inventory

These are the feature's visible strings and accessible names. Tool names and starter labels are exactly those in the tables above. Dynamic values use localized numbers and dates. Windows supplies folder/file dialog and overwrite-confirmation text.

- Panel: Capture; Region; Window; Screen; All displays; Freeform; Last region; Delay; No delay; 3 seconds; 5 seconds; 10 seconds; Recent; No captures yet; Open folder; Text; QR code; Color. Future installed-provider labels: Record; GIF; Convert.
- Editor: Select; Pen; Highlighter; Line; Arrow; Rectangle; Ellipse; Text; Redact; Eraser; Crop; Color; Width; Size; Outline; Fill; Undo; Redo; Copy text; Scan QR; Copy; Save; Save as; Cancel; Retry. Size pill: `<width> x <height>`.
- Settings: Screen capture; After capture; Copy; Save; Copy and save; Save to; Choose; File type; PNG; JPEG; Ctrl-drag to copy; Magnifier; Recent captures; Shortcuts; Edit.
- Recent: Annotate; Remove recent capture; Clear recent captures; Delete capture file; File missing; `<width> x <height>`; `<time>`.
- Confirms: Discard capture? (Cancel, Discard); Clear recent captures? (Cancel, Clear); Delete <filename>? (Cancel, Delete).
- Bar/status: Copied; Saved; Copied and saved; Copy failed; Save failed; Copied. Save failed; Saved. Copy failed; Recent capture failed; Open folder failed; Delete failed; Capture failed; Capturing in <seconds>. A saved result includes the filename as secondary content, never a tooltip-style instruction.

Shared-layer refusals and countdown copy are finalized in `capture-layer.md`; OCR and QR copy is finalized in `text-and-qr.md`. Lumen resolves label fit in both themes before approval. Marge runs the consolidated copy through Decide before the installer.

## Shared code and ownership

Proposed implementation projects, to Marge before solution edits: `Chippi.Features.ScreenCapture` and `Chippi.Features.ScreenCapture.Tests`. `Chippi.Capture` is Kayla's shared project, not duplicated here. Add every new test project to Chippi.slnx.

- Core: catalog/module integration and shared capture provider contract; reuse commands, shortcuts and settings. No feature hooks or private hotkey registrar.
- Chippi.UI: consume the shared selector; markup/output controls and focus-aware contextual keys; sealing primitive reuse if extraction is needed. Lumen builds the approved controls, developers wire them.
- Island: single Capture page/provider host, tile, precomputed Recent thumbnails, result navigation and bar status. Keep existing saved tile order and fullscreen visibility rules.
- App: compose providers, clipboard and storage adapters, Settings navigation and shutdown cancellation. No UAC helper or new package for this feature.
- Shortcuts: consume Kayla's Print Screen exception and existing ShortcutSetting/Edit flow. No contextual shortcut service extension.

## Tests and acceptance

CI: geometry-to-export fixtures at 100%, 150% and 200%, negative origins, portrait and display gaps; freeform PNG and JPEG background; every tool and undo/redo; opaque redaction verified in clipboard bytes, encoded files, retained copies and thumbnails; crop and text focus; removed provider; duplicate starters; missing/detached targets; collisions, denied folders, disk full, cancellation and partial output recovery; 51-entry eviction, missing/replaced external files, corrupted index, orphan recovery and per-profile isolation. Use injected clocks and fake clipboard/file adapters. No owner's clipboard or real-file deletion.

Offscreen shots: Lumen's final mockup beside dark/light renders of the home, installed-feature panel variants, four/zero recents, delay, each tool, edge-flipped bars, full-display selection, JPEG transparency preview, Settings at full and 860 width, missing-file and partial-failure states. Render at actual sizes; no placeholder glyphs. Screenshot data is synthetic.

One completed-feature Sandbox run, clipboard redirection and networking off: seed a dev profile without Sound and 50 synthetic captures; open disposable target windows with distinctive pixels/text. Use Print Screen from Chippi and another app; draw and annotate, Undo/Redo, Copy and inspect the Sandbox clipboard, quick Save, Save as and cancel it; change Save to to a created test folder and confirm the next quick save lands there. Test Ctrl-drag, all modes, delay cancellation, redaction re-open, entry 51, Clear, external file missing, and Recycle Bin deletion of a throwaway saved file. Exercise Search, Radial, panel and shortcut doors. Kayla's shared spike is evidence for the freeze/Print Screen path; do not rerun it without a changed input path. Owner physical-display checks never copy private content outside Sandbox.

Targets below are proposals to measure in the implementation, not observed performance:

| Interaction | Acceptance target and measurement |
|---|---|
| Key to frozen screen | Target and measurement belong to `capture-layer.md`, set from Kayla's Sandbox spike before implementation approval |
| Release to both bars | p95 <= one 60 Hz frame (16.7 ms), with draft already filled |
| Markup handler | UI-thread time per pointer move: p95 <2 ms with a 3440 x 1440 frozen image and 50 objects already drawn; time handler entry to exit |
| Active stroke in Sandbox | No skipped frames during a 2-second stroke at 60 Hz; sample DWM frames and post moves once per frame; count only the active stroke |
| Physical 165 Hz display | Travis compares drawing feel with Snipping Tool in his live check; report his assessment, with no inferred pointer-to-photon measurement |
| Ctrl+C to clipboard ready | p95 <=150 ms for 3440 x 1440 composed pixels in Sandbox, including flattening |
| Capture panel open | Warm p95 <=100 ms, cold <=200 ms with 50 entries; four thumbnails already filled, no UI-thread full decode |

Record ten cold/warm opens and at least 30 active interactions per latency series; report hardware, dimensions, median, p95 and worst, separating encoding from clipboard contention. A native physical 165 Hz result cannot be inferred from Sandbox's refresh rate. The Displays panel and brightness slider timings belong to `displays.md`.

## Done means and not verified

Done means approved brief and mockups, one feature PR combining Lumen's screens with engine/wiring, touched tests run once in Release, x64/ARM64 builds and green CI, review, then the Sandbox use above with screenshots and timing report. Hand back exact commands and results, plus physical mixed-DPI/HDR/165 Hz gaps. No implementation, runtime use, screenshots or measurements have been performed for this brief. The shared-layer spike and final mockups are pending from their owners. Scrolling capture is a wave 2 trial, not a wave 1 mode.
