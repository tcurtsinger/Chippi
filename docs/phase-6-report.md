# Chippi Phase 6 report: Windows and input

Phase 6 (the shared input layer, Debounce, Focus follows mouse, Close protection, Smooth scrolling, Shortcuts, Text snippets, Snap, the App switcher and saved app arrangements) is built and has passed Travis's live checks. It was shaped with Travis on 2026-11-04 ([phase-6/](phase-6/README.md)), then built in three waves and a last step, each from a confirmed brief and, for every new surface, an approved mockup. Each PR merged once its CI passed and another agent had reviewed it. Phase 6 is PRs #220 to #274, 2026-11-04 to 2026-11-05.

## What was built

### Shaping

| PR | What |
|---|---|
| #220, #224 | The Phase 6 README: Travis's decisions on scope, games, the action model, Shortcuts and the eleven feature calls, then his answers to tasks 224 to 227 (the Actions regroup, what starts on, where Always on lives) |
| #221, #222, #223 | Behavior specs A and B, the architecture (one input hub, shared actions, the shortcut service, the pause) and every feature brief |
| #226, #228 | Mockups for Actions and Shortcuts (approved), wave 1's Settings rows and Close protection's bar line; Travis's wave 1 answers (real app icons in app lists, "Press again to close" for 6 seconds, a beep when the bar can't show) |

### Wave 1: the input layer, Debounce, Focus follows mouse, Close protection, Smooth scrolling

| PR | What |
|---|---|
| #225 | Actions regroup (option B): Lock, Turn off displays and Screen saver move into Power, End frozen apps into Quit apps |
| #227 | `Chippi.Input`: one keyboard hook and one mouse hook with a watchdog for every feature, filters instead of hooks, `ChippiInput.Marker` for Chippi's own input, and the Windows Sandbox runner (`tests/Chippi.Input.Tests/Sandbox/run.ps1`). The Radial gesture, the key recorder and Cleaning mode's lock moved onto it |
| #233 | The pause: Core's fullscreen check, Settings › General › Always on, Pause in these apps, and the Radial menu's own Open over fullscreen apps switch |
| #234 | The hub lets a filter give a held key back (a Debounce bounce on the press had kept the whole hold) |
| #231 | Debounce: click and key filters on the hub (clicks 25 ms by default, 5 to 100; keys 5 ms, 0 to 200, plus a window per key) |
| #229 | Focus follows mouse: Windows' own setting at 250 ms with the window raised, the original setting put back when it's off, paused or after a crash |
| #232 | Close protection: Alt+F4 twice, "Press again to close" on the bar; Ctrl+W per app |
| #235, #237, #239 | Actions takes other features' tiles, commands get on/off, the Input menu with Focus and Close protection switches and one icon per row |
| #236 | Smooth scrolling: smooth and linear wheel scrolling, Distance, Response and Coast, per-app speeds and Leave alone |
| #238 | One shortcut service (`Chippi.Core.Shortcuts`): every Chippi shortcut in one list, Chippi's own clashes named |
| #240 | Command bar: the native search timeout test made deterministic |
| #241 | Wave 1 installers, from `bf939a5` |
| #243 | Records the wave 1 pass |

### Wave 2: Shortcuts

| PR | What |
|---|---|
| #242 | The shared action model: every verb is one `ChippiAction` any door can fire (a tile, Search, a Radial slice, a key, a Caps Lock combo, a mouse button) |
| #246 | Every feature gets the shell's one action PC (`ModuleContext.Actions`, `WindowsActionPc`) |
| #245 (replaces #244), #248 | The Shortcuts engine, its Key and Mouse button doors, and refusing a shortcut that's in use when it's added |
| #249 | The Caps Lock door: Caps Lock combos |
| #250 | Bindings run on the shell's actions |
| #251 | Settings can change any of Chippi's shortcuts (`ShortcutEdits`: saved keys, Change, Remove) |
| #247, #253 (replaces #252) | The revised Settings › Shortcuts mockup, then the page |
| #254 | AGENTS.md: review and test at larger intervals (see How the work changed) |
| #255 | Wave 2 installers, from `82aff92` |
| #256 | Records the wave 2 pass |

### Wave 3: Text snippets, Snap, the App switcher

| PR | What |
|---|---|
| #257 | Mockups for Text snippets, Snap's zones and editor, and the App switcher (approved) |
| #258 | Text snippets: typed triggers expand as keystrokes, never pasted, so the clipboard is untouched; Settings › Clipboard & text › Text snippets |
| #259 | The App switcher: engine, focus hand-off, overlay with Windows' live previews, Settings page |
| #261 | Snap: the Snap menu in Actions, editable zones, Shift+drag placement |
| #260, #262 | Test fixes: the smoke log read with the writer's sharing; the Command bar's Forget save awaited before cleanup |
| #263, #266, #268 | Wave 3 installers, the last from `0145b6d` |
| #264, #265, #267 | Fixes from Travis's check: Add features keeps its scroll position; shortcuts are set only in Settings › Shortcuts; the App switcher brings the window to its display |
| #269 | Records the wave 3 pass |

