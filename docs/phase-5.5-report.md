# Chippi Phase 5.5 report: Everyday tools, continued

Phase 5.5 (Quick actions, Cleaning mode, AI agent usage, the Command bar and the Radial menu) is built, one pull request per feature, each built from its confirmed brief and approved mockup in [phase-5.5/](phase-5.5/README.md), in the order of [the handoff](phase-5.5-handoff.md). Each merged with Travis's OK once its CI passed. Phase 5.5 is PRs #178 to #186.

## What was built

| PR | What |
|---|---|
| #178 | All five briefs and approved mockups, shaped with Travis by asking rather than assuming |
| #179 | Quick actions: the Actions page (19 actions, a picker of which show), its confirms and shortcuts, Settings › Quick actions with Kept apps. The home goes to five across with one-word names |
| #180 | AI agent usage: Claude Code's and Codex's limits and sessions on the Agents page, "Waiting on you" and near-limit alerts on the bar, Switch to. Built in its own session alongside #179 |
| #181 | Cleaning mode: every screen black and the keyboard locked until Esc three times or Unlock; from Settings, a shortcut or the Actions page's Cleaning |
| #182 | Installers with Quick actions, AI agent usage and Cleaning mode: release artifact manifest |
| #183 | Command bar: Ctrl+Alt+Space or the home's Search for apps, windows, files, Windows Settings pages, anything Chippi does (with values: "keep awake 2 hr", "volume 40"), clipboard copies, sums, emoji and the web. Core gains Chippi's commands list, which the Radial menu reuses |
| #184 | Radial menu: hold the middle, Back or Forward button (or a shortcut) for a wheel of up to 8 slices at the pointer, with submenus 3 deep, 12 kinds of slice and Settings' wheel editor. The pet learns to look toward a point and to grin; the clipboard hub copies a selection and puts the clipboard back |
| #185 | End-of-phase cleanup, and installers with the Command bar and the Radial menu |
| #186 | From the live checks: Restart Explorer asks the shell to exit, and files rank after apps and windows in the Command bar; this report; installers with both fixes |

## How to build and run

Unchanged from the [Phase 1 report](phase-1-report.md#how-to-build-and-run). Additions, all in AGENTS.md:
- Nothing that acts on the PC (lock, sleep, quit apps, the radios, dark mode, window moves, media keys) runs in tests or dev runs: each is behind an interface and tested on fakes.
- Keyboard and mouse hooks (Cleaning mode, the Radial menu) and the Radial menu's selection copy are checked only in Windows Sandbox, by probes that drive the real hooks with `SendInput` beside a target window in a second process. The Radial menu's hooks pass on only input Chippi sends itself, so a probe's counts as a hand's.
- AI agent usage starts the owner's real `claude` and `codex` for its checks; install it in a dev profile only for a run that needs it.

## Test results

At `main` `f6b4372` (CI run 41485459601, push): Release win-x64 and win-arm64, 0 warnings and 0 errors each. Every test project except UiSmoke's desktop cases: 1,876 passed, 0 failed (Audio 119, Clipboard 55, Core 113, AI agent usage 101, Cleaning mode 22, Clipboard features 68, Command bar 40, Downloads 131, Keep awake 56, Plain paste 14, Quick actions 48, Radial menu 64, Scratchpad 27, Shelf 27, Sound 152, System monitor 141, Island 268, Notifications 26, Rules 122, Sound smoke 8, UiSmoke 2, URL cleaning 272). UiSmoke's 16 desktop cases skip, as always on CI. #186 adds one Command bar test.

## Done when (handoff §4)

| Check | Status | Evidence |
|---|---|---|
| Both runtimes build with no warnings; the tests pass | Done | See Test results |
| Each surface matches its mockup, dark and light, with shots beside it and timings | Done | Shots of every surface in each PR, rendered from the real controls. Timings: the Command bar's rows 0.17ms median per keystroke to find and 2ms to redraw; the Radial menu's wheel on screen about 5ms after the hold (#184) |
| Travis's live checks pass | Done, two fixes to re-test | See Live checks |

## Deviations from the mockups

- **AI agent usage:** "Waiting on you" and the near-limit alert keep the bar's pink Caution, not the mockup's yellow (Travis, 2026-11-01).
- **Radial menu:** a Command slice has a Shell row, PowerShell first, above Command (Travis, 2026-11-02).
- **Command bar:** over a fullscreen video in Chrome, opening the bar takes Chrome out of fullscreen, as Alt+Tab does. Travis accepted that over a bar that never takes the keyboard (live check, 2026-11-02); its brief and DESIGN.md say so.

## Known issues

- **Not run on real hardware:** ARM64 (both runtimes build, and CI runs the tests on x64 only), and the desktop UiSmoke suite.
- **The Radial menu over a game that hides the pointer** opens in the screen's middle and follows the mouse's movement; Travis's live check passed in a borderless game, but an exclusive-fullscreen game wasn't tried. Several monitors at mixed scales weren't tried either.
- **The Command bar's file results** come from Windows' own index, so only indexed places, and a tool's caches if Windows indexes them (#186 keeps them below apps and windows).

## Not fixing (from reviews)

None left open: Codex raised three findings on #179 and two each on #183 and #184, and all were resolved before those merged.

## Live checks

Steps: the "Live checks" section of each feature's PR (#179, #180, #181, #183, #184), walked through with Travis on 2026-11-02 on the installers from `f6b4372` (#185).

| Feature | Result | Found |
|---|---|---|
| Quick actions | 14 of 15 passed | Restart Explorer left a black screen for half a minute and a half-drawn taskbar: Windows' Restart Manager took 30 seconds to give up on the shell, then ended it, which Windows took for a crash. #186 asks the shell to exit as Task Manager does; in Windows Sandbox the shell and taskbar came back in 0.9s, three times, with no crash logged |
| AI agent usage | 5 of 5 passed | |
| Cleaning mode | 7 of 7 passed | |
| Command bar | 14 of 16 passed | Check 3: a fullscreen Chrome video leaves fullscreen as the bar opens (accepted; see Deviations). Check 5: typing "chrome" listed only `chrome…` files from a browser tool's caches. #186 ranks files, copies and emoji after apps, windows, Chippi and Settings |
| Radial menu | 15 of 15 passed | |

Re-test of Restart Explorer and the Command bar's "chrome" on the #186 installers, built from `ab328d1` (x64 Setup 82,866,840 bytes, ARM64 Setup 76,158,064): to do.

## Cleanup

Done after the last feature merged (#185, recorded in the [end-of-phase checklist](end-of-phase-checklist.md)). After this report merges, its worktree and branch go too.
