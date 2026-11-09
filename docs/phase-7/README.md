# Phase 7: Capture and app management

Screen capture with markup, screen recording, text and QR from screen, a color picker, media tools, displays and brightness, app updates, an uninstaller, a cleaner and a port manager (PLAN.md). Shaped with Travis on 2026-11-06 from four pieces of research in this folder: [behavior spec A](behavior-spec-a.md) (Capture), [behavior spec B](behavior-spec-b.md) (Apps and System), the [architecture](architecture.md) and the [surfaces](surfaces.md) (where every feature lives, with [rough boards](design/surfaces.html)).

## Travis's direction (2026-11-06)

- **The bar:** "Windows snipping tool with the ease of use of Lightshot." Snipping Tool sets what's included (modes, delay, markup, text actions, recording, history); Lightshot sets the flow: one key, drag, mark up in place beside the selection, Copy or Save with one click or Ctrl+C and Ctrl+S, Esc cancels. No upload.
- **Shortcuts for each thing,** set just like every other shortcut: only in Settings › Shortcuts, shown read-only with Edit on each feature's page.
- **Capture gets its own tile and panel** on the island home. Small one-shot verbs go into the Actions menus where they make sense. The app stays coherent: one rule decides where everything goes ([surfaces.md](surfaces.md) section 1).
- **Learn from the competition:** each spec has a "Best of the field" table per feature, and each brief says what it took from each tool.

## Decided (Travis, 2026-11-06)

- **Scope (task 478):** all ten features in full. Left for later: emoji and sticker markup, GIF editing, making QR codes, barcodes other than QR, extra video formats (WebM, AVI), an Explorer menu for capture; app update schedules, backup and restore, version pins; uninstall restore points and "Remove broken entry"; emptying the Recycle Bin and finding large folders in the Cleaner; closing a single connection in Port manager.
- **Print Screen (task 479):** Capture region gets Print Screen when Screen capture is installed. Chippi switches off Windows' "Use the Print screen key to open screen capture" setting, records what it found, and puts it back if the shortcut changes or the feature is removed. It's changed in Settings › Shortcuts like any other. Win+Shift+S stays Windows'. The shortcut service gains a narrow exception: Print Screen alone and with modifiers is bindable, nothing else bare.
- **Whole page (task 480):** a short trial of scrolling capture after wave 1. It ships only if the stitching holds up on real pages (a browser, Settings, a long document); otherwise it's left out and the report says why.
- **Where captures go (task 481):** Copy, Ctrl+C and Ctrl-drag copy. Save and Ctrl+S save without asking to the save folder, which is Pictures\Screenshots unless Travis sets another folder in Settings › Screen capture; Save as asks. Settings can make every capture copy, save or both. Recent captures keeps the last 50 finished (copied or saved) with thumbnails in Chippi's own data folder, its index sealed like Clipboard history; the panel shows four, Settings shows all. Cancel or Esc keeps nothing.
- **Shortcut scope (tasks 471, 482; recording revised 2026-11-09):** a global slot for each capture mode (region, window, full screen, all displays, freeform, last region, after delay), Record window, Record screen, Pause/Resume recording, Stop recording, Copy text from screen, Scan QR, Pick color, Brightness up and down, Check app updates, Clean junk files, and opening each panel. Verbs that act on one row or one result (update or uninstall this app, free this port, delete this capture) and the capture editor's keys (Ctrl+C, Ctrl+S, Ctrl+Z, Esc, Enter, arrows, the tool letters) have no global slot: they work inside their panel or the capture screen, fixed as Lightshot's are, and are never registered with `RegisterHotKey`. No slot has a default except Print Screen.
- **Admin helper (task 483):** built in wave 3, narrow, with the risk accepted as stated in [architecture.md](architecture.md) section 6: a separate program, started with one UAC prompt only when Travis presses a button that needs it, a closed list of jobs each checked again inside the helper, exits when done. It covers batched updates and uninstalls, leftovers in Program Files and `HKLM`, Windows' own temp and update caches, and stopping a service or an admin process holding a port.
- **Libraries (task 484):** ZXing.Net (Apache-2.0) for QR codes and `Microsoft.WindowsPackageManager.ComInterop` (MIT) for winget. No ffmpeg: Windows' own encoders cover MP4, GIF, PNG and JPEG.
- **Deleting (task 485):**
  - Uninstaller leftovers: shown after the app's own uninstaller runs. Ticked only when they verifiably belong to that app; anything shared with another installed app is never offered. Files go to the Recycle Bin; registry keys are saved to a backup file first.
  - Cleaner: measures first, then "Clean 1.0 GB for good?" with Cancel and Clean. Caches and temp files are deleted for good, since the Recycle Bin frees no space until emptied. Files in use are skipped and counted.
  - Port manager: Free asks first ("End node.exe to free port 3000?"). A service is stopped, never its host process. System and TIME_WAIT rows have no Free. The row is read again before anything runs.
- **Where things live (task 486):** Lumen's map in [surfaces.md](surfaces.md), all five calls as recommended:
  1. **Displays** gets a home tile, the 12th: a slider per display, an All displays row, a Contrast view. The home holds 12 at today's tile size, so Capture and Displays fill it.
  2. **Actions** gains an **Apps** menu: Update apps, Clean junk files, Uninstall (opens Settings). Actions reaches 19 of its 20 places.
  3. **Port manager** has no tile or menu: "port 3000" in Search, and Settings › System › Ports.
  4. **Media tools** is the Convert page on the Capture panel, not a tile.
  5. **While recording (revised 2026-11-09),** the expanded Chippi puck holds the large red dot and timer plus icon-only Pause/Resume and Stop. No separate floating pill. The puck is excluded from the recording.
