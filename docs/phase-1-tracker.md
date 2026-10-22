# Phase 1 tracker

Owner of this file: Marge (PM). Brief: [phase-1-handoff.md](phase-1-handoff.md). DESIGN.md wins over taste.

## Process
- Each workstream works on its own branch (`ws0-foundation`, `ws1-tray-flyout`, ...) in its own worktree and touches only the folders it owns.
- Before merge: a second member reviews against DESIGN.md and the brief, `dotnet build -c Release` is clean (zero warnings) for win-x64 and win-arm64, and `dotnet test` passes.
- After merge: Marge pushes `main` to origin (Travis approved push-after-merge on 2026-10-21).
- Core contract changes after WS0 lands go through Kayla.
- The .NET SDK is at `C:\Program Files\dotnet`; prepend it to PATH in each shell.

## Workstreams

| # | Workstream | Owner | Reviewer | Depends on | Status |
|---|---|---|---|---|---|
| WS0 | Foundation: solution, Core, config, logging, single instance, themes, fonts, base controls | Kayla | Karl | none | merged be308b1, pushed |
| WS1 | Tray icon and flyout | Tray agent (subagent, X:\code\chippi-ws1) | Kayla | WS0 | merged (head 9216b08); Keep awake tile verified in integrated flyout |
| WS2 | Settings window, Features hub, Permissions | Kayla (X:\code\chippi-ws2) | Karl | WS0 | merged and pushed (head 4a835fb) |
| WS3 | Island and Gumdrop | Island agent (subagent, X:\code\chippi-ws3) | Karl | WS0 | merged and pushed (head 68cd2f8); island verified live: toolwindow, noactivate, topmost, centered |
| WS4 | Keep awake module | Karl (X:\code\chippi-ws4), first | Kayla | WS0 | merged 522476c (head 98dfb6f); power state 0x3 on, 0x0 off (non-admin) |
| WS5 | Build, CI, Velopack, licensing | Karl (X:\code\chippi-ws5), after WS4 | Kayla | WS0 | merged 53306bc; uninstall hook + own Main in c325359; repack after integration |
| Lead | Integration (App.xaml*, composition root), DESIGN.md review, final report | Marge | Kayla | all | todo |

Specs per workstream: [phase-1-workstreams.md](phase-1-workstreams.md). Lead seam on main: 4a1bdf3 (ShellContext/ShellCommands/slots/Gumdrop stub/CsWin32), 8f23b12 (CHIPPI_DEV_PROFILE). Seam review: Kayla, before push.

## Decisions log
- 2026-10-21: origin = https://github.com/tcurtsinger/Chippi.git. Push main after each reviewed merge (Travis).
- 2026-10-21: DESIGN.md updated (Travis approved): light badge text Source #7B2DB8 / Overlap #8A4B00, light active segment text #1A2036, new lowest island kind "Status" used by Keep awake.
- 2026-10-21: Kayla's gap fills (Coming soon card, tool tile off, 36px segments outside flyout, 10px icon chip radius, primary/switch hover, slider hover 15 to 18px) and preset membership stay as drafts; Travis approves from screenshots at the WS2 review.
- 2026-10-21: Themes and controls move to a WPF class library `src/Chippi.UI` (Marge) so feature modules can use the custom controls. WS0 owns it.
- 2026-10-21: JetBrains Mono upstream static files declare weights 436/472; styles request 472 to hit the SemiBold file. Visual weight is 600 as designed.
- 2026-10-21: pushed main to 2bf875b (seam + fixes, WS4, WS5, WS1), all reviewed. WS3 held until its fullscreen fix.

- 2026-10-21: WS4 adds `tests/Chippi.Features.KeepAwake.Tests`; Marge adds it to Chippi.slnx in the WS4 merge.
- 2026-10-21: WS5 approved: repo-local vpk 1.2.161 (rollForward, host has only .NET 10), central Velopack 1.2.161, LICENSE/notices copied next to the exe, `VelopackApp.Build().Run()` in OnStartup after EnsureWindowsDirectoryEnvironment. Lead-owned edits land as one separate commit on ws5-build-ci.

## Follow-ups
- Kayla: `SingleInstance` calls `AllowSetForegroundWindow` before signalling the owner; then WS1 drops its cross-process focus workaround.
- Travis, at hub review: tool tiles are about 111px wide in three columns, so names over about 10 characters are cut ("A long samp..."). Wrap to two lines or keep ellipsis?
- Done: Keep awake recovers after a native failure (ed95ba9). Optional: one retry in ToggleAsync if a timed release fails at the same moment as a toggle (Kayla).
- WS3 must marshal hub events with BeginInvoke, never Invoke (Keep awake's ReleaseOnExit blocks the UI thread on exit).
- Kayla: `ConfigStore.SectionChanged(name)` event; then WS3 drops its settings.json file watcher.
- Phase 3, not now: `IslandActivity` elapsed/duration so the expanded media row can show mono times (DESIGN §5). No real media in Phase 1.
- Lead at integration: measure CPU of the 30 fps equalizer on the transparent island window; Narrator pass on flyout, settings, island.
- Travis, at hub review: WS3 additions where DESIGN.md is silent: status rows in the expanded island (needed for the new Status kind), "Nothing live right now" empty line, wide awake overriding the header pet while Keep awake is on.
- WS3 untested: 150% and 200% scaling, real exclusive-fullscreen game and PowerPoint, F11; system-wide "busy" may hide the island on the other monitor.
- WS1 untested: 150% and 200% scaling, taskbar on other edges, real Explorer restart, Narrator. Lead covers these at integration.

- Travis, DESIGN.md wording: the island anchors 8px below the monitor's work-area top, not the display's top edge. Identical with a bottom taskbar; with a top taskbar or appbar it avoids covering it (Karl's P2, lead kept it).

- Kayla: Settings › Island & pet preview sits on the island color, so the 112px ground shadow (black 35%) is invisible. DESIGN §7 requires it; put the preview on Panel or Surface.

## Escalations open
- Final app icon: ship the generated Gumdrop .ico, Travis approves at the end.
- `powercfg /requests` needs admin. Travis to choose: allow an elevated run, run it himself, or leave the WS4 evidence gap open until sign-off.

## Risks
- Shell_NotifyIconGetRect needs the icon's hWnd and uID, which WinForms NotifyIcon (Hopper's tray) hides. Likely fix: own the icon via Shell_NotifyIcon through CsWin32 in WS1.
- Velopack `vpk` on .NET 10 and ARM64 is unproven here. If it blocks, stop and escalate (brief §9).

