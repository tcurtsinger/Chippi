# Chippi Phase 4 report: System monitor

Phase 4 (the System monitor) is built and on GitHub at `main` `763a619`. Every merge went through a second member's review first and merged only after both CI runs passed. The monitor lives in the island and the docked pages only, as Travis decided: one tile, one page and a Settings page, with five opt-in alerts. It reads everything as a standard user. CPU and board temperatures, motherboard fans and fan control wait for Hardware sensors (PLAN.md backlog). The installers are built from `763a619` (`pack/phase-4`, PR #64). Travis ran the live checks (task 26) on that installer and reported "Pass" on 2026-10-27. ARM64 is built and packaged but never run.

Brief: [phase-4-handoff.md](phase-4-handoff.md), with [phase-4/architecture.md](phase-4/architecture.md), [phase-4/behavior-spec.md](phase-4/behavior-spec.md) and the approved mockup in [phase-4/design/](phase-4/design/notes.md). Phase 4 is PRs #57 to #64.

## What was built

| PR | What |
|---|---|
| #57 | M0 engine: `Chippi.Features.SystemMonitor` with sources behind interfaces (CPU, memory, GPU, network, disks, battery, thermal zone) and fakes. One sampler thread polls only for live watches; PDH queries open on it, never on the UI thread. Immutable snapshots, 120-point history with gaps (pause, resume, Pending after a value, a failed read retried after 10 s, a device that disappears). GPU by PnP device instance ID through D3DKMT (temperature, fan), NVML loaded by full path for NVIDIA, busiest-engine load; network from GetIfTable2 hardware rows that aren't filter copies; disks by PhysicalDisk with volume letters. Settings model with `Normalized()` |
| #58 | Docs: brief, research and design notes into `docs/phase-4/`, DESIGN.md §5 System monitor, PLAN.md "Hardware sensors" backlog and no tray readouts, catalog text without "in the tray" |
| #59 | M1 alerts: CPU load and GPU temperature sustained 12 s of fresh readings; memory, low disk space (volumes of 10,000,000,000 bytes or more, per volume) and low battery while discharging; repeat cooldown per kind and target; never on stale data, resume doesn't count as sustained. Five notice kinds through the existing notifier; Caution on the tile and tab while an alert holds |
| #61 | Audio engine shutdown race (`AudioService.cs:415`) that failed CI runs on x64: shutdown now serializes queue completion and disposal, and repeated stops return the same task |
| #62 | M2 island: the System monitor tile with its status reading, the 640-wide page (CPU with the logical processor strip, Memory, GPU and adapter pickers, Network, Disks, Battery, Thermal zone) and the docked version. Approved shell hooks: `IIslandTile.SetPageNote`, `IIslandTile.OnScreen`, `ModuleContext.OpenIslandPage` (a notice click opens the page). Review fixes: unload after module stop, a stale tile status, stale headlines and skipped gaps |
| #60 | M3 Settings › System › System monitor: Update speed, Tile status, Readings, Units, Alerts with thresholds, per-GPU temperature limits and Repeat interval. Rows for battery and GPU temperature appear only where the hardware reports them |
| #63 | M4 smoke tests on fakes: the real module and shell on a fake 1280×720 monitor (tile, page on the top and a side edge, docked, Settings, close and reopen), and an alert through the notifier with its repeat after the cooldown |
| #64 | Phase 4 installers: release artifact manifest |

## How to build and run

