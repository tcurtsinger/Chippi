# Chippi Phase 1 report

Phase 1 is built and on GitHub at `main`: the tray icon and flyout, the settings window with the Features hub, the island with Gumdrop, the module system, packaging, and Keep awake as the reference module. Everything merged went through a second member's review first. Two acceptance checks are still open: the flyout at 150% and 200% on the main display (waiting on Travis's OK to change its scaling), and any run on ARM64 hardware.

Brief: [phase-1-handoff.md](phase-1-handoff.md). Day-by-day decisions and follow-ups: [phase-1-tracker.md](phase-1-tracker.md).

## What was built

| Area | What's there | Where |
|---|---|---|
| Foundation | .NET 10 solution, `Directory.Build.props` (nullable, warnings as errors, SPDX header enforced at build), `global.json`, central package versions, `app.manifest` (PerMonitorV2, Windows 11, asInvoker) | repo root, `src/Chippi.App` |
| Core (no WPF) | Module contract and registry with live install/uninstall, presets and "Coming soon" placeholders; island activity hub with priorities; versioned, atomic `settings.json` that keeps unknown sections, with export/import and a `SectionChanged` event; rolling, size-capped logs; single instance with foreground handoff; HKCU Run registration; permissions model with the "unused" flag | `src/Chippi.Core` |
| Design system | Dark and Light themes with Hopper's key names plus the DESIGN.md additions, Follow Windows, static Manrope and JetBrains Mono, Lucide icons, every §6 control with hover, focus ring and disabled states, and a debug control gallery | `src/Chippi.UI` |
| Tray and flyout | Tray icon owned through `Shell_NotifyIcon` (re-added after an Explorer restart), the generated Gumdrop `.ico`, a flyout anchored with `Shell_NotifyIconGetRect` on any taskbar edge, the Tools tab, an empty state, and a themed right-click menu | `src/Chippi.App/Tray`, `Flyout` |
| Settings | Custom titlebar on the Windows 11 frame; General (theme, run at startup, export/import), Features hub (presets, search, counts, badges, 40 Coming-soon placeholders), Island & pet, Permissions, Shortcuts, Appearance, About | `src/Chippi.App/Settings` |
| Island and Gumdrop | Non-activating topmost island that follows the foreground monitor and hides over fullscreen apps; resting, live and expanded states; a debug menu that injects fake activities; Gumdrop drawn from the §7 path data with four moods, the small form, blink, squish and reduced motion | `src/Chippi.App/Island`, `Pet` |
| Keep awake | Indefinite, for a duration, or until a time, with an option to keep the display on. One dedicated thread holds and releases the execution state. It has a Tools tile, a settings page, an island Status activity (pet wide awake) and celebrates when a timed session ends, and it recovers from a failed Windows call without a reinstall | `src/Features/Chippi.Features.KeepAwake` |
| Build and packaging | Publish profiles for win-x64 and win-arm64, a GitHub Actions workflow (both platforms build, tests run on x64), Velopack 1.2.161 as a repo-local tool, `build/pack.ps1`, LICENSE (GPL-3.0) and THIRD-PARTY-NOTICES.md | `build`, `.github`, repo root |

The composition root is `src/Chippi.App/App.xaml.cs`, and the process entry is `src/Chippi.App/Program.cs` (Velopack runs before WPF starts).

## How to build and run

The .NET 10 SDK is at `C:\Program Files\dotnet`; add it to `PATH` if `dotnet` isn't found.

```powershell
dotnet build src\Chippi.App\Chippi.App.csproj -c Release -r win-x64     # or -r win-arm64
dotnet test Chippi.slnx -c Release
src\Chippi.App\bin\Release\net10.0-windows\win-x64\Chippi.exe
```

