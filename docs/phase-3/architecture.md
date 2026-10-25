# Phase 3 Sound architecture

All five Sound features run on one shared audio service with one engine thread, as Hopper does: the features are thin modules over it, and none of them touches Core Audio directly. The island moves from a stack of panels to tiles and pages that several modules can contribute to, and Mic processing's driver comes from a pinned, hash-checked copy of VB-Audio's setup that Chippi launches elevated only when the user presses Install Driver.

Status: design draft for Marge's brief, 2026-10-25. Written from `main` 48b3b42, Hopper d3e22d1 (`X:\code\hopper`), Karl's [behavior-spec.md](behavior-spec.md) and the mockup in `X:\code\chippi-scratch\phase-3-prep\mockup`. No feature code. Spike results are in spikes\ (`chippi-scratch\phase-3\spikes`); where this document depends on one, it says so.

## 1. Projects

| Project | What it holds | References |
|---|---|---|
| `src/Chippi.Audio` (new) | The audio service: device catalog, default-role control, sessions, endpoint volume and mute, meters, the mic pipeline and DSP, the transport, per-app device policy. No WPF, no module code. `net10.0-windows10.0.19041.0` | NAudio 2.2.1 (MIT, as Hopper), Chippi.Core (logging only) |
| `src/Features/Chippi.Features.Sound` (new) | The five `IFeatureModule`s (audio-routing, mic-processing, app-volume, mic-mute, volume-boost) and their Settings, flyout and island views | Chippi.Audio, Chippi.Core, Chippi.UI |
| `src/Chippi.Core` | Contract additions in §5 and §6: island tiles and pages, flyout sections, feature requirements | none new |
| `src/Chippi.App/Island` | Tile grid, page host, orbit buttons, Now Playing (shell-owned) | |
| `tests/Chippi.Audio.Tests`, `tests/Chippi.Features.Sound.Tests` (new) | Policy and state tests against a fake device system; no real devices | |

One Sound assembly instead of five keeps the shared service in one process-wide place and lets the shared Microphone and Output pages live next to the modules that fill them. Hopper's code moves into `Chippi.Audio` (relicensed GPL-3.0-or-later, as agreed), rewritten where Karl's spec marks it Changed.

Native code: `rnnoise.dll` per runtime under `runtimes\win-x64\native` and `runtimes\win-arm64\native`, loaded by the process architecture. Per the spike (`chippi-scratch\phase-3\spikes/rnnoise-arm64.md`), both are built from Hopper's MSVC-patched copy of xiph/rnnoise `9acc1e5` (BSD-3; upstream at that commit doesn't compile with MSVC), with `/MT` so an ARM64 PC needs no VC++ runtime. This PC lacks the ARM64 compiler (`Microsoft.VisualStudio.Component.VC.Tools.ARM64`), so the build goes in CI (`windows-latest` builds both, `windows-11-arm` test-loads the ARM64 DLL) unless Travis approves adding that component here. The DLLs are built once, committed with their hashes, and rebuilt only when the source changes.

## 2. The audio service

```
            Sound modules (UI thread)          Chippi.Audio
 ┌──────────────┐   commands (async)   ┌─────────────────────────────────────────┐
 │ AudioRouting │ ───────────────────▶ │ AudioService                            │
 │ MicProcessing│                      │  engine thread (MTA, one queue)         │
 │ AppVolume    │ ◀─────────────────── │   DeviceCatalog   RoleArbiter           │
 │ MicMute      │  AudioSnapshot +     │   SessionCatalog  EndpointControls      │
 │ VolumeBoost  │  events (UI thread)  │   MicPipelineHost PerAppDevicePolicy    │
 └──────────────┘                      └─────────────────────────────────────────┘
```