- **Defaults (task 487; recording revised 2026-11-09):** recordings are MP4, system audio on, mic audio off, cursor shown, and one display per screen recording. Both audio switches are in the recording menu and use the current default devices at Start, with no app-audio or device chooser. Brightness keys act on the monitor under the pointer, 10% a press, changeable in Settings.
- **Recording planning update (2026-11-09):** [screen-recording.md](screen-recording.md) records the current decisions. Window and Screen only; manual Start/Stop, no replay buffer. Original/1080p and 30/60 fps controls; Stop saves automatically and adds Recent. Shortcuts and Radial actions share start, pause/resume, and stop commands. Mic loss keeps video running; no automatic device replacement mid-take initially. The puck's larger dot and time share one vertically centered row, without a visible "Recording" label; Chippi retains his normal panel destination. The conversation mockup supersedes the old region-and-pill boards for recording. Fullscreen reachability and sustained encoding performance remain technical checks. **Travis authorized implementation on 2026-11-09: "Ok let's build away".**
- **Recording animation choice (2026-11-09):** Travis chose Chippi holding the little camcorder beside his face. Keep motion gentle, with distinct starting, recording, paused, and finished states; keep the timer's dot steady. The tightly spaced dot/time/buttons are centered together beside the pet. This selects a planning direction, not an implementation or installer change.
- **Recording performance priority (2026-11-09):** minimize impact on the computer and gameplay. Verify real hardware encoding, avoid unnecessary frame copies/work, bound memory and queues, and write incrementally. Include the puck animation in repeatable off/on comparisons, measuring game frame times as well as recorder CPU/GPU/RAM/disk use. No performance percentage is established yet. The implementation uses 1080p30 as the low-impact default. Gameplay measurements remain pending.
- **Waves (task 488):** as below.
- **Wave 1 briefs (task 505):** Screen capture, Color picker, Text and QR and Displays confirmed as written.
- **All displays (task 504):** the slider moves every display by the same amount and keeps their difference ([displays.md](displays.md)).
- **Mockups:** wave 1 mockups are SerenityOS designs in `designs/` (Home, Capture panel, Frozen screen, Capture results, Displays, Settings › Capture, Settings › Shortcuts). Travis picks a version of each and presses Build; Kayla's design notes go into the build, not new versions. Picked so far: Settings › Capture v3 (with the shorter labels from Travis's notes on it, drawn as v4), Settings › Shortcuts v2, Home v1.

## Lead calls (Marge)

- **Build order, three waves:**
  1. **Capture core:** the capture spike (Sandbox, no PR: the ways to read the screen timed from key to frozen screen, and Print Screen with Windows' setting on and off), then the shared capture layer and selector overlay, then Screen capture with markup, Color picker, and Text and QR. Lumen builds the Capture tile and panel. Displays and brightness runs alongside.
  2. **Motion:** Screen recording, then Media tools on the same encoder code. The scrolling capture trial (task 480) runs at the start of this wave.
  3. **Apps:** the admin helper first (full review: Chippi's first elevated code), then App updates, Uninstaller, Cleaner and Port manager.
- **Briefs and mockups:** each wave's briefs and mockups are written, reviewed and confirmed by Travis before that wave is built. Each brief has a Shortcuts section (every verb and its slot) and a "What we took" line per competitor.
- **Coherence check:** before each wave's installer, Marge walks the home, the Actions menus, Search, the Radial menu and Settings against [surfaces.md](surfaces.md), with shots: nothing duplicated, orphaned or in the wrong group.

## Hard limits

- **Admin windows:** a standard-user Chippi may not be able to capture an elevated window on its own (unverified, wave 1 spike). A full-screen capture includes it.
- **Brightness:** only monitors that answer DDC/CI. Docks and adapters often don't pass it; such a display reads "Brightness control failed" with the likely causes in Settings.
- **The helper's risk:** Chippi is unsigned and installed in Travis's user folder, so a program already running as him could swap the helper file. The full fix (signing, installing under Program Files) is out of scope.

## Briefs

| Brief | Owner | Wave | Mockup |
|---|---|---|---|
| Capture layer and selector (`capture-layer.md`) | Kayla | 1 | Lumen: the frozen screen and its bars |
| Screen capture (`screen-capture.md`) | Karl | 1 | Lumen: Capture tile and panel, results, Settings |
| Color picker (`color-picker.md`) | Herb | 1 | Lumen: Color page, bar line, Settings |
| Text and QR (`text-and-qr.md`) | Herb | 1 | Lumen: Text and QR pages, Settings |
| Displays and brightness (`displays.md`) | Kayla | 1 | Lumen: Displays tile and panel, Settings |
| Screen recording (`screen-recording.md`) | Herb | 2 | Later |
| Media tools (`media-tools.md`) | Herb | 2 | Later |
| Admin helper (`admin-helper.md`) | Kayla | 3 | None |
| App updates (`app-updates.md`), Uninstaller (`uninstaller.md`), Cleaner (`cleaner.md`), Port manager (`port-manager.md`) | Herb | 3 | Later |

Each brief is confirmed by Travis, and each new surface's mockup is approved, before it's built.
