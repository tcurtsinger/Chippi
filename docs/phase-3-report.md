# Chippi Phase 3 report: Sound

Phase 3 (the Sound group and the island grid) is built and on GitHub at `main` `1e6341c`. Every merge went through a second member's review first, except the one-line CI SDK pin (#41). The installers from `fcdb871` are in `build\artifacts\releases\0.1.0` (PR #46, 12/12 hashes checked). Travis's live checks are still to come, so nothing that changes his audio setup has run on real hardware yet. ARM64 is built and packaged but never run.

Brief: [phase-3-handoff.md](phase-3-handoff.md), with [phase-3/architecture.md](phase-3/architecture.md) and [phase-3/behavior-spec.md](phase-3/behavior-spec.md). Phase 3 is PRs #31 to #46.

## What was built

| PR | What |
|---|---|
| #31 | Brief, architecture and behavior spec (Hopper parity table, three Hopper gaps, Install Driver states) |
| #32 | S0 Core contracts: `AddIslandTile` with shared tiles and status priority, `AddFlyoutSection`, `IFeatureRequirement`, `IslandActivity.TileKey`. Keep awake and File organizer moved to tiles |
| #33 | DESIGN.md › Island rewritten for the tile grid, pages and orbit buttons; the flyout Sound tab, the vertical volume bar and the mic-off glyph |
| #34 | RNNoise built in CI for x64 and ARM64 from Hopper's patched source (`/MT`, KERNEL32 imports only), with a load-and-process smoke test on both |
| #35 | S1 `Chippi.Audio`: engine thread, device catalog, role arbiter with repair back-off, priority policy, sessions, endpoint volume and mute, `FakeDeviceSystem` |
| #36 | S2 Audio routing (Hopper port): output and mic priority lists, removed devices stay removed, call-role switching, notices; Settings page, flyout and island sections |
| #37 | S3 Mic processing (Hopper port): capture → RNNoise → gate → AGC → VB-CABLE, VB-CABLE recognized by device identity, the pipeline follows its mic, crash recovery marker, Install Driver row, VB-CABLE package pinned by size, SHA-256 and signer |
| #38 | Sound UI tests run on a pumped STA dispatcher (test-host crash) |
| #39 | S4 App volume: per-app volume and mute, per-app output device behind its gate (x64 build 26300, readback after every write) |
| #40 | S6 Island grid: tiles three across, pages that morph out of the tab, orbit buttons (one column on side edges), last page, Esc-then-click keyboard, Now Playing on Windows media sessions |
| #41 | CI pins the .NET SDK to 10.0.402 (Microsoft's 10.0.403 download returned 409) |
| #42 | S5 Mic mute shortcut: `RegisterHotKey` (no keyboard hook), latch that owns only the mics it muted, caution glyph on the tab and flyout header |
| #43 | Microphone tile shows mic-off, in Caution, only while muted |
| #44 | S7 Sound smoke tests on fakes: Sound tab, Settings pages, Install Driver states, island grid pages |
| #45 | rnnoise.dll kept out of the single-file bundle (published builds couldn't load it) |
| #46 | Phase 3 installers: release artifact manifest |
| `0a7fab2` (PR pending) | Now Playing gets the system volume (from App volume) and output picker (from Audio routing) below the player, each only while its feature is installed |

## How to build and run

