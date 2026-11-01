# Chippi

A Windows 11 tray app for the owner's own PC (C#, .NET 10, WPF): a widget on the desktop (a puck with the pet) that opens a panel with a page per feature (Sound, Clipboard, Scratchpad, Shelf, System, Keep awake, File organizer and more), and a Settings window. GitHub: `tcurtsinger/Chippi`.

- **Design:** `DESIGN.md` wins over taste. Mockups are under `docs/**/design/`.
- **Plans:** `PLAN.md` has the phases. Each phase's brief is `docs/phase-N-handoff.md`, with its `behavior-spec.md`, `architecture.md` and report beside it.
- **Hopper** (`X:\code\hopper`) is reference only. Never change it.

## Build

- The .NET SDK (10.0.403, from `global.json`) is at `C:\Program Files\dotnet`, which isn't on PATH. Prepend it in each shell.
- Build like CI, once per runtime: `dotnet build src/Chippi.App/Chippi.App.csproj -c Release -r win-x64 --no-incremental`, then `win-arm64`. The SDK rejects `-r` on `Chippi.slnx`.
- Warnings are errors.
- From PowerShell 5.1, run dotnet through `cmd /c`, so its stderr isn't turned into errors.

## Test

- CI runs every project under `tests/` in Release on x64, in its own job beside the two builds. It builds `Chippi.slnx` once and runs the tests without building again, so a new test project must be in `Chippi.slnx`. Locally, run the projects your change touches, and let CI run the rest. A test file other projects link (`tests/Chippi.Island.Tests/WpfTestThread.cs` is in six feature test projects) needs `dotnet build Chippi.slnx -c Release` before you push. Through `cmd /c`, a `|` in `--filter` is a pipe, not "or": run one filter at a time.
- CI runs once per pull request and once on `main` after a merge, not on branch pushes. A change that only touches Markdown, `docs/` or `phase-1-release-artifacts.csv` runs no CI.
- Run in Release too when a test touches Debug-only members.
- `Chippi.UiSmoke` drives real windows and keys on the desktop. It's skipped unless `CHIPPI_UI_SMOKE=1`; only enable it when the owner has set a time (`tests/Chippi.UiSmoke/README.md`).
- Tests that show windows (`Chippi.Island.Tests` and the like), dev runs and renders may run while a game or Zoom is in front (the owner, 2026-10-29). While the owner is doing live checks, leave those tests to CI.
- The CI runner's GPU has no engine counters, so tests that read hardware must accept null readings.
- Feature test projects build controls without the app's themes, so a control there has default templates: test sizes, alignment and fit in `Chippi.SoundSmoke` or `Chippi.Island.Tests`, which load them, or in the shots.
- Make a flaky test deterministic with injected timers and clocks, not longer waits: the panel's morph and the controls' fill fades step each frame from a clock a test can hold (`IslandPanelWindow.Clock`, `FillFade.Clock`). Local runs share the owner's real mouse, so tests that press the widget pin its pointer (`IslandWindow.PointerAt`), and the island tests' hooks ignore the real keyboard and mouse (`IslandWindow.WatchesInput`). Wait for the panel's open, page or close with `IslandWindowTests.Settle`, not a fixed pump.

## Run it

- **Never against the real profile.** `CHIPPI_DEV_PROFILE=<name>` (or `--dev-profile <name>`) gives a run its own instance, settings (`%APPDATA%\Chippi-dev\<name>`) and logs (`%LOCALAPPDATA%\Chippi-dev\<name>\logs`). A dev profile is single-instance.
- **Never install Sound modules in a dev profile:** routing claims the owner's default devices.
- **Snapshots without showing anything:** a Debug build run with `--debug` saves PNGs to a folder with `--island-pages <dir>`, `--island-shots <dir>`, `--settings-snapshot <dir>` or `--flyout-snapshot <dir>` (the tray menu).
  `--island-pages` and `--island-shots` show the widget and the panel on screen; both shoot dark, then light. A click elsewhere closes the panel mid-run, and the widget hides while a fullscreen app is in front on its display, so check the shots. `--island-shots` also drops the widget in each corner and past every edge and checks where it lands (`r6-drop-check.txt`).