### Thread model
- **One engine thread** (MTA, `BlockingCollection<Action>` queue), exactly Hopper's `EngineLoop`. Every Core Audio call that enumerates, switches defaults, changes volume or mute, or starts and stops the pipeline runs there, one at a time.
- **Commands** are `Task`-returning methods that enqueue and complete when the engine finishes (`SetDefaultAsync`, `SetSessionVolumeAsync`, `MuteAllCapturesAsync`, `StartProcessingAsync`). Callers never block the UI thread.
- **Notifications** from Core Audio (`IMMNotificationClient`, session and endpoint-volume callbacks) arrive on COM threads. They only enqueue; they never touch state. Device-change bursts go through Hopper's 400 ms debounce, now on the engine thread (a timer that enqueues), not a WPF `DispatcherTimer`, so `Chippi.Audio` has no WPF.
- **State out:** after each operation the engine builds an immutable `AudioSnapshot` (endpoints with state and roles, sessions, mute, processing state, meters are separate) and posts `SnapshotChanged` to a `SynchronizationContext` the shell supplies (the UI thread). Views bind to the snapshot and never read COM objects.
- **DSP stays on audio callbacks** (capture `DataAvailable` and render pull), as in Hopper's `MicPipeline`. The engine only starts, stops and swaps pipelines.
- **Meters** poll at 50 ms on the engine thread only while a view that shows them is visible (subscription count), so an idle Chippi costs no polling.

### Lifetime
`AudioService` is a process-wide singleton owned by the Sound assembly, reference-counted by lease: each Sound module takes a lease in `StartAsync` and disposes it in `StopAsync`. The first lease starts the engine thread; the last one stops it after a graceful shutdown (§3 restore). Modules never dispose the service directly. This keeps "install only the features you use" true: with no Sound feature installed, no audio code runs.

### Components
| Component | Job | From Hopper |
|---|---|---|
| `DeviceCatalog` | Active and known endpoints for both flows, defaults per role, friendly names, device identity (PnP instance and driver for VB-CABLE recognition, Karl gap 3) | `AudioDeviceWatcher` |
| `RoleArbiter` | Who may set each default role (§3). The only caller of `IPolicyConfig::SetDefaultEndpoint` | `DefaultDeviceController` |
| `PriorityPolicy` | Pure: given a list, the active endpoints and an exclusion set, returns the winner. Unit tested with no COM | `PriorityResolver` |
| `SessionCatalog` | Render sessions grouped by app identity (exe path, else display name, else "System sounds"), volume, mute, peak; external changes reflected through session callbacks | new |
| `EndpointControls` | Endpoint volume and mute for render and capture endpoints, including mute-all for every active capture endpoint | new |
| `MicPipelineHost` | Owns at most one `MicPipeline`; rebuilds it when the winning real mic changes (Karl gap 1); writes the recovery marker (gap 2) | `MicPipeline`, `RenderDeviceTransport`, `Dsp\*` |
| `IMicTransport` | Where processed audio goes. `VbCableTransport` now; an own-driver transport can replace it later | `IMicTransport`, `VbCable` |
| `PerAppDevicePolicy` | Per-app output through `IAudioPolicyConfigFactory` (21H2+ IID `ab3d4648-…6324`, Set slot 25, Get slot 26). Reports `IsAvailable = false` when activation fails, a Get on a playing session fails, or the build isn't on the verified list; callers then hide the device picker and keep volume and mute. Reads back after every Set. Never calls slot 27 (it clears every app's setting) | new; spike per-app-device.md (`chippi-scratch\phase-3\spikes/per-app-device.md`) |
| `AudioFakes` (tests) | `IDeviceSystem` fake: endpoints appear, vanish, change roles and mute, so every policy is tested without touching real devices | new |

## 3. How the modules use it

### Role ownership
Two features want the default capture roles: Audio routing (pin the real mic) and Mic processing (point apps at CABLE Output while it runs). Hopper settles this inside one controller; in Chippi they are separate modules, so the service owns the rule:

- `RoleArbiter` keeps an owner per (flow, role): `Routing`, `Processing` or none.
- Audio routing claims render Console and Multimedia (and Communications when "Switch call audio too" is on), and the same three capture roles for the real mic.
- While Mic processing is active with "Make the cleaned mic the default" on, it takes the capture roles from routing. Routing keeps computing its winner, which becomes the processing **source**, not the default.
- When processing stops (switch off, failure, module stop, VB-CABLE removed), the capture roles go back to routing, which applies its winner at once. Without Audio routing installed, processing restores the role assignments it recorded before taking them.
- A role nobody owns is left to Windows.

