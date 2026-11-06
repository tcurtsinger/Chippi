# Phase 7: Capture behavior research

Research date: 2026-11-06. Owner: Karl. Reviewer: Herb. Task: 465.

Status: research proposal for Step 2 decisions, not an approved implementation brief. Public documentation only; no competitor was installed or exercised on Travis's PC. Competitor behavior below is documented behavior, not a measured usability result. All Chippi behavior and numerical limits below are recommendations unless marked settled.

## Settled direction and common doors

Travis's bar is Windows Snipping Tool's features with Lightshot's ease of use. Capture has its own Tools tile and panel, bringing the home to 12 tools. The panel gathers capture modes, Record, Text and QR, Color, and recent results. Lumen owns the surfaces and fit at 520 by 360 with no scrolling; overflow opens Settings. Media jobs belong in the doors map before a separate tile is considered.

Every external verb below is a shared action, available to Search, Radial and the appropriate Actions menu, with a separate named shortcut slot in Settings > Shortcuts. Slots start unassigned. Feature pages show keys read-only with Edit. Print Screen is a pending opt-in decision; Windows shortcuts are not claimed implicitly. An action requiring a file or result opens its picker when invoked without one. Destructive or state-dependent actions validate their context before doing anything.

Local editor keys are contextual commands, not additional global registrations. They operate only while the overlay/result owns focus. The existing global shortcut service must never register plain Ctrl+C, Ctrl+Z or Esc for these commands. Customizable editor-scoped rows are a proposal requiring architecture work and an owner decision, not a capability of the current service. Fixed contextual keys remain usable without that extension.

## Screen capture

### Best of the field

