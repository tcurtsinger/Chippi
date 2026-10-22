# Keep awake (WS4)

KeepAwakeModule registers the System feature, Essentials/Power user membership, Overlaps PowerToys badge, Tools tile, settings page and Status island activity. It acquires no execution request until the user starts a session.

Modes are indefinite, duration and next local occurrence of an end time. Duration expiration uses TimeProvider's monotonic timestamp; wall-clock changes cannot extend it. Until-time sessions use local wall time. A nonexistent daylight-saving time is rejected. Ambiguous times use the platform's local offset choice. Settings are retained after uninstall, but sessions always start off after launch or reinstall. Saving changed options during an active session restarts that session.

AwakeSession owns one dedicated thread. Every SetThreadExecutionState call, including release, runs there. Stop, uninstall, process exit and native failure release the request. Native failure ends the worker. The next user toggle waits for its cleanup and creates a fresh worker without reinstalling or automatically resuming. The interface IExecutionState and injected TimeProvider allow deterministic tests without Windows power changes.

## Build and run

Use the SDK selected by global.json, with C:\Program Files\dotnet on PATH.

```powershell
$env:CHIPPI_DEV_PROFILE = 'ws4'
dotnet build src\Chippi.App\Chippi.App.csproj -c Release -r win-x64
dotnet build src\Chippi.App\Chippi.App.csproj -c Release -r win-arm64
dotnet test tests\Chippi.Core.Tests -c Release
dotnet test tests\Chippi.Features.KeepAwake.Tests -c Release
```

The [WS4] slot only registers the module and its exit cleanup. The temporary debug host has been removed following review; --debug does not install, activate, or uninstall Keep awake. Historical screenshots below came from that removed host. Native verification now uses a separate console probe and never runs powercfg or requests elevation.

## Verification and gaps (2026-10-21)

- App Release win-x64 and win-arm64: zero warnings/errors. ARM64 is build-only on this x64 host.
- Core: 57 tests passed. Feature: 14 tests passed, covering validation, future local occurrence, both clocks, same-thread native calls, manual stop, timed success once, active disposal, concurrency and failure cleanup.
- Real native start/stop/uninstall completed without logged errors. Debug hosts closed, no startup entry is touched.
- Screenshots: X:\code\chippi\screens\ws4-dark.png and ws4-light.png. These render the real settings page in the temporary host, not the not-yet-merged WS2 shell.
- Powercfg output on, stopped and uninstalled is saved alongside the screenshots. Every command returned exit 1: administrator privileges required. Power-request visibility and release remain unverified externally until Travis approves that diagnostic. The app remains standard user.
- DESIGN.md leaves the module page layout and input choices unspecified. Reused the existing title/body/mono/badge styles, segmented mode control, switch and themed ComboBox. Duration choices: 15/30/60/90/120/240 minutes plus any persisted valid value (1..1440). End-time choices have minute precision. No new styles or packages.
- No auto-resume, deliberately. A manual start is always required after restart/reinstall.
- Timed activity uses the contract's source line for its end time. The Core activity contract has no countdown field; no Core changes made.
- Live keyboard/Narrator checks and integrated WS1 tile/WS2 navigation/WS3 moods remain integration checks. No claim of those checks here.

## Review risk

Two-way change, confined to the feature, its tests and its App composition slot. Runtime impact is Windows idle sleep/display prevention while the user enables it. No startup registration, telemetry, network or elevation is added.