### Saved app arrangements (Snap's last step)

| PR | What |
|---|---|
| #270 | The arrangements brief |
| #271 | The arrangements mockups (approved), with Travis's answers in the brief |
| #272 | Saved app arrangements in Snap: capture, the review and Edit in Settings › Snap › Arrangements, Actions › Snap › Arrangements, the run page and the bar's line. Shared changes: display IDs and names in shared code, the App switcher's window list shared, and `IActionPc` gains launching with arguments without elevation, an admin check for Start menu targets and placement by saved bounds |
| #273 | "1 needs attention", and one live-check file instead of two |
| #274 | Arrangements installers, from `b830c09` |

## How to build and run

Unchanged from the [Phase 1 report](phase-1-report.md#how-to-build-and-run). Additions, all in AGENTS.md:
- **Input:** no feature hooks the keyboard or mouse itself. It adds a filter to `ModuleContext.Input`, the shell's one input hub. Its tests feed the filter by hand or use the hub's fakes; real hooks are checked only in Windows Sandbox with `tests/Chippi.Input.Tests/Sandbox/run.ps1`, which refuses while a fullscreen app is in front or another Sandbox runs.
- **Actions:** no feature sends keys, moves windows or opens programs itself. It runs each `ChippiAction` over `ModuleContext.Actions`, and its tests pass a fake `IActionPc`.
- **Shortcuts:** no feature registers a hotkey itself. It adds each shortcut to `ModuleContext.Shortcuts`; Settings › Shortcuts is the only place one is recorded.
- **Foreground:** after a press the hub swallowed, a Chippi window earns the foreground with one marked tap of the unassigned key 0x07, then one retry, never a loop. To hand the keyboard on, activate the other window before hiding Chippi's.
- `dotnet publish` of the app needs `pwsh` for the soundboard tools; build self-contained for the Sandbox instead.
- New timing reports: `CHIPPI_INPUT_TIMINGS`, `CHIPPI_SWITCHER_TIMINGS`, `CHIPPI_SWITCHER_LIVE`, `CHIPPI_ARRANGEMENT_TIMINGS`.

## Test results

At `main` `b830c09`, the build Travis's last check ran on (CI run 42154070779, push): Release win-x64 and win-arm64, 0 warnings each. Every test project: 2,776 passed, 0 failed, 16 skipped, across 31 projects (Phase 5.5 ended at 1,876 across 22). UiSmoke's 16 desktop cases skip, as always on CI.

New in Phase 6: Input 55, App switcher 61, Close protection 24, Debounce 23, Focus follows mouse 18, Shortcuts 55, Smooth scrolling 20, Text snippets 56, Window layouts (Snap and arrangements) 64. The rest: Audio 151, Clipboard 55, Core 157, AI agent usage 121, Cleaning mode 24, Clipboard features 88, Command bar 122, Downloads 149, Keep awake 56, Plain paste 15, Quick actions 97, Radial menu 72, Scratchpad 48, Shelf 27, Sound 272, System monitor 141, Island 373, Notifications 26, Rules 122, Sound smoke 10, UiSmoke 2, URL cleaning 272.

`main` `16312c6` adds only the installer manifest (#274), which runs no CI.

## Timings

All from the PRs named, measured by their authors.

| What | Time | Where |
|---|---|---|
| The hub's mouse hook per event | median 0.9 µs, p99 12.1 µs over 91,285 events (target under 50 µs) | #227, `CHIPPI_INPUT_TIMINGS`, Release |
| Debounce's filters per event | median under 0.1 µs, 200,000 events each | #231 |
| A snippet typed out, last trigger key to finished text | 13 to 16 ms for 5 characters (the first 105 ms), 160 ms for 200 | #258, Sandbox |
| The App switcher's overlay taking the foreground | 3 to 36 ms after a shortcut, an island click or Search | #259, Sandbox |
| Snap's first placement change | 10 to 42 ms, sampled on DWM frames | #261, Sandbox |
| Settings › Shortcuts with 15 shortcuts, build and layout | p50 12 ms, p99 24 ms | #253 |
| A four-window arrangement, run to done, with app startup | 1.8 to 1.9 s, three runs | #272, Sandbox |
| Six Start menu admin checks before launching | 33 ms total (about 0.8 s each before the review fix) | #272 |
| Arrangements pages, warm | Settings 9 to 13 ms, the island list 3 to 4 ms, the run page 5 to 7 ms | #272 |

## Calls Travis made during the phase

Besides the decisions in [phase-6/README.md](phase-6/README.md):
- **Shortcuts are set only in Settings › Shortcuts** (wave 3 check, 2026-11-05, #265). A feature's own page shows its keys read-only with Edit, which opens the editor inside that shortcut's row in Settings › Shortcuts. Record and Change edit inside the row.
- **The App switcher brings the window to you** (task 430, #267). A window picked from another display moves to the display the switcher opened on, unlike Alt+Tab. Fullscreen and admin windows, and any that refuse the move, come to the front where they are.
- **New arrangement captures at once** (task 441). There's no separate Capture step; Recapture is on the review.
- **Guessed runs stay on the bar** (task 442). A run that's partly done, guessed a window or moved one to another display stays on the bar until dismissed.

## How the work changed

From #254 (Travis, 2026-11-05: "Going forward, the team should only be reviewing each others work at larger intervals. Same with running the tests."): one review per feature when it's ready, a second look only for a fix that changes behavior, CI as the test gate, and one Sandbox check per feature at the end. The arrangements step ran that way: one review of #272, one focused recheck of the two matching fixes, one final Sandbox run (30 of 30).

## Deviations from the mockups

- **Shortcuts on feature pages:** the approved wave 3 boards ([wave-3.html](phase-6/design/wave-3.html)) had a Shortcut row with Record on the feature's own page. Since #265 those pages show the keys read-only with Edit (see Calls).

## Known issues

- **Not run on real hardware:** ARM64 (both runtimes build; CI runs the tests on x64 only), and the desktop UiSmoke suite.
- **Admin windows:** the hooks can't act on a window running as admin. Over one, Caps Lock is plain Caps Lock and Snap can't move it; the App switcher brings it to the front where it is. Arrangements skip an app that needs admin with "Needs admin", with no Windows prompt, and capture leaves admin windows out.
- **Anti-cheat:** untested on Travis's games. With Always on off, no Phase 6 feature sends input into a fullscreen game. The Radial menu is the exception: its Open over fullscreen apps switch is on by default.
- **The App switcher:** a key pressed right behind its shortcut reaches the app that was in front; the overlay's retry recovers the foreground, and that one key is lost (#259).
- **Arrangements:** no atomic placement, exact stacking order or rollback; a launched window can show before it moves. Two windows of one app are matched by nearest bounds and marked Guessed.
- **Close protection** covers keys only.
- **Mixed-scale monitors and a removed display** can't be shown in the single-display Sandbox; they were covered only by Travis's wave 3 and arrangements checks.

## Not fixing (from reviews)

- **A failed settings write** closes the Text snippets editor and loses the draft (#258), and the App switcher's `Update` saves with no catch (#259). Both happen only when Chippi can't write its own settings file, and the failure is logged (Marge, 2026-11-05).
- **An action of an unknown kind** does nothing when run (#242, Marge).

## Deferred

- **Taking over Alt+Tab** as an option, once the App switcher has proven itself (task 211).
- **Mouse-button drag gestures** (task 211).
- **Text snippets:** a paste mode, and import and export (task 211).
- **The home's five- and six-across layout:** ships when Phase 7 takes the home past 16 tools.
- **Volume boost:** still deferred since Phase 3; the experiment stays on `spike/boost-loopback`.

## Live checks

Steps: a file per wave, given with each task, with how to make every test file, what to type and where to click.

| Check | Task | Build | Result | Found |
|---|---|---|---|---|
| Wave 1: the input layer, Always on, Debounce, Focus follows mouse, Close protection, Smooth scrolling, the Radial menu on the hub | 331 | `bf939a5` (#241) | Pass | The Radial menu's cold-start flick (issue #230) passed too |
| Wave 2: Shortcuts (keys, Caps Lock combos, mouse buttons, Settings › Shortcuts) | 374 | `82aff92` (#255) | Pass | |
| Wave 3: Text snippets, Snap, the App switcher | 422 | `0145b6d` (#268) | Pass | Record on feature pages didn't take a shortcut and opened the editor at the bottom; toggling a feature in Add features scrolled to the top; the switcher brought a window to the front on its own display, not the one he was on. Fixed in #265, #264 and #267 before the pass |
| Saved app arrangements (11 sections, about 40 minutes) | 458 | `b830c09` (#274; x64 Setup 147,420,611 bytes) | Pass | |

All four passed on 2026-11-05.

## Cleanup

Karl runs the [end-of-phase checklist](end-of-phase-checklist.md) after this report merges (task 460), and records it there. This report's worktree and branch go too.
