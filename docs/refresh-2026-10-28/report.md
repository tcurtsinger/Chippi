# Chippi refresh report: Island and Settings

Draft, 2026-10-29. The refresh is merged through `main` `90f920b` (PR #107). The installers were built from `7b4a8f1`; the later commits only record their artifact manifest. Travis's installed-build live check (task 123) is pending. This report does not mark the refresh accepted or cleanup complete.

Brief: [brief.md](brief.md). Approved references: [fixed-size/](fixed-size/), [sound-monitor/](sound-monitor/) and [settings-shell/](settings-shell/). DESIGN.md and PLAN.md record the revised surfaces rule: the island shows live state and controls in a fixed body; Settings starts with the same controls, then configuration and complete lists.

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
| #107 | Final release artifact manifest |

## How to build and run

Use the pinned SDK from `global.json` and the existing publish profiles. Packaging uses `build/pack.ps1 -Version 0.1.0` for win-x64 and win-arm64. The script restores the pinned Velopack tool and validates the bundled VB-CABLE archive against its pin. No driver installation is part of packaging.

Development and automated checks use isolated dev profiles and fake clipboard/audio sources. The portable startup check used an empty custom feature set with the island and recording disabled. It confirmed startup only, not tray clicks, island opening or installed update behavior. Its owned process, dev profile, shortcut and notification COM registration were removed; the installed Chippi remained running and its settings hash was unchanged.

## Test results

Final installer source `7b4a8f1`: [push CI run 40887785625](https://github.com/tcurtsinger/Chippi/actions/runs/40887785625).

- Release application builds for win-x64 and win-arm64: 0 warnings, 0 errors each; both publish jobs passed.
- Regular tests on x64: 1,357 passed, 0 failed. Audio 114, Clipboard 50, Core 111, Clipboard features 67, Downloads 122, Keep awake 32, Plain paste 14, Scratchpad 27, Shelf 27, Sound 92, System monitor 87, Island 192, Notifications 26, Rules 117, Sound smoke 7, URL cleaning 272.
- UiSmoke: 2 windowless checks passed; 16 desktop cases skipped. The desktop suite was not run for this refresh.
- PR #106's reviewed head `f678b53` also passed all four push/PR CI jobs before merge. Karl independently ran its Settings window (10) and Scratchpad (27) tests.
- Review regressions included valid-import page rebuilding, Scratchpad load-failure Retry, shared note updates, empty monitor surface hiding, unmatched-file singular/plural wording, shortcut card styling, hardware filtering and cached availability on reopening.

Offscreen before/after renders are under this folder's part directories. Chippi page comparison: `X:\code\chippi-scratch\refresh\chippi-settings-comparison.html` (28 renders, seven pages in dark and light). These use actual WPF controls with fake data; they do not certify native input or motion.

## Acceptance checklist

| Check | Status | Evidence |
|---|---|---|
| Fixed island size and compact page fit | Automated/review checks complete; live pending | Part A-D reviews, renders and Island tests; task 123 section 2 |
| Shared Settings layout and controls | Automated/review checks complete; live pending | PRs #97, #99-#105; task 123 sections 1, 3, 4 and 6 |
| Wording and surfaces docs | Done | PRs #103 and #106; Marge's wording decisions |
| Both platform packages match the manifest | Done | PR #107, independent checks by Karl, Kayla and Marge |
| Update in place and installed user flows | Pending | Travis's task 123 |
| End-of-refresh cleanup | Pending | After live results and report integration |

## Decisions

- Travis (task 65): Settings Clean URL shows the cleaned link and Copy only, with no "Removed" caption.
- Sidebar order follows Part F1 where Part E's differed; Now Playing is always first under Sound.
- Settings remembers its last page, size and position across restarts.
- Settings Paste goes to the window Settings was switched from, otherwise Copy and "Copied".
- The island Shelf shows up to 40 items ("Shelf full"); Settings lists them all. System monitor Settings lists every thermal zone and drive the island leaves out.
- Notifications reads which notice kinds the PC supports once per opening, through a read-only property, with no watch API.
- Wording: errors are short "… failed" fragments (no "Couldn't …" remains), no explaining tooltips, straight apostrophes and quotes.

## Deviations from DESIGN.md

No unapproved design deviations recorded. Tray-based Paste behavior is an observation in live step 5.3, not a predetermined pass/fail: the window Windows reports during activation determines whether Chippi pastes or falls back to Copy.

## Known issues

- ARM64 was built and packaged, not run on ARM64 hardware.
- Native tray, island, Settings frame and input behavior await the installed-build live check. The portable check covers startup only.
- On the first Notifications opening, feature groups remain hidden until availability checks finish. Later openings immediately show cached availability while refreshing it. A feature installed while this page is open appears after reopening. Kayla reported both as nonblocking review notes.
- Hardware checks run once per page opening, without a watch subscription or timer. The small read-only Core availability property was explicitly approved by Marge.

## Flaky tests

This report does not claim that all timing flakes have been eliminated. Final-source CI passed. Availability tests use a controlled gate to check first opening and reopening, rather than assuming sensor-query timing. Earlier Phase 5 timing lessons remain in [phase-5-report.md](../phase-5-report.md#flaky-tests).

## Not fixing (from reviews)

The first-open Notifications wait and refresh-on-reopen behavior above were accepted as nonblocking. The existing Phase 5 docked Shelf limitation remains historical context; the refresh removes the tray's docked-page route in favor of Settings. No Phase 5.5 feature work is included in this report.

## Live checks

Steps: `X:\code\chippi-scratch\refresh\live-checks.md`, reviewed by Karl and Marge, assigned to Travis as task 123. Result: pending. Marge will add the observed outcomes and any follow-up fixes.

| Section | Result |
|---|---|
| 0. Setup | Pending |
| 1. Tray and Settings window | Pending |
| 2. Island | Pending |
| 3. Sound and System pages | Pending |
| 4. Clipboard and text pages | Pending |
| 5. Paste from Settings | Pending; 5.3 records observations only |
| 6. Chippi pages | Pending |
| 7. Afterwards | Pending |

## Installers

Source `7b4a8f1`, manifest commit `bc9527c`, merged in #107 as main `90f920b`. Release folder: `X:\code\chippi\build\artifacts\releases\0.1.0`. All 12 sizes and SHA-256 hashes match `phase-1-release-artifacts.csv`; the two platform CSV files are additional indexes. Kayla also checked both native RNNoise DLLs and the pinned VB-CABLE ZIP inside both packages.

| Platform | Setup bytes | Portable bytes | Full package bytes |
|---|---:|---:|---:|
| win-x64 | 82,197,182 | 74,682,088 | 74,683,070 |
| win-arm64 | 75,481,010 | 69,572,060 | 69,573,042 |

Packaging evidence: `X:\code\chippi-scratch\refresh\pack\pack.log`, `artifacts.csv`, `portable-startup.log`; independent review script `X:\code\chippi-scratch\refresh\pack-check\check.ps1`. Marge archived the preceding installers in `X:\code\chippi-archive\phase-5-installers-0.1.0-fa63a54` before copying and rechecking this build.

## Cleanup

Pending, following [end-of-phase-checklist.md](../end-of-phase-checklist.md). Marge owns integration and cleanup after the live check. Four known test profiles remain for cleanup: `chippi-pages-test`, `failed-shortcut-test`, `notice-filter-test` and `render-chippi`. Archive refresh scratch evidence and remove merged worktrees/branches only after verifying ownership and clean state. Preserve the retained boost spike and the installed app's data. No cleanup is claimed complete here.