- A plain `dotnet build` or F5 builds for this machine's platform. Building the `.slnx` with `-r` isn't supported by the SDK, so build the app project per platform.
- `CHIPPI_DEV_PROFILE=<name>` gives a run its own instance, settings, logs and run-at-startup entry, separate from the real profile.
- `--debug` turns on the placeholder flyout tabs and the island's fake-activity menu (right-click the island).
- Debug builds only: `--gallery`, `--flyout-snapshot <dir>` and `--settings-snapshot <dir>` render screens to PNG.
- Packages: `build\pack.ps1 -Version 0.1.0` writes Setup.exe, a portable zip and the Velopack feed files to `build\artifacts\releases\0.1.0\<platform>\`. Nothing is signed and no update feed is set.

Settings live in `%APPDATA%\Chippi\settings.json` and logs in `%LOCALAPPDATA%\Chippi\logs\`.

## Screenshots

All are in [phase-1/screens](phase-1/screens).

| Surface | Files |
|---|---|
| Flyout | `flyout-dark.png`, `flyout-light.png`, `flyout-empty-dark.png`, `tray-menu-dark.png`, `tray-menu-light.png` |
| Features hub | `hub-dark.png`, `hub-light.png` |
| Other settings pages | `settings-general-dark.png`, `settings-island-pet-light.png`, `settings-permissions-dark.png`, `settings-about-light.png`, `keep-awake-settings-dark.png`, `keep-awake-settings-light.png` |
| Island | `island-resting-dark.png`, `island-live-dark.png`, `island-live-keepawake-dark.png`, `island-expanded-dark.png`, `island-expanded-celebrating-light.png` (the island is dark in both themes) |
| Gumdrop | `gumdrop-moods-dark.png`, `gumdrop-moods-light.png`, `gumdrop-colors-dark.png`, `gumdrop-small-22-4x-dark.png`, `gumdrop-celebrate-frames.png` |
| App icon | `app-icon-frames.png` (16 to 256px) |

## Test results

At `main` `59ebf8c`:
- Release win-x64 and win-arm64, and Debug with no platform set: 0 warnings, 0 errors each.
- `dotnet test Chippi.slnx -c Release`: 81 passed, 0 failed. That's 63 Core tests (config round-trip, atomic writes, unknown sections kept, presets, install → uninstall → reinstall restoring settings, the permissions "unused" logic, island priorities, single instance, logs, run at startup) and 18 Keep awake tests (timing, the dedicated thread, release on every path, recovery after a failure).
- Tests run on x64 only. This machine can't execute ARM64 binaries, so ARM64 is built and packaged but never run.

## Acceptance checklist (brief §7)

| Check | Status | Evidence |
|---|---|---|
| Build and test with 0 warnings, both platforms | Done | See Test results |
| Core tests cover the five listed areas | Done | `tests/Chippi.Core.Tests` |
| Gumdrop tray icon; a second launch focuses the flyout | Done | Checked live by Karl and Marge |
| Flyout anchoring at 100% / 150% / 200%, multi-monitor; Esc and click-away; matches §5 | Partly | 100% with the taskbar at the bottom checked live. 150% and 200% not run: the tray only lives on the main display, and changing its scaling needs Travis's OK. Other taskbar edges are handled in code but not run |
| Hub: Keep awake installs and uninstalls live; presets, search, counts, badges; Coming soon not installable | Done | WS2 review plus a UI Automation pass |
| Island: three states, follows the foreground monitor, hides over fullscreen, never takes focus, not in Alt+Tab | Done | Checked live. Also centered at 150% and 175% on the secondary monitor (Windows' maximum for 1080p) |
| Gumdrop moods and small form match §7; blink, squish, reduced motion | Done | Frame strips in `gumdrop-celebrate-frames.png`, Karl's geometry review |
| Keep awake prevents sleep and releases on stop, uninstall and exit; island and pet states | Done | System power state `0x3` while on and `0x0` after stop and after uninstall, read without admin (Travis approved this in place of `powercfg /requests`) |
| Themes, focus rings, keyboard, Narrator reads icon buttons | Done, with one gap | Themes and focus rings checked. Screen-reader names checked through UI Automation (the tree Narrator reads); Narrator itself wasn't run, by Travis's choice. Tab order checked in the flyout; no full keyboard-only walk of every settings page |
| Run at startup; logs and settings in the documented folders | Done | Toggled through UI Automation in a dev profile, entry written and removed |
| Velopack Setup.exe and portable package for both platforms | Done | Sizes and SHA-256 in `phase-1-release-artifacts.csv` (repo root, untracked). x64 portable runs; x64 Setup only checked with `--help` (no install); ARM64 not run |

## Deviations from DESIGN.md

Every deviation Travis reviewed on 2026-10-21 is now part of DESIGN.md (light badge and segment text, the Status island kind, the Windows 11 settings frame, the control details, the island additions, tool tile ellipsis). What's left:

- **JetBrains Mono weight.** The upstream static files declare weights 436 and 472, not 500 and 600, so the mono styles ask for 472 to load the SemiBold file. It looks like 600, as designed. No synthetic bold.
- **Gumdrop moods are code-driven animations,** not the visual states §9 suggests. The falling-asleep sequence needed exact timing that visual states can't express cleanly.
- **Pet details DESIGN.md doesn't specify, filled in by WS3:** sleepy eye stroke 3.4, the z's placed as text origins, the small form still blinks, blink 120ms total, squish peaking at 110ms. Proposed for §7 if you want them written down.
- **Equalizer runs at 30 fps** whenever media is live. With no real audio in Phase 1, it only moves for the debug menu's fake media.

## Known issues

- **Not run:** the flyout at 150% and 200% (main display), the taskbar on the top or side edges, a real Explorer restart (only the message was simulated), an exclusive-fullscreen game, PowerPoint presentation mode, browser F11, Narrator itself, any ARM64 execution, and a real Setup.exe install and uninstall (so the uninstall hook that removes the run-at-startup entry hasn't run live).
- **Equalizer cost:** about 4.7% of one CPU core while media is live, against 0.5% at rest. Fine for Phase 1. Before real media (Phase 3), it should run from real audio levels at 20 fps or less and stop during silence.
- **System-wide "busy":** on this PC, Windows sometimes reports a fullscreen app system-wide when none is in front. The island now ignores that flag and relies on the foreground window's bounds plus D3D fullscreen and presentation mode.
- **Expanded island media** shows no elapsed or total time, because activities don't carry them yet, and the transport buttons do nothing. Both wait for real media in Phase 3.
- **Tool tile names** longer than about 10 characters end in an ellipsis (approved; the tooltip and the screen-reader name carry the full name).
- **Unsigned builds** will get a SmartScreen warning until code signing is set up.
- Optional hardening, not done: one retry in Keep awake's toggle if a timed session's release fails at the same moment, and a log line when Windows refuses to bring the flyout forward.
- Review notes and helper scripts from this phase sit untracked in the repo root (`*-review.md`, `uia-*.ps1`, `screens/`). They aren't in git; delete or archive them when convenient.

## Suggestions for Phase 2 (Downloads organizer)

1. **Reuse what exists.** The hub already lists "Downloads organizer" as a placeholder. Phase 2 swaps it for a real module in `src/Features/Chippi.Features.Downloads`, contributing the Tidy tab through `AddFlyoutTab`, a Stash island activity, and `ReportSuccess` so Gumdrop celebrates. The `StashRow` control and the Tidy tab's segmented control are already built.
2. **Keep the rules engine out of WPF,** in its own library with unit tests against temp folders: extensions, regex, size and date, first match wins, placeholders, the ignore file, the grace period and lock checks, the 0 KB skip, and misleading executable names. Most of Phase 2's risk is there, and it's the easiest part to test.
3. **Decide three things before building:**
   - **History and undo storage:** SQLite through `Microsoft.Data.Sqlite` (MIT) is what PLAN.md implies.
   - **Toasts:** Windows app notifications need an app identity, which a Velopack install doesn't provide by default, so choose between a registered AppUserModelID and an island-only notice.
   - **Explorer context menu:** the Windows 11 top-level menu needs a sparse package. The classic "Show more options" menu works per user without admin.
4. **Watch for browser partial files** (`.crdownload`, `.part`, `.opdownload`) and files still locked by the browser. That's where organizers usually move a file too early.
5. **Process:** keep one worktree per stream, with every change reviewed before merge and run from one lead thread. Two lead threads directing the same team caused the mid-phase confusion. Add the UI Automation checks used in this phase (toggles, tree names) to CI as a smoke test, since they caught two real accessibility bugs.
