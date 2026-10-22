# Phase 1 tracker

Owner of this file: Marge (PM). Brief: [phase-1-handoff.md](phase-1-handoff.md). DESIGN.md wins over taste.

## Process
- Each workstream works on its own branch (`ws0-foundation`, `ws1-tray-flyout`, ...) in its own worktree and touches only the folders it owns.
- Before merge: a second member reviews against DESIGN.md and the brief, `dotnet build -c Release` is clean (zero warnings) for win-x64 and win-arm64, and `dotnet test` passes.
- After merge: Marge pushes `main` to origin (Travis approved push-after-merge on 2026-10-21).
- Core contract changes after WS0 lands go through Kayla.
- The .NET SDK is at `C:\Program Files\dotnet`; prepend it to PATH in each shell.
- One lead thread, no side jobs: members work in the room, background agents report only to Marge (after the 2026-10-21 hold).

## Workstreams

| # | Workstream | Owner | Reviewer | Depends on | Status |
|---|---|---|---|---|---|
| WS0 | Foundation: solution, Core, config, logging, single instance, themes, fonts, base controls | Kayla | Karl | none | merged and pushed (be308b1) |
| WS1 | Tray icon and flyout | Tray agent (subagent, X:\code\chippi-ws1) | Kayla | WS0 | merged and pushed (head 9216b08); Keep awake tile verified in integrated flyout |
| WS2 | Settings window, Features hub, Permissions | Kayla (X:\code\chippi-ws2) | Karl | WS0 | merged and pushed (head 4a835fb) |
| WS3 | Island and Gumdrop | Island agent (subagent, X:\code\chippi-ws3) | Karl | WS0 | merged and pushed (head 68cd2f8); island verified live: toolwindow, noactivate, topmost, centered |
| WS4 | Keep awake module | Karl | Kayla | WS0 | merged and pushed (98dfb6f, recovery ed95ba9); power state 0x3 on, 0x0 off (non-admin) |
| WS5 | Build, CI, Velopack, licensing | Karl | Kayla | WS0 | merged and pushed (53306bc; own Main and uninstall hook c325359); repack after integration |
| Lead | Integration (App.xaml*, composition root), DESIGN.md review, final report | Marge | Kayla | all | in progress |

Specs per workstream: [phase-1-workstreams.md](phase-1-workstreams.md).

## Decisions log
- 2026-10-21: origin = https://github.com/tcurtsinger/Chippi.git. Push main after each reviewed merge (Travis).
- 2026-10-21: DESIGN.md updated (Travis approved): light badge text Source #7B2DB8 / Overlap #8A4B00, light active segment text #1A2036, new lowest island kind "Status" used by Keep awake.
- 2026-10-21: Themes and controls move to a WPF class library `src/Chippi.UI` (Marge) so feature modules can use the custom controls. WS0 owns it.
- 2026-10-21: JetBrains Mono upstream static files declare weights 436/472; styles request 472 to hit the SemiBold file. Visual weight is 600 as designed.
- 2026-10-21: WS5 approved: repo-local vpk 1.2.161, central Velopack 1.2.161, LICENSE/notices copied next to the exe. Velopack runs in our own `Program.Main` before WPF, after the %windir% fix.
- 2026-10-21: the island anchors to the work-area top (Karl's P2, lead kept it; Travis approved in the design review).
- 2026-10-21: Travis approved design review items 1-7 (folded into DESIGN.md §5/§6, presets in PLAN.md), the generated Gumdrop .ico as the app icon, and the non-admin Keep awake proof (0x3 on, 0x0 off) in place of powercfg.

## Follow-ups
- Done (558121f, merged): ConfigStore.SectionChanged, SingleInstance foreground handoff, preview ground shadow, tool tile tooltip.
- Done (f7bc49d, merged): island listens to SectionChanged; flyout focus workaround removed; real second-launch focus verified by Karl.
- Optional: log a warning when SetForegroundWindow is refused (FlyoutWindow, TrayMenuWindow), for future focus reports.
- Optional: one retry in Keep awake's ToggleAsync if a timed release fails at the same moment as a toggle.
- Karl: repack both RIDs from integrated main; confirm both portable builds and x64 Setup launch.
- Measured 2026-10-21 (x64 Release): resting island 0.5% of one core; live media with the 30 fps equalizer 4.7% of one core, GPU about 0.15%. Fine for Phase 1 (fake media only). Phase 3: drive the equalizer from real peak levels at 20 fps or less and stop it during silence.
- Verified 2026-10-21: island centered and scaled on the secondary at 100%, 150% and 175% (its max); scaling restored to 100%. Flyout UIA tree: every control named, pet hidden, Keep awake exposes toggle + state; Quit works via UIA.
- Fixed (425e2a0, merged): UI Automation toggles now drive state for the Keep awake tile, Island switch and Run at startup; verified by Kayla and Karl via UIA (power 0x3 on, 0x0 off).
- Lead after the fix: UIA pass on the settings window; flyout at 150%/200% needs the primary display (ask Travis); 200% impossible on the 1080p secondary.
- Phase 3, not now: `IslandActivity` elapsed/duration so the expanded media row can show mono times (DESIGN §5).
- Untested so far: taskbar on other edges, real Explorer restart, exclusive-fullscreen game, PowerPoint, F11, ARM64 execution (no ARM64 hardware).

## Escalations open
- None.