Unchanged from the [Phase 1 report](phase-1-report.md#how-to-build-and-run). Additions:
- System monitor needs no admin and installs nothing. It is safe in a dev profile.
- Tests that read real hardware (`WindowsSourcesTests`) accept missing readings: the CI runner's Hyper-V GPU has no engine counters.
- Never test alerts with CPU burner threads on Travis's PC. Use a low threshold on a reading that's already high (GPU temperature at 40 °C) instead.

## Test results

At `main` `763a619` (CI run 40459651091, push):
- Release win-x64 and win-arm64 with `--no-incremental`: 0 warnings, 0 errors each.
- Every test project except UiSmoke: 757 passed, 0 failed (Audio 110, Core 106, Downloads 117, Keep awake 30, Sound 68, System monitor 79, Island 100, Notifications 25, Rules 117, Sound smoke 5). Phase 3 ended at 664.
- UiSmoke (the desktop suite) wasn't run in Phase 4.
- Polling cost on Travis's PC (shipped sampler, `chippi-scratch\phase-4\report\cost`): 0 reads and 0 ms CPU in 30 s with nothing watching; with everything the page shows and the processor strip open at 2 s, 484 ms CPU over 300 s (150 ticks), 0.16% of one core. architecture.md §1 estimated about 0.24%.

## Acceptance checklist (brief §6)

| Check | Status | Evidence |
|---|---|---|
| 0 warnings on both platforms; tests pass on x64; behavior covered on fakes (gaps, hotplug, identity, multi-GPU, filter adapters, sleep, alert sustain and cooldown) | Done, one gap | See Test results. `SamplerCoreTests` (`Resume_puts_a_gap_in_every_series`, `An_adapter_that_disappears_gets_a_gap`, `A_failed_read_is_Unavailable_a_gap_never_0_and_retried_after_10_s`, `Pending_after_a_value_ends_the_line`), `ModuleTests` (`The_page_picks_the_chosen_device_or_falls_back`: three GPUs and two adapters, a saved choice and a removed one), `WindowsSourcesTests` (real hardware as a standard user, Basic Render excluded). Gap: no test covers filter-copy adapters; the row filter (`NetworkSource.cs:43`, hardware rows that aren't filters) was checked on Travis's PC with the M0 check app and is live step 6.3. `AlertEvaluatorTests` (`Resume_restarts_sustain_even_after_a_short_sleep`, `Memory_repeats_after_cooldown_and_recovery_does_not_bypass_it`, `Volumes_have_separate_cooldowns_and_are_deduplicated`) |
| Tile, page(s), docked version and Settings match the approved mockup and DESIGN.md §3 | Done | Offscreen renders against the mockup (M2, M3, dark and light Settings). Every new string checked by the reviewer and by Marge's Decide pass (52 strings; results in `chippi-scratch\phase-4\strings\`). M4 smoke: page under 550 DIP with no scrolling for a five-disk desktop (496) and a two-disk laptop with battery and thermal zone (502), processor strip open; card with round buttons inside 672 DIP on the top and a side edge. One deviation, below. Live: sections 2 and 3. Result: pass |
| Readings agree in definition with Task Manager | Done | Live: section 4 (CPU Processor Utility, memory in use and committed, GPU busiest engine, dedicated and shared, network, disk active time and speeds). Result: pass |
| Alerts fire, repeat after the cooldown and respect Notifications settings, with controlled inputs | Done | `AlertSmokeTests` (notice, repeat after 2 min, stops when off), `AlertEvaluatorTests`, `ModuleTests`. Live: sections 7 and 8 (Memory in use at 50%, or GPU temperature at 40 °C; notice click opens the page). Result: pass |
| Polling cost near architecture.md §1; nothing polls when nothing is shown and no alert is on | Done | Full page at 2 s: 0.16% of one core on Travis's PC (estimate 0.24%); idle: 0 reads, 0 ms. `SensorSamplerTests.Nothing_is_read_while_nothing_watches`; M4 smoke checks the sampler's need is None after the island closes |
| Installer installs, updates in place and uninstalls; Travis's live checks pass | Done | `pack/phase-4` (`62dca8b`, PR #64): 12/12 hashes checked by Karl, Kayla and Marge; both rnnoise.dll files and the VB-CABLE zip match their pins in all four packages, and the app bundles System monitor. The x64 portable starts under a dev profile. Live: sections 1 and 10. Result: pass |

## Deviations from DESIGN.md

- **The GPU temperature notice uses the driver's full adapter name** ("NVIDIA GeForce RTX 4080 86 °C"), where design notes #34 show "RTX 4080" (#59, not fixing).

## Known issues

- **Five-disk laptop at 1280×720 on a side edge doesn't fit.** A laptop with battery, thermal zone, five physical disks and the processor strip open makes the page 574 DIP (budget 550), and the card with round buttons 702 DIP on a side edge against a 672 DIP work area (598 on the top edge). Marge ruled it an edge case; the layout is Travis's approved design and isn't trimmed for it. The numbers are kept in `chippi-scratch\phase-4\m4\edge-case-c571bae.log` (#63).
- **No CPU or board temperature, no motherboard fans.** They need the PawnIO driver and an admin helper (Hardware sensors in PLAN.md). The Thermal zone card shows only where firmware exposes one, and is never labelled CPU. Travis's PC has none.
- **Battery and Thermal zone are checked on fakes only.** Travis's PC has neither.
- **NVIDIA's fan % is the driver's intended speed, not a measurement.** NVML supplies it as a percentage; measured RPM shows only where D3DKMT reports it.
- **The Phase 3 installers are gone from the release folder**: the first Phase 4 packing attempt overwrote them. Nothing depends on them, and they can be rebuilt from `a4511eb`.
- **CI SDK pin** from Phase 3 (#41) is still in place at 10.0.402.
- **Not run on real hardware:** ARM64 and the desktop UiSmoke suite.

## Not fixing (from reviews)

- M1: Caution republishes every tick while an alert holds, so the island hub raises Changed every 1 to 2 s.
- M2: when the pages are already open, a notice click makes System monitor the last page used.
- M3: the Settings page's first capability discovery can take focus off a control.
- M3: dragging a threshold slider saves at each 5% step.
- D: the headers of `phase-4/architecture.md` and `phase-4/behavior-spec.md` still say research draft.
- M4: the five-disk laptop case above.
- Copy: "Overlaps Task Manager" stays, following DESIGN.md's Overlap badge pattern; "40 to 110 °C" stays as the GPU limit's error, as the shortest useful one.

## Live checks

Steps: `chippi-scratch\phase-4\live-checks.md` (task 26), reviewed by Karl. Travis reported "Pass" for every step on 2026-10-27. Installer `X:\code\chippi\build\artifacts\releases\0.1.0\win-x64\ChippiApp-win-x64-Setup.exe`, built from `763a619`.

| Section | Result |
|---|---|
| 1. Install and turn it on | Pass |
| 2. Tile | Pass |
| 3. Page | Pass |
| 4. Readings against Task Manager | Pass |
| 5. Logical processors | Pass |
| 6. GPU and network adapter pickers | Pass |
| 7. Alerts | Pass |
| 8. Notice click | Pass |
| 9. Gap after sleep | Pass |
| 10. Update in place and uninstall | Pass |

## Cleanup

Completed after the live-check results merged (#67). See [Phase 4 cleanup](end-of-phase-checklist.md#phase-4-cleanup-2026-10-27) for the verified 377-file archive and removals. The audit folder, current installers, boost experiment and installed Chippi were preserved. One local report reference is retained because its commit is not ancestry-merged, although its patch is already on main. The cleanup worktree remains until its PR merges.
