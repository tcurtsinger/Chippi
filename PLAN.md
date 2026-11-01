# Chippi

A Windows tray app that bundles the utilities people usually install one by one,
behind a single tray icon, with a widget you place anywhere on the desktop
and a pet companion. Install only the features you use.

Inspired by [vorssaint-utils](https://github.com/vorssaint/vorssaint-utils) (macOS)
and [Mouzi](https://github.com/hsr88/mouzi) (Downloads organizer). Absorbs
[Hopper](https://github.com/tcurtsinger/Hopper) (audio routing and mic processing).

## Decisions

| Topic | Decision |
|---|---|
| Name | Chippi |
| Stack | C# / .NET 10 + WPF |
| Target | Windows 11, x64 + ARM64 |
| License | GPL-3.0-or-later |
| Audience | Public open-source release (GitHub releases, installer, auto-update) |
| Look | Native Windows 11 (2026-10-23): neutral grays, one flat accent (Follow Windows by default, or Blue, Violet, Green, Pink), Segoe UI Variable. It replaced Direction A "Studio Rack" after the [UI/UX audit](docs/phase-1-2-ui-ux-audit.md). See [DESIGN.md](DESIGN.md) |
| Island | A widget on the desktop (2026-10-29): a 50px puck with the pet, dragged anywhere on any display, with a chip beside it for the song or the top activity. A click opens a 520 × 360 panel out of the puck, toward the screen's middle: a Quick Settings home, and a page per feature with a back arrow. Before that, a tab docked on a screen edge (2026-10-25) |
| Surfaces | Two surfaces, the island and Settings (2026-10-28). The island's pages are the only quick panel; each feature's page is built once. Nothing on the island scrolls except a Scratchpad note's text: pages adapt to fit, and what still doesn't fit is in the feature's Settings page, which may hold information for this, not just configuration. The tray icon opens Settings on the last page used; right click is Settings and Quit. Hide tray icon leaves the island as the only door. Settings groups pages by the catalog's groups, Sound by task (Output, Microphone, App volume), and can do everything the island can, its island controls first (2026-10-26, [mockup](https://claude.ai/artifact/RJAX7v2QT6MLpPRgZQVXeS); revised 2026-10-28, [refresh](docs/refresh-2026-10-28/brief.md)) |
| Pet | Chippi, a soft blob with big shiny eyes that lives in the widget (2026-10-29; it replaced Gumdrop). See DESIGN.md §7 |
| Mouzi | Rebuilt from scratch with the same functionality. Its code is reference only |
| vorssaint | Feature list and behavior are the spec. We do not port its code |
| Hopper | Merged in as the Audio routing feature. Its repo is archived once Chippi covers it. Hopper is marked MIT (intended); its code is relicensed under GPL-3.0-or-later here |
| Overlap with Windows / PowerToys | Decided feature by feature, flagged when we reach each one |
| Elevation | Runs as a normal user. Features that need admin (temps, fans) go through an optional elevated helper |
| Presets | **Essentials** (first run): Keep awake, Downloads organizer, App volume, System, Displays and brightness, Clipboard history, Paste as plain text, Clean URL. **Audio**: the Sound group. **Power user**: almost everything, minus Mic processing, Volume boost and the Everything-only set. **Everything only**: Fan control, Cleaning mode, Focus follows mouse, Click and key debounce, Media tools, Cleaner. Source of truth: `src/Chippi.App/Settings/Features/FeatureCatalog.cs` (approved 2026-10-21) |

## Architecture

- **Shell.** Single instance, tray icon (a door to the island's pages), settings window,
  island host window, startup registration, config store, logging, update check,
  settings export/import.
- **Feature modules.** Each feature is a module with an id, settings, required
  permissions and start/stop hooks. The Features hub installs and uninstalls them;
  uninstalled modules never load, and reinstalling restores their settings. Presets
  pick a starting set.
- **Permissions page.** Lists each permission or elevation a module needs, which
  modules use it, and flags ones nothing uses anymore.
- **Libraries.** NAudio (audio), LibreHardwareMonitor (sensors), CsWin32 (Win32
  interop), WinRT APIs (OCR, screen capture, media sessions), SQLite (history).

## Roadmap

### Phase 0: Design directions (done)
Three directions were mocked up on a
[design canvas](https://claude.ai/artifact/CMBT4oFV3oGZRUei1d7K17). Direction A
"Studio Rack" won, with the Gumdrop blob as the pet. Studio Rack was replaced on
2026-10-23 by the native Windows 11 look ([mockup](https://claude.ai/artifact/5M1W8XSCiDPvCpc5Hrd2Bq));
Gumdrop stays. Both are written up in [DESIGN.md](DESIGN.md).

### Phase 1: Shell (done)
Tray icon and flyout, settings window, Features hub with presets, permissions page,
island host with the pet, startup, config, logging, installer. Keep awake is built
here too, as the reference module that proves the module system end to end. The
build brief is [docs/phase-1-handoff.md](docs/phase-1-handoff.md). Done 2026-10-22:
[report](docs/phase-1-report.md).

### Backlog (requested, not yet scheduled)
- **Hardware sensors** (Travis, 2026-10-26): a later step using the PawnIO driver and an admin helper for CPU package/core and board temperatures and motherboard fans, followed by hardware-qualified fan control. Phase 4 uses standard-user readings only; no tray readouts. See [Phase 4 architecture](docs/phase-4/architecture.md#2-temperatures-and-fans).
- ~~**Island position**~~ (Travis, 2026-10-22): moved into Phase 2.5 as the movable island (top, left or right edge; designed 2026-10-23). The bottom edge and corners were left out on purpose: the taskbar and window buttons live there.
- **Volume boost backend** (Travis, 2026-10-25, task 21): its own phase, deferred out of Phase 3. Windows caps app volume at 100%, and replaying a captured copy failed as tested (muting the app or setting it to 0 also silences its capture).
  - First, test the untried no-driver ways of silencing an app.
  - Then evaluate a dedicated virtual audio device and a render audio plugin (APO).
  - Cost each route: signing, ARM64, installer, crash recovery.
  - Findings so far: [docs/phase-3/boost-gate.md](docs/phase-3/boost-gate.md) (Karl's gate) and [docs/phase-3/boost-experiment.md](docs/phase-3/boost-experiment.md) (route 1 experiment).

### Phase 2: Downloads organizer (Mouzi-style) (done)
Brief: [docs/phase-2-handoff.md](docs/phase-2-handoff.md). Done 2026-10-23 as the File organizer:
[report](docs/phase-2-report.md).
- Watched folders with Auto / Manual / Paused modes
- Rules: extensions, regex, size and date conditions, first match wins, reorderable
- Destination placeholders: `{year}`, `{month}`, `{day}`, `{extension}`, `{filename}`
- Ignore file per folder, system and browser temp files skipped, grace period and
  file-lock check, 0 KB placeholder skip
- Preview matched operations and approve them, rename-only templates
- History and verified undo (SQLite), toast notifications
- Explorer context menu entry, archive import (zip / tgz)
- Misleading executable filename warnings

### Phase 2.5: Native look refresh (done)
Brief: [docs/ui-refresh-handoff.md](docs/ui-refresh-handoff.md). Moves the shell, Keep awake and the Downloads organizer to the 2026-10-23 design before Phase 3 adds more screens on the old one. Done 2026-10-24 (PRs #1 to #26), in the [Phase 2 report](docs/phase-2-report.md). The flyout and the movable tab have gone since: the island's pages replaced the flyout at the end of Phase 3 (#52), and the widget replaced the tab in Phase 5.1.
- New theme tokens in dark and light, the accent setting, Segoe, tabular numbers
- Restyled controls, grouped cards, no gradients except Gumdrop
- Panel-first flyout: icon tab strip, Keep awake and Tidy panels
- Settings sidebar with search and installed features; Add features replaces the Features hub
- Movable island: a tab the user drags to the top (default), left or right edge, which opens on hover

### Phase 3: Sound (done)
Done 2026-10-26 (PRs #31 to #54): [report](docs/phase-3-report.md).
- Audio routing from Hopper: output priority switching, mic pinning, mic processing
- Per-app volume mixer, per-app output device, mic mute shortcut
- ~~Volume boost past 100%~~: deferred to its own phase (see Backlog, "Volume boost backend"); App volume stays 0 to 100%

### Phase 4: System monitor (done)
Brief: [docs/phase-4-handoff.md](docs/phase-4-handoff.md). Approved 2026-10-26. Done 2026-10-27 (PRs #57 to #64): [report](docs/phase-4-report.md).
CPU, GPU, memory, battery, network, disk activity and available GPU/thermal-zone temperatures, with opt-in alerts in the island and docked pages. CPU/board sensors and fan control are deferred to Hardware sensors.

### Phase 5: Clipboard and everyday tools (done)
Brief: [docs/phase-5-handoff.md](docs/phase-5-handoff.md). Design: [docs/phase-5/design/notes.md](docs/phase-5/design/notes.md). Approved 2026-10-28. Done 2026-10-28 (PRs #69 to #90): [report](docs/phase-5-report.md).
Clipboard history, paste as plain text, clean URL, scratchpad and shelf, on three island tiles: Clipboard, Scratchpad and Shelf.

### Phase 5.1: Shell redesign (done)
Brief: [docs/shell-redesign-handoff.md](docs/shell-redesign-handoff.md). Design: [docs/shell-redesign/design/](docs/shell-redesign/design/). Approved 2026-10-29.
The island at the top of the screen becomes a blob widget you drag anywhere, opening a Windows 11–style panel with a Quick Settings home and the pet's violet as the accent. A new blob replaces Gumdrop. Built in PRs #122 to #136 on `redesign/shell`. Live check passed (Travis, 2026-10-29, task 181, on the build from `4f52e49`).
- Known cost (2026-10-29): at 165 Hz the panel's open skips about 2 frames (up to 11 on the first open after start), its close about 6 and a page switch about 3. The panel's shadow is 1 or 2 of them. Travis kept the grow-out-of-the-puck open over a cheaper fade and scale (task 167).
- Pet passes since (2026-11-01, PRs #174 to #176): bolder and layout, then animate, delight and overdrive, then optimize, harden and polish. See DESIGN.md §7.

### Phase 5.5: Everyday tools, continued
Shaping since 2026-11-01: [docs/phase-5.5/](docs/phase-5.5/README.md). All five are shaped and their mockups approved (Quick toggles became Quick actions); building them is next. Quick toggles, cleaning mode, command bar, radial menu, AI agent usage in the island (Travis, 2026-10-27, tasks 27 and 28). Quick panel leaves the catalog; keep awake already shipped.

### Phase 6: Windows and input
Not started. Window layouts and snapping, app switcher with previews, quit and close protection,
focus follows mouse, smooth and linear scrolling, mouse button shortcuts, click and
key debounce, super key, text snippets, shortcut manager.

### Phase 7: Capture and app management
Not started. Screen capture selector, screenshot and annotate, screen recording, OCR and QR,
color picker, media tools, winget update manager, uninstaller with leftovers,
cleaner, port manager, displays and brightness. Displays and brightness is in the
Essentials preset, the only one of its features not built yet.

## Open items
- **Updates:** Velopack builds the installers, but no update feed is set and the app never checks for updates (the update check in Architecture, auto-update in Decisions).
- **Signing:** the installers are unsigned.
- **First release:** nothing is on GitHub releases yet.
- **Hopper:** Sound has covered it since Phase 3; its repo isn't archived yet.
- **ARM64:** built and packaged every phase, never run on ARM64 hardware.
