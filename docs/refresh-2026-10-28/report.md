# Chippi refresh report: Island and Settings

Updated 2026-10-30 against `main` `4132538`. The refresh shipped in PRs #92 to #109, through `d443ed1`. Phase 5.1 subsequently replaced its island with the movable widget and panel. Marge closed the refresh live check (task 123) as superseded by the redesign live check (task 181), which Travis marked Pass on the build from `4f52e49`. The final build from `16bfd6f`, including #136, is installed. The refresh has no separate live-check Pass.

Brief: [brief.md](brief.md). Approved references: [fixed-size/](fixed-size/), [sound-monitor/](sound-monitor/) and [settings-shell/](settings-shell/). The refresh revised the surfaces rule, retained in DESIGN.md and PLAN.md: the island shows live state and controls in a fixed body; Settings starts with the same controls, then configuration and complete lists.

## What was built

| PR | What |
|---|---|
| #92 | Part A: one 520 x 360 island, 496 x 304 body, stable shape between pages, compact grid and orbit controls; tray click opens Settings |
| #93 | Part E: Phase 5 page and Settings fixes supporting the refresh |
| #94 | Part B: compact Sound page, one-line device rows, mic processing wording and states |
| #96 | Part C: compact System monitor readings and Disks tables, whole rows within the island |
| #95 | Part D5-D7: Clipboard, Scratchpad and Shelf pages fitted to the fixed body |
| #98 | Part D1-D4: Now Playing, App volume, Keep awake and File organizer pages fitted to the fixed body |
| #99 | Shared Settings layout toolkit: sections, cards, rows, expanders, combos and empty states |
| #97 | Settings window frame, grouped sidebar, narrow rail, scrolling navigation, placement and state restoration |
| #100 | Sound Settings: island controls above configuration, plus a new Now Playing page |
| #102 | System Settings: all monitor readings, thermal zones and drives; expandable alert thresholds; Keep awake controls; File organizer folder actions and folded unmatched preview rows |
| #101 | Clipboard and text Settings: complete searchable history, Pause/Resume and row menus, Clean URL controls, new shared-session Scratchpad and Shelf pages; Settings Paste targets the window switched from |
| #103 | Two-surfaces documentation, short error fragments and removal of the duplicate empty-preview message |
| #104 | Chippi Settings: Add features, Shortcuts, Permissions, General import confirmation, Notifications layout, Island preview and About; no feature overlap badges |
| #105 | Once-per-opening hardware availability check hides unsupported notification kinds, keeps saved choices, and avoids a Low battery flash; shortcut capture rows share their parent card |
| #106 | Final wording changes, explanatory tooltip removal and straight quotes in Scratchpad delete confirmations |
| #107 | Initial refresh release artifact manifest |
| #108 | Island-lag fixes by Travis's UI/UX agent: old-page snapshot fades over the new page; latest-only serialized volume writes; fewer app-output reads; Clipboard builds at most 16 rows; System monitor primes rate readings and reuses last-visit readings |
| #109 | Rebuilt release artifact manifest including #108 |

## How to build and run

Use the pinned SDK from `global.json` and the existing publish profiles. Packaging uses `build/pack.ps1 -Version 0.1.0` for win-x64 and win-arm64. The script restores the pinned Velopack tool and validates the bundled VB-CABLE archive against its pin. No driver installation is part of packaging.

Development and automated checks use isolated dev profiles and fake clipboard/audio sources. The portable startup check used an empty custom feature set with the island and recording disabled. It confirmed startup only, not tray clicks, island opening or installed update behavior. Its owned process, dev profile, shortcut and notification COM registration were removed; the installed Chippi remained running and its settings hash was unchanged.

## Test results