| Tool and source | Documented flow and strength | What Chippi takes, proposed |
|---|---|---|
| [Snipping Tool](https://support.microsoft.com/en-us/windows/apps/use-snipping-tool-to-capture-screenshots) | Open the overlay, choose rectangle, window, full screen or freeform, then select. The app offers delay, pen, highlighter, shapes, crop, eraser and undo/redo, local text actions, automatic saving and Save as. | Cover those capture and editing jobs in the frozen-screen flow. Version-specific additions need a baseline check before claiming parity. |
| [Lightshot hotkeys](https://app.prntscr.com/en/hotkeys.html), [tutorials](https://app.prntscr.com/en/tutorials.html) | Print Screen starts selection. Select an area, annotate beside it, then Ctrl+C copies or Ctrl+S saves. Holding Ctrl when starting the drag copies immediately on release; Ctrl+A expands the selection to full screen. Its save shortcut uses the most recent folder without a Save As dialog. | Key, drag, markup in place, Copy or Save. Offer Ctrl-drag for immediate copy and Ctrl+A to expand the selection. Show the destination before quick saving. Keep Save as separate. Cloud upload stays out. |
| [ShareX region capture](https://getsharex.com/docs/region-capture.html), [scrolling capture](https://getsharex.com/docs/scrolling-screenshot.html) | Region selection supports a magnifier and last-region reuse. After-capture tasks can copy, save or edit. Scrolling capture selects a scroll area and automatically scrolls and stitches it. | Magnifier, repeat last region and explicit output policy. Scrolling is a separately approved capability, with a stitch preview. |
| [Greenshot help](https://getgreenshot.org/help/) | Print starts a region, drag and release selects it, then the editor/destination workflow offers annotation, highlighting and obfuscation. Output destinations and naming are configurable. | Predictable naming and destinations. Include solid redaction; avoid a mandatory destination dialog on every capture. |
| [Flameshot keys](https://flameshot.org/docs/guide/key-bindings/) | GUI keys select pen, line, arrow, rectangle, circle, marker and text. Arrow keys move selection by one pixel; Shift+arrows resize it. | Precise keyboard adjustment and discoverable tool keys in the overlay. Keep the selection adjustable after release. |
| [CleanShot X](https://cleanshot.com/) | The macOS product groups screenshots, annotation and recording in one tool. Its public product page is an ease-of-use reference, not evidence of Windows feasibility. | One Capture home for related results and tools. Lumen evaluates the preview and result treatment; no cloud service dependency. |

### Proposed selector contract

Starting from any door hides Chippi's transient panels before sampling. Freeze each display once for still-image selection; markup never exposes a live background beneath a redaction. Show the cursor's coordinates and selection size, with a magnifier that can be hidden. Use physical pixel dimensions for the output and account for each display's DPI independently. A spanning rectangle includes only captured display pixels, with a defined transparent gap between displays in PNG.

Region: drag in any direction, release to expose the nearby toolbar, then move or resize the selection. For the fast path, invoke Capture region, hold Ctrl before starting the drag, drag the region, and release to copy immediately and close on success. This bypasses markup and adds no save unless the output policy explicitly requests it. A failed copy keeps the draft. Ctrl+A within selection expands it to the current display, matching Lightshot; inside a text editor it selects text instead. Arrow keys move one pixel and Shift+arrows resize. Window: hover highlights the proposed window bounds, click freezes that choice, and review shows exactly what will be exported. Full screen: use the display containing the pointer; All displays is a separate verb. Freeform: drag a closed outline, then export the mask inside its bounding box. PNG preserves transparency; JPEG requires an explicit background.

Repeat last region samples fresh pixels at the previous physical rectangle. If the display layout changed, return to selection with an explanation rather than silently clipping it. Delay choices are proposed as Off, 3, 5 and 10 seconds, with a visible countdown and Cancel. Delay occurs before freezing, so menus can be opened. Capture after delay opens a mode-and-delay picker (region, window, screen, all displays or freeform), then starts that chosen mode. Delay is an option on those modes, not a sixth selection geometry. Esc cancels selection without saving, copying or adding a history item. A second capture request while editing offers to finish or discard the current draft; it never silently loses markup.

The toolbar stays on a visible display beside the selection and moves away from the active handle. It offers pen, highlighter, line, arrow, rectangle, ellipse, text, solid redaction, eraser, crop, Undo and Redo, then Copy, Save and Save as. Object selection permits adjustment before flattening. Redaction exports opaque pixels and strips hidden original pixels from exported artifacts. Blur/pixelation are optional cosmetic tools, never described as secure redaction. Emojis and decorative effects are parity extras for the scope decision, not prerequisites for the fast path.

Copy exports the currently composed result. Save uses the configured folder and a collision-safe timestamp name; Save as asks for a destination. Propose PNG by default and JPEG as an explicit lossy option. Cancellation creates no file. A failed copy/save leaves the draft open with a retry and an error ending in "failed". Successful Copy or Save ends the overlay. If both are configured, report partial failure accurately and keep the recoverable draft.

### Whole page decision

Recommend a bounded scrolling-capture feasibility spike before promising it in Phase 7. ShareX documents automatic scrolling and stitching, but that does not establish reliability across browsers, fixed headers, virtualized lists or animated content. It also changes the target's scroll position, unlike a frozen capture.

If approved: select one scrollable area, show Start and Cancel, capture with a visible Stop, stop on no progress or a size/time bound, and show a stitched preview before Copy/Save. Label partial results. Never send wheel events to an unconfirmed foreground window. Compare overlap seams and fixed headers; reject an untrustworthy stitch. Propose 30 seconds and 100 megapixels as initial safety bounds for the spike, not shipping guarantees. Defer browser DOM capture/extensions. "Full screen" and "whole scrolling page" stay distinct in labels and shortcuts.

### Results and retention decision

Recommend local results with thumbnails, type, dimensions/duration, time and file status. Reopen should provide Copy, Save as, Annotate and Open folder. A missing file is shown as missing. Removing a recent entry and deleting a saved file are different commands. Never delete external files through a retention policy.

Pending owner choices: Copy, Save, or both after finishing; destination; whether history retains image content; count/age/byte limits; delete behavior. Proposed conservative default is explicit Copy/Save and a recent-file index only, with no second hidden image archive. An unsaved draft is discarded when explicitly cancelled. A history shortcut opens the list; it does not copy the latest private image automatically.

Snipping Tool parity also needs a version check: Microsoft announced [GIF export in an Insider rollout](https://blogs.windows.com/windows-insider/2025/06/19/gif-export-in-snipping-tool-begins-rolling-out-to-windows-insiders/). That announcement alone does not prove availability on the owner's installed build. The plan calls for history; this research treats Chippi history as a product requirement, not a verified Snipping Tool history feature.

## Screen recording

### Best of the field

| Tool and source | Documented flow and strength | What Chippi takes, proposed |
|---|---|---|
| [Snipping Tool](https://support.microsoft.com/en-us/windows/apps/use-snipping-tool-to-capture-screenshots) | Win+Shift+R or Record > New selects an area; Start begins recording. The preview offers further editing in Clipchamp. | Region-first recording with a deliberate Start and a useful result preview. Do not launch another editor just to save. |
| [ScreenToGif repository](https://github.com/NickeManarin/ScreenToGif) | Records a selected screen area, edits it, and saves GIF or video. The website currently redirects to another product; the repository is the research baseline. | Preview and trim before GIF export. A full frame-by-frame animation editor is a scope extra. |
| [ShareX project](https://github.com/ShareX/ShareX) | Combines video/GIF capture with configurable capture workflows. Its development branch may describe unreleased behavior. | Separate Record video and Record GIF verbs; keep ordinary recording independent of upload workflows. No assumption about its current encoder implementation. |
| [Xbox Game Bar accessibility guide](https://support.microsoft.com/en-us/accessibility/windows/use-a-screen-reader-to-record-your-screen-with-xbox-game-bar) | Keyboard-driven app/game recording, start/stop with Win+Alt+R, microphone control, and saved clips. | A stop shortcut and unmistakable recording state. Background replay recording is outside the proposed Phase 7 scope. |

Propose region, window and one-display recording, MP4 video and GIF export. A region stays fixed in screen coordinates; a window capture follows the chosen window only if architecture confirms reliable support. Show bounds and selected audio sources before Start, a cancellable countdown, then elapsed time and Pause, Resume, Stop and Discard on the bar. Stop finalizes a playable draft and opens preview with Trim, Save, Save as and Export GIF. Discard asks "Discard recording?" with Cancel and Discard. Keep recording controls out of the output.

System audio and microphone are separate explicit toggles, both initially off. Never play the result automatically. Cursor inclusion is explicit; click indicators are optional. GIF has no audio. Propose 30 fps video, 15 fps GIF and a 60-second GIF export limit initially; encoder, file-size and long-recording limits await architecture and measurement. Show progress and Cancel during encoding. A cancelled export removes only its partial output. Disk-full, device loss, sleep and encoder failure must preserve a playable partial result where possible and report what was recovered. Secure desktop and protected content are unsupported, with visible refusal rather than a successful blank result.

## Text and QR

### Best of the field

| Tool and source | Documented flow and strength | What Chippi takes, proposed |
|---|---|---|
| [Snipping Tool text actions](https://support.microsoft.com/en-us/windows/apps/use-snipping-tool-to-capture-screenshots) | Text actions performs local OCR, supports selected/all text copying and quick email/phone redaction. | Local OCR and editable review. Detection-based redaction is an aid, not a guarantee that all secrets were found. |
| [PowerToys Text Extractor](https://learn.microsoft.com/en-us/windows/powertoys/text-extractor) | A shortcut selects screen text, including text in images/video, for clipboard use. | A direct Copy text from screen verb with the shared selector. Show recognition failures without overwriting the clipboard. |
| [ShareX project](https://github.com/ShareX/ShareX) | Lists OCR and QR scanning among its screen tools. | Decode locally into a review result. The decoded payload is data, never an instruction to launch automatically. |

Copy text from screen selects an area, recognizes it locally, and copies the recognized text on success. Extract text instead opens editable review with Copy all and selected-text copying. Make OCR language selectable from installed supported languages; show missing-language guidance without installing anything. Preserve paragraphs where possible; do not promise tables or perfect reading order. Empty recognition leaves the clipboard alone. Provide Retry with the same region and Reselect.

Scan QR selects an area, lists multiple detections separately, and shows the full payload and type. Copy is explicit. Open link is a second action after review, restricted to supported web URLs with the destination visible; never auto-open payment, shell or app commands. No detection and invalid payload are separate states. QR generation and additional barcode families are scope extras.

## Color picker

### Best of the field

| Tool and source | Documented flow and strength | What Chippi takes, proposed |
|---|---|---|
| [PowerToys Color Picker](https://learn.microsoft.com/en-us/windows/powertoys/color-picker) | Shortcut activates picking; select a pixel and copy in a configured format. Its editor supports color formats and history. | Magnified pixel selection, a preferred copy format, and a small local palette. |
| [Flameshot keys](https://flameshot.org/docs/guide/key-bindings/) | The annotation UI has a screen-color picking command. | Reuse the same picker for annotation ink and the standalone action. |

Pick color shows a magnifier and live format value, click copies the preferred format, Esc cancels. Pick and review instead opens HEX, RGB and HSL values with individual Copy controls and a recent palette. Propose HEX as default, with CSS RGB/HSL choices. Define HEX as sRGB #RRGGBB; report sampled screen color rather than claiming the source asset's original color. HDR and color-managed displays need measured conversion tests. Sampling must exclude the picker itself. Keyboard arrows move the sample by a pixel and Enter accepts.

## Media tools

### Best of the field

| Tool and source | Documented flow and strength | What Chippi takes, proposed |
|---|---|---|
| [PowerToys Image Resizer](https://learn.microsoft.com/en-us/windows/powertoys/image-resizer) | Select one or more files in Explorer, choose Resize with Image Resizer, then choose sizes/options. | Batch inputs and reusable size presets. Keep originals by default. Explorer integration needs a separate scope decision. |
| [HandBrake queue](https://handbrake.fr/docs/en/latest/advanced/queue.html) | Open source, select title and preset, add encodes to a queue. | Named presets, queue progress and individual job failures. Do not reproduce the full transcoding settings surface. |
| [Squoosh project](https://github.com/GoogleChromeLabs/squoosh) | Compresses images into multiple formats locally in the browser, with visual comparison. | Before/after size and an image comparison before export. No server upload. |

Open files or drop them into Media tools, choose Convert, Resize or Compress, preview output settings and destination, then Run. Show dimensions, format, estimated size when available and actual bytes after completion. Preserve aspect ratio by default; Fit, Fill/crop and Stretch are explicit alternatives. Never enlarge without an explicit choice. Preserve transparency or ask for a background when converting to JPEG.

Propose PNG/JPEG image output and MP4/GIF video output as the initial contract. WebP, HEIF, AVIF, extra codecs, frame editing and target-byte compression need architecture/scope decisions. Unsupported input is refused before starting. Metadata removal is explicit and defaults to removing private metadata from new exports; orientation must be baked correctly. Originals are never overwritten by default. Collision handling generates unique names. A batch reports each success/failure, permits cancellation between files, and cleans only its own partial files. No audio preview starts automatically.

## Verb and shortcut inventory

Each semicolon-separated name below is a separate shared action and separate Settings > Shortcuts slot, unassigned by default. Names are proposed labels. Parameterized actions carry an explicit selected item or open a picker. A global invocation must not infer a target from a stale background selection or silently use the latest private result. With no recording, Pause, Resume and Stop do nothing and report that no recording is active. Delete capture file first opens an explicit file/result picker when invoked globally, then asks "Delete <filename>?" with Cancel and Delete; Remove recent capture changes only the index after confirmation. No file is deleted by the chord alone. Local commands additionally have the contextual keys listed below.

| Feature | External verb / shortcut slots |
|---|---|
| Screen capture | Capture region; Capture window; Capture full screen; Capture all displays; Capture freeform; Capture last region; Capture after delay; Open recent captures; Annotate image; Copy capture; Save capture; Save capture as; Open capture folder; Remove recent capture; Delete capture file |
| Conditional scrolling | Capture scrolling area; Stop scrolling capture |
| Recording | Record region; Record window; Record screen; Record GIF; Start recording; Pause recording; Resume recording; Stop recording; Discard recording; Preview recording; Trim recording; Save recording; Save recording as; Export GIF |
| Text and QR | Copy text from screen; Extract text; Scan QR; Copy extracted text; Copy QR result; Open QR link; Reselect text region; Retry text recognition |
| Color | Pick color; Pick and review color; Copy HEX; Copy RGB; Copy HSL; Clear color history |
| Media | Convert media; Resize images; Compress media; Add media files; Run media jobs; Cancel media job; Retry media job; Open media output |

Contextual overlay commands: Copy (Ctrl+C), Save (Ctrl+S), Save as (Ctrl+Shift+S), Cancel (Esc), accept (Enter), Undo (Ctrl+Z), Redo (Ctrl+Y), selection move/resize (arrows/Shift+arrows), and each markup tool through its toolbar. Propose tool keys P pen, H highlighter, L line, A arrow, R rectangle, E ellipse, T text, D redact, X eraser and C crop, active only outside text editing. Proposed, pending owner decision and architecture support: each editor command gets its own customizable contextual shortcut slot in Settings > Shortcuts under Capture editor, including Select, Pen, Highlighter, Line, Arrow, Rectangle, Ellipse, Text, Redact, Erase, Crop, Undo, Redo, Copy, Save, Save as, Accept and Cancel. These proposed slots are scoped to the editor and require a separate contextual binding mechanism, not RegisterHotKey. Global slots remain unassigned.

## Decisions for Marge to put on Tasks

1. Confirm scope extras: scrolling, emoji/decorative markup, GIF editing, QR generation/barcodes, extra media codecs and Explorer integration. Recommend bounded scrolling feasibility first and defer the other extras.
2. Approve Print Screen opt-in and how Windows' existing setting is explained. Leave Win+Shift+S and Win+Shift+R to Windows.
3. Choose output policy, capture folder and retention. Recommend explicit Copy/Save with a local recent-file index initially.
4. Confirm recording audio/cursor defaults, GIF limit and initial output formats after architecture review. Recommend silent by default and explicit audio choices.
5. Resolve shortcut scope before briefs: the approved plan says every verb gets a slot, which this inventory follows. Herb recommends global slots only for starter actions plus Stop recording, with result/editor commands contextual. Karl recommends preserving every external verb slot with explicit target pickers and confirmations, and editor commands contextual. Travis chooses the rule for both specs together. Customizable contextual keys would require architecture work and would still be edited only in Settings > Shortcuts. Never implement contextual keys as global RegisterHotKey bindings.
6. Approve libraries/codecs through Kayla's architecture decision, with ARM64 and licenses recorded. No package is approved by this research.

## Verification required before feature completion

Docs review must check cited facts, proposal labels and coverage against the approved plan. This research cannot establish latency or native behavior. No app runtime changed, so no build or feature suite is warranted for this PR.

Later implementation proof: Sandbox only for clipboard/OCR/QR/color copying and real input. Use fixture windows, never the owner's screen content. Cover negative display coordinates, mixed DPI, disconnect/reconnect, rotated displays, HDR, transparency, protected/secure content, clipboard contention, unavailable destinations and cancellation at every stage. Use dozens of recent results and a batch of 100 disposable images, plus a long video at real display resolution. Check actual encoded playback, image pixels, redaction flattening, OCR language refusal and QR payload safety, not merely file existence.

Measure cold/warm shortcut-to-freeze, release-to-toolbar, stroke latency, Copy-to-clipboard, recording start/stop and page/result opening. Compare Snipping Tool and Lightshot with the same fixture and timing method only where available without installation; otherwise leave the comparison to Travis's live check and label it unmeasured. Target numbers must be set in the approved briefs after the capture spike. This document reports no invented timings or competitor hands-on results.