Unchanged from the [Phase 1 report](phase-1-report.md#how-to-build-and-run). Additions:
- Sound features need a real audio setup to do anything; all tests use `FakeDeviceSystem`. Never install Sound modules in a dev profile on Travis's PC without his OK: Audio routing claims the default roles when it starts.
- Debug `--debug --island-pages <folder>` renders the island grid and pages; `CHIPPI_ISLAND_SHOTS=<folder>` makes `IslandPanelShots` save them off screen.
- Mic processing's recovery marker is `%LOCALAPPDATA%\Chippi\data\mic-processing\processing-owner.json`.

## Test results

At `main` `1e6341c`:
- Release win-x64 and win-arm64 with `--no-incremental`: 0 warnings, 0 errors each.
- Every test project except UiSmoke: 647 passed, 0 failed (Audio 100, Core 103, Downloads 117, Keep awake 30, Sound 70, Island 81, Notifications 25, Rules 117, Sound smoke 4). Island is 82 in Debug, which adds the shots test. Phase 2.5 ended at 425.
- CI runs every test project on x64, including the new `Chippi.SoundSmoke`, and builds both platforms. RNNoise's own CI job loads the DLL on x64 and ARM64 runners.
- UiSmoke (the desktop suite) wasn't run in Phase 3.

## Acceptance checklist (brief §8)

| Check | Status | Evidence |
|---|---|---|
| 0 warnings both platforms; policy tests cover the behavior spec, Hopper's parity table and its three gaps | Done | See Test results. Parity rows on fakes in the Sound and Audio tests (#36, #37). Gaps: `The_pipeline_follows_the_winning_mic`, `Recovery_after_a_crash_puts_back_roles_still_on_the_cable`, `A_real_mic_named_like_the_cable_is_still_a_mic` (`tests/Chippi.Audio.Tests/ProcessingTests.cs`) |
| Audio routing: higher-priority output switches in, falls back on disconnect, leaves Windows' choice alone with nothing listed; pinned mic stays (AirPods stereo); removed device stays removed | Done in tests, waiting on a live check | Routing tests on fakes (#36). Live: `live-checks.md` Part A steps 2 to 4. Result: pending |
| Mic processing: cleaned mic through CABLE Output; follows its mic; stop and uninstall restore the real mic; Install Driver states; RNNoise loads on x64 and in CI on ARM64 | Partly | Pipeline, recovery and requirement states in tests (#37); Install Driver states in S7 smoke tests. RNNoise: CI on x64 and ARM64 (#34); a single-file harness with the shipping publish settings loads it from the published layout and processes a frame (#45). Install Driver has never run, and VB-CABLE identity is untested on real hardware. Live: Part B. Result: pending |
| App volume: volume, mute and output device per app from Settings, flyout and island | Done in tests, waiting on a live check | S4 tests and the S7 smoke test (Settings slider reaches the fake engine; island mixer page). No per-app routing write was made on real hardware. Live: Part A step 5 covers volume and mute; the output picker has no live step yet. Result: pending |
| Mic mute shortcut: mutes every mic, new mics join, caution glyph on the tab, flyout header shows muted | Done in tests, waiting on a live check | `MicMuteTests`, `MicMuteTabTests`, S7 flyout test. The native hotkey has never been pressed. Live: Part A step 6. Result: pending |
| Volume boost: works as its spike decided, or deferred with Travis's sign-off | Done (deferred) | Deferred with Travis's sign-off (task 21, 2026-10-25) to its own phase, "Volume boost backend" in PLAN.md. See Volume boost below |
| Island: grid with tiles only for installed features; pages, orbit buttons and morph; Esc, then after a click Tab, arrows and Enter | Done in tests, waiting on a live check | `IslandPanelTests`, `IslandOrbitTests`, offscreen shots (#40). Now Playing's system volume and output picker: `Now_playing_shows_system_volume_and_output_picker_only_for_installed_features` (`0a7fab2`). Clicking in and the keyboard were checked by reading the code only. Live: section 1. Result: pending |
| Installer installs, updates in place, uninstalls cleanly; uninstall restores the real mic and never removes VB-CABLE | Partly | Packages built and hash-checked (#46); the x64 portable starts under a dev profile. Update in place: section 1 step 1. Uninstall: Part B step 5 (optional). Result: pending |
| Hopper: Travis told once Chippi covers it | Waiting on the live checks | Told after Parts A and B pass |

## Deviations from DESIGN.md

- **App volume's flyout output pickers sit below each app row**, not beside it, to fit the flyout's width (S4).
- **Two values were extrapolated from the mockup** when DESIGN.md was written (#33): the side-column 12 px offset with 8 px gaps, and Home/End on volume bars. They're in DESIGN.md, so the code matches it.

## Known issues

- **CI SDK pin.** CI uses SDK 10.0.402 until Microsoft's 10.0.403 download works; then the pin comes out (#41). Local builds aren't affected.
- **After a crash, the cable stays the default mic** until Chippi runs again (documented limit, brief §2). Mics muted by the shortcut also stay muted after a crash, with no marker; the next start shows them as Muted and the shortcut unmutes them.
- **A mic that disconnects while muted** can't be unmuted by Chippi once the last audio lease has ended (S5).
- **VB-CABLE's signing certificate expired in 2025.** The timestamped signature still verifies; if VB-Audio re-signs the package, the pin in `build/vendor/vb-cable.json` needs updating.
- **ARM64:** per-app output devices are switched off on ARM64 (the gate is x64 only), and VB-Audio's setup is x64 only, so Install Driver under emulation is untested. No ARM64 PC was available.
- **Not run on real hardware:** routing switches, per-app routing writes, the mute hotkey, Install Driver and VB-CABLE detection, media transport buttons, clicking into the island, and an uninstall of a Phase 3 build. Live checks cover these; see the checklist.

## Not fixing (from reviews)

- S2: the flyout volume slider may tug during a fast drag (check in the live run); the section's own "Sound" heading moves to the tab once more sections arrive.
- S4: a stale reapply in `RefreshOutputsAsync` skips the remaining apps for that snapshot; the next snapshot retries.
- S4: a per-app output write that fails partway through its roles can leave the app's roles on different outputs; the picker is then switched off, with no rollback.
- S5: no crash marker for the mute latch (see Known issues).
- S5: the test added in `d0fa298` is missing a blank line before the next test.
- S7: if a cleanup assertion fails, that run's `s7-*` profile folder stays behind.

## Live checks

Steps: `X:\code\chippi-scratch\phase-3\live-checks.md` (task 20).

| Section | Result |
|---|---|
| 1. Update and island | Result: pending |
| Part A: routing, app volume, mute | Result: pending |
| Part B: Mic processing with VB-CABLE | Result: pending |

## Volume boost

Deferred out of Phase 3 with Travis's sign-off (task 21, "Plan a driver route"). App volume stays 0 to 100%. It becomes its own later phase, "Volume boost backend" in PLAN.md's backlog.

- **Gate (Karl):** no-go for shipping on the evidence available. Windows caps app volume at 100%, so a boost means processing the app's audio. [phase-3/boost-gate.md](phase-3/boost-gate.md).
- **Experiment, route 1 (Kayla, Travis chose "Experiment now" on task 19):** capture one app with process loopback, quiet its own output, replay it with gain. No-go as tested, in Windows Sandbox on build 26100 x64: muting the app's session or setting it to volume 0 also silenced its capture, so there was nothing to replay. Capture isolation worked (only the chosen app), and a 60 s replay at gain 2 had no dropouts with a 1.99x level. Latency wasn't measured reliably. Other no-driver ways of silencing an app weren't tested. Nothing reached Travis's speakers. [phase-3/boost-experiment.md](phase-3/boost-experiment.md), code on branch `spike/boost-loopback`.
