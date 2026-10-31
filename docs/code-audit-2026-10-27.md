# Code Audit Report

**Project**: Chippi, a Windows 11 tray suite in C# (.NET 10, WPF, xUnit). Audited `main` at `e85d20b` (Phase 4 merged), 2026-10-27.
**Scope**: full project (`src/`, `tests/`, `build/`, `native/`, `.github/`)
**Findings**: 74 total (7 high, 42 medium, 25 low)

**Method:**
- Seven read-only reviewers looked at one area each: standards, duplication, logging, security, tests, dependencies, and performance (added because the request asked where the app can be optimized).
- ✓ marks a finding I re-checked myself, in the code or by running it.
- **Baseline:** a Release build with 0 warnings; 756 tests pass and 1 fails (the flaky test below); the test run takes 16s.

---

## High Priority

### Security
- Cross-volume moves and archive imports drop Mark-of-the-Web: only the main data stream is copied, and there's no Zone.Identifier handling anywhere in the code. A downloaded installer or Office file moved to another drive then skips SmartScreen and Protected View ✓ — `src/Features/Chippi.Features.Downloads/Engine/Execution.cs:211` (and `src/Features/Chippi.Features.Downloads/Import/ArchiveImporter.cs:170`)

### Standards
- When a Sound control fails, the error is never logged. The modules set `Error` and rethrow, and every Sound view's `async void Run` swallows it with a bare `catch { }`. The Keep awake views log the same kind of error and show it ✓ — `src/Features/Chippi.Features.Sound/Volume/VolumeView.cs:162` (and `Routing/RoutingPage.cs:123`, `Routing/RoutingQuickView.cs:156`, `Processing/ProcessingViews.cs:106`, `Mute/MicMuteView.cs:115`, `Mute/MicMuteModule.cs:84`)

### Duplication
- Four separate sets of UI builders: `SettingsUi` (App), `RoutingUi` (Sound, used beyond routing), Downloads `Ui`, and System monitor's page helpers. `Text`, `Card`/`Group`, section headers, `Pad` and `Columns` are duplicated, with different defaults. There are five ways to build an On/Off switch; only `SettingsUi` guards against re-entry, and Downloads overrides its own switch style afterwards — `src/Features/Chippi.Features.Sound/Routing/RoutingUi.cs:17` (and `src/Features/Chippi.Features.Downloads/Views/Settings/Ui.cs:18`, `src/Chippi.App/Settings/SettingsUi.cs:101`, `src/Features/Chippi.Features.SystemMonitor/Views/SystemMonitorSettingsPage.cs:232`)

### Performance
- The app wakes up all day with nothing to do:
  - The File organizer loop wakes every 250ms (taking a semaphore and a lock, then scanning), even when nothing is tracked or every folder is Paused.
  - Keep awake's worker thread wakes 4 times a second while Keep awake is off, plus a 30-second tile timer that keeps writing "Off".
  
  ✓ — `src/Features/Chippi.Features.Downloads/Engine/DownloadsEngine.cs:356` (and `Engine/Execution.cs:18`, `src/Features/Chippi.Features.KeepAwake/AwakeSession.cs:115`, `src/Features/Chippi.Features.KeepAwake/KeepAwakeModule.cs:70`)

### Testing
- A flaky test on `main`: `A_click_outside_closes_the_island_and_one_on_it_does_not` failed 3 of 9 runs on Travis's PC. It drives a real island window on the primary display, so the real pointer and whatever window is in front affect the result ✓ — `tests/Chippi.Island.Tests/IslandWindowTests.cs:92`
- Tests wait a fixed time, then assert, racing real timers:
  - AudioServiceTests waits `Delay(100)`, then expects an exact enumeration count against a 20ms debounce and 300ms back-off.
  - ModuleServicesTests waits `Delay(300)`, then expects both relayed requests.
  - About 12 island panel tests pump for 400ms, then expect exact widths and opacity while the morph animation may still be running.
  
  — `tests/Chippi.Audio.Tests/AudioServiceTests.cs:129` (and `tests/Chippi.Core.Tests/ModuleServicesTests.cs:435`, `tests/Chippi.Island.Tests/IslandPanelTests.cs:62`)
- Seven hand-written UI-thread (STA) test runners that shut down differently. These caused the earlier "blocked thread" and "dispatcher stopped too early" flakes ✓ — `tests/Chippi.Features.Sound.Tests/MicMuteUiTests.cs:84` (and `RoutingUiTests.cs:124`, `VolumeUiTests.cs:37`, `tests/Chippi.Features.KeepAwake.Tests/KeepAwakeViewTests.cs:65`, `tests/Chippi.Rules.Tests/RulesEditorTests.cs:119`, `tests/Chippi.SoundSmoke/SoundScene.cs:48`, `tests/Chippi.Island.Tests/WpfTestThread.cs:9`)

