# Phase 3 Sound behavior spec

Chippi should preserve Hopper's ordered default-device selection and fallback, then add the approved Sound controls on one shared audio service. Mic processing will initially use bundled VB-CABLE, installed only from an explicit Install Driver action in Add features.

Status: research/specification, 2026-10-25. No feature code, driver installation, audio capture, playback or audio-setting changes performed. Proposed behavior below is for Marge's brief, not approval to implement it.

## Sources and precedence

Hopper source inspected at d3e22d1e6bd7766860ca9f10447aa259e6ebd48e. Paths/line numbers below are relative to X:\code\hopper. README.md is stale about processing being future work; the implemented controller and pipeline are the behavioral evidence. PRODUCT.md describes set-and-forget routing.

Mockup: X:\code\chippi-scratch\phase-3-prep\mockup\README.md, published version 1792904964-1982. Read Main.dc.html, FlyoutSound.dc.html, Island.dc.html and IslandTabs.dc.html; inspected routing, processing and mixer renders. Sample device/app names, percentages, dates and levels are not requirements or migrated preferences. Later room decisions override the mockup's old Get VB-CABLE / Check again flow. Existing File organizer naming and terse-copy rule supersede older Downloads organizer/helper captions in the mockup.

## Hopper parity

Same means preserve behavior in the new module architecture. Changed means an intentional addition or correction to be recorded in the brief. Dropped means Hopper-specific shell behavior replaced by Chippi's shell.