- **Shots in tests:** the offscreen page shots run when `CHIPPI_ISLAND_SHOTS` names an output folder. They render dark; `CHIPPI_SHOTS_THEME=light` renders them light. The island follows the app theme.

## Done means used, with proof

- Run it with a dev profile and use what changed the way the owner will, with real-size data: hundreds of clipboard items, a dozen audio devices, several drives.
- Hand back proof:
  - **Screenshots:** before and after, next to the mockup when there is one.
  - **Timings:** for anything that should feel fast (opening a page, switching pages, dragging a slider). A page opens already filled, and a switch doesn't stutter. Count skipped frames only inside the animation: WPF's `CompositionTarget.Rendering` slows when nothing changes on screen, and it misreads a window that only moves, so time a drag by where the window is on each DWM frame (`DwmFlush`, then `GetWindowRect`), with moves posted once a frame like a mouse.
  - **What you couldn't run,** said plainly.
- Passing tests and a clean build are needed, but they aren't done.
- When driving it with UI Automation, toggles act on Checked and Unchecked: a UIA Toggle raises no Click.

## The owner's PC

- **Real input:** never click or type on the desktop while a fullscreen app or Zoom is in front; `build/fullscreen-check.ps1` exits 1 then. Never send keys blind: confirm what's in front first.
- **Clipboard:** never read, write or log the owner's real clipboard. Clipboard and input checks run only in Windows Sandbox (a `.wsb` with clipboard redirection and networking off).
- **Ask first** before installing anything or changing the audio setup.
- **No sound** without warning.
- **No CPU stress tests:** one froze the PC.
- **Logs:** tests never write the real `chippi.log`. `tests/Directory.Build.targets` gives them their own (`SmokeLog.cs`).
- **Explorer menu:** tests never change the owner's Organize entry. Only Chippi.exe changes the real menu (`ExplorerRegistration.CanChange`), so a test-hosted `DownloadsModule` has no registration; a test of the verb injects one under its own key.

## How work goes

- **One branch and worktree per change**, in `%LOCALAPPDATA%\chippi-work\<name>`, kept only while its PR is open: once it merges, remove the worktree and its local branch, and `chippi-work` itself when nothing else is open. The main checkout, `X:\code\chippi`, is the source of truth and the only Chippi folder that lasts (the owner, 2026-10-31); never add one to `X:\code`. Work in a worktree, never in its files, but keep its `main` current (see Merging).
- **Review:** another agent reviews each PR. Call out new projects in `Chippi.slnx`, and changes to shared code (Core, App, the island, Chippi.UI), in the PR.
- **Merging:** a PR merges only when every CI job is green, with a merge commit. A PR that runs no CI merges once it's reviewed. Don't wait on `main`'s run after a merge: check it's green before you open the next PR.
- **After a merge,** bring `X:\code\chippi` up to date in the same turn: `git fetch`, then `git merge --ff-only origin/main` (the owner, 2026-10-31). Check `git status` first and never discard his changes: if a tracked file is modified, or the merge would overwrite an untracked file that isn't empty or the same as `main`'s, stop and ask him.
- **Scratch:** shots, probes and review notes go in your session's own temp folder, never the repo, `X:\code` or a folder of their own, and are deleted once the PR merges. Shots the owner should see are sent to them; nothing is archived (the owner, 2026-10-31). At the end of a phase, follow `docs/end-of-phase-checklist.md`.
- **Installers:** made with `build/pack.ps1` (`build/README.md`) in a worktree. Every file is hashed against `phase-1-release-artifacts.csv`. Refresh them when the owner is about to install or at the end of a phase, not after every merge. Before removing the pack worktree, copy its set over `X:\code\chippi\build\artifacts\releases\<version>\` (Git ignores it) and check each file against `artifacts.csv`: only the newest set is kept.
- **Live checks for the owner:** each step says how to make every test file, what to type and where to click.
- **Copy:** bare-verb buttons. A confirm is a short question with Cancel and a verb. Errors read "… failed".

## Keep this file current

When a step here changes, or you learn one, change this file in the same pull request.
