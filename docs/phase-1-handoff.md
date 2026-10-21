# Chippi Phase 1 handoff: the app shell

You are the build team for **Chippi**, a Windows 11 tray app that bundles everyday utilities behind one tray icon. It also has a floating "island" pill at the top of the screen and a small pet called Gumdrop. Phase 1 builds the **shell**: everything that individual features plug into. It also builds one small real feature, Keep awake, to prove the plumbing works end to end.

The repo is `X:\code\chippi`. It holds `PLAN.md`, `DESIGN.md` and this brief, but no code yet.

## 1. Read first

| Source | What it gives you |
|---|---|
| `PLAN.md` | Product decisions, architecture outline, roadmap. Phase 1 is your scope. |
| `DESIGN.md` | **The source of truth for every pixel**: colors (dark and light), type, radii, every component, the three surfaces, and Gumdrop's exact geometry and moods. When the brief and DESIGN.md disagree, DESIGN.md wins. |
| `X:\code\hopper` | The user's existing .NET 10 WPF tray app, which Chippi absorbs in Phase 3. Reuse its patterns and code now (list below). Don't modify the Hopper repo. |
| [Design canvas](https://claude.ai/artifact/CMBT4oFV3oGZRUei1d7K17) | Clickable HTML mockups of direction A ("Studio Rack"). The link is private to the user, so ask them to share it if you need it. DESIGN.md already has every number. |
| [vorssaint-utils](https://github.com/vorssaint/vorssaint-utils), [Mouzi](https://github.com/hsr88/mouzi) | Behavior references only. Read them to understand how a feature should *behave*. **Never copy their code, strings, icons or assets.** vorssaint is GPL with a trademark policy covering its look, and Chippi must be its own thing. |

**Port from Hopper** (adapt namespaces to `Chippi.*`):
- `Ui/Themes/Dark.xaml`, `Light.xaml`, `SharedStyles.xaml` and `Ui/ThemeManager.cs`. Keep the resource key names.
- `Ui/LucideIcon.cs` and `Ui/LucideIcons.cs` for icons.
- `Ui/TrayIcon.cs`, `Diagnostics/Log.cs`, `Config/ConfigStore.cs` and `SystemIntegration/StartupRegistration.cs`.
- The `App.xaml.cs` startup hardening: unhandled-exception logging, the single-instance mutex and the `%windir%` environment fix.

## 2. Environment

- Windows 11, .NET 10 SDK, and VS Build Tools 2022. Node and Python are available. There is no Rust and none is needed.
- `X:\code\chippi` is **not a git repo yet**. Run `git init` with a .NET `.gitignore` and commit on `main`. Use branches or worktrees per workstream. **Do not create a GitHub remote, push, publish a release or sign anything** without the user's explicit OK.

## 3. Fixed decisions (don't relitigate)

- C# / .NET 10 / **WPF**. Not WinUI and not Tauri.
- Windows 11 only, **x64 and ARM64**. Builds are RID-specific, so never set `PlatformTarget` to x64-only.
- License **GPL-3.0-or-later**. Add a `LICENSE` file, SPDX headers in source files and `THIRD-PARTY-NOTICES.md`.
- The app runs as a **standard user**. Never set `requireAdministrator`. Later features that need admin will use an optional elevated helper, so in Phase 1 you only model the "needs admin" capability.
- Local-first: **no telemetry and no network calls** in Phase 1.
- Look: direction A "Studio Rack" with the Gumdrop pet, exactly as specified in DESIGN.md. No stock WPF chrome anywhere.
- Features are modules the user can install and uninstall. An uninstalled module never loads, and reinstalling restores its settings.
- When a feature overlaps something built into Windows or PowerToys, it ships with an "Overlaps …" badge. Don't drop the feature.

## 4. Phase 1 scope

### 4.1 Solution and foundations
- Projects:
  - `src/Chippi.App`: the WPF exe, holding shell windows, tray, flyout, settings, island and pet.
  - `src/Chippi.Core`: contracts, module registry, config, logging and permissions model. It must not reference WPF.
  - `src/Features/Chippi.Features.KeepAwake`: the reference module.
  - `tests/Chippi.Core.Tests`: xUnit tests.
- `Directory.Build.props` sets nullable on, implicit usings, latest LangVersion, warnings as errors for our code, and SPDX in file templates.
- Libraries you may use (all permissive): CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting/DependencyInjection, Microsoft.Windows.CsWin32 for P/Invoke, and Velopack for packaging (see §4.8). Add others only with a reason in your report.
- **Fonts:** bundle **static** TTFs, never variable ones. Use Manrope 500/600/700/800 and JetBrains Mono 500/600, all OFL, taken from the official upstream repos. WPF can't select weights from a variable font, which is why Hopper's Manrope rendered ExtraLight.
- **`app.manifest`:** PerMonitorV2 DPI awareness, Windows 11 supportedOS, `asInvoker`.

### 4.2 Module system (Core)
- A minimal contract along these lines (refine it as needed):
  ```csharp
  public interface IFeatureModule
  {
      string Id { get; }                          // "keep-awake"
      string Name { get; }                        // "Keep awake"
      string Description { get; }                 // one line, sentence case
      FeatureGroup Group { get; }                 // Files, Sound, System, ClipboardText, Windows, Capture, Apps
      IReadOnlyList<Capability> Requires { get; } // e.g. Admin, VbCable
      IReadOnlyList<FeatureBadge> Badges { get; } // Source / Overlap / Needs
      Task StartAsync(ModuleContext context, CancellationToken ct);
      Task StopAsync(CancellationToken ct);
  }
  ```
- `ModuleContext` gives a module the following:
  - Typed settings storage: its own section in the config, kept after uninstall.
  - A logger.
  - A way to publish **island activities**.
  - Optional UI contributions: a flyout tool tile, a flyout tab and a settings page.
- The registry discovers modules and starts or stops them live when installed state changes. It applies presets (Essentials, Audio, Power user, Everything, Custom), and toggling a single module switches the preset to Custom.
- Config lives at `%APPDATA%\Chippi\settings.json`. Write it atomically and version it. Unknown sections are preserved. Settings export and import are a single JSON file.
- Logs go to `%LOCALAPPDATA%\Chippi\logs\`, rolling and size-capped.
- Permissions model: each capability, which installed modules need it, and whether it's currently unused.

### 4.3 Theme and controls (App)
- Port Hopper's theme dictionaries, then add the DESIGN.md additions: `AccentGradientDeepBrush`, `DangerDeepBrush`, `IslandBrush`, the `Pet*` brushes and the badge styles.
- Dark is the default. Light mirrors it, and a "Follow Windows" option is included. The island stays dark in both themes.
- Build these as templated controls with every state (hover, focus ring, disabled) per DESIGN.md §6:
  - primary and secondary buttons, and icon buttons
  - switch and segmented control
  - slider with the gradient fill
  - badge, status dot and search box
  - app volume row, stat tile, stash row, tool tile and feature card (with their visuals only; no real data yet)
- Keyboard: Tab order, visible focus rings and Esc behavior everywhere. Screen readers: every icon-only button has `AutomationProperties.Name`.

### 4.4 Tray icon and flyout
- The tray icon is Gumdrop's small form (DESIGN.md §7). Generate a multi-resolution `.ico` (16 to 256px) from the geometry; the small form covers ≤ 24px.
- Left-click toggles the flyout, and right-click opens a minimal menu (Open settings, Quit). A second app launch also opens the flyout.
- The flyout is a borderless, topmost window anchored with `Shell_NotifyIconGetRect`, following DESIGN.md §5 for size, position and padding. It closes on Esc and on deactivation.
- Tabs are Sound · System · Tidy · Tools, shown only when some installed module contributes to them (maximum 4).
  - In Phase 1, only **Tools** has real content: the Keep awake tile.
  - Other tabs show their placeholder state, and only if a debug flag is on.
- The footer has Settings and Quit.

### 4.5 Settings window
- Custom titlebar and the sidebar from DESIGN.md §5.
- **General:** theme, run at startup (HKCU Run, via Hopper's `StartupRegistration`), and export/import settings.
- **Features hub:**
  - Groups, cards and badges, with live install/uninstall switches.
  - Presets, search, and the "N of M installed" count.
  - Seed it with **placeholder registrations** for the PLAN.md features: name, description, group, badges, and no implementation, flagged "Coming soon" and not installable. That way the hub looks real.
- **Island & pet:** island on/off, pet color (Synth default and the 5 solids), a large Gumdrop preview that cycles through moods, and "Reduce motion" (defaults to the Windows setting).
- **Permissions:** capabilities, the modules using each, and an unused flag.
- **Shortcuts, Appearance, About:** simple and real where trivial. About shows version, license and third-party notices.

### 4.6 Island and Gumdrop
- The island is a borderless window with `AllowsTransparency`, topmost, and `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`. It never takes focus and never shows in Alt+Tab or the taskbar.
- It sits top-center on the monitor of the foreground window, 8px below the top edge. It hides while a fullscreen app, game or presentation is in front (`SHQueryUserNotificationState` plus a foreground-window bounds check).
- Its states follow DESIGN.md §5:
  - **Resting** pill with a sleepy pet.
  - **Live** pill with the highest-priority activity. Priority is media, then timer, then download/stash, then agent.
  - **Expanded** on hover after a short delay, or on click.
- Phase 1 sources: Keep awake publishes an activity. Add a hidden debug menu that injects fake activities (media, timer, stash, agent) to exercise every layout, but don't integrate real media sessions yet.
- **Gumdrop control:**
  - A `UserControl` with `Mood` (Idle, Celebrating, Sleepy, WideAwake), `Size` and `PetColor` properties.
  - Build it from the exact path data in DESIGN.md §7, with the small form at ≤ 22px.
  - Motion: blink every 4–7s, celebrate squish and spark fade, and eyes easing shut on falling asleep.
  - Reduced motion (from `SystemParameters.ClientAreaAnimation`, or the user's override) means no blink or squish, and moods swap instantly.
- Mood wiring:
  - Resting island → Sleepy.
  - Live and flyout header → Idle.
  - A module reporting success → Celebrating for about 2.5s.
  - Keep awake on → WideAwake.

### 4.7 Reference module: Keep awake
- Uses `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED [| ES_DISPLAY_REQUIRED])`.
- Modes: indefinitely, for a duration, or until a time. Option: keep the display on too.
- Surfaces:
  - A Tools tile in the flyout (toggle, with the state line "Until 6:00 PM").
  - A card in the hub (System group, "Overlaps PowerToys" badge).
  - A settings page.
  - An island activity while active (pet WideAwake). When a timed session ends, the pet celebrates.
- Always release the execution state on stop, on uninstall and on app exit.

### 4.8 Build, CI and packaging
- `dotnet publish` profiles for `win-x64` and `win-arm64`: self-contained, single-file where practical.
- A GitHub Actions workflow that builds and tests both RIDs. Commit it, but it won't run until the user creates a remote.
- Installer and auto-update use **Velopack**. `vpk pack` should produce a Setup.exe and a portable build locally. Leave the update feed URL unset. If Velopack blocks you, stop and report rather than switching tools silently.

## 5. Out of scope for Phase 1

- The Downloads organizer (Phase 2).
- Merging Hopper's audio engine (Phase 3).
- Sound, System monitor, Clipboard, Windows and Capture features beyond placeholder registrations.
- Real media-session or agent-usage integration.
- Code signing, a GitHub remote, releases and an update feed.

## 6. Suggested workstreams

Give each workstream a single owner, with exclusive folders so agents never edit the same files.

| # | Workstream | Owns | Depends on |
|---|---|---|---|
| WS0 | Foundation: solution, Core contracts, config, logging, single instance, theme port, fonts, base controls | `Directory.Build.props`, `src/Chippi.Core/**`, `src/Chippi.App/Themes/**`, `src/Chippi.App/Controls/**` | none. **Lands first.** |
| WS1 | Tray icon and flyout | `src/Chippi.App/Tray/**`, `src/Chippi.App/Flyout/**` | WS0 |
| WS2 | Settings window, Features hub, Permissions | `src/Chippi.App/Settings/**` | WS0 |
| WS3 | Island window and Gumdrop | `src/Chippi.App/Island/**`, `src/Chippi.App/Pet/**` | WS0 |
| WS4 | Keep awake module | `src/Features/Chippi.Features.KeepAwake/**` | WS0 |
| WS5 | Build, CI, packaging, licensing files | `build/**`, `.github/**`, `LICENSE`, `THIRD-PARTY-NOTICES.md` | WS0 |
| Lead | Integration, review against DESIGN.md, final report | `src/Chippi.App/App.xaml*`, composition root | all |

- Changes to Core contracts after WS0 lands go through the WS0 owner.
- Keep commits small, and rebase on `main` before merging.
- The lead reviews each workstream against DESIGN.md before merging it.

## 7. Done when

- [ ] `dotnet build -c Release` and `dotnet test` pass with zero warnings for `win-x64` and `win-arm64`.
- [ ] Core tests cover:
  - config round-trip and atomic writes
  - unknown sections preserved
  - preset logic
  - install → uninstall → reinstall restoring settings
  - the permissions "unused" logic
- [ ] On launch, the Gumdrop tray icon appears, and a second launch focuses the flyout instead of starting another instance.
- [ ] The flyout anchors correctly with the taskbar at the bottom and on multiple monitors at 100%, 150% and 200% scale. It closes on Esc and click-away, and matches DESIGN.md §5.
- [ ] In the Features hub:
  - toggling Keep awake starts and stops it live
  - presets, search, counts and badges work
  - "Coming soon" placeholders aren't installable
- [ ] The island shows all three states and follows the foreground monitor. It hides over fullscreen apps, never steals focus and never appears in Alt+Tab.
- [ ] Gumdrop's four moods and its small form match the DESIGN.md §7 geometry. Blink and squish work, and reduced motion disables them.
- [ ] Keep awake actually prevents sleep (`powercfg /requests` shows it). It releases on stop, uninstall and exit, and its island and pet states work.
- [ ] Dark, light and Follow Windows themes match DESIGN.md tokens. Focus rings appear everywhere, the app is fully keyboard-navigable, and Narrator reads every icon button.
- [ ] Run at startup works, and logs and settings land in the documented folders.
- [ ] Velopack builds Setup.exe and a portable package for both RIDs.

## 8. Guardrails

- **DESIGN.md over taste.** If it's silent on something you need, propose an addition in your report instead of improvising silently.
- No code, strings, icons or assets from vorssaint-utils or Mouzi.
- No telemetry, no network, no admin requirement, no stock WPF chrome, no emoji in the UI, and no redrawn third-party logos.
- Don't modify `X:\code\hopper`. Copy what you need into Chippi.
- Don't push, create remotes, publish, sign or change system settings beyond what the app itself does at runtime (HKCU Run and the execution state).

## 9. Escalate to the user

- Anything that would change a fixed decision or DESIGN.md.
- Velopack not working out.
- The final app icon. Ship the generated Gumdrop `.ico`, but flag it for approval.
- Creating the GitHub repo or pushing.

## 10. Report back

When Phase 1 is done, report:
- what was built
- how to build and run it
- screenshots of the flyout, the hub, the three island states, and Gumdrop's moods in dark and light (if your tooling can capture them)
- test results
- deviations from DESIGN.md, with reasons
- known issues
- suggestions for Phase 2 (the Downloads organizer)