| Behavior | Hopper evidence (file:line) | Chippi disposition |
|---|---|---|
| Separate persisted ordered output and input lists, endpoint ID plus display name | Config/AppConfig.cs:4,14,17 | Same; independent preferences |
| Highest active listed endpoint wins; comparison case-insensitive | Audio/PriorityResolver.cs:14 | Same; disconnected entries remain in the list |
| Fallback is the next available listed endpoint, not necessarily the former default | Audio/PriorityResolver.cs:17 | Same; reconnecting a higher-ranked device promotes it again |
| No listed endpoint active means no forced Windows change | Engine/HopperController.cs:440 | Same; do not invent an arbitrary fallback |
| Routing, switching and pipeline start/stop serialized on a background MTA thread | Engine/HopperController.cs:32,67,73 | Same design intent; immutable snapshots to UI, DSP still runs on audio callbacks |
| Add/remove/state/default changes trigger reconciliation; property changes ignored | Audio/AudioDeviceWatcher.cs:75 | Same triggers; changed: refresh labels/identity metadata on relevant property changes |
| Bluetooth event bursts restart a 400 ms debounce | Engine/HopperController.cs:53,345 | Same; coalesce rather than repeatedly switch |
| Startup reconciles without an output notice | Engine/HopperController.cs:124 | Same |
| Seed empty lists once, current multimedia default first; intentionally emptied lists remain empty | Engine/HopperController.cs:115,352; Config/AppConfig.cs:19 | Same; separate seeding state from list emptiness |
| New endpoints after initial seeding are not automatically appended | Engine/HopperController.cs:115,191 | Changed: approved mockup shows New devices / Add to the end. Proposed default: append previously unseen devices, retain removals as explicit exclusions so they do not return |
| Move up/down saves and immediately reconciles | Engine/HopperController.cs:154 | Same; mockup adds drag handles, retain keyboard ordering |
| Make preferred promotes an already listed item to the top; no one-off override | Engine/HopperController.cs:171 | Same; selection from quick UI must have explicit, consistent preferred-device semantics |
| Add appends if ID absent, Remove removes by ID | Engine/HopperController.cs:191,207 | Same; persist immediately, including explicit removal |
| Change all three roles: Console, Multimedia, Communications | Audio/DefaultDeviceController.cs:10,31 | Same by default. Changed: mockup offers independent Switch call audio too / Pin the call mic too; off leaves Communications untouched while Console/Multimedia follow the list |
| All-role drift is repaired even when multimedia default already matches | Audio/AudioDeviceWatcher.cs:72; Engine/HopperController.cs:446 | Same within the roles this module owns |
| Only visible multimedia output change emits AutoSwitched; mic and role-only repairs are silent | Engine/HopperController.cs:381,453 | Same quiet default. Changed: optional Mic pinned again notice exists in approved Notifications mockup, initially off |
| Repair stale IDs by equal friendly name, avoiding an endpoint already claimed by another entry | Engine/HopperController.cs:391 | Changed: preserve repair intent, but only auto-bind an unambiguous candidate. A duplicate name is not physical identity; keep unresolved entries for user selection rather than guess |
| Multimedia default name is UI state, not every role's state | Audio/AudioDeviceWatcher.cs:40; Engine/HopperController.cs:147 | Same; expose role divergence separately rather than claim every role follows |
| Meter reads OS endpoint peaks; endpoint vanishing returns zero and retries next poll | Audio/EndpointLevelMeter.cs:24 | Same, no capture required for system peak; distinguish unknown device from real silence in status |
| Persist processing master on successful start and resume it on next launch | Engine/HopperController.cs:127,293 | Same intent, after dependency/device readiness and mute/recovery checks; no false On on failed start |
| Real capture mic is top available non-cable input; if none listed, first active non-cable input | Engine/HopperController.cs:319 | Same fallback intent; changed: identify transport endpoints by verified device identity, not substring |
| CABLE Input is the render sink; CABLE Output is the app-facing capture endpoint | Processing/VbCable.cs:17,37; Processing/IMicTransport.cs:10 | Same; do not capture the sink's paired output and feed back into itself |
| CABLE In 16ch is deliberately not selected | Processing/VbCable.cs:12 | Same; detect the ordinary verified render/capture pair |
| Installed currently means only render endpoint present | Processing/VbCable.cs:52 | Changed: require usable render AND capture endpoints, native architecture and successful readiness check |
| AutoPinDefaultMic defaults true but can be turned off | Config/MicProcessingSettings.cs:32; Engine/HopperController.cs:423 | Same. On: route owned default roles to processed capture endpoint while active. Off: real-mic priority policy remains, apps must explicitly select CABLE Output |
| Disabling processing disposes pipeline then reconciles real default input | Engine/HopperController.cs:262 | Same with ownership-aware restoration and working real-device fallback |
| Graceful exit tries to restore the listed real mic, stops pipeline, waits at most 3 seconds | Engine/HopperController.cs:472,493 | Changed: reliable asynchronous shutdown, restore only roles still owned by processing, and exclude transport endpoints. Do not copy bounded teardown as proof cleanup finished |
| Start failure disposes pipeline, displays status | Engine/HopperController.cs:304 | Same; concise failure, clear actual running state and safe routing |
| Capture -> mono -> 48 kHz -> input meter -> RNNoise -> gate -> AGC -> output meter -> sink format | Processing/MicPipeline.cs:51 | Same DSP order; validate unsupported sink/channel formats explicitly |
| Input bridge has 500 ms headroom, trims excess above about 100 ms; render requests 50 ms | Processing/MicPipeline.cs:22,42,100; Processing/RenderDeviceTransport.cs:13 | Preserve bounded-buffer intent; these are buffer settings, NOT measured end-to-end latency |
| Suppression handles 480-sample frames and int16-range float scaling; disabled stage bypasses | Processing/Dsp/RnnoiseSuppressor.cs:21,48 | Same; native DLL loaded for process architecture, explicit failure if unavailable |
| Gate threshold default0.015 (~-36.5 dBFS), attack5 ms/release120 ms | Config/MicProcessingSettings.cs:20; Processing/Dsp/NoiseGate.cs:23 | Same default unless brief chooses mockup -50 dB sample instead; dB UI converts with amplitude = 10^(dB/20) |
| AGC target0.12 (~-18.4 dBFS), max gain10 (+20 dB), min gain0.3, silence floor0.005 | Config/MicProcessingSettings.cs:26; Processing/Dsp/AutoGain.cs:18,30,49 | Same; RMS150 ms, gain-down30 ms/up300 ms, final hard clamp. This is not proof of distortion-free limiting |
| Stage toggles/parameters take effect live; persistence debounced500 ms, meters poll50 ms | Config/MicProcessingSettings.cs:3; Ui/MicProcessingViewModel.cs:26 | Same semantics for Sound controls, distinct from explicit Save in File organizer rules |
| Meter taps use peak with0.85 decay per read block | Processing/Dsp/MeterTap.cs:14,25 | Same meter intent; real values, not mockup animated samples |
| JSON writes through temp-and-replace; corrupt load falls back to defaults | Config/ConfigStore.cs:20,47 | Changed: use Chippi config validation/recovery and module IDs, never read/write Hopper config without a separately approved migration |
| Hopper theme, tray, startup registration and vendor download action | Config/AppConfig.cs:29; Ui/MicProcessingViewModel.cs:75 | Dropped/replaced by Chippi shell and approved bundled-driver workflow; no Hopper fonts/theme port |

## Confirmed gaps and proposed corrections

### 1. Pipeline does not follow the winning real mic

Confirmed: MicPipeline.cs:37 binds a specific capture device once. Device events call ReconcileAll at HopperController.cs:62; that reconciles defaults at381, but does not replace the pipeline. ProcessingEnabled tests only _pipeline != null (217); neither MicPipeline nor RenderDeviceTransport subscribes to capture/playback stopped failures.

Proposed: resolve a real input independently of the virtual default on the engine thread. On an eligible winner change, stop/dispose the old pipeline and create a new one for that ID. If it cannot start, restore a working real input under owned roles, publish Stopped once, and show actual inactive state. Reappearance can retry if processing remains requested. Source-change restart must preserve the global mic-mute latch, never briefly unmute.

