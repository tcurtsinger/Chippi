# Phase 1 workstreams WS1 to WS5

Owner: Marge (lead). Read first, in order: `docs/phase-1-handoff.md` (the brief), `DESIGN.md` (wins over taste), `src/Chippi.Core/README.md` (the Core and Chippi.UI API), `src/Chippi.App/App.xaml.cs` and `src/Chippi.App/Shell/*.cs` (the shell seam). Hopper (`X:\code\hopper`) is read-only reference.

## Rules for every workstream

**Where you work**
- Work only in your worktree (`X:\code\chippi-wsN`) on your branch. All five branch from `main` at `8f23b12`.
- Touch only the folders you own, plus your marked `[WSn]` slot in `App.xaml.cs` and the `NativeMethods.txt` in your own folder (CsWin32 is already referenced by Chippi.App and KeepAwake; list your APIs there).
- If you need a new package, a csproj change, or a file outside your folders, ask Marge. If you need a Core contract change, ask Kayla. Don't edit them yourself.

**Shared building blocks**
- **Shell seam:**
  - Take `ShellContext` in your constructor (Config, LoggerFactory, Island hub, Registry, Commands, DebugMode).
  - Cross-surface actions go through `ShellCommands`: `RegisterFlyout` / `ShowFlyout` / `ToggleFlyout`, `RegisterSettings` / `OpenSettings(page)`, and `Quit`.
  - Never reference another workstream's window type.
- **Pet:**
  - Place Gumdrop via `Chippi.App.Pet.Gumdrop` (`Mood`, `Size`, `PetColor`).
  - Until WS3 merges it's a gradient-circle stub with the same API.
  - Where the text beside it carries the state, set `AutomationProperties.AccessibilityView="Raw"`.
- **Windows and resources:**
  - Every window uses `ChippiWindowStyle`.
  - Theme keys resolve with `DynamicResource`, so the theme can switch live.
  - Use the Chippi.UI controls and styles. Don't restyle them locally; if one is wrong, report it to Kayla.

**Quality bar**
- Every icon-only button has `AutomationProperties.Name`.
- Every interactive element shows the 2px focus ring.
- Tab order is logical, and Esc does the obvious thing.
- No stock WPF chrome, no emoji, no gradient text, no network, no telemetry.
- No code, strings or assets from vorssaint-utils or Mouzi.
- SPDX header on every source file.

**Running the app**
- Set `CHIPPI_DEV_PROFILE=wsN` before launching. That gives your run its own instance, its own `%APPDATA%\Chippi-dev\wsN\settings.json` and its own logs, so parallel streams don't collide.
- `--debug` (or `CHIPPI_DEBUG=1`) turns on debug surfaces.
- Close every Chippi you start. Leave no HKCU Run entry and no execution state behind.

**Done means**
- `dotnet build src\Chippi.App\Chippi.App.csproj -c Release -r win-x64` and the same with `-r win-arm64` finish with 0 warnings. (Building the .slnx with `-r` is not supported.)
- `dotnet test tests\Chippi.Core.Tests -c Release` passes.
- Small commits on your branch. Don't merge, push or rebase onto other workstream branches.

**Report back with**
- the commit hash
- both build outputs and the test count
- screenshots saved to `X:\code\chippi\screens\wsN-*.png` (dark and light where it applies)
- every place DESIGN.md was silent and what you chose ("gaps")
- known issues

## WS1: Tray icon and flyout (`src/Chippi.App/Tray/**`, `src/Chippi.App/Flyout/**`)
Brief §4.4 and DESIGN.md §5 "Tray flyout", §6, §7 Sizes.
- **Tray icon:**
  - Own it with `Shell_NotifyIcon` via CsWin32 on a hidden message window, not WinForms `NotifyIcon`. You need the hWnd and uID for `Shell_NotifyIconGetRect`.
  - Re-add the icon on `TaskbarCreated` (Explorer restart).
  - Tooltip "Chippi".