## Medium Priority

### Standards
- File organizer Undo is handled three ways. TidyView catches and shows errors. The Preview view's click handler has no try/catch, so an error leaves Undo disabled with no message. History discards the task, so its errors are never seen — `src/Features/Chippi.Features.Downloads/Views/Settings/PreviewView.cs:301` (and `Views/Settings/HistorySection.cs:183`, `Views/Tidy/TidyView.xaml.cs:162`)
- The four callers of `DownloadsModule.SaveSettings` catch different exception sets, and RulesSection drops a failed rule save silently — `src/Features/Chippi.Features.Downloads/Views/Settings/RulesSection.cs:721` (and `Views/Settings/SettingsSection.cs:242`)
- Settings are saved in two orders. System monitor and Mic mute change `Settings` before saving, so a failed write leaves memory and file out of sync, and a save after stop is silently dropped — `src/Features/Chippi.Features.SystemMonitor/SystemMonitorModule.cs:121` (and `src/Features/Chippi.Features.Sound/Mute/MicMuteModule.cs:110`)
- `SettingsUi.SaveApp` has no error handling and about 14 Settings handlers call it. IslandHost has its own copy that catches and logs errors. `GetSection<AppSettings>` is spelled out in 7 more places — `src/Chippi.App/Settings/SettingsUi.cs:137` (and `src/Chippi.App/Island/IslandHost.cs:457`)
- The VB-CABLE requirement blocks the UI thread with `GetAwaiter().GetResult()` when Add features loads, and can start the whole audio engine to do it. Its `Dispose` never awaits the lease release — `src/Features/Chippi.Features.Sound/Processing/VbCableRequirement.cs:41` (and `:134`)
- The Notifications page doesn't rebuild when shown again, so a feature installed meanwhile is missing until Settings reopens. The other built-in pages refresh on `Loaded` — `src/Chippi.App/Settings/Pages/NotificationsPage.cs:63`
- Very large files and methods:
  - `DownloadsEngine.cs`: 1007 lines.
  - `RulesSection.Editor`: one 262-line method in a 786-line file.
  - `IslandPanelWindow.xaml.cs` (901 lines) and `IslandWindow.xaml.cs` (845 lines).
  - Most files are under 300 lines.
  
  ✓ — `src/Features/Chippi.Features.Downloads/Engine/DownloadsEngine.cs:1` (and `Views/Settings/RulesSection.cs:302`)

### Duplication
- The four Sound modules copy the same lifecycle and command wrapper:
  - Start: lease, refresh, subscribe.
  - Stop: unsubscribe, gate, release.
  - Each command: gate, then "uninstalled" check, then `Error = AudioErrors.Reason`, then rethrow.
  
  ModuleRegistry repeats its own gate block 5 times — `src/Features/Chippi.Features.Sound/Volume/AppVolumeModule.cs:43` (and `Routing/AudioRoutingModule.cs:49`, `Processing/MicProcessingModule.cs:74`, `Mute/MicMuteModule.cs:58`, `src/Chippi.Core/Modules/ModuleRegistry.cs:116`)
- View boilerplate is pasted into 9 views: subscribe on Loaded, unsubscribe on Unloaded, and the `CheckAccess` → `BeginInvoke(Refresh)` line. The `async void Run(Task)` wrapper exists 6 times, even though `ProcessingUi.Run` already exists — `src/Features/Chippi.Features.Sound/Volume/VolumeView.cs:35` (and `src/Features/Chippi.Features.KeepAwake/KeepAwakeIslandView.cs:67`, `src/Features/Chippi.Features.Sound/Processing/ProcessingViews.cs:103`)
- Feature, notice and activity ids are repeated as string literals:
  - "mic-mute", "keep-awake", "audio-routing" and "system-monitor" in several places.
  - Each alert kind in 3 places.
  - "Stopped · ".Length used to cut a status string apart.
  - `FeatureCatalog` still holds a copy of the shipped System monitor descriptor, and its own comment says to delete it.
  
  — `src/Chippi.App/Settings/SettingsWindow.xaml.cs:50` (and `src/Chippi.App/Island/IslandHost.cs:25`, `src/Features/Chippi.Features.SystemMonitor/Alerts/AlertEvaluator.cs:26`, `src/Chippi.App/Settings/Features/FeatureCatalog.cs:38`)