### Per module
| Module | Uses | Owns |
|---|---|---|
| Audio routing | `DeviceCatalog`, `PriorityPolicy`, `RoleArbiter` | Two priority lists (seeded once), exclusions for explicit removals, the ID-repair rule from Karl's spec, "Output switched" and "Mic pinned again" notices |
| Mic processing | `MicPipelineHost`, `IMicTransport`, `RoleArbiter`, meters | Stage settings (live, saved after 500 ms), the source choice, the recovery marker, the "Stopped" notice, the Install Driver requirement (§6) |
| App volume | `SessionCatalog`, `EndpointControls`, `PerAppDevicePolicy` | Remembered output per app identity, reapplied when the app or device returns |
| Mic mute shortcut | `EndpointControls`, global hotkey (shell `RegisterHotKey` on a message window, Capability `InputHooks`) | The mute latch: applied to every active capture endpoint, re-applied to endpoints that appear, kept across pipeline restarts; the muted island activity |
| Volume boost | Nothing until its spike decides a backend | Placeholder module, not installable until then |

### Recovery (Karl gap 2)
Before Mic processing takes a capture role it writes `processing-owner.json` in its data folder: the roles it took and the device IDs it replaced. A normal stop restores them and deletes the file. At the next start, if the file exists and the recorded cable endpoint still holds a role, the service restores the recorded devices before anything else, then deletes the file. App uninstall (Velopack hook) and module uninstall run the same restore. A crash followed by no relaunch leaves the cable as default until Chippi runs again; that limit goes in the brief, not hidden.

### Mute and processing
The mute latch belongs to Mic mute shortcut, not the pipeline. A pipeline rebuild never unmutes: it starts the new capture already muted if the latch is set, and the CABLE Output capture endpoint is muted too, so apps hear nothing either way.

## 4. Settings, flyout and notices
- **Settings:** each module keeps its own page through `SetSettingsPage`, as Keep awake and File organizer do. Audio routing's page has the Output and Microphone tabs (a SelectorBar, as File organizer).
- **Flyout Sound tab:** one tab for all Sound modules (§5 flyout sections). Each module adds its section; the tab exists while at least one does.
- **Notices:** three new kinds on the app-wide Notifications page (Output switched, Mic pinned again, Stopped) through the existing `INotifier`, defaults as Karl lists them. "Mic pinned again" is raised only when the arbiter repairs a role someone else changed, never for its own write (the engine ignores the notification echo of a change it just made, matching on endpoint and role within 2 s).

## 5. Island: tiles and pages

### Contract (Chippi.Core)
`AddFlyoutPanel`'s `createIslandView` goes; the island gets its own registration:

```csharp
public interface IModuleUi
{
    // existing
    void AddFlyoutPanel(string name, string icon, Func<object> createView);
    void SetSettingsPage(Func<string?, object> createView);

    // new: one tab shared by several modules (the Sound tab), sections in module order
    void AddFlyoutSection(string tabKey, string tabName, string icon, int order, Func<object> createSection);

    // new: an island tile; modules that use the same key share one tile and one page
    IIslandTile AddIslandTile(IslandTileSpec spec);
}

public sealed record IslandTileSpec(
    string Key,              // "sound.microphone"; shared key = shared tile
    string Name, string Icon,
    int Order,               // grid order; Now Playing is always first
    double PageWidth,        // 328 default; mixer 344
    int SectionOrder,        // where this module's section sits on a shared page
    Func<object> CreateSection,
    IslandOrbit Orbit = IslandOrbit.None); // Left or Right round button

public interface IIslandTile { void SetStatus(string status); } // "Muted", "1 hr left"
```