### 2. Unexpected death can strand defaults on the silent cable

Confirmed for crash/forced termination: restoration exists only in Dispose at472, not in the transport/driver. A clean exit attempts restoration; it is not correct to say every uninstall necessarily strands the mic, since an uninstall that causes clean disposal can restore it. RestoreDefaultInputToReal also assumes the saved priority winner is real; seeding/manual additions can include a cable.

Proposed: record pre-processing role assignments and a recovery marker before taking ownership, restore after normal stop/module removal/app uninstall, and recover on next launch if the recorded cable is still default. Exclude verified transport endpoints from real-mic resolution. If no listed real mic is available, use an available previous real endpoint or report no usable input rather than silently claim restoration succeeded. A separate user-mode watchdog could restore after a main-process crash, but that is a lead decision and cannot guarantee recovery after power loss, session shutdown or both processes dying. Document that limitation; do not promise crash-proof recovery from an in-process finally block.

### 3. Cable name heuristic rejects legitimate microphones

Confirmed: HopperController.cs:331 rejects any name containing CABLE or VB-Audio. A real mic called Cable Room Mic is excluded. VbCable.cs:25/45 further relies on editable endpoint names.

Proposed: identify the installed vendor device through driver/PnP identity and its paired endpoint IDs, with exact-ID exclusion of that pair from capture-source selection. Friendly names remain display/fallback hints, not proof. Require an explicit resolution if identity is ambiguous.

## Approved mockup additions and required behavior

Main.dc.html line references below refer to the offline html file. Most dropdowns are static drawn controls, not implemented policy. Only the visible selected option is established; alternate options need a lead call.

### Audio routing and mic processing surfaces

Main: Output/Microphone tabs at268-370, processing at375-449. Show priority, connected/unavailable/current state and add/reorder/remove/prefer. Off output auto-switch leaves Windows alone; off mic pinning stops enforcing its real-input roles. Default call-role switches are On, matching Hopper. New-device append and no-preference fallback controls need semantics as described above; never silently re-add an explicitly removed endpoint. Bluetooth stereo is the purpose of pinning a real mic, not a universal guarantee if an app explicitly opens the headset mic.

Mic processing shows actual source and input/output meters, per-stage switches, threshold, target and max-gain controls, source selection (Pinned mic in mockup), and optional default-cable routing. Mockup slider ranges (Main:851-853): gate -70 to -20 dB, AGC target -30 to -6 dB, max boost0 to30 dB. Its -50 dB gate is sample state; Hopper default differs. Preserve preference data when the feature is disabled. Do not turn missing-dependency detection into silent installation.

### App volume

Main:452-501; FlyoutSound.dc.html Apps section; Island.dc.html mixer. System endpoint volume/mute0-100%, plus open render sessions grouped by stable app identity, per-app volume/mute and output picker. Quiet-session visibility is optional; remember each app's output is a persisted preference, not a PID. Reflect changes made outside Chippi and remove closed sessions. Multiple sessions/processes, unavailable remembered outputs and unnamed/system sessions need explicit grouping/fallback rules in architecture. Proposed: device fallback uses Windows while retaining the preference, reapplied on app/device return. Per-app output remains a spike, not an established documented API. No driver needed solely for normal session volume/mute.

### Mic mute shortcut

Main:506-542; default drawn shortcut Ctrl+Shift+M, Change control, All microphones, optional tone. Use distinct mic-off glyph. Mute covers active capture endpoints including the cable, stays latched when a new endpoint appears, and updates from external device mute changes. Proposed toggle rule: mute all unless all are muted; if all muted, unmute all. Expose mixed/failed states rather than claiming all muted after a partial failure. Hotkey collision reports failure; do not steal or synthesize Windows Win+Alt+K. Tone is an optional runtime feature, never played during research/testing without permission.

Room decision overrides optional island-display switch: while muted the caution mic-off glyph is ALWAYS on the closed tab, after the highest activity glyph; muted-only counts as live. Do not let processing restart or device switch defeat the mute latch. Lead must decide whether disable/uninstall restores previous endpoint mute states; avoid silently unmuting an existing user mute.

### Volume boost

Main:545-578, sample boosted app list, add-app, max200%, limiter toggle. Island mixer has vertical faders, a100% line and caution above100%. Normal App volume stays0-100 until an approved boost backend exists. Boost installation alone must not change any application's level. Gain2x at200% is about+6 dB amplitude, not twice perceived loudness. A limiter reduces clipping risk but cannot promise unchanged dynamics. Research gate: app isolation, original-path suppression, latency, distortion, device disconnect and recovery must be proven before enabling above100%; otherwise defer with Travis approval.

