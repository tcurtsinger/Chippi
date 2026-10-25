# Chippi Phase 2 and 2.5 report

Phase 2 (the File organizer, first called the Downloads organizer) and Phase 2.5 (the native Windows 11 look) are built and on GitHub at `main` `398935f`. Travis passed Phase 2 on 2026-10-23 ("Pass for those three": undo, notifications and the island) and the last Phase 2.5 fix on 2026-10-24 ("Ok great. Pass"). Every merge went through a second member's review first. ARM64 is built and packaged but never run.

Briefs: [phase-2-handoff.md](phase-2-handoff.md) and [ui-refresh-handoff.md](ui-refresh-handoff.md). Phase 2 merged straight to `main` before the repo used pull requests (`bdc9fce` to `b3b672e`, 2026-10-22 and 23). Phase 2.5 is PRs #1 to #26.

## What was built

### Phase 2: File organizer

| Area | What's there | Where |
|---|---|---|
| Rules | Rules model, first-match planner, placeholders, rename templates, `.chippiignore`, misleading-name check, path validation | `src/Chippi.Rules` |
| Pipeline | Watcher, readiness (grace period, lock, partial and 0 KB checks, retry with backoff), executor that skips its own output, SQLite history (schema v1) behind `IHistoryStore`, SHA-256 verified undo, archive import (zip, tgz, tar.gz) with entry, size and ratio limits | `src/Features/Chippi.Features.Downloads/Engine`, `Import` |
| Views | Tidy flyout view, Preview and approve, rules editor, History, organizer settings page; the app-wide Notifications page | `Views`, `src/Chippi.App/Settings` |
| Notices | Windows notifications through the classic COM activator (click routing, update-safe registration, removed on uninstall) and island notices, chosen per event kind | `src/Chippi.App/Notifications` (`86c21ba`, `669c058`) |
| Explorer | Per-user classic menu entry "Organize", removed on module and app uninstall, plus a startup sweep for stale keys | `b033613`, `49d2887` |
| Tests | Desktop UI smoke tests, run only with `CHIPPI_UI_SMOKE=1` | `tests/Chippi.UiSmoke` (`d4cbb95`) |

### Phase 2.5: native look and fixes

| PR | What |
|---|---|
| #1 | Plan: UI/UX audit, rewritten DESIGN.md, the brief with the R0 font and accent spike |
| #2 | R1 foundation: neutral tokens, accent setting (Follow Windows, Blue, Violet, Green, Pink) read live from Windows, Segoe UI Variable with tabular numbers, restyled controls, Manrope and JetBrains Mono removed |
| #3, #5 | Flaky Downloads pipeline and notice tests fixed |
| #4 | R2 Settings: data-driven sidebar with search, Add features replaces the Features hub |
| #6 | R3 flyout: `IModuleUi.AddFlyoutPanel`, icon tab strip, Keep awake panel with duration chips |
| #7 | R4 Downloads views on the new cards, Tidy panel |
| #8, #10 | R6 cleanup and UiSmoke rewrite, island smoke tests |
| #9 | R5 movable island: a tab dragged to the top, left or right edge, opened on hover |
| #11, #12, #13 | Fixes from the mockup comparison, DESIGN.md synced to the approved mockup |
| #14 | The open island grows out of the tab as one shape (Travis's change, 2026-10-23) |
| #15 | UiSmoke waits for the flyout content |
| #17 | Folder picker for Move destinations, Move placeholders dropped, renamed "File organizer" |
| #19 | Settings opens the section its caller asks for (cause of the ".exe not organized" report) |
| #21 | Rules save with a Save button; saving or switching a rule on sorts the files already in the folder |
| #23 | Switches no longer flicker when the page redraws |
| #25 | Terse copy: bare-verb buttons, question-only confirms, every History run collapsible, error reasons as fragments |
| #16, #18, #20, #22, #24, #26 | Installer manifests; Kayla hash-checked each set (12/12) |

## How to build and run

