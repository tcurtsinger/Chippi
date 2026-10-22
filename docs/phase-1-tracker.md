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
| WS0 | Foundation: solution, Core, config, logging, single instance, themes, fonts, base controls | Kayla | Karl | none | in progress |
| WS1 | Tray icon and flyout | TBD after WS0 | TBD | WS0 | todo |
| WS2 | Settings window, Features hub, Permissions | TBD after WS0 | TBD | WS0 | todo |
| WS3 | Island and Gumdrop | TBD after WS0 | TBD | WS0 | todo |
| WS4 | Keep awake module | TBD after WS0 | TBD | WS0 | todo |
| WS5 | Build, CI, Velopack, licensing | TBD after WS0 | TBD | WS0 | todo |
| Lead | Integration (App.xaml*, composition root), DESIGN.md review, final report | Marge | Kayla | all | todo |

## Decisions log
- 2026-10-21: origin = https://github.com/tcurtsinger/Chippi.git. Push main after each reviewed merge (Travis).
- 2026-10-21: DESIGN.md updated (Travis approved): light badge text Source #7B2DB8 / Overlap #8A4B00, light active segment text #1A2036, new lowest island kind "Status" used by Keep awake.
- 2026-10-21: Kayla's gap fills (Coming soon card, tool tile off, 36px segments outside flyout, 10px icon chip radius, primary/switch hover, slider hover 15→18px) and preset membership stay as drafts; Travis approves from screenshots at the WS2 review.
- 2026-10-21: Themes and controls move to a WPF class library `src/Chippi.UI` (Marge) so feature modules can use the custom controls. WS0 owns it.
- 2026-10-21: JetBrains Mono upstream static files declare weights 436/472; styles request 472 to hit the SemiBold file. Visual weight is 600 as designed.

## Escalations open
- Final app icon: ship the generated Gumdrop .ico, Travis approves at the end.

## Risks
- Shell_NotifyIconGetRect needs the icon's hWnd and uID, which WinForms NotifyIcon (Hopper's tray) hides. Likely fix: own the icon via Shell_NotifyIcon through CsWin32 in WS1.
- Velopack `vpk` on .NET 10 and ARM64 is unproven here. If it blocks, stop and escalate (brief §9).