- The tile's status comes from the module whose `SectionOrder` is lowest unless another module sets one (Mic mute's "Muted" wins over processing's "Live": the host takes the status from the highest-priority non-empty setter, priority = `SectionOrder`, and the mute module registers first). Marge can simplify this to "last writer wins" if she prefers.
- Tiles for the mockup: Now Playing (shell), Volume mixer (`sound.mixer`: App volume, plus Volume boost's 200% range when installed), Output (`sound.output`: Audio routing output list, App volume system volume), Microphone (`sound.microphone`: routing input, processing, mute), Keep awake, Downloads (File organizer).
- Keep awake and File organizer move from `createIslandView` to `AddIslandTile`. Their current island views become their pages unchanged.

### Host (Chippi.App/Island)
- `IslandPanelWindow` keeps its one-shape morph. Today it morphs tab → 328-wide island; it will morph between any two rectangles: tab → grid, grid → page, page → page. Each page reports its size (width from the spec, height measured), and `Place()` already re-centers on the tab.
- **Orbit buttons** are drawn inside the panel window, outside the shape, so `Covers()` and the outside-click hook (PR #29) treat them as part of the island. The window's transparent margin grows by 52 px (40 px button plus 12 px gap) on the sides that have buttons. Their positions follow the shape on every frame of the morph.
- **Left top:** back to grid (on the grid: back to the last page). **Left below:** the tile with `Orbit = Left` (Keep awake). **Right:** Settings, then `Orbit = Right` (Volume mixer). **Below:** the live activity's tile (music opens Now Playing, muted mic opens Microphone); this uses the existing `IslandActivity` with a new optional `TileKey`.
- **Side edges:** not designed. On Left and Right the buttons can't sit on the edge side. My proposal for Marge: put both columns on the open side, stacked (back, Keep awake, Settings, mixer), and the live button below. Needs Travis's approval before it's built.
- **Last page:** stored in `AppSettings.IslandLastPage` (tile key). Opening restores it if the tile still exists, else the grid.
- **Keyboard:** the grid is a 3-column focus scope (arrows move, Enter opens, Esc goes back to the grid, Esc on the grid closes). The island still never activates, so keys come through the existing low-level keyboard hook while open; Tab and arrows need the same treatment as Esc. That is the riskiest part of the island work: a hook that swallows Tab would break the app in front. Proposal: keys reach the island only after the user clicks into it (the click already gives us intent); until then only Esc is watched, as today.
- **Now Playing** is shell-owned (`Chippi.App/Island/Media`), using WinRT `GlobalSystemMediaTransportControlsSessionManager` (available at the app's 19041 target). It's always present, needs no feature install, and has no audio-service dependency.

## 6. VB-CABLE package and Install Driver

### Where it lives
- **Ship the zip unchanged.** VB-Audio's readme allows copying the package only "AS IS without any modification" (vbcable-arm64.md (`chippi-scratch\phase-3\spikes/vbcable-arm64.md`)), so Chippi ships `VBCABLE_Driver_Pack45.zip` whole (1,318,877 bytes, SHA-256 `B950E39F…16BFB`), never a trimmed set of files.
- **Pinned in the repo:** `build/vendor/vb-cable.json` holds the vendor URL, the zip's size and SHA-256, the SHA-256 of `VBCABLE_Setup_x64.exe` inside it (`734C35DF…4F41`), and the pinned Authenticode signer (BUREL VINCENT, EV). The zip is not committed.
- **Fetched at pack time:** `build/pack.ps1` downloads the zip, checks size and hash against the pin, and copies it into the package at `vendor\vb-cable\VBCABLE_Driver_Pack45.zip`. Any mismatch fails the build. CI does the same. Updating VB-CABLE is a reviewed change to the pin file.
- **Shipped:** Velopack carries it inside the app folder (`%LOCALAPPDATA%\ChippiApp\current\vendor\vb-cable`). About and THIRD-PARTY-NOTICES.md credit it as VB-Audio donationware from www.vb-cable.com, as the readme asks.
- **Extracted on demand:** Install Driver extracts the zip to `%LOCALAPPDATA%\Chippi\data\mic-processing\vb-cable\<zip hash>\` (all 31 files, since the setup reads its INF and drivers from its own folder), then checks the setup exe's hash and signature there before launching.
- **Architectures:** the driver supports ARM64 (its INF has an `NTARM64` section and an ARM64 `.sys`), but the pack has no ARM64 setup exe. Both x64 and ARM64 Chippi launch `VBCABLE_Setup_x64.exe`; on ARM64 it runs under emulation. Whether it then installs the ARM64 driver correctly is likely but untested, and needs an ARM64 machine before the brief claims it.
- **Licensing, open:** the readme says integrating the package into another installation procedure needs the author's agreement; VB-Audio's licensing page seems to allow bundling with a free app under the donationware terms, and requires paid licenses for company use. Launching setup from a button is closer to "integrate" than to "copy as is". Before Chippi ships with the zip, Travis should get written confirmation from VB-Audio. This is the one blocker on the bundling decision.

### Launch
1. The user presses **Install Driver** on Mic processing's Add features row.
2. Chippi extracts the zip (above) and re-hashes the setup exe against the pin and checks its Authenticode signature chains to the pinned VB-Audio signer. On mismatch: "Driver package damaged", no launch.
3. `Process.Start` with `UseShellExecute = true`, `Verb = "runas"`, working directory the package folder. Windows shows its admin prompt; Chippi itself stays a standard-user process.
4. `Win32Exception` with error 1223 means the prompt was cancelled: back to Install Driver, no message. Any other failure: "Couldn't start driver setup".
5. Chippi waits for the setup process without blocking (`WaitForExitAsync`). The exit code is logged, not trusted as proof: the row's state comes from the devices (below). VB-Audio's setup is its own window where the user presses its install button; no silent switches are documented, so Chippi passes none.
6. Only one setup at a time; the button shows busy while it runs.

### Row states
A new Core contract lets a feature's Add features row depend on something outside Chippi before it is installed:

```csharp
public interface IFeatureRequirement
{
    RequirementState State { get; }      // Missing, Installing, RestartNeeded, Ready, Unsupported
    event EventHandler? StateChanged;
    Task InstallAsync(CancellationToken ct); // Missing only
}
// FeatureDescriptor gains: Func<IFeatureRequirement>? Requirement
```

- The hub creates the requirement while the Add features page is visible and disposes it after, so nothing watches devices when nobody looks; a running Mic processing module holds its own instance.
- **Ready** means the verified CABLE Input render and CABLE Output capture pair is present and active (device identity, not names; Karl's change to Hopper's `IsInstalled`).
- **RestartNeeded** means setup exited and the pair isn't there yet. It is remembered in the module's data folder so it survives a Chippi restart, and cleared once the pair appears or after a reboot with no pair (back to Missing).
- **Ready** shows the normal switch; it never turns the feature on by itself.
- **VB-CABLE removed while running:** the module sees the pair vanish, stops processing, hands the capture roles back (§3), raises "Stopped", and the row returns to Install Driver. Disabled but present (not removed) is shown as Missing too, per Travis's wording, but Install Driver then opens Windows' Sound settings instead of running setup again. Marge should confirm that last part.

## 7. Workstreams this suggests
Marge owns the split; this is the shape the code suggests.

| Stream | Owns | Depends on |
|---|---|---|
| S0 Contracts | Core: `AddIslandTile`, `AddFlyoutSection`, `IFeatureRequirement`, `IslandActivity.TileKey`; Keep awake and File organizer moved to tiles | none, lands first |
| S1 Audio service | `Chippi.Audio`: engine, catalog, arbiter, policy, sessions, endpoint controls, fakes and tests | S0 not needed |
| S2 Routing | Audio routing module and views | S1 |
| S3 Processing | Pipeline port, RNNoise both arches, VB-CABLE requirement and package pin, recovery | S1, S0 |
| S4 App volume and mixer | App volume module, per-app device (behind the spike), mixer page | S1, S0 |
| S5 Mic mute | Hotkey, latch, island muted glyph | S1, S0 |
| S6 Island grid | Tile grid, page morph, orbit buttons, last page, Now Playing | S0 |
| S7 Smoke tests | UiSmoke for the Sound tab, Add features rows and the grid; no real device changes | after S2 to S6 |
| Gate | Volume boost backend | its spike and Travis |

## 8. Risks
- **Keyboard on a non-activating island** (§5): Tab and arrows through a global hook can break the app in front. Keep Esc-only until the user clicks into the island.
- **Per-app output device** is undocumented. The spike worked on build 26300 x64, but: the setting is per exe path, not per process; Get works only while the app plays audio; and an app's open stream does not move until the app reopens it. So the mixer's device picker takes effect on the app's next stream, and the Output page should show the chosen device, not claim the app already moved. A Windows update that reorders the interface under the same IID can't be detected at runtime, so the verified-build gate matters. ARM64 untested.
- **Default-role fights** with other software (Windows itself, vendor audio apps): the arbiter repairs drift as Hopper does, but two tools both enforcing defaults will loop. Log repeated repairs and back off after 3 in 10 s.
- **Testing without touching Travis's audio:** all policy is tested against `AudioFakes`. Real-device checks need his go-ahead and a restore step, per the Phase 3 rules.
- **Crash leaves the cable as default** until Chippi starts again (§3). Documented limit.