Unchanged from the [Phase 1 report](phase-1-report.md#how-to-build-and-run). Additions:
- Debug `--settings-snapshot` also renders the File organizer's rules, history and review views.
- History is in `%LOCALAPPDATA%\Chippi\data\downloads-organizer\history.db`. A dev profile's notification registration is removed with `--cleanup-dev-notifications`.

## Screenshots

The post-fix mockup comparisons are `X:\code\chippi-scratch\phase-2.5\fix-settings\index.html` and `fix-flyout-island\index.html` (their before set is `final-snap`), with later sets beside them (`switch-flicker`, `rules-save`, `terse-copy\shots\after`). They go into the end-of-phase archive. `docs/phase-1/screens` shows the old look.

## Test results

At `main` `398935f`:
- Release win-x64 and win-arm64 with `--no-incremental`: 0 warnings, 0 errors each.
- Every test project except UiSmoke: 425 passed, 0 failed (Core 97, Downloads 115, Rules 117, Island 42, Keep awake 29, Notifications 25). Phase 2 ended at 348 (`e8018d4`).
- UiSmoke: 15/15 passed at `5e5d56c` (Marge, 2026-10-24). Not run since.
- Tests run on x64 only. CI builds both platforms and tests x64.

## Acceptance checklist

### Phase 2 (brief §7)

| Check | Status | Evidence |
|---|---|---|
| 0 warnings both platforms; tests cover the behavior spec | Done | 310 tests at `a971c72`, 348 at `e8018d4`. `PipelineTests`, `ReviewFixTests`, `ArchiveImportTests`, `PlannerTests`, `ValidationAndSafetyTests` |
| Manual: preview, approve moves, undo restores with matching hash | Done | Travis ran it on his install (2026-10-22); the re-offer bug he found was fixed in `e8018d4`. Hash matching is checked in tests, not live |
| Silent: grace period; skips partial, locked, 0 KB, cloud placeholders; never moves files already there | Done in tests, changed later | Pipeline tests. #21 changed the last part at Travis's request: saving a rule or switching it on now sorts files already in the folder |
| Held files amber with reason, never moved silently | Done in tests | `Misleading_names_are_held_even_in_silent`, `The_ignore_file_keeps_files_in_place`; amber dot reviewed in `32c0f94`. Not run live |
| Notices reach the places set; Tidy lists every event | Partly | Travis passed notifications (2026-10-23). Windows notifications shown on dev profiles and in Windows Sandbox. No recorded check that Tidy lists every event |
| Explorer "Organize" only while installed and on; opens preview; removed on uninstall | Done on dev builds | Karl ran both Explorer menus on a dev build; `ExplorerLifetimeTests`. Not run from an installed build |
| Archive import previews within limits, moves nothing until approved | Done in tests | `ArchiveImportTests`. Not run live |
| Installer installs, updates in place, uninstalls; `e_sqlite3.dll` loads in packaged x64 | Partly | Travis updated in place several times. History works on his installed x64 build, so SQLite loads; no explicit check. No uninstall in Phase 2 |
| New labels follow the Bare Label Rule | Done | Reviews per PR; #25 swept the whole app |

### Phase 2.5 (brief §7)

| Check | Status | Evidence |
|---|---|---|
| 0 warnings both platforms | Done | See Test results |
| Mockup screens match in dark and light, Follow Windows and Violet; Windows accent change recolors live | Partly | Post-fix comparisons `fix-settings/index.html` and `fix-flyout-island/index.html` (#11, #12), spec synced in #13. Marge's live run at `5e5d56c`: the flyout's selected tab went from gray to blue without a restart. Island recoloring wasn't observed |
| Keep awake for an hour is one click; Organize now visible without scrolling | Done by review | Six duration chips (#6); Organize now beside the folder (#12). Not measured on a small display |
| Island drags to top, left, right, snaps, opens on hover; reopens there after restart; never covers caption buttons | Partly | 18/18 live checks passed across sessions 1 and 3 (Top hover and stay from session 1, the rest at `414d221`). Reopening after a restart and the caption-button rule weren't recorded as checks |
| Installed features in the sidebar; Add features lists only working features | Done | #4 review; UiSmoke `AddFeaturesExposesAvailableSwitchesAndComingLaterNames` passed |
| No Manrope or JetBrains Mono; no gradient outside Gumdrop | Done | No font files or keys in `src`. The only `LinearGradientBrush` is `PetSynthBrush` in `Chippi.UI/Themes/Palette.xaml`, used only for Gumdrop |
| No description under page titles; Bare Label Rule | Done | #4 removed the page description style; #25 swept captions and errors |
| Installer installs, updates in place, uninstalls | Partly | Travis updated his install in place on 2026-10-24 and passed the latest build (#26) the same day. Not every package PR was installed. No uninstall in Phase 2.5 |

## Deviations from DESIGN.md

None. Each design change this phase went into DESIGN.md in the same commit or PR (#13, #14, #17, #21, #25). The mockup's open-island boards no longer apply; DESIGN.md › Island is the spec.

## Known issues

- **Unsaved rule draft lost on quit.** The leave prompt covers Settings navigation and closing the Settings window, but Quit from the tray or flyout exits without asking.
- **Logs left after uninstall.** `%LOCALAPPDATA%\Chippi\logs` stays, as in Phase 1.
- **History likely left after uninstall.** `history.db` is in `%LOCALAPPDATA%\Chippi\data`, outside the install folder (`ChippiApp`), like the logs. Not checked, since no uninstall was run.
- **Old failure reasons stay long.** History rows written before #25 keep the full-sentence reasons; only new rows use the short fragments.
- **UiSmoke not run since #25.** #25 changed `TidyTests` (the removed empty-state caption is now expected absent). Its last run was 15/15 at `5e5d56c`.
- **Flyout-wait flake.** The #15 fix (re-query the window on each poll, wait on the footer Settings button) was followed by a 15/15 run, but the cause was inferred, so one clean run doesn't prove it's gone.
- **Not run:** any ARM64 execution, an uninstall of a Phase 2 or 2.5 build, Explorer "Organize" and archive import on an installed build, and a live hash check on undo.

## Suggestions for Phase 3 (Sound)

1. **Island position.** Travis's backlog item in PLAN.md is done as the movable island (top, left or right; the bottom edge and corners are left out on purpose). Phase 3's media activity and equalizer should be built into the grown island shape, and the Phase 1 equalizer cost note still applies.
2. **UI/UX rework.** Travis deferred it on 2026-10-24: "We can rework some UI/UX in the later phases." Collect his notes before Phase 3's screens are designed, so Sound is built on the reworked patterns.
3. **Copy rules first.** Write Phase 3's strings to DESIGN.md §3 from the start: bare verbs, question-only confirms, fragments for errors.
4. **Rule drafts and quit.** If Phase 3 adds editors with drafts, give Quit the same leave prompt, and fix the File organizer at the same time.
5. **Schedule UiSmoke** after every change to a smoke-tested view, and once more before the Phase 3 merge.
