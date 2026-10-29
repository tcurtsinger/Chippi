# Chippi

A Windows tray app that bundles the utilities people usually install one by one,
behind a single tray icon, with a floating island pill at the top of the screen
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
| Island | A movable tab docked on the top (default), left or right screen edge; opens on click to a grid of tiles, each opening its own page (2026-10-25); one fixed size, 520 × 360 (2026-10-28) |
| Surfaces | Two surfaces, the island and Settings (2026-10-28). The island's pages are the only quick panel; each feature's page is built once. Nothing on the island scrolls except a Scratchpad note's text: pages adapt to fit, and what still doesn't fit is in the feature's Settings page, which may hold information for this, not just configuration. The tray icon opens Settings on the last page used; right click is Settings and Quit. Hide tray icon leaves the island as the only door. Settings groups pages by the catalog's groups, Sound by task (Output, Microphone, App volume), and never repeats a daily control (2026-10-26, [mockup](https://claude.ai/artifact/RJAX7v2QT6MLpPRgZQVXeS); revised 2026-10-28, [refresh](docs/refresh-2026-10-28/brief.md)) |
| Pet | Gumdrop, a blob with a face in the Synth gradient that lives in the island. See DESIGN.md §7 |
| Mouzi | Rebuilt from scratch with the same functionality. Its code is reference only |
| vorssaint | Feature list and behavior are the spec. We do not port its code |
| Hopper | Merged in as the Audio routing feature. Its repo is archived once Chippi covers it |
| Overlap with Windows / PowerToys | Decided feature by feature, flagged when we reach each one |
| Elevation | Runs as a normal user. Features that need admin (temps, fans) go through an optional elevated helper |
| Presets | **Essentials** (first run): Keep awake, Downloads organizer, App volume, System monitor, Displays and brightness, Clipboard history, Paste as plain text, Clean URL. **Audio**: the Sound group. **Power user**: almost everything, minus Mic processing, Volume boost and the Everything-only set. **Everything only**: Fan control, Cleaning mode, Focus follows mouse, Click and key debounce, Media tools, Cleaner. Source of truth: `src/Chippi.App/Settings/Features/FeatureCatalog.cs` (approved 2026-10-21) |

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

### Phase 1: Shell
Tray icon and flyout, settings window, Features hub with presets, permissions page,
island host with the pet, startup, config, logging, installer. Keep awake is built
here too, as the reference module that proves the module system end to end. The
build brief is [docs/phase-1-handoff.md](docs/phase-1-handoff.md).

### Backlog (requested, not yet scheduled)
- **Hardware sensors** (Travis, 2026-10-26): a later step using the PawnIO driver and an admin helper for CPU package/core and board temperatures and motherboard fans, followed by hardware-qualified fan control. Phase 4 uses standard-user readings only; no tray readouts. See [Phase 4 architecture](docs/phase-4/architecture.md#2-temperatures-and-fans).
- ~~**Island position**~~ (Travis, 2026-10-22): moved into Phase 2.5 as the movable island (top, left or right edge; designed 2026-10-23). The bottom edge and corners were left out on purpose: the taskbar and window buttons live there.
- **Volume boost backend** (Travis, 2026-10-25, task 21): its own phase, deferred out of Phase 3. Windows caps app volume at 100%, and replaying a captured copy failed as tested (muting the app or setting it to 0 also silences its capture).
  - First, test the untried no-driver ways of silencing an app.
  - Then evaluate a dedicated virtual audio device and a render audio plugin (APO).
  - Cost each route: signing, ARM64, installer, crash recovery.
  - Findings so far: [docs/phase-3/boost-gate.md](docs/phase-3/boost-gate.md) (Karl's gate) and [docs/phase-3/boost-experiment.md](docs/phase-3/boost-experiment.md) (route 1 experiment).

### Phase 2: Downloads organizer (Mouzi-style)
Brief: [docs/phase-2-handoff.md](docs/phase-2-handoff.md). In progress since 2026-10-22.
- Watched folders with Auto / Manual / Paused modes
- Rules: extensions, regex, size and date conditions, first match wins, reorderable
- Destination placeholders: `{year}`, `{month}`, `{day}`, `{extension}`, `{filename}`
- Ignore file per folder, system and browser temp files skipped, grace period and
  file-lock check, 0 KB placeholder skip
- Preview matched operations and approve them, rename-only templates
- History and verified undo (SQLite), toast notifications
- Explorer context menu entry, archive import (zip / tgz)
- Misleading executable filename warnings

### Phase 2.5: Native look refresh
Brief: [docs/ui-refresh-handoff.md](docs/ui-refresh-handoff.md). Moves the shell, Keep awake and the Downloads organizer to the 2026-10-23 design before Phase 3 adds more screens on the old one.
- New theme tokens in dark and light, the accent setting, Segoe, tabular numbers
- Restyled controls, grouped cards, no gradients except Gumdrop
- Panel-first flyout: icon tab strip, Keep awake and Tidy panels
- Settings sidebar with search and installed features; Add features replaces the Features hub
- Movable island: a tab the user drags to the top (default), left or right edge, which opens on hover

### Phase 3: Sound
- Audio routing from Hopper: output priority switching, mic pinning, mic processing
- Per-app volume mixer, per-app output device, mic mute shortcut
- ~~Volume boost past 100%~~: deferred to its own phase (see Backlog, "Volume boost backend"); App volume stays 0 to 100%

### Phase 4: System monitor
Brief: [docs/phase-4-handoff.md](docs/phase-4-handoff.md). Approved 2026-10-26.
CPU, GPU, memory, battery, network, disk activity and available GPU/thermal-zone temperatures, with opt-in alerts in the island and docked pages. CPU/board sensors and fan control are deferred to Hardware sensors.

### Phase 5: Clipboard and everyday tools
Brief: [docs/phase-5-handoff.md](docs/phase-5-handoff.md). Design: [docs/phase-5/design/notes.md](docs/phase-5/design/notes.md). Approved 2026-10-28.
Clipboard history, paste as plain text, clean URL, scratchpad and shelf, on three island tiles: Clipboard, Scratchpad and Shelf.

### Phase 5.5: Everyday tools, continued
Quick toggles, cleaning mode, command bar, radial menu, AI agent usage in the island (Travis, 2026-10-27, tasks 27 and 28). Quick panel leaves the catalog; keep awake already shipped.

### Phase 6: Windows and input
Window layouts and snapping, app switcher with previews, quit and close protection,
focus follows mouse, smooth and linear scrolling, mouse button shortcuts, click and
key debounce, super key, text snippets, shortcut manager.

### Phase 7: Capture and app management
Screen capture selector, screenshot and annotate, screen recording, OCR and QR,
color picker, media tools, winget update manager, uninstaller with leftovers,
cleaner, port manager, displays and brightness.

## Open items
- Install the .NET 10 SDK on this machine (not present yet).
- Hopper's bundled `rnnoise.dll` is x64 only; build an ARM64 copy.
- Hopper is marked MIT (intended); its code is relicensed under GPL-3.0-or-later here.