- Duplicated formatters disagree, and users can see the difference:
  - Downloads shows KB/MB/GB; System monitor shows KiB/MiB/GiB.
  - Two "X hr Y min" formatters round differently.
  - AlertEvaluator re-implements the temperature and percent formats.
  
  — `src/Features/Chippi.Features.Downloads/Views/Settings/Ui.cs:258` (and `src/Features/Chippi.Features.SystemMonitor/Views/Format.cs:19`, `src/Features/Chippi.Features.KeepAwake/KeepAwakeText.cs:35`, `src/Features/Chippi.Features.SystemMonitor/Alerts/AlertEvaluator.cs:113`)

### Logging
- Two logging paths write to the same rolling file. The static `Log` facade (about 50 call sites) hardcodes category "App", ignores the minimum level and takes pre-built strings. Its own doc comment limits it to code that runs before dependency injection exists, yet Keep awake views, Add features and the tray use it while holding a logger — `src/Chippi.Core/Diagnostics/RollingFileLog.cs:138` (and `src/Features/Chippi.Features.KeepAwake/KeepAwakeIslandView.cs:135`, `src/Chippi.App/Settings/Pages/AddFeaturesPage.cs:112`)
- The shared audio service takes the logger of whichever caller creates it first. Mic processing's Add features and meter paths pass `NullLogger`, so if Add features opens before any Sound feature runs, the whole audio stack logs nothing until restart ✓ — `src/Features/Chippi.Features.Sound/SoundAudio.cs:20` (and `src/Features/Chippi.Features.Sound/Processing/MicProcessingModule.cs:55`)
- Warnings spam on hot paths with no dedupe:
  - A failing media session warns with a stack trace on every media event.
  - "Reading sessions failed" warns on every volume or session event, such as during a slider drag.
  - An unreadable ignore file warns on every readiness check.
  - An invalid module section logs an error on every read.
  
  — `src/Chippi.App/Island/Media/MediaSessions.cs:213` (and `src/Chippi.Audio/AudioService.cs:517`, `src/Features/Chippi.Features.Downloads/Engine/DownloadsEngine.cs:867`, `src/Chippi.Core/Config/ConfigStore.cs:305`)
- Toast activation errors go to `Console.Error`, which a WinExe throws away — `src/Chippi.App/Notifications/ToastCom.cs:24`
- Failures swallowed with no log:
  - A corrupt mic-processing crash marker becomes null, so VB-CABLE can stay the default mic with no trace.
  - The Velopack uninstall hooks catch everything silently.
  
  — `src/Features/Chippi.Features.Sound/Processing/MicProcessingModule.cs:278` (and `src/Chippi.App/Program.cs:28`)

### Security
- Elevated VB-CABLE setup:
  - It runs elevated from a user-writable folder; only the setup exe's hash and signer are checked, not the files or DLLs beside it.
  - The trust anchor (`vb-cable.json`) sits in the same folder.
  - The `wintrust.dll` import has no System32 search pin.
  - Revocation isn't checked.
  
  A same-user process could plant a DLL that runs behind a genuine-looking UAC prompt ✓ (pin missing) — `src/Features/Chippi.Features.Sound/Processing/VbCablePackage.cs:53` (and `:44`, `:116`, `:131`, `:164`)
- Settings import applies the whole file without review. A crafted file can add Silent watched folders with UNC rule destinations, which move existing files at once (imported Downloads settings skip re-baselining), and can turn on the Explorer and Run-key entries — `src/Chippi.Core/Config/ConfigStore.cs:170` (and `src/Chippi.App/Settings/Pages/GeneralPage.cs:157`, `src/Features/Chippi.Features.Downloads/Engine/DownloadsEngine.cs:816`)
- CI supply chain:
  - Actions are pinned by movable tags (`checkout@v7`, `setup-dotnet@v6`, `upload-artifact@v6`, `download-artifact@v7`).
  - The committed `rnnoise.dll` binaries are checked only against a `hashes.csv` committed beside them, with no rebuild comparison.
  
  — `.github/workflows/build.yml:23` (and `.github/workflows/rnnoise.yml:20`, `:56`)

### Performance
- Any tile status change rebuilds every grid tile (new icons, text and handlers). With the grid open and the CPU status on, that's every 1–2 seconds. It also happens while the island is closed, with one dispatch per change ✓ — `src/Chippi.App/Island/IslandGrid.cs:61` (and `src/Chippi.App/Island/IslandPanelWindow.xaml.cs:219`)
- `IslandActivityHub.Publish` raises `Changed` even when nothing changed. A holding alert republishes every sampler tick, a muted mic every audio snapshot, and media every event, and each one refreshes the tab, grid and orbit with the island closed — `src/Chippi.Core/Island/IslandActivityHub.cs:66` (and `src/Features/Chippi.Features.SystemMonitor/Alerts/MonitorAlerts.cs:92`, `src/Features/Chippi.Features.Sound/Mute/MicMuteModule.cs:130`)
- The System monitor page rebuilds visuals every tick instead of updating them:
  - The disk table is a new Grid each time.
  - Each card line gets new TextBlocks.
  - Each graph render allocates a new brush, pens and geometry.
  - The network peak is computed by LINQ over copied history.
  
  — `src/Features/Chippi.Features.SystemMonitor/Views/SystemMonitorView.cs:406` (and `:182`, `src/Features/Chippi.Features.SystemMonitor/Views/Graph.cs:55`)