Historical refresh installer source `1f13495`: [push CI run 40919456997](https://github.com/tcurtsinger/Chippi/actions/runs/40919456997). Earlier source `7b4a8f1` passed [run 40887785625](https://github.com/tcurtsinger/Chippi/actions/runs/40887785625) before #108.

- Release application builds for win-x64 and win-arm64: 0 warnings, 0 errors each; both publish jobs passed.
- Regular tests on x64: 1,364 passed, 0 failed. Audio 114, Clipboard 50, Core 111, Clipboard features 68, Downloads 122, Keep awake 32, Plain paste 14, Scratchpad 27, Shelf 27, Sound 95, System monitor 90, Island 192, Notifications 26, Rules 117, Sound smoke 7, URL cleaning 272.
- UiSmoke: 2 windowless checks passed; 16 desktop cases skipped. The desktop suite was not run for this refresh.
- PR #106's reviewed head `f678b53` also passed all four push/PR CI jobs before merge. Karl independently ran its Settings window (10) and Scratchpad (27) tests.
- Review regressions included valid-import page rebuilding, Scratchpad load-failure Retry, shared note updates, empty monitor surface hiding, unmatched-file singular/plural wording, shortcut card styling, hardware filtering and cached availability on reopening.

Offscreen before/after renders are under this folder's part directories. External scratch paths below are historical; the refresh and redesign evidence was archived during Phase 5.1 cleanup. Chippi page comparison: `X:\code\chippi-scratch\refresh\chippi-settings-comparison.html` (28 renders, seven pages in dark and light). These use actual WPF controls with fake data; they do not certify native input or motion.

## Acceptance checklist

| Check | Status | Evidence |
|---|---|---|
| Fixed island size and compact page fit | Automated/review checks complete; island superseded | Part A-D reviews, renders and Island tests; Phase 5.1 replaced this island |
| Shared Settings layout and controls | Automated/review checks complete; later installed check passed | PRs #97, #99-#105; task 181 on the redesign build |
| Wording and surfaces docs | Done | PRs #103 and #106; Marge's wording decisions |
| Both refresh packages match the manifest | Done | PR #109, independent checks by Karl, Kayla and Marge |
| Refresh installed user flows (task 123) | Superseded | Marge closed task 123; Travis marked task 181 Pass on `4f52e49` |
| Final redesign installation | Done, reported by Marge | Task 190 Yes; silent Setup exit 0; installed build `16bfd6f` |
| Refresh and redesign cleanup | Files cleaned; record PR pending | Kayla's task 191, Herb's disk check, PR #139; report worktree retained for this PR |

## Decisions

- Travis (task 65): Settings Clean URL shows the cleaned link and Copy only, with no "Removed" caption.
- Sidebar order follows Part F1 where Part E's differed; Now Playing is always first under Sound.
- Settings remembers its last page, size and position across restarts.
- Settings Paste goes to the window Settings was switched from, otherwise Copy and "Copied".
- The island Shelf shows up to 40 items ("Shelf full"); Settings lists them all. System monitor Settings lists every thermal zone and drive the island leaves out.
- Notifications reads which notice kinds the PC supports once per opening, through a read-only property, with no watch API.
- Wording: errors are short "… failed" fragments (no "Couldn't …" remains), no explaining tooltips, straight apostrophes and quotes.

## Island-lag follow-up (#108)

Travis requested Kayla as the sole reviewer. Karl did not review #108. Kayla first requested changes, then approved `6a55406` after its fixes. Her [initial findings](https://github.com/tcurtsinger/Chippi/pull/108#issuecomment-6503082367) and [recheck verdict](https://github.com/tcurtsinger/Chippi/pull/108#issuecomment-6503377860) record the evidence.

- A second sampler wake could cut the first two samples' interval to about 0.1 ms. The first interval now waits the full 100 ms despite ordinary wakes, while stop still interrupts it.
- Controls on the new page could receive a tile's second click beneath the old-page snapshot. Input now waits until that snapshot has faded.
- App outputs are refreshed on app/output changes, or on a later sound event when the last read is over two seconds old. This is not a two-second polling timer. Slider changes still avoid an output read on every step.

Kayla ran the relevant suites and confirmed failure under reverted code for the sampler wait, hidden-page input and excessive output reads. She explicitly noted that the two-second refresh itself has no dedicated test. Her verified Island count is 192, not the agent's reported 195. The fixes merged in `1f13495` and shipped in the refresh installer. Task 123 was later superseded by task 181; no separate live verdict was recorded for #108 on the refresh build.

## Deviations from DESIGN.md

No unapproved design deviations recorded. Tray-based Paste behavior is an observation in live step 5.3, not a predetermined pass/fail: the window Windows reports during activation determines whether Chippi pastes or falls back to Copy.

## Known issues

- ARM64 was built and packaged, not run on ARM64 hardware.
- The refresh portable check covered startup only. Task 123 has no separate completed live result. The later redesign build passed task 181.
- On the first Notifications opening, feature groups remain hidden until availability checks finish. Later openings immediately show cached availability while refreshing it. A feature installed while this page is open appears after reopening. Kayla reported both as nonblocking review notes.
- Hardware checks run once per page opening, without a watch subscription or timer. The small read-only Core availability property was explicitly approved by Marge.

## Flaky tests

This report does not claim that all timing flakes have been eliminated. Final-source CI passed. Availability tests use a controlled gate to check first opening and reopening, rather than assuming sensor-query timing. During Kayla's #108 recheck, a tab-drag test failed once, then passed three isolated reruns and a second full Island run. It also passed on merged main `1f13495`. No timing rewrite was made; Marge will assign one if it recurs. Earlier Phase 5 timing lessons remain in [phase-5-report.md](../phase-5-report.md#flaky-tests).

## Not fixing (from reviews)

The first-open Notifications wait and refresh-on-reopen behavior above were accepted as nonblocking. The existing Phase 5 docked Shelf limitation remains historical context; the refresh removes the tray's docked-page route in favor of Settings. No Phase 5.5 feature work is included in this report.

## Live checks

The refresh steps were at `X:\code\chippi-scratch\refresh\live-checks.md`, reviewed by Karl and Marge and assigned as task 123. Marge closed that task when Phase 5.1 replaced the island. Its sections have no separately recorded Pass, Fail or Skipped results; the Paste observation in step 5.3 is also unrecorded.

Travis marked the replacement live check, task 181, Pass on the build from `4f52e49` (PLAN.md records 2026-10-29). The historical refresh steps have no individual live results. That build predates #136's cleanup fixes: the Settings preview, remembered Keep awake length, Synth swatch and pet-color preview redraw.

Travis then answered Yes to installing the final build (task 190). Marge reported silent Setup exit 0, successful installation in the installer log, the running file version `0.1.0+16bfd6f84683e184599f35cc07ab7d9bc0a57983`, and no warnings or errors in the app log since restart. Marge did not inspect its screen. Karl did not run that installation or observe its UI.

## Installers

### Historical refresh builds

The last refresh source was `1f13495`, manifest commit `16dc592`, merged in #109 as `d443ed1`. The earlier build used source `7b4a8f1` and manifest `bc9527c` (#107). Both have been superseded. At delivery, all 12 files matched their manifests; Kayla also checked both native RNNoise libraries and the pinned VB-CABLE ZIP inside both packages.

| Platform | Setup bytes | Portable bytes | Full package bytes |
|---|---:|---:|---:|
| win-x64 | 82,198,782 | 74,683,688 | 74,684,670 |
| win-arm64 | 75,482,767 | 69,573,817 | 69,574,799 |

Historical packaging evidence was under `refresh\pack-2`, including `pack.log`, `artifacts.csv`, `portable-startup.log` and `startup-check.ps1`. The earlier build evidence was under `refresh\pack` and `refresh\pack-check`. The original release folder was `X:\code\chippi\build\artifacts\releases\0.1.0`. These paths describe the refresh delivery, not the currently installed build. Earlier installer archives include `refresh-installers-0.1.0-7b4a8f1` and `phase-5-installers-0.1.0-fa63a54` under `X:\code\chippi-archive`.

### Replacement redesign builds

Task 181 used source `4f52e49`, with x64 Setup 82,202,288 bytes. The final installer used main `16bfd6f`, with x64 Setup 82,202,609 bytes and ARM64 Setup 75,487,558 bytes. PR #138, manifest commit `7edce0c`, merged as `4132538`, records that final set. Karl built both platforms; Herb independently matched all 12 sizes and SHA-256 hashes. Both PR CI runs and main's CI on `16bfd6f` passed. Packaging was build-only; ARM64 execution remains untested.

Full 12-file sets are preserved in `X:\code\chippi-archive\phase-5.1-installers-0.1.0-4f52e49` and `phase-5.1-installers-0.1.0-16bfd6f`. Herb rehashed both sets, 12/12 each. The retained x64 Setups are under `X:\code\chippi-scratch\shell-redesign\installer-4f52e49` and `installer-16bfd6f`; the final folder also contains Marge's install log.

## Cleanup

Kayla completed the refresh and redesign file cleanup on 2026-10-30 under [end-of-phase-checklist.md](../end-of-phase-checklist.md), and Herb checked her list on disk. PR #139 records it and is pending integration at this report's update.

- The refresh and redesign scratch evidence is archived at `X:\code\chippi-archive\phase-5.1-cleanup-2026-10-30\phase-5.1-leftovers-2026-10-30.zip` (4,839 files). Kayla checked each archived file's size and SHA-256 before removing originals. Herb confirmed the file count, without repeating that per-file hash check.
- Cleanup removed 20 worktrees, 23 local branches and 24 GitHub branches after ancestry checks. This unmerged report worktree was retained for publication; it is removed after its PR merges.
- Both Chippi-dev roots (25 profiles), nine temporary folders, ten dev Explorer keys, eleven notification CLSIDs and eleven dev Start Menu shortcuts were removed. This covers the four profiles previously listed here: `chippi-pages-test`, `failed-shortcut-test`, `notice-filter-test` and `render-chippi`. Kayla reported a clean rescan; Herb independently checked the folders and shortcuts, without repeating the registry scan.
- Both installer sets and retained Setup copies remain. The installed app, its settings, `audit-2026-10-27`, the retained `spike/boost-loopback` branch and the owner's `.impeccable/` folder were preserved.
