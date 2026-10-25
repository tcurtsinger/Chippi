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
| `RoleArbiter` (internal) | Owner per role: Processing over Routing, unclaimed roles left to Windows. The only caller of `SetDefault`. Reports each write as `Claim`, `Switch` or `Repair`; backs off a role for 60 s after 3 repairs in 10 s. |
| `SessionCatalog` (internal) | Render sessions on every active output, grouped by `AppKey` (exe path with Squirrel's `app-x.y.z` folder dropped, else display name, else system sounds). Chippi's own sessions are left out. |
| `IDeviceSystem` | The seam. `CoreAudioDeviceSystem` is the real one (NAudio and `IPolicyConfig`); tests use `FakeDeviceSystem` in `tests/Chippi.Audio.Tests/AudioFakes.cs`. |

Notifications: device and default changes are debounced 400 ms and then reconcile (re-read,
enforce claims, publish). Volume, mute and session changes only re-read levels and sessions.
Our own default writes echo back as a notification; the reconcile it causes finds nothing to do.