- Every volume or session event re-enumerates all sessions on every output:
  - It creates new COM wrappers that are never disposed.
  - It reads `FileVersionInfo` from disk per unnamed session.
  - A slider drag repeats this many times a second.
  
  — `src/Chippi.Audio/CoreAudioDeviceSystem.cs:116` (and `:255`, `src/Chippi.Audio/AudioService.cs:172`)
- App volume's `async void OnSnapshot` queues a backlog of full refreshes during snapshot bursts. Each refresh probes every app's output with an engine round trip, even when no App volume view is shown — `src/Features/Chippi.Features.Sound/Volume/AppVolumeModule.cs:127` (and `:146`, `src/Chippi.Audio/PerAppDevicePolicy.cs:89`)
- Enabled alerts poll all day at the page's update speed:
  - Low disk also collects three disk-rate counters it never uses.
  - Low battery reads the battery report every tick.
  
  — `src/Features/Chippi.Features.SystemMonitor/Alerts/AlertEvaluator.cs:40` (and `Sensors/Platform/DiskSource.cs:36`, `Sensors/Platform/BatterySource.cs:40`)
- GPU reads:
  - NVML and D3DKMT are polled every tick. That can keep a hybrid laptop's discrete GPU awake while a GPU temperature alert is on.
  - The engine-counter parse allocates per instance (often thousands) every tick.
  - Sources stay open until the sampler is disposed.
  
  — `src/Features/Chippi.Features.SystemMonitor/Sensors/Platform/GpuSource.cs:32` (and `:113`, `Sensors/SamplerCore.cs:82`)
- Settings writes block the UI thread:
  - Opening another island page writes all of `settings.json` synchronously (write-through, flush, retry sleeps up to about 1.5s) while the global mouse hook is installed.
  - System monitor's threshold sliders save on every slider tick.
  
  — `src/Chippi.App/Island/IslandHost.cs:60` (and `src/Chippi.Core/Config/ConfigStore.cs:337`, `src/Features/Chippi.Features.SystemMonitor/Views/SystemMonitorSettingsPage.cs:186`)
- File organizer History queries SQLite on the UI thread, with one query per run on top of the first 500-run query, and rebuilds the whole list on every change. Tidy queries on every event without coalescing — `src/Features/Chippi.Features.Downloads/Views/Settings/HistorySection.cs:119` (and `src/Features/Chippi.Features.Downloads/Views/Tidy/TidyView.xaml.cs:93`)
- Settings search builds every feature's settings page on every keystroke, just to read the labels ✓ — `src/Chippi.App/Settings/SettingsWindow.xaml.cs:313` (and `:234`)
- The fullscreen tracker's location-change hook delivers every caret and child-object move in the foreground app to Chippi's UI thread — `src/Chippi.App/Island/ForegroundTracker.cs:224`
- Startup runs the File organizer's SQLite migrate, folder rescans and crash reconcile (which can hash files) synchronously on the UI thread, before the island and tray exist — `src/Chippi.App/App.xaml.cs:182` (and `src/Features/Chippi.Features.Downloads/DownloadsModule.cs:100`, `src/Features/Chippi.Features.Downloads/Engine/DownloadsEngine.cs:110`)

### Testing
- Island tests share one UI-thread dispatcher across test classes that run in parallel, so one test can run inside another's pump loop. A test also changes the static `VbCableRequirement.BootTime` while other classes run — `tests/Chippi.Island.Tests/WpfTestThread.cs:31` (and `tests/Chippi.Features.Sound.Tests/ProcessingTests.cs:302`)
- Nine polling helpers with timeouts from 3 to 15 seconds and different ways of failing; the 3-second ones are tight for CI — `tests/Chippi.Features.Sound.Tests/MicMuteUiTests.cs:80` (and `RoutingUiTests.cs:114`)
- The Downloads test fixture ignores `SpinUntil`'s result, and `Settle` still sleeps for watcher events, the same race as the earlier notice flake — `tests/Chippi.Features.Downloads.Tests/Fixture.cs:102`
- Duplicated test doubles: 6 fake clocks (the Downloads one isn't thread-safe), 4 notifier recorders, 5 settings stores, 3 UI fakes, about 11 logical-tree walkers, and `Pump` copied within one project — `tests/Chippi.Features.Sound.Tests/RoutingFakes.cs:46` (and `tests/Chippi.Island.Tests/IslandWindowTests.cs:267`, `IslandPanelTests.cs:398`)
- Docked placement math (`FlyoutPlacement`) is pure, but only one case is tested: a bottom taskbar at 100%. Untested: top, left and right taskbars; other scale factors; small displays; shadow trimming — `src/Chippi.App/Flyout/FlyoutPlacement.cs:105`
- Untested logic: Tidy's row projection (ordering, 3-row cap, action mapping) and the fullscreen decision in `ForegroundTracker.Evaluate` — `src/Features/Chippi.Features.Downloads/Views/Tidy/TidyProjection.cs:70` (and `src/Chippi.App/Island/ForegroundTracker.cs:94`)
- Missing failure paths:
  - Cancellation during archive import, Approve/Undo and the driver install.
  - Corrupt archives.
  - Resume at the module level.
  - Keep awake's tile timer can't be driven by a fake clock.
  
  — `src/Features/Chippi.Features.Downloads/Import/ArchiveImporter.cs:43`

### Dependencies
- The self-contained app ships .NET runtime 10.0.13. The 10.0.14 security release came out on 2026-10-25 and fixes 4 CVEs ✓ — `Microsoft.NETCore.App / WindowsDesktop.App@10.0.13 (10.0.13 -> 10.0.14)`, SDK in `global.json`
- No NuGet lock files, so transitive versions (SQLitePCLRaw, CsWin32 metadata) aren't frozen and CI can't run a locked restore or cache packages — `Directory.Build.props`
- Tests never run on ARM64: the arm64 leg only cross-builds on an x64 runner, though `rnnoise.yml` already uses `windows-11-arm` — `.github/workflows/build.yml`
- `THIRD-PARTY-NOTICES.md` is missing the SQLite stack that ships with the File organizer: Microsoft.Data.Sqlite (MIT), SQLitePCLRaw (Apache-2.0, whose license text must be included) and native SQLite — `THIRD-PARTY-NOTICES.md`

## Low Priority

### Standards
- Two ways to build views: Keep awake's settings page and Tidy are XAML, while every other page and island view is built in code — `src/Features/Chippi.Features.KeepAwake/KeepAwakePage.xaml:4` (and `src/Features/Chippi.Features.KeepAwake/KeepAwakeIslandView.cs:17`)
- `DownloadsSettings` is a mutable class with a hand-written `Clone()` and is changed in place on start; every other module's settings type is an immutable record — `src/Features/Chippi.Features.Downloads/Model/DownloadsSettings.cs:104`
- Naming: some Task-returning methods have no `Async` suffix (`AppVolumeModule.Change`, `MediaSessions.Command`), and static fields mix the `s_` and `_` prefixes — `src/Features/Chippi.Features.Sound/Volume/AppVolumeModule.cs:111` (and `src/Features/Chippi.Features.Sound/SoundAudio.cs:15`)
- Bare catch-all blocks in Chippi.Audio, where the rest of the project filters to `COMException` — `src/Chippi.Audio/MicrophoneMute.cs:28` (and `src/Chippi.Audio/AudioService.cs:579`)
- The startup file changes with every feature (28 changes, the most-edited file), and registrations have caused merge conflicts ✓ — `src/Chippi.App/App.xaml.cs:163`

### Duplication
- The island drop-down picker is built twice, and its popup chrome four times — `src/Chippi.App/Island/Media/NowPlayingView.cs:171` (and `src/Features/Chippi.Features.SystemMonitor/Views/SystemMonitorView.cs:496`)
- Drag-to-reorder wiring and the list move are duplicated — `src/Features/Chippi.Features.Sound/Routing/RoutingPage.cs:75` (and `src/Chippi.App/Settings/Pages/IslandPetPage.cs:215`)
- Win32 monitor and DPI helpers are written two or three times — `src/Chippi.App/Island/IslandNative.cs:57` (and `src/Chippi.App/Tray/MonitorDpi.cs:15`)
- Theme resource keys are raw strings ("CaptionTextStyle" appears 55 times) and tile order numbers are scattered across 6 modules — `src/Features/Chippi.Features.SystemMonitor/Views/SystemMonitorView.cs:421`

### Logging
- `Debug.WriteLine` for a missing accessible name is compiled out of Release, so it never reaches the log — `src/Chippi.UI/Controls/IconButton.cs:57`
- `LogDebug` calls never write: the level is fixed at Information, and `--debug` doesn't lower it. Toast startup also logs into the real profile before switching to the dev profile — `src/Chippi.App/App.xaml.cs:97`
- The debug menu ships in Release behind `--debug` / `CHIPPI_DEBUG` and writes real settings. A stale comment there still says "before WS4 lands" — `src/Chippi.App/Island/IslandHost.cs:89`
- Plain-text logs and toasts include user paths and file names, and Held/Failed toasts can show on the lock screen. Fullscreen app class names are logged at Information — `src/Features/Chippi.Features.Downloads/Engine/DownloadsEngine.cs:862` (and `:964`, `src/Chippi.App/Island/ForegroundTracker.cs:121`)

### Security
- `CHIPPI_DEV_PROFILE` from the environment isn't validated; only `--dev-profile` is. `..\x` or `C:\x` escapes the dev folder ✓ — `src/Chippi.Core/SystemIntegration/ExplorerRegistration.cs:66` (and `src/Chippi.App/App.xaml.cs:78`)
- When the bundled RNNoise DLL fails to load, the resolver falls back to default probing by bare name instead of failing — `src/Chippi.Audio/Processing/Rnnoise.cs:35`
- Single-file publish extracts native DLLs to `%TEMP%\.net` and doesn't verify them on later launches — `src/Chippi.App/Properties/PublishProfiles/win-x64.pubxml:8`
- Release artifacts aren't Authenticode-signed — `build/pack.ps1:41`
- `.gitignore` gaps: `*.snk` is commented out; `*.pem`, `*.key`, `*.p12`, crash dumps and stray `*.db` files aren't covered. Nothing sensitive is tracked today — `.gitignore:253`

### Testing
- Tests tied to internals: a "fresh worker" check by managed thread id (ids get reused), "the control left the tree" used as the done signal, and values found by `FontSize` 20 — `tests/Chippi.Features.KeepAwake.Tests/KeepAwakeRecoveryTests.cs:55` (and `tests/Chippi.Features.Sound.Tests/RoutingUiTests.cs:32`)
- The slowest test (11.5s) reads real PDH, GPU and battery sources and sleeps past a 10-second staleness window inside the unit suite ✓ — `tests/Chippi.Features.SystemMonitor.Tests/WindowsSourcesTests.cs:104`
- Tests that pass trivially:
  - The screenshot "tests" report green when their environment variable is unset.
  - Two tests only restate constants.
  - A dispose test never checks the thread.
  - Negative checks after fixed sleeps pass by default on slow machines.
  
  — `tests/Chippi.Island.Tests/IslandPanelShots.cs:31`

### Dependencies
- `xunit` 2.x is deprecated (alternative: `xunit.v3`); the test SDK and runner are each one major version behind — `xunit@2.9.3 (2.9.3 -> xunit.v3 4.0.2)`, `Microsoft.NET.Test.Sdk@17.14.1 (-> 18.11.0)`, `xunit.runner.visualstudio@3.1.4 (-> 4.0.1)`
- `Microsoft.Extensions.Logging*` sit at 10.0.0 while the runtime and SQLite are at 10.0.13 — `Microsoft.Extensions.Logging@10.0.0 (10.0.0 -> 10.0.14)`
- `NAudio.Wasapi@2.2.1 (2.2.1 -> 3.3.0)`; `SQLitePCLRaw.*@2.1.13 (-> 3.0.5, through Microsoft.Data.Sqlite)`; `Microsoft.Windows.CsWin32@0.3.366 (-> 0.3.367)`
- The SDK version lives in two places (`global.json` 10.0.400 latestPatch, CI 10.0.402), and CI doesn't cache NuGet packages — `global.json`, `.github/workflows/build.yml`

---

# Implementation Plan

## Quick Wins (< 30 min each)
| # | Finding | File(s) | Fix |
|---|---------|---------|-----|
| 1 | .NET 10.0.14 security release | `global.json`, `build.yml` | Pin SDK 10.0.403 in one place (`global-json-file`), rebuild, ship |
| 2 | Keep awake polls while off | `AwakeSession.cs:115`, `KeepAwakeModule.cs:70` | Block on `Take()` with no timeout; arm one due-time timer only for a timed session |
| 3 | Sound errors never logged | 6 Sound views, 4 modules | Log in each module's catch with `context.Logger` before rethrowing |
| 4 | Toast errors to Console | `ToastCom.cs:24` | `Log.Error` |
| 5 | Swallowed marker and uninstall-hook failures | `MicProcessingModule.cs:278`, `Program.cs:28` | Log a warning |
| 6 | wintrust import not pinned to System32 | `VbCablePackage.cs:116` | Add `[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]` |
| 7 | Unvalidated `CHIPPI_DEV_PROFILE` | `ExplorerRegistration.cs:66`, `App.xaml.cs:78` | Run the variable through `ValidateProfile` too |
| 8 | Notifications page stale after installs | `NotificationsPage.cs:63` | Rebuild in `Loaded`, like its sibling pages |
| 9 | Missing notices | `THIRD-PARTY-NOTICES.md` | Add Microsoft.Data.Sqlite, SQLitePCLRaw (with the Apache-2.0 text) and SQLite |
| 10 | Stale catalog descriptor and comment | `FeatureCatalog.cs:38`, `IslandHost.cs:423` | Delete the shipped System monitor copy and the stale comment |
| 11 | `.gitignore` gaps | `.gitignore` | Add `*.snk`, `*.pem`, `*.key`, `*.p12`, `*.dmp`, stray `*.db` |
| 12 | Actions by tag | `build.yml`, `rnnoise.yml` | Pin each action to a commit SHA |
| 13 | RNNoise falls back to default probing | `Rnnoise.cs:35` | Fail closed when the bundled DLL can't load |
| 14 | Debug.WriteLine, Debug logs never written | `IconButton.cs:57`, `App.xaml.cs:97` | Use the logger; let `--debug` lower the level to Debug |

## Medium Effort (30 min - 2 hours each)
| # | Finding | File(s) | Fix |
|---|---------|---------|-----|
| 1 | Mark-of-the-Web lost | `Execution.cs:211`, `ArchiveImporter.cs:170` | Copy the `Zone.Identifier` stream on cross-volume moves; stamp extracted entries with the archive's zone. Add tests |
| 2 | Flaky click-outside test | `IslandWindowTests.cs:92` | Drive it with a fake foreground and pointer, or move it to UiSmoke |
| 3 | File organizer 250ms loop | `DownloadsEngine.cs:356` | Sleep until the next due time, or until Track/SortExisting signals |
| 4 | Audio logger set by first caller | `SoundAudio.cs:20`, `MicProcessingModule.cs:55` | Give `SoundAudio` its own `Module.sound` logger from the factory; never `NullLogger` |
| 5 | Grid rebuilds every tile | `IslandGrid.cs:61` | Keep tiles by key and update name, status and dot in place; rebuild only when the set or order changes |
| 6 | Hub raises unchanged activities | `IslandActivityHub.cs:66` | Compare with the current activity and skip equal publishes |
| 7 | Settings writes on the UI thread | `IslandHost.cs:60`, `SystemMonitorSettingsPage.cs:186` | Debounce the threshold saves (like Mic processing's 500ms); write the last page off the UI thread or on close |
| 8 | Hot-path warning spam | `MediaSessions.cs:213`, `AudioService.cs:517`, `DownloadsEngine.cs:867`, `ConfigStore.cs:305` | Log once per distinct failure until it recovers |
| 9 | Undo handled three ways | `PreviewView.cs:301`, `HistorySection.cs:183` | One `RunUndoAsync` that catches, logs and shows; use it in all three places |
| 10 | Save order and `SaveApp` error handling | `SystemMonitorModule.cs:121`, `MicMuteModule.cs:110`, `SettingsUi.cs:137` | Save first, then assign; give `SaveApp` IslandHost's catch and delete IslandHost's copy |
| 11 | VB-CABLE requirement blocks the UI | `VbCableRequirement.cs:41` | Create asynchronously; await the lease release in `DisposeAsync` |
| 12 | Settings search builds every page | `SettingsWindow.xaml.cs:313` | Cache each page's labels after its first build (or give features a label list) |
| 13 | System monitor page rebuilds per tick | `SystemMonitorView.cs:406`, `Graph.cs:55` | Keep the controls and set values; freeze and cache pens and brushes in `Graph` |
| 14 | Alerts poll at page speed | `AlertEvaluator.cs:40`, `DiskSource.cs:36`, `BatterySource.cs:40` | Give alerts their own slower cadence (disk 60s, battery 30s or the power event) and a free-space-only need |
| 15 | Shared test helpers | `tests/` | One linked `TestUi` (an STA runner that pumps, `Until` with a 10s default and message, tree walkers), one `ManualClock`, one settings store and notifier, linked with the existing `<Compile Include Link>` pattern |
| 16 | Fixed-delay test asserts | `AudioServiceTests.cs:129`, `ModuleServicesTests.cs:435`, `IslandPanelTests.cs:62`, `Fixture.cs:102` | Poll for the condition, or drive time with the fake clock; set `Motion.Override` where the morph isn't under test |
| 17 | Island tests share a dispatcher in parallel | `WpfTestThread.cs:31` | Disable parallelization for Island.Tests (`xunit.runner.json`), or one dispatcher per class |
| 18 | Slow real-source test | `WindowsSourcesTests.cs:104` | Inject the rate clock, or move the test to an opt-in smoke project |
| 19 | Lock files and CI cache | `Directory.Build.props`, `build.yml` | Turn on `RestorePackagesWithLockFile`, commit the lock files, use locked mode and setup-dotnet's `cache` |
| 20 | ARM64 tests | `build.yml` | Add a `windows-11-arm` test leg |
| 21 | FlyoutPlacement, TidyProjection, ForegroundTracker tests | `tests/` | Table tests for each taskbar edge and scale; row projection; the fullscreen decision |
| 22 | Formatters disagree | `Ui.cs:258`, `Format.cs`, `KeepAwakeText.cs` | One `Chippi.Core` formatter for sizes (binary units, as decided for Phase 4) and durations |
| 23 | Magic ids | Several | Use the modules' `Id` constants; give notice and alert kinds constants, like `DownloadsNotices` |
| 24 | Startup on the UI thread | `App.xaml.cs:182`, `DownloadsModule.cs:100` | Start modules after the island shows; move the SQLite migrate and rescans to the engine thread |
| 25 | History N+1 on the UI thread | `HistorySection.cs:119`, `TidyView.xaml.cs:93` | One joined query on a background thread; coalesce refreshes |

## Complex (> 2 hours)
| # | Finding | File(s) | Fix |
|---|---------|---------|-----|
| 1 | Four UI-helper sets and five switches | `SettingsUi`, `RoutingUi`, Downloads `Ui`, System monitor helpers | Move one set into Chippi.UI (Text, Card, Section, Row, Switch with the re-entry guard, Run), switch every caller, delete the copies |
| 2 | Sound module lifecycle and view boilerplate | 4 Sound modules, 9 views | A small `AudioModuleCore` (lease, gate, error and log) and a view helper (subscribe while loaded, dispatch refresh, Run) |
| 3 | Elevated driver setup hardening | `VbCablePackage.cs` | Copy the package to a folder only admins can write (or verify every file's hash), embed the trust anchor in the assembly, check revocation |
| 4 | Settings import review | `ConfigStore.cs:170`, `GeneralPage.cs:157` | Validate imported feature sections, pause imported Silent folders until confirmed, and re-baseline them |
| 5 | Audio session enumeration cost | `CoreAudioDeviceSystem.cs:116`, `AudioService.cs:172`, `AppVolumeModule.cs:127` | Cache sessions by id, dispose COM wrappers, cache app names per exe, coalesce snapshots, probe outputs only while App volume shows |
| 6 | GPU polling | `GpuSource.cs`, `SamplerCore.cs` | Release sources when nothing watches; slow NVML for alerts only; parse engine counters without allocating per instance |
| 7 | Static `Log` facade | ~50 call sites | Move callers that hold a logger to `ILogger` with templates; keep `Log` for pre-DI startup only |
| 8 | Large files | `DownloadsEngine.cs`, `RulesSection.cs`, `IslandPanelWindow.xaml.cs` | Split by responsibility when next touched (rule editor, engine loop vs execution, panel morph vs content) |
| 9 | xunit.v3 migration | 11 test projects | Move to xunit.v3 and Test SDK 18 together |

## Suggested Fix Order
1. **Security and shipping:** .NET 10.0.14, Mark-of-the-Web, the wintrust pin, RNNoise failing closed, the notices. These are user-facing risks, and most are small.
2. **Flaky tests and shared test helpers:** the click-outside flake, one `TestUi` and clock, polling instead of sleeping. This makes every later change safe to verify.
3. **Shared code:** one UI-helper set in Chippi.UI, the Sound module core and view helper, formatters, id constants. Later fixes then land in one place.
4. **Idle and UI-thread performance:** the Keep awake and File organizer loops, the grid update in place, hub dedupe, settings write debouncing, alert cadence, then GPU and audio enumeration.
5. **Error handling and logging consistency:** logging Sound errors, one Undo path, save order, log dedupe, then moving off the static `Log`.
6. **CI and dependencies:** SHA pins, lock files, the ARM64 test leg, the xunit.v3 migration.
7. **Splitting large files:** opportunistically, when each is next touched.

## Verification
- [ ] `dotnet build -c Release` reports 0 warnings and 0 errors (TreatWarningsAsErrors)
- [ ] All tests pass 10 runs in a row, including `Chippi.Island.Tests`
- [ ] New tests cover Mark-of-the-Web on cross-volume moves and imports, FlyoutPlacement edges, TidyProjection and ForegroundTracker
- [ ] Idle check: with the island closed and nothing running, Chippi's CPU wakeups drop (ETW or Process Explorer context-switch delta), and none of Keep awake, the File organizer or System monitor wake on a fixed tick
- [ ] UiSmoke run on Travis's PC (with `CHIPPI_DEV_PROFILE`) after the UI-helper and grid changes
