# Chippi.Audio

The shared audio service behind every Sound feature (docs/phase-3/architecture.md §2 and §3).
No WPF and no module code. Ported from Hopper's engine, watcher and default-device controller.

## Use

```csharp
var service = new AudioService(() => new CoreAudioDeviceSystem(logger), SynchronizationContext.Current, logger);
await using var lease = service.Acquire();          // first lease starts the engine, last stops it
service.SnapshotChanged += (_, snapshot) => ...;    // on the context given above
await service.ClaimRolesAsync(RoleOwner.Routing, AudioFlow.Render, roles,
    active => PriorityPolicy.Resolve(list, active)); // runs on the engine thread at every reconcile
```

| Type | What it is |
|---|---|
| `AudioService` | One MTA engine thread and queue. Commands (`ClaimRolesAsync`, `ReleaseRolesAsync`, `RefreshAsync`, `SetEndpointVolumeAsync`, `SetEndpointMuteAsync` (returns failed IDs), `SetAppVolumeAsync`, `SetAppMuteAsync`, `InvokeAsync`) complete when the engine has run them. Events `SnapshotChanged`, `RolesChanged`, `MetersChanged` arrive on the context. `WatchMeters(endpoints, apps)` polls peaks every 50 ms until disposed. |
| `AudioSnapshot` | Immutable: endpoints (active, disabled, unplugged), defaults per `RoleKey`, owners, endpoint levels, apps. `Active(flow)`, `Known(flow)`, `Default(flow, role)`, `Owner(flow, role)`, `Find(id)`. |
| `PriorityPolicy` | Pure. `Resolve` (highest active listed, else null = leave Windows alone), `Seed` (default first), `AppendNew` (to the end, never a removed ID), `Repair` (stale ID re-matched only when exactly one endpoint has the name). |
| `RoleArbiter` (internal) | Owner per role: Processing over Routing, unclaimed roles left to Windows. The only caller of `SetDefault`. Reports each write as `Claim`, `Switch` or `Repair`; backs off a role for 60 s after 3 repairs in 10 s, then the engine reconciles when the back-off ends. |
| `SessionCatalog` (internal) | Render sessions on every active output, grouped by `AppKey` (exe path with Squirrel's `app-x.y.z` folder dropped, else display name, else system sounds). Chippi's own sessions are left out. |
| `IDeviceSystem` | The seam. `CoreAudioDeviceSystem` is the real one (NAudio and `IPolicyConfig`); tests use `FakeDeviceSystem` in `tests/Chippi.Audio.Tests/AudioFakes.cs`. |

Notifications: device and default changes are debounced 400 ms and then reconcile (re-read,
enforce claims, publish). Volume, mute and session changes only re-read levels and sessions.
Our own default writes echo back as a notification; the reconcile it causes finds nothing to do.

## Mic processing (`Chippi.Audio.Processing`)

| Type | What it is |
|---|---|
| `VbCable` | Recognizes VB-CABLE by `AudioEndpoint.Filter`, the KS filter pin its INF registers (`...\vbaudiovacwdm2022_out1_topo_.../00010001` for CABLE Input, `_in1_` for CABLE Output), never by names. `Find` gives Missing, Disabled, Ready (one pair on one device) or Ambiguous. `IsCable` matches any of its endpoints, CABLE In 16ch (pin `00010002` on the same filter) too. |
| `MicPipeline` | Hopper's chain: capture, mono, 48 kHz, input meter, RNNoise, gate, AGC, output meter, the transport's mix format. `RenderDeviceTransport` renders into CABLE Input. `Failed` fires when capture or render stops by itself. |
| `MicPipelineHost` (internal) | On the engine, every reconcile: stops on a missing driver or mic, rebuilds when the winning real mic changes, waits after a failure until its mic changes or returns. Takes all three capture roles for CABLE Output when asked, writing the marker first, and gives them back on stop. |
| `AudioService.SetProcessingAsync(request)` / `RecoverProcessingAsync(marker)` | On or off, and crash recovery: roles still on the marker's cable go back to what it replaced. `AudioSnapshot.Processing` says what actually runs; `MeterReadings.ProcessingInput`/`Output` carry the levels. |
| `RnnoiseNative` | Loads `runtimes\win-<arch>\native\rnnoise.dll` (from native/rnnoise) for the process architecture. |

Tests use `FakePipelines` from AudioFakes.cs: nothing captures, renders or plays.

## App volume

`AudioService.GetAppOutputAsync` performs a read-only capability probe for an app with a session;
`SetAppOutputAsync` applies one persisted output to each current process of that app. These run on
the engine through optional `IPerAppDeviceSystem`, with no default-role claim. Session snapshots
include process IDs for detecting app return; persistence remains keyed by `AppKey`, never PID.

`PerAppDevicePolicy` activates the undocumented factory on every Windows 11 build (22000 on), x64
and ARM64: the interface ID and slots Windows' own volume mixer uses, verified by the Phase 3 spike
on build 26300 x64. Activation asks for that interface ID, so a Windows that changed it fails before
any slot is called. Windows 10 keeps volume and mute but hides the output picker, and
`AudioService.AppOutputUnsupported` says why for Settings. Read probes must succeed before Set; each
write is read back. A value in a format Chippi doesn't know disables the instance at once; other
failures can be one app's (a process that just closed), so three in a row disable it, and a success
starts the count again. Each is logged. It never calls clear-all. An output choice affects the next
audio stream, not necessarily an existing stream. Tests use fake policy methods; no test changes real routing.