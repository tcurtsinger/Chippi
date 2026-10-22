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
| Look | Direction A "Studio Rack": Hopper's design system grown into a suite. See [DESIGN.md](DESIGN.md) |
| Island | Top-center floating pill: media, timers, downloads, notifications, AI agent usage |
| Pet | Gumdrop, a blob with a face in the Synth gradient that lives in the island. See DESIGN.md §7 |
| Mouzi | Rebuilt from scratch with the same functionality. Its code is reference only |
| vorssaint | Feature list and behavior are the spec. We do not port its code |
| Hopper | Merged in as the Audio routing feature. Its repo is archived once Chippi covers it |
| Overlap with Windows / PowerToys | Decided feature by feature, flagged when we reach each one |
| Elevation | Runs as a normal user. Features that need admin (temps, fans) go through an optional elevated helper |
| Presets | **Essentials** (first run): Keep awake, Downloads organizer, App volume, System monitor, Displays and brightness, Clipboard history, Paste as plain text, Clean URL. **Audio**: the Sound group. **Power user**: almost everything, minus Mic processing, Volume boost and the Everything-only set. **Everything only**: Fan control, Cleaning mode, Focus follows mouse, Click and key debounce, Media tools, Cleaner. Source of truth: `src/Chippi.App/Settings/Features/FeatureCatalog.cs` (approved 2026-10-21) |

## Architecture

- **Shell.** Single instance, tray icon, tray flyout panel with tabs, settings window,
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
"Studio Rack" won, with the Gumdrop blob as the pet. Both are written up in
[DESIGN.md](DESIGN.md).

### Phase 1: Shell
Tray icon and flyout, settings window, Features hub with presets, permissions page,
island host with the pet, startup, config, logging, installer. Keep awake is built
here too, as the reference module that proves the module system end to end. The
build brief is [docs/phase-1-handoff.md](docs/phase-1-handoff.md).

### Backlog (requested, not yet scheduled)
- **Island position:** let the user move the island beyond top-center: a vertical bar on the left or right edge, the bottom edge, or a corner. Needs DESIGN.md layouts for vertical and bottom pills (Travis, 2026-10-22). The display setting (main, follow active, one monitor) shipped in Phase 1.

### Phase 2: Downloads organizer (Mouzi-style)
Brief: [docs/phase-2-handoff.md](docs/phase-2-handoff.md). In progress since 2026-10-22.
- Watched folders with Silent / Manual / Paused modes
- Rules: extensions, regex, size and date conditions, first match wins, reorderable
- Destination placeholders: `{year}`, `{month}`, `{day}`, `{extension}`, `{filename}`
- Ignore file per folder, system and browser temp files skipped, grace period and
  file-lock check, 0 KB placeholder skip
- Preview matched operations and approve them, rename-only templates
- History and verified undo (SQLite), toast notifications
- Explorer context menu entry, archive import (zip / tgz)
- Misleading executable filename warnings

### Phase 3: Sound
- Audio routing from Hopper: output priority switching, mic pinning, mic processing
- Per-app volume mixer, per-app output device, mic mute shortcut
- Volume boost past 100% (needs research: Windows session volume caps at 100%)

### Phase 4: System monitor
CPU, GPU, memory, temperatures, battery, network, tray readouts, alerts. Fan control
flagged as research (vendor specific).

### Phase 5: Clipboard and everyday tools
Clipboard history, paste as plain text, clean URL, shelf, command bar, quick panel,
quick toggles, radial menu, scratchpad, keep awake, cleaning mode, AI agent usage in
the island.

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