### Flyout, island and notifications

One Sound tab (audio-lines glyph), showing only installed contributions. Output selector and system volume, real/processed mic status and mute action, processing switch, app levels and Settings entry all share state with Settings and island. Missing-dependency Set up links to Add features; obsolete Get VB-CABLE card is superseded. Muted header is caution; no stale On if processing failed.

Island: three-column grid with Now Playing plus installed-feature tiles, status text rather than shortcut text. Tile/page mapping in the mockup: Now Playing, Volume mixer, Output, Microphone, Keep awake and Downloads (use current File organizer naming for user-facing feature references). Audio routing + mic processing + mic mute contribute to one Microphone page, not duplicate mic tiles. First opening uses grid; later openings restore last page if still available, otherwise grid. This resolves the kickoff's 'opens on grid' vs 'reopens last page' ambiguity for the brief.

Island.dc.html:459 widths: grid328, Now Playing440, mixer344, other pages328. One contiguous shape morphs with page size. Round40px buttons: left grid/back and Keep awake; right Settings and mixer; below live music/muted mic. Tab hover remains swell-only, click opens, outside click/Esc close. Side-edge button geometry is UNAPPROVED and must be proposed before implementation. Outside-hit region must include orbit buttons and child popups. Keyboard Tab/Enter/Esc and reduced motion must remain usable.

Now Playing source selection, media transport, seek/progress and system volume use actual media session support, with disabled unavailable actions and an empty state. Keep awake/File organizer pages reuse current behavior, do not copy mockup Undo last move wording or static30-second grace into engine policy.

Notifications (Main:816-817): Audio routing Output switched and Mic pinned again; Mic processing Stopped. Drawn defaults: Output switched island-only; Mic pinned again off in both places; Stopped both. Preserve the existing app-wide per-kind choices. Re-pin should only announce real intervention if opted in, not our own callback echo.

## Install Driver state machine

Latest Travis decision,2026-10-25, supersedes mockup Main:375 missing-cable card and kickoff's 'never bundle/install'. Chippi stays a normal per-user app; only the vendor setup is elevated.

| State | Add features / Mic processing row | Transition |
|---|---|---|
| No usable VB-CABLE pair | Install Driver replaces install switch | Explicit click verifies pinned vendor package/hash and starts vendor setup with UAC; no automatic elevated action |
| Setup running | Busy state; prevent duplicate setup launches | UAC cancelled or installer fails: return Install Driver with concise error; never say installed |
| Setup succeeded, pair not ready | Restart to finish | Persist pending state; don't force reboot. Process exit alone is not success proof; inspect vendor result and device readiness |
| Verified pair ready | Normal install/remove switch, like other features | Device watcher refreshes automatically, including after reboot; driver presence itself does not activate Mic processing |
| Feature installed | Normal feature on/off controls in its surfaces | Explicit On starts processing only after readiness/source validation |
| Driver removed or pair lost | Stop processing, clear actual On, emit Stopped; Install Driver returns | Restore safe real-mic roles if possible, preserve settings, never reinstall automatically |

Do not treat a disabled/disconnected device as proof the software was uninstalled: distinguish dependency present-but-unavailable from truly missing where driver inventory permits. User wording requests Install Driver when unavailable; capability status must not cause repeated installs for an already installed but disabled cable. Lead should settle that row state.

Package redistribution must preserve vendor identity, attribution and donation/licensing information; see virtual-mic-routes.md. Driver removal is separate from removing the Chippi feature or app. Do not remove a shared driver owned/used by other applications.

## Acceptance cases for the brief

- List A>B>C: B active wins; connecting A wins; removing A falls back B; none active leaves Windows alone. Empty preference remains empty. Test every owned role and independent call-role Off.
- Same-name replacement is ambiguous without identity proof; no duplicate endpoint assignment. Explicit removal survives reconnect.
- Processing uses real input, renders to cable, optional default routing on/off, follows source changes, stops safely on missing device, and preserves mute through restart.
- Clean stop restores only owned roles. Crash recovery limitations are documented and exercised outside Travis's real setup with approval.
- Install/UAC cancellation/failure/restart-ready/removal are distinct states; dependency presence never auto-enables processing.
- Session grouping, external volume/mute synchronization, remembered missing output, hotkey conflict/new-mic mute and partial failures have tests.
- No audio/device-changing live check without prior permission and restoration. x64 and ARM64 build/native dependency checks precede runtime claims.

## Decisions still needed before feature code

Marge: confirm new-device/explicit-removal policy, role ownership between routing and processing, ambiguous ID repair UI, processing restart preference after failures, mic-unmute/disable semantics, source picker alternatives and no-device behavior, default gate value, last-page reopen semantics and side-edge round-button layout. These are specified or proposed above so omissions do not become accidental behavior. Volume boost remains gated; an own driver is research only.