- **Icon file:**
  - Generate a multi-resolution `.ico` (16, 20, 24, 32, 40, 48, 64, 256) from the DESIGN.md §7 geometry: small form at 24px and below, full detail above.
  - Commit the generator in `Tray/` (a small C# or Python script) and the generated `Tray/Assets/chippi.ico`. The lead sets `ApplicationIcon` at integration.
- **Clicks:**
  - Left-click toggles the flyout.
  - Right-click opens a minimal themed menu (Open settings, Quit) with no stock chrome.
  - Call `shell.Commands.RegisterFlyout(toggle, show)`. A second launch already calls `ShowFlyout`.
- **Flyout window:**
  - Borderless, topmost, 380 wide, up to 612 tall, 16px radius, window lift, 14px padding, 12px gap.
  - Anchored from `Shell_NotifyIconGetRect`: 12px above the taskbar, right edge 16px from the screen edge. Handle the taskbar on any edge and the icon in the overflow area.
  - Must be correct per-monitor at 100%, 150% and 200%.
  - Closes on Esc, on deactivation and on a second icon click. Guard against the deactivate-then-click race that reopens it.
- **Header:** 34px chip square with Gumdrop 28px Idle, "Chippi" Headline, a Caption status line (for example "1 feature on"), and a settings `IconButton` 32×30.
- **Tabs:**
  - Segmented trough per §5.
  - Show only the tabs an installed module contributes to (`FeatureEntry.FlyoutTabs`), with Tools also shown when any module contributes a tool tile.
  - With `DebugMode`, also show the placeholder tabs with an empty state.
  - Re-render on `Registry.Changed`, which fires on any thread, so marshal to the UI thread.
- **Tools tab:** a 3-column grid of `ToolTile` bound to each installed entry's `ToolTiles` (`ToolTileModel`). Toggling calls `ToggleAsync`.
- **Footer:** Settings (opens settings) and Quit (`Commands.Quit`).
- **Wiring:** fill your `[WS1]` slot and return true.
- **Debug testing:** to see a tile before WS4 lands, register a Debug-only fake module from inside your folder, and only when `DebugMode` is on.

## WS2: Settings window, Features hub, Permissions (`src/Chippi.App/Settings/**`)
Brief §4.5 and DESIGN.md §5 "Settings window", §6.
- **Window:**
  - 1160×720 default, resizable, 13px radius, window lift, `WindowChrome` with a custom 44px titlebar.
  - Titlebar: Gumdrop at 22px (small form), "Chippi Settings" 13/700, and window buttons 32×30 with 7px radius. Close hovers Signal Red.
  - Snap and maximize must still work.
  - Single window: reopening activates it.
- **Sidebar:** 220px, with General, Features, Island & pet, Shortcuts, Permissions, Appearance and About. Items are 36px with a 16px icon, 13/600 text and the segmented-active style. The version and license sit in mono at the bottom.
- **General:**
  - Theme (Dark, Light, Follow Windows), applied live via `ThemeManager` and saved.
  - Run at startup via `StartupRegistration`. Test, then restore.
  - Export and import settings via `ConfigStore.Export/Import` with themed file dialogs. Catch `InvalidDataException` and `IOException`, and show an inline message rather than a stock dialog. Call `Registry.ReloadAsync()` after an import.
- **Features hub:**
  - Display title, Body description and the mono "N of M installed" count.
  - Preset segment (Essentials, Audio, Power user, Everything, Custom), a 36px search box, then groups as Label eyebrows over 3-column `FeatureCard` grids with 12px gaps.
  - Live install switches call `SetInstalledAsync`.
  - Search filters by name and description.
  - The preset follows the registry.
- **Placeholders:**
  - Register "Coming soon" entries for every PLAN.md feature, Phases 2 to 7, in `Settings/Features/FeatureCatalog.cs`.
  - Each gets name, one-line description, group, badges (Source "From Hopper" for Hopper features, Overlap for the Windows/PowerToys overlaps PLAN.md implies, Needs where obvious), icon and draft preset membership.
  - Call it from your `[WS2]` placeholder slot.
  - Travis approves the membership table at review, so list it in your report.
- **Island & pet:**
  - Island on/off, pet color (Synth plus the 5 solids), and a 112px Gumdrop preview cycling the four moods with an accessible name such as "Gumdrop pet, celebrating".
  - Reduce motion: Follow Windows / On / Off, mapped to `AppSettings.ReduceMotion` and `Motion.Override`.
  - Save to `AppSettings`. WS3 reads it.
- **Permissions:** each capability from `Registry.GetPermissions()`, the modules using it, and an "Unused" flag with a dismiss action.
- **Shortcuts, Appearance and About:**
  - Shortcuts is an honest empty state.
  - Appearance can hold the theme if General gets crowded. Your call; note it.
  - About shows version, license (GPL-3.0-or-later) and third-party notices, read from `THIRD-PARTY-NOTICES.md` copied to output. Show a short fallback if it's missing; WS5 writes it.
- **Module pages:** a feature's settings page comes from `FeatureEntry.SettingsPage` and is reachable from its card (for example by clicking the card).
- **Wiring:** `shell.Commands.RegisterSettings(page => ...)` in your slot.

## WS3: Island and Gumdrop (`src/Chippi.App/Island/**`, `src/Chippi.App/Pet/**`)
Brief §4.6 and DESIGN.md §5 "Island", §7 in full.
- **Gumdrop:**
  - Replace the stub's body and keep the API.
  - Exact path data from §7: body, shine, eyes, mouth and ground shadow (64px and up), all four moods, and the small form at 22px and below.
  - Synth gradient at 135° or the solid `PetColor`, with eyes and mouth always pet ink.
  - Motion:
    - Blink every 4 to 7s at random, eyes scaling to 10% height for 120ms. This is the only loop.
    - Mood cross-fade over 150ms.
    - Celebrate squish (1.06, 0.94) from (60,108) over about 400ms, with sparks fading over 1.2s.
    - Falling asleep: eyes ease shut over 600ms, then the z's fade in.
  - Reduced motion (`Motion.IsReduced` and `Motion.Changed`) means no blink, no squish and instant swaps.
  - Celebrating auto-returns to the previous mood after about 2.5s.
- **Island window:**
  - Borderless, `AllowsTransparency`, topmost.
  - `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`; it never activates, even on click (handle `WM_MOUSEACTIVATE`).
  - Not in Alt+Tab or the taskbar.
  - Top-center of the monitor holding the foreground window, 8px below the top edge, following the foreground monitor (WinEvent hook on `EVENT_SYSTEM_FOREGROUND`, not polling).
  - Hides while `SHQueryUserNotificationState` reports busy, D3D fullscreen or presentation, or the foreground window covers its monitor (exclude the desktop and shell windows).
  - Per-monitor DPI correct.
- **States per §5:**
  - Resting 84×34 with Sleepy.
  - Live up to 292×38 with Idle, title, tertiary source and a 5-bar gradient equalizer. Under reduced motion the equalizer is static.
  - Expanded 328 wide with 24px radius, on hover after about 400ms or on click. It collapses on mouse leave.
  - Expanded shows the sections that have data. With fake data, show them all.
  - Driven by `IslandActivityHub.Current` and `Changed` (any thread, so marshal).
  - Mood wiring: resting means Sleepy, live means Idle, `Succeeded` means Celebrating, and Keep awake's activity (`Status` kind, module id `keep-awake`) means WideAwake.
- **Settings:** respect `AppSettings.IslandEnabled` and `PetColor` live (re-read on `ConfigStore` change or `Imported`). Ask Kayla if you need a config-changed event.
- **Debug menu:** with `DebugMode`, right-clicking the island opens a themed menu that injects fake Media, Timer, Stash, Agent and Status activities plus "Celebrate", and a Clear all. Use your own publisher via `CreatePublisher("debug")`.
- **Wiring:** fill your `[WS3]` slot.

## WS4: Keep awake (`src/Features/Chippi.Features.KeepAwake/**`)
Brief §4.7.
- **Module:**
  - `KeepAwakeModule : IFeatureModule`.
  - Descriptor: id `keep-awake`, "Keep awake", group System, Overlap badge "Overlaps PowerToys", icon from `LucideIcons`, draft presets Essentials and Power user.
- **Holding the PC awake:**
  - `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED [| ES_DISPLAY_REQUIRED])`.
  - The flag is per-thread, so hold it on one dedicated long-lived thread and release it on that same thread.
- **Modes and settings:**
  - Modes: indefinitely, for a duration, or until a time. Option: keep the display on.
  - Settings persist via `context.Settings`, including the last mode and duration. Never auto-resume across restarts unless the user turned it on and it was indefinite. Your call; document it.
- **Surfaces:**
  - A Tools tile via `context.Ui.AddToolTile`, with a state line such as "Until 6:00 PM", "For 1h 30m" or "Indefinitely" (mono where it's a number).
  - A settings page (`SetSettingsPage`) built only from Chippi.UI controls.
- **Island and pet:**
  - While active, publish an island activity of kind `Status`, titled "Keep awake" with the source line "Until 6:00 PM".
  - When a timed session ends on its own, call `ReportSuccess` so the pet celebrates.
- **Release** on stop, on uninstall (the registry calls `StopAsync`), on app exit and on failure.
- **Tests:** unit tests for the timing and state logic, with the Win32 call behind an interface. These need a new test project or folder. Ask Marge before adding a project, because the solution file is the lead's.
- **Verify:** `powercfg /requests` shows Chippi while on and nothing after stop. Paste both outputs.
- **Wiring:** register the module in your `[WS4]` slot.

## WS5: Build, CI, packaging, licensing (`build/**`, `.github/**`, `LICENSE`, `THIRD-PARTY-NOTICES.md`, `src/Chippi.App/Properties/PublishProfiles/**`)
Brief §4.8 and §3.
- **Publish:**
  - `dotnet publish` profiles for win-x64 and win-arm64: self-contained, single-file where practical. WPF single-file needs native libs extracted or included, so test that the published exe actually starts.
  - If a profile needs csproj properties, ask Marge.
- **CI:** a GitHub Actions workflow that builds both RIDs and runs tests on x64. Pin the SDK from `global.json`. No secrets, no publishing, no release step.
- **Velopack:**
  - Use the `vpk` CLI as a repo-local dotnet tool (`.config/dotnet-tools.json`; ask Marge, since it's at the repo root).
  - Add Velopack startup hooks only in a proposal to Marge (it touches `App.xaml.cs`).
  - `build/pack.ps1` produces Setup.exe and a portable zip for both RIDs. Leave the update feed unset. No signing.
  - If Velopack blocks you, stop and report. Don't switch tools.
- **Licensing:**
  - `LICENSE`: the full GPL-3.0 text.
  - `THIRD-PARTY-NOTICES.md`: Manrope (OFL, aaronbell/manrope@6f81ebe), JetBrains Mono (OFL, JetBrains/JetBrainsMono@1937130), Lucide (ISC), Hopper (relicensed GPL by its author), and every NuGet package that ships in the output with its license.
  - The About page reads this file, so ask Marge to have it copied to output.
- **Report:** the artifact list with sizes, whether each exe launches, and any blocker.
