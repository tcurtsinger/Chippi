# Chippi Phase 5 report: Clipboard and text

Phase 5 (Clipboard history, Clean URL, Paste as plain text, Scratchpad and Shelf) is built and on GitHub at `main` `028a58a`. Every merge went through a second member's review first and merged only after both CI runs passed. The features live in three island tiles (Clipboard, Scratchpad, Shelf) and the docked pages, as Travis decided: Clean URL sits behind the Clipboard header's link button, and plain paste is a shortcut only. Recording is off until turned on, and nothing listens to the clipboard unless History is recording or automatic cleaning is on. The installers were built from `ce2afc4` (`pack/phase-5`, PR #80), rebuilt from `9b72093` (`pack/phase-5b`, PR #84) with two fixes found while they waited: Mic processing reading "No driver" with VB-CABLE installed (#82), and test runs writing to the installed app's log (#83). Travis's live checks on that build passed (task 34, 2026-10-28). The four things they turned up are fixed (#86 to #88) or recorded as a limit (#89), and the final installers were built from `f342b70` (`pack/phase-5c`, PR #90). Travis's spot-check of the final build (task 57) is pending. ARM64 is built and packaged but never run.

Brief: [phase-5-handoff.md](phase-5-handoff.md), with [phase-5/architecture.md](phase-5/architecture.md), [phase-5/behavior-spec.md](phase-5/behavior-spec.md), [phase-5/scope.md](phase-5/scope.md) and the approved design in [phase-5/design/](phase-5/design/notes.md). Phase 5 is PRs #69 to #90.

## What was built

| PR | What |
|---|---|
| #69 | Docs: approved design notes and scope into `docs/phase-5/`, DESIGN.md › Clipboard and text |
| #70 | Brief |
| #71 | C0 `Chippi.Clipboard`: one listener (`AddClipboardFormatListener`) that runs only while a lease holds it, the privacy gate (`ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory` read as a DWORD and failing closed, `Clipboard Viewer Ignore`, apps to skip), format reader and writer, paste sender (no keystrokes to elevated or unreadable targets: the plain text becomes the copy), and `SealedDatabase` (SQLite, each entry sealed with AES-GCM, one key wrapped with DPAPI; erase checkpoints, vacuums, deletes and makes a new key). `IClipboardHub` in Core and `ModuleContext.Clipboard`; `FakeClipboard` and `FakeForeground` for tests. Catalog glyphs; Quick panel removed. Real Windows behavior checked in Windows Sandbox only |
| #72 | C1 Clean URL rules: pure engine, global and site rules (proper subdomains only), every rule and the five examples in behavior-spec §5 on fake data. No network access |
| #73 | C2 Scratchpad: store, autosave and flush, tabs, rename, Markdown preview (notes #32 subset, rendered by Chippi), Export with the modal-dialog guard, Load and Save failures. Approved shell hooks: `IslandTileSpec.CreateHeader`, `IslandTileSpec.InitialFocus`, `ModuleContext.HoldIslandOpen` |
| #75 | C3 Clipboard tile and page: History (search, pin, paste, copy, delete, clear, erase, pause, limits, expiry) and the Clean URL view, first use, Settings › Clipboard history and Clean URL (automatic cleaning, sites to skip), the uninstall question. Approved shell hooks: `IslandTileSpec.HandleEscape` (before the panel and the global Esc closer), paste target captured before the island takes the keyboard (the taskbar counts as none), `ModuleContext.IslandPasteTarget` and `CloseIsland`, `IFeatureModule.UninstallQuestion` with the Add features popover. Review fixes: the Clean URL add row is reused, the thumbnail cache checks bytes, expired entries drop out on read |
| #74 | C4 Paste as plain text: Shortcut row (none by default), paste handoff, restore that never overwrites a newer copy, the beep and the six-second tab glyph (accent, or Caution when nothing could paste) |
| #77 | C5 Shelf: separate sealed store (128 MiB cap, a full Shelf refuses and never evicts), files by reference, text, links and pictures (refused over 16.6 MP or 16 MiB of PNG; over 100 files refused), page with drop in, drag out (always Copy; pinned items stay) and keyboard, docked. Approved shell hooks: `IslandTileSpec.OpensOnDrag` and the drag over the closed tab (Inbox glyph, opens after 250 ms without the keyboard, closes 500 ms after the drag leaves tab and panel, the tab never takes the drop) |
| #78 | Global shortcuts need Ctrl or Alt (plain paste and mic mute) |
| #79 | Drag-tab timers injected so tests fire them by hand (see Flaky tests); stale `clean-url` and `scratchpad` catalog placeholders removed |
| #76 | C6 smoke tests on fakes, the real modules in the island shell: the Clipboard page (header, search and Clean URL) and the Scratchpad and Shelf pages fit 1280×720 on the top edge, a side edge and docked; Scratchpad keeps pasted tabs and leaves Esc to the shell; a Shelf drop routes through the panel and releases the hold, drag-out payloads keep the item and its files, and the drag over the tab opens after 250 ms and closes after 500 ms on injected timers; plain paste's shortcut drives the tab glyph and creates no tile. A UiSmoke Scratchpad keyboard case (not run) |
| #80 | Phase 5 installers: release artifact manifest |
| #81 | This report |
| #82 | Mic processing showed "Stopped · No driver" with VB-CABLE installed. Pack 45 puts CABLE Input and CABLE In 16ch on one topology filter (pins `00010001` and `00010002`), so both matched as the input and `VbCable.Find` returned Ambiguous, which showed as No driver. `AudioEndpoint.Filter` is now the connector ID and only pin `00010001` is CABLE Input; Ambiguous has its own status, "More than one cable". Confirmed with a read-only probe on Travis's PC (`chippi-scratch\cable-probe`) |
| #83 | Test runs wrote to the installed app's log (`%LOCALAPPDATA%\Chippi\logs`): SoundSmoke's fake Install Driver click and a Notifications callback test. Every Windows test project now logs to its own dev profile and fails if its log points at the real one |
| #84 | Phase 5 installers rebuilt from `9b72093`: release artifact manifest |
| #85 | Clipboard hub: a listener read between an own write and its bookkeeping recorded Chippi's own copy (see Flaky tests). In the final installers (#90) |
| #86 | From live check 1.2 (task 48, Travis: yes): a Clipboard row for one copied image file (png, jpg, jpeg, gif, bmp, webp) shows the picture, scaled to 320 wide and read off the UI thread. Online-only files are never opened (Offline and both recall attributes are checked first), and files over 16 MiB or 16.6 MP keep the file icon |
| #87 | From live check 2.2: the Clean URL field stays on one line at full width, and the result shows only the clean link (the "Removed" line is gone; DESIGN.md and notes #23 and #26 updated) |
| #88 | From live check 4.5: Scratchpad's ⋯ and note-tab menus were unstyled Windows menus, and Export showed a replacement character. They now use the island menu, Export… shows its dots, and the delete question's quotes are fixed. The same audit moved Clipboard history's Add app picker to the Settings menu style |
| #89 | From live check 5.8 (task 50, Travis: leave it): the docked Shelf only drags items out. It closes on a click elsewhere like every docked page, so a drop from another app can't reach it. DESIGN.md, notes #50 and architecture.md record the limit |
| #90 | Final Phase 5 installers from `f342b70`: release artifact manifest |

## How to build and run

Unchanged from the [Phase 1 report](phase-1-report.md#how-to-build-and-run). Additions:
- Never read, write or log Travis's real clipboard. Tests use the fakes in `tests/Chippi.Clipboard.Tests/Fakes`; real clipboard and input spikes run in Windows Sandbox only (`chippi-scratch\phase-5\sandbox`, `c0-check`).
- A standard user can't create a window station, so the real clipboard can't be isolated on Travis's PC.
- The clipboard features need no admin and install nothing. They are safe in a dev profile, as long as recording stays off there.

## Test results

At `main` `f342b70`, the final installers' source (CI run 40784871601, push; `028a58a` after it changes only the manifest):
- Release win-x64 and win-arm64 with `--no-incremental`: 0 warnings, 0 errors each.
- Every test project except UiSmoke: 1,247 passed, 0 failed (Audio 114, Clipboard 50, Clean URL rules 272, Clipboard features 54, Core 111, Downloads 118, Keep awake 32, Plain paste 14, Scratchpad 25, Shelf 17, Sound 71, System monitor 80, Island 139, Notifications 26, Rules 117, Sound smoke 7). Phase 4 ended at 757; Core, Keep awake and Sound grew with #66. #83 adds one log check to each Windows test project.
- UiSmoke: its 2 windowless log checks pass; its 16 desktop cases skip, as always on CI. The desktop suite wasn't run in Phase 5.
- Earlier installers: `ce2afc4` (CI run 40761850949) passed 1,216 and `9b72093` (CI run 40770613072) passed 1,235, both with 0 warnings. Since then #85, #86 and #88 added 12 tests.

## Acceptance checklist (brief §6)

| Check | Status | Evidence |
|---|---|---|
| 0 warnings on both platforms; tests pass on x64; behavior-spec §10 checks within scope covered on fakes; deferred features stay out | Done | See Test results. Privacy: `HubTests` (`A_private_copy_is_never_read`, `A_private_copy_that_lands_after_the_early_check_is_still_never_read`, `The_history_marker_is_read_as_a_dword_and_fails_closed`, `An_allowed_history_marker_never_overrides_an_exclusion`, `An_app_to_skip_is_not_read_for_that_lease_only`, `A_private_copy_pastes_and_stays_private_throughout`). Clean URL: `Chippi.UrlCleaning.Tests` (272). Quick panel is out of the catalog; the Phase 5.5 features stay there as coming-soon entries that can't be installed (`FeatureCatalog.cs`, `CatalogTests`) |
| Tiles, pages, docked versions and Settings match the approved notes and DESIGN.md §3 | Done, live pending | Offscreen renders of the Clipboard page and Shelf (`chippi-scratch\phase-5\c3\render`, `c5\render`). Every new string checked by the reviewer and by Marge's Decide pass. C6 smoke fits the Clipboard, Scratchpad and Shelf pages at 1280×720 on the top edge, a side edge and docked; Settings and the confirmations are checked live only. Live: sections 1 to 6 passed (task 34); the four fixes from them (#86 to #89) are in the final build. Spot-check of the final build (task 57): _pending_ |
| Nothing listens to the clipboard unless History is recording (not paused) or automatic cleaning is on | Done | `HubTests.Nothing_listens_without_a_lease_and_the_last_lease_stops_it`, `HubTests.On_demand_reads_never_start_the_listener`, `ClipboardHistoryTests.Nothing_listens_or_is_created_until_recording_is_on_and_not_paused`, `AutoCleanerTests.Off_by_default_and_listens_only_while_on` |
| Installer installs, updates in place and uninstalls; Travis's live checks pass, using test text he copies himself | Done, spot-check pending | Live checks on `pack/phase-5b` (`9a478d6`, PR #84, built from `9b72093`): sections 0, 7 and 8 passed (task 34). Final build `pack/phase-5c` (`fa63a54`, PR #90, built from `f342b70`): 12/12 hashes checked by Kayla and Marge; both rnnoise.dll files and the VB-CABLE zip match their pins in both packages. Earlier builds are archived in `chippi-archive\phase-5-installers-0.1.0-ef35929` and `phase-5-installers-0.1.0-9a478d6`. Spot-check of the final build (task 57): _pending_ |

## Deviations from DESIGN.md

None recorded in reviews.

## Known issues

- **Clipboard behavior is tested on fakes.** The real clipboard, paste into elevated targets and the privacy markers were checked in Windows Sandbox (C0), not on Travis's PC. Live sections 1 to 3 were the first real run, and passed.
- **Plain paste into an admin window beeps and leaves plain text as the copy.** That's the design (notes #19): Chippi never sends keystrokes to an elevated window.
- **The docked Shelf only drags items out.** A docked page closes on a click elsewhere, so a drag from another app can't reach it. Drops go through the island tab. Travis's call (task 50); recorded in DESIGN.md by #89.
- **Not run on real hardware:** ARM64 and the desktop UiSmoke suite (including the new Scratchpad keyboard case).

## Flaky tests

Phase 3 (#50, the mic mute test racing the latch's own retry) and Phase 4 (#61, the audio engine shutdown race) both showed the same pattern: a test that depended on timing passed locally and failed on CI's x64 runner, which is slower and busier than Travis's PC. In Phase 5, `DragTabTests` waited on real 250 ms and 500 ms `DispatcherTimer`s and failed the same way, on `main` `81945ca` (after #77) and on #79's first push run.

#79 removed the wait instead of lengthening it: `IslandWindow` makes its drag timers through an injectable `CreateTimer` (`IslandTimer.cs`), and the tests fire them by hand, with no pumping or sleeping. A test checks that both timers come from the factory with the approved delays, so a timer that bypasses it fails 4 of the 6 drag tests. Lesson kept for later phases: tests drive timers through a seam rather than waiting on wall time.

A fake clock alone wasn't enough for `HubTests.The_temporary_copy_and_the_restore_are_not_recorded`, which failed on #84's x64 PR run. One clock advance fired the listener's settle read (on the thread pool) and the paste's restore together, and a read landing after the restore closed but before the hub noted it as its own recorded the restored copy. The gap was in the product too, for every write Chippi makes. #85 counts each own write until it's noted, and the listener waits while one is in flight. A test puts the read in that window on purpose (3 of 3 failures before, passes after), and the original test failed on run 6 of a 2,000-run loop before and passed all 2,000 after. Second lesson: when a fake clock fires two things at once, a test of their order needs an explicit hook, not luck.

## Not fixing (from reviews)

- C3: automatic Clean URL writes the clean link unconditionally (`AutoCleaner.RewriteAsync`). A copy made between the original copy and that write is replaced by the cleaned link. The hub has no conditional write (Marge's call).
- C4: `IslandWindow` names `PlainPasteModule.Id` to pick out plain paste feedback from the other activities, instead of a generic flag on the activity.
- C4: while the plain paste glyph shows, Gumdrop wakes on the tab for its six seconds.
- C4: "Shortcut unavailable" stays as the error for a shortcut without Ctrl or Alt, matching mic mute.
- C6: the Clipboard, Scratchpad and Shelf smoke tests reach private members by reflection.
- Fixed rather than kept: the stale catalog placeholders (#77, #79).

## Live checks

Steps: `chippi-scratch\phase-5\live-checks.md` (task 34), reviewed by Karl and Marge. Installer `X:\code\chippi\build\artifacts\releases\0.1.0\win-x64\ChippiApp-win-x64-Setup.exe` (82,161,514 bytes), built from `9b72093`. Travis's result, 2026-10-28: Pass.

Step fixes during the run, after Travis's questions:
- 0.3 and 1.2 now spell out making `pic.png` and typing each test line before copying it.
- Every Shelf drop goes through the island tab, since the island closes when Explorer is clicked.
- 5.8 checks drag out only (task 50, #89).

| Section | Result | Found |
|---|---|---|
| 0. Setup | Pass | |
| 1. Clipboard history | Pass | A copied image file showed only a file icon: #86 |
| 2. Clean URL | Pass | The link wrapped to a second line, and the removed list wasn't wanted: #87 |
| 3. Paste as plain text | Pass | |
| 4. Scratchpad | Pass | Classic Windows menu with a broken glyph: #88 |
| 5. Shelf | Pass | The docked Shelf can't take drops: #89 |
| 6. Keyboard | Pass | |
| 7. Install | Pass | |
| 8. Afterwards | Pass | |

Final build (`pack/phase-5c`, 82,165,247 bytes, built from `f342b70`): Travis's spot-check of the thumbnail, the Clean URL field and the menus (task 57) is _pending_.

## Cleanup

After this report merges: remove the `chippi-report` worktree and the merged Phase 5 branches on GitHub (keep `main` and `spike/boost-loopback`), and archive `chippi-scratch\phase-5` and `chippi-scratch\cable-probe` to `chippi-archive`, following the [end-of-phase checklist](end-of-phase-checklist.md).
