# Chippi

A Windows 11 tray app for the owner's own PC (C#, .NET 10, WPF): an island at the top of the screen with a page per feature (Sound, Clipboard, Scratchpad, Shelf, System monitor, Keep awake, File organizer and more), and a Settings window. GitHub: `tcurtsinger/Chippi`.

- **Design:** `DESIGN.md` wins over taste. Mockups are under `docs/**/design/`.
- **Plans:** `PLAN.md` has the phases. Each phase's brief is `docs/phase-N-handoff.md`, with its `behavior-spec.md`, `architecture.md` and report beside it.
- **Hopper** (`X:\code\hopper`) is reference only. Never change it.

## Build

- The .NET SDK (10.0.403, from `global.json`) is at `C:\Program Files\dotnet`, which isn't on PATH. Prepend it in each shell.
- Build like CI, once per runtime: `dotnet build src/Chippi.App/Chippi.App.csproj -c Release -r win-x64 --no-incremental`, then `win-arm64`. The SDK rejects `-r` on `Chippi.slnx`.
- Warnings are errors.
- From PowerShell 5.1, run dotnet through `cmd /c`, so its stderr isn't turned into errors.

## Test

- CI runs every project under `tests/` in Release on x64. Locally, run the projects your change touches, and let CI run the rest.
- Run in Release too when a test touches Debug-only members.
- `Chippi.UiSmoke` drives real windows and keys on the desktop. It's skipped unless `CHIPPI_UI_SMOKE=1`; only enable it when the owner has set a time (`tests/Chippi.UiSmoke/README.md`).
- The owner permits tests and renders while busy, including with Zoom or a fullscreen game in front (2026-10-29). `build/fullscreen-check.ps1` remains a diagnostic; its exit 1 does not block these authorized runs. Target any input explicitly at the test app; never send keys blind.
- The CI runner's GPU has no engine counters, so tests that read hardware must accept null readings.
- Make a flaky test deterministic with injected timers and clocks, not longer waits.

## Run it

- **Never against the real profile.** `CHIPPI_DEV_PROFILE=<name>` (or `--dev-profile <name>`) gives a run its own instance, settings (`%APPDATA%\Chippi-dev\<name>`) and logs (`%LOCALAPPDATA%\Chippi-dev\<name>\logs`). A dev profile is single-instance.
- **Never install Sound modules in a dev profile:** routing claims the owner's default devices.
- **Snapshots without showing anything:** a Debug build run with `--debug` saves PNGs to a folder with `--island-pages <dir>`, `--island-shots <dir>`, `--settings-snapshot <dir>` or `--flyout-snapshot <dir>` (the tray menu).
  `--island-pages` and `--island-shots` show the widget and the panel on screen, so check what's in front first; both shoot dark, then light. `--island-shots` also drops the widget in each corner and past every edge and checks where it lands (`r6-drop-check.txt`).
- **Shots in tests:** the offscreen page shots run when `CHIPPI_ISLAND_SHOTS` names an output folder. They render dark; `CHIPPI_SHOTS_THEME=light` renders them light. The island follows the app theme.

## Done means used, with proof

- Run it with a dev profile and use what changed the way the owner will, with real-size data: hundreds of clipboard items, a dozen audio devices, several drives.
- Hand back proof:
  - **Screenshots:** before and after, next to the mockup when there is one.
  - **Timings:** for anything that should feel fast (opening a page, switching pages, dragging a slider). A page opens already filled, and a switch doesn't stutter.
  - **What you couldn't run,** said plainly.
- Passing tests and a clean build are needed, but they aren't done.
- When driving it with UI Automation, toggles act on Checked and Unchecked: a UIA Toggle raises no Click.

## The owner's PC

- **Background checks:** the owner permits tests and renders with games or Zoom in front. Never send keys blind: confirm the target app before any input.
- **Clipboard:** never read, write or log the owner's real clipboard. Clipboard and input checks run only in Windows Sandbox (a `.wsb` with clipboard redirection and networking off).
- **Ask first** before installing anything or changing the audio setup.
- **No sound** without warning.
- **No CPU stress tests:** one froze the PC.
- **Logs:** tests never write the real `chippi.log`. `tests/Directory.Build.targets` gives them their own (`SmokeLog.cs`).

## How work goes

- **One branch and worktree per change** (`X:\code\chippi-<name>`). The main checkout, `X:\code\chippi`, is the owner's.
- **Review:** another agent reviews each PR. Call out new projects in `Chippi.slnx`, and changes to shared code (Core, App, the island, Chippi.UI), in the PR.
- **Merging:** a PR merges only when every CI job is green, with a merge commit. Then watch `main`.
- **Scratch:** shots, probes and review notes go in `X:\code\chippi-scratch\<work>\`, never the repo. At the end of a phase, follow `docs/end-of-phase-checklist.md`.
- **Installers:** made with `build/pack.ps1` (`build/README.md`). Every file is hashed against `phase-1-release-artifacts.csv`.
- **Live checks for the owner:** each step says how to make every test file, what to type and where to click.
- **Copy:** bare-verb buttons. A confirm is a short question with Cancel and a verb. Errors read "… failed".

## Keep this file current

When a step here changes, or you learn one, change this file in the same pull request.
