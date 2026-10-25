# Chippi Phase 3 brief: Sound

Phase 3 builds the Sound group: Audio routing and Mic processing (ported from Hopper), App volume, Mic mute shortcut and Volume boost, on one shared audio service. The island moves from a stack of panels to a grid of tiles that open pages, and the flyout gets one Sound tab. It builds on the Phase 1 and 2 shell without changing its fixed decisions.

Read first, in order:
1. This brief.
2. [phase-3/architecture.md](phase-3/architecture.md): projects, the audio service, role ownership, island tiles and pages, the VB-CABLE package (Kayla).
3. [phase-3/behavior-spec.md](phase-3/behavior-spec.md): Hopper parity with source lines, the three Hopper gaps, the mockup's behavior, the Install Driver states (Karl).
4. The approved mockup, `X:\code\chippi-scratch\phase-3-prep\mockup\` (start with `README.md`). Device, app and file names in it are sample data.
5. Hopper, `X:\code\hopper`. Travis's own code: port it (relicensed GPL-3.0-or-later). Mouzi and vorssaint stay reference only.
6. `DESIGN.md` and `PLAN.md` Phase 3. Their island text is out of date until §5 below lands.

Research behind this brief, archived at the end of the phase: `X:\code\chippi-scratch\phase-3\virtual-mic-routes.md` and `spikes\` (RNNoise ARM64, VB-CABLE ARM64, per-app output device).

## 1. Decisions (Travis, 2026-10-24 and 25)

| Topic | Decision |
|---|---|
| Mockup | Approved as drawn: Settings pages for all five features, Add features, Notifications, the flyout Sound tab and the island. Copy follows the Bare Label Rule, not the mockup's longer captions |
| Flyout | One **Sound** tab (audio-lines glyph) for every installed Sound feature |
| Mic mute glyph | Mic-off. Mic processing keeps `Mic`; no two features share a glyph |
| Muted mic on the island tab | Always shown, in caution color, after the top activity's glyph. A muted mic counts as live |
| Island | Opens on click to a **grid of tiles** (3 across): Now Playing plus one per installed feature, each with its status. Each tile opens its own page; the island stays one shape and morphs to each page's size (grid 328, Now Playing 440, Volume mixer 344). Round 40 px buttons orbit the open island |
| Hopper parity | Everything Hopper does today carries over, above all default-device priority and fallback (behavior-spec.md, parity table) |
| VB-CABLE | Mic processing keeps VB-CABLE, as Hopper does, and **Chippi ships it**. While it's missing, Mic processing's row in Add features shows **Install Driver** instead of its switch. That runs VB-Audio's own setup (one admin prompt, then a reboot); afterwards the row has the normal switch. Chippi itself stays standard-user. VB-Audio's OK, given for Hopper, covers it (Travis, 2026-10-25) |
| Volume boost | Research gate first. Windows caps app volume at 100%, so a boost means processing the app's audio. If it needs a driver or an APO, the options go to Travis before anything is built |
| Own virtual mic | Researched (virtual-mic-routes.md): not the only route, and nothing ready-made replaces VB-CABLE today. After Phase 3, test an effect (APO) on the real mic on a spare PC, never Travis's. Buy no certificate |
| Notifications | New groups on the app-wide page: Audio routing (Output switched, Mic pinned again) and Mic processing (Stopped) |

## 2. Lead calls (Marge)

**Routing and processing**
- **New devices** go to the end of their list. A device the user removed is remembered and never returns on its own; Add brings it back.
- **Role ownership:** Audio routing owns the default roles. While Mic processing runs with "set as default mic" on, it owns the capture roles and routing's winner becomes the processing source. Stopping hands them back. With "Switch call audio too" off, the Communications role is left alone. A role nobody owns is left to Windows.
- **Stale device IDs** are re-matched by name only when exactly one device has that name. Otherwise the entry shows as unavailable.
- **Processing follows its mic:** when the winning real mic changes, the pipeline rebuilds on the new one. After a failure it restarts when its mic returns, if still switched on; no other retry loop. No mic: "Stopped · No microphone".
- **Source:** "Pinned mic" (top of the input list) by default, or one chosen mic.
- **VB-CABLE is recognized by device identity**, not by "cable" in a name.
- **Gate default** −36.5 dB (Hopper's). The mockup's −50 is sample data.
- **Crash recovery:** before taking a capture role, processing records what it replaced. A normal stop, module removal and app uninstall restore it; the next launch restores it after a crash. No watchdog process. Limit, documented: after a crash the cable stays the default mic until Chippi runs again.
- **Fights over defaults** with other software: repair as Hopper does, back off after 3 repairs in 10 s, and log it.

**Mute**
- The shortcut mutes every mic unless all are already muted, then it unmutes all. New mics are muted while the latch is on, and a pipeline rebuild never unmutes.
- Turning Mic mute off, or removing it, unmutes only the mics Chippi muted.
- The shortcut never takes or imitates Win+Alt+K. A taken shortcut shows "Shortcut in use".

**App volume**
- **Per-app output device** uses Windows' undocumented `IAudioPolicyConfigFactory` (spike: works on build 26300 x64). It's enabled only when activation and a read probe pass, it reads back every write, and it never calls the method that clears every app's setting. On any failure the device picker hides; volume and mute keep working. The choice applies to the app's next stream, so the mixer shows the chosen device without claiming the app has moved.
- The remembered output belongs to the app (exe), not the process, and is reapplied when the app or device returns.

**Island**
- Reopening goes to the last page if it still exists, else the grid.
- **Keyboard:** only Esc is watched until the user clicks into the island; then Tab, arrows and Enter work inside it. A global hook that takes Tab would break the app in front.
- A shared tile's status comes from its highest-priority module (Mic mute's "Muted" wins on Microphone).
- **Side edges:** proposal in §5, needs Travis's yes.

**Install Driver**
- States: Install Driver → busy → Restart to finish → the normal switch. Cancelling the admin prompt goes back to Install Driver with no message. Driver presence never turns Mic processing on.
- VB-CABLE removed while running: processing stops, roles go back, "Stopped" is raised, the row returns to Install Driver.
- **Installed but disabled in Windows:** the row says **Driver disabled** with a **Sound settings** button. It never reruns setup.
- The package ships unmodified (VB-Audio's readme requires it), pinned by size and SHA-256 in `build/vendor/vb-cable.json`, checked at pack time and again, with its signer, before launch. About and THIRD-PARTY-NOTICES credit it as VB-Audio donationware with a donate link. Uninstalling Chippi never removes it.
- **No rename of VB-CABLE's endpoints** (Travis asked, 2026-10-25; spike `endpoint-rename.md`). A standard user can't write the endpoint name through the documented API, so renaming would need Chippi's own admin helper and would be lost on every reinstall. Chippi shows the real name, "CABLE Output", so it matches what Discord and other apps list. With "set as default mic" on, apps using the default mic need nothing. "CABLE In 16ch" is left alone; Chippi never selects it.
- **RNNoise for ARM64** is built in CI from Hopper's patched source, with `/MT`. Nothing is installed on Travis's PC to build it.

## 3. Open items for Travis

1. **Island buttons on a side edge** (§5): approve or change.
2. **ARM64 hardware:** no ARM64 PC is available, so ARM64 is build-checked and the RNNoise DLL is test-loaded in CI, but VB-CABLE setup under emulation and per-app devices on ARM64 stay untested unless Travis has one.

## 4. Fixed decisions carried forward

- C# / .NET 10 / WPF, Windows 11, x64 and ARM64, GPL-3.0-or-later.
- Standard user only. The only elevated step is VB-Audio's setup, started by the user.
- No telemetry and no network calls.
- No stock WPF chrome. DESIGN.md wins over taste. The Bare Label Rule (DESIGN.md §3).
- The island never hides at rest. Hover swells it, click opens it (PR #29).

## 5. Design work before views

Marge, before the views are built:
- **DESIGN.md › Island** rewritten for the grid, pages, page widths, orbit buttons and the muted glyph; the old stack of panels goes.
- **DESIGN.md** gains the flyout Sound tab, the vertical volume bar (to 200% with a 100% line and caution above it when Volume boost is installed) and the mic-off glyph.
- **PLAN.md:** the Island row says opens on click, grid of tiles.
- **Side-edge orbit buttons, proposal:** on the Left or Right edge the buttons can't sit on the edge side, so all four stack in one column on the open side, top to bottom: back, Keep awake, Settings, Volume mixer. The live button stays below the island. Travis approves before S6 builds it.

## 6. Workstreams

| # | Workstream | Owner | Reviewer | Starts |
|---|---|---|---|---|
| S0 | Core contracts: `AddIslandTile`, `AddFlyoutSection`, `IFeatureRequirement`, `IslandActivity.TileKey`; Keep awake and File organizer moved to tiles with their pages unchanged | Kayla | Karl | now; lands first |
| S1 | `Chippi.Audio`: engine thread, device catalog, role arbiter, priority policy, sessions, endpoint controls, fakes and tests | Kayla | Karl | now |
| S2 | Audio routing module and views | Karl | Kayla | after S1 |
| S3 | Mic processing: pipeline port, DSP, transport, recovery, VB-CABLE requirement and pin, RNNoise CI build for both architectures | Kayla | Karl | after S0 and S1 |
| S4 | App volume: sessions, per-app device behind its gate, Volume mixer page | Karl | Kayla | after S0 and S1 |
| S5 | Mic mute shortcut: hotkey, latch, muted glyph | Karl | Kayla | after S0 and S1 |
| S6 | Island grid: tiles, page morph, orbit buttons, last page, keyboard, Now Playing | Kayla | Karl | after S0; side edges after Travis's yes |
| S7 | UI smoke tests: Sound tab, Add features rows, the grid. No real device changes | Karl | Kayla | after S2 to S6 |
| Gate | Volume boost backend spike, then options to Travis | Karl | Kayla | after S1 |
| Lead | DESIGN.md and PLAN.md updates (§5), composition slots, integration, report | Marge | Kayla | now |

## 7. Process

- Branch and worktree per workstream. Each owner touches only the folders they own. A second member reviews before merge. Marge merges through a GitHub PR, then pushes `main` and removes the branch and worktree.
- **Review bar:** only bugs that break functionality or normal use block a merge. Theoretical edge cases and extra hardening get one line as "not fixing".
- Scratch, review notes and probes go in `X:\code\chippi-scratch\phase-3\`, never the repo root. Dev runs use `CHIPPI_DEV_PROFILE`.
- **Travis's PC:** nobody installs anything (VB-CABLE, other drivers, compilers) without asking. Nobody changes his audio setup without asking: default output, default mic, mute, app volumes and per-app devices. That includes the registry stores behind them: a test that has to write one asks first, backs it up first, and restores it after. All policy is tested against `AudioFakes`. No audible test sound without asking.
- Desktop checks: screenshot every monitor first, confirm the foreground window belongs to the test target before any key, and stop background scripts by PID with a time limit.
- One lead thread. No side jobs.
- At the end of the phase, run the [end-of-phase checklist](end-of-phase-checklist.md).

## 8. Done when

- Build and tests pass with 0 warnings for win-x64 and win-arm64. Policy tests cover the behavior spec, including Hopper's parity table and its three gaps.
- **Audio routing:** a higher-priority output switches when connected, and disconnecting falls back down the list; with nothing listed available, Windows' choice is left alone. The pinned mic stays pinned, so AirPods stay in stereo. A removed device stays removed.
- **Mic processing:** with VB-CABLE installed, apps hear the cleaned mic through CABLE Output; the pipeline follows its mic; stopping and uninstalling restore the real mic. Without VB-CABLE, Add features shows Install Driver and the states in §2 work. RNNoise loads on x64 and in CI on ARM64.
- **App volume:** volume, mute and (where the gate passes) output device per app work from Settings, the flyout and the island.
- **Mic mute shortcut:** the shortcut mutes every mic, new mics join the latch, the island tab shows the caution glyph and the flyout header shows muted.
- **Volume boost:** works as its spike decided, or is deferred with Travis's sign-off.
- **Island:** opens on the grid with tiles only for installed features; pages, orbit buttons and the morph work as in §1 and §5; Esc, and after a click Tab, arrows and Enter, work.
- **Installer:** installs, updates in place and uninstalls cleanly. Uninstall restores the real mic and never removes VB-CABLE.
- **Hopper:** once Chippi covers it, Travis is told so he can archive the Hopper repo.
