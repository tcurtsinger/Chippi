# Volume boost gate: recommendation

> Copied from `X:\code\chippi-scratch\phase-3\boost-gate\findings.md` (Karl). "This folder" and the files it names (`offline/`, `inventory/`, the logs) are in that folder, which moves to the Phase 3 leftovers zip in `X:\code\chippi-archive` at the end of the phase.

**NO-GO for shipping per-app 100-200% Volume boost in Phase 3 on the evidence available.** Recommend asking Travis to defer it, keep App volume at 0-100%, and approve a separate backend experiment only if he wants to pursue it now.

The gain calculation works. A complete audio path that replaces an app's original playback, preserves other apps, works with Mic processing, and recovers safely has not been proved. This is a gate result, not a claim that Windows makes the feature impossible.

Date: 2026-10-25. Branch `phase-3/boost-gate`, worktree `X:\code\chippi-boost-gate`, base main `f1e477a`. No production changes or commits; the worktree remains clean. All spike code and evidence are in this folder.

## What was actually checked

- Read the approved brief, architecture, behavior spec, mockup boost page, earlier per-app routing/VB-CABLE spikes, and current audio service/pipeline code.
- Ran `offline/BoostMath.csproj`: **10 checks pass**, using generated sample arrays only. No WAV recording, sound playback, audio COM object, registry call or device mutation.
- Ran `inventory/Inventory.csproj`: endpoint/default getters only, through the existing `CoreAudioDeviceSystem`. Windows build 26300, native x64; 13 known endpoints, 7 active render, 2 active capture, no VB-CABLE identities. All six default-role values were identical before and after. No stream or session was opened/enumerated and no setter or registry API was called. See `inventory-results.log`.
- Consulted Microsoft's current API docs/sample source and VB-Audio's published page/manual. These establish API scope, not real transport performance. No driver/APO, compiler or other dependency was installed. No existing audio setting was changed.

## Established facts

**The regular mixer cannot supply this gain.** `ISimpleAudioVolume.SetMasterVolume` accepts 0 through 1 and rejects values outside that range. Chippi already clamps `SetAppVolumeAsync` to this range (`src/Chippi.Audio/AudioService.cs:150-153`); the native implementation forwards it to session volume (`CoreAudioDeviceSystem.cs:126`). Removing that clamp is not a boost backend. [Microsoft API contract](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-isimpleaudiovolume-setmastervolume).

**App capture is available without our own driver.** Microsoft's application-loopback sample captures a process and its descendants, independently of the physical endpoint, on build 20348 or later. It receives silence if that tree has no render stream. This gives a candidate input to DSP, not evidence that playback has been replaced. [Microsoft sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/).

**Exclude means exclude from the captured stream.** The process-loopback enum has include/exclude target-tree modes; neither defines suppression of the original render path. The sample activates `VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK`, initializes a loopback capture client and writes captured buffers to a file. It does not take over an app's output. [Enum contract](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ne-audioclientactivationparams-process_loopback_mode), [sample source](https://raw.githubusercontent.com/microsoft/Windows-classic-samples/main/Samples/ApplicationLoopback/cpp/LoopbackCapture.cpp).

**Loopback compatibility cannot be assumed universal.** The ordinary endpoint-loopback documentation specifies shared-mode use and describes protected-content restrictions. The exact behavior of every target-app stream with process-loopback still needs testing; these restrictions are not proof that every process-loopback case fails. [Microsoft loopback documentation](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording).

**An APO is another route, without a new virtual kernel transport.** Windows supports native user-mode effects in the audio engine. SFX runs per stream before the render mix, MFX per mode, and EFX after the endpoint mix. SFX is bypassed for raw streams; EFX includes raw streams but affects the endpoint's combined signal. Thus endpoint gain alone does not satisfy independent app sliders. Deployment/configuration and stream-to-app identification still need design and testing. [Microsoft APO architecture](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/audio-processing-object-architecture), [APO implementation](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/implementing-audio-processing-objects).

**New Windows 11 APIs do not by themselves supply the missing backend.** The APO framework exposes effect controls, notifications and property stores. Its AEC reference-loopback discussion distinguishes pre-volume and post-volume taps. That must not be generalized into a guarantee that ordinary per-process loopback still supplies useful samples after muting the source app. Mute-plus-replay remains an experiment, not a verified solution or a ruled-out possibility. [Windows 11 APO APIs](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/windows-11-apis-for-audio-processing-objects).

**VB-CABLE is transport, not independent app gain.** The vendor describes forwarding CABLE Input playback to CABLE Output recording; multiple apps can use the input. Its manual documents buffering and latency tuning. No fixed end-to-end delay can be inferred from a buffer-size setting. [Vendor page](https://vb-audio.com/Cable/), [reference manual, pp. 3 and 12-15](https://vb-audio.com/Cable/VBCABLE_ReferenceManual.pdf).

## Offline DSP results

These are calculations on normalized generated samples, not recordings or listening tests. Reproduce with `dotnet run --project offline/BoostMath.csproj -c Release`; complete output is `offline-results.log`.

| Case | Observed result | Implication |
|---|---|---|
| 1 kHz sine, peak 0.2, gain 2 | Peak 0.4; RMS ratio 2; +6.020600 dB | 200% can double amplitude when there is headroom; not twice perceived loudness |
| 1 kHz sine, peak 0.8, gain 2 | Peak 1.6; 54.17% of samples exceed unity | Unrestricted gain needs clipping/headroom management |
| Hard-clamp that boosted sine at unity | Harmonic ratio h2-h23: 17.3885% | Clamping is audibly risky DSP, not a transparent limiter |
| Stereo-linked block peak scaling, ceiling 0.98 | Peak 0.98; effective gain 1.225x; channel ratio preserved | Protection reduces requested boost on loud material |
| That reference on a steady sine | Harmonics below displayed precision | Does not establish transient handling or quality on music |
| Two individually in-range app signals combined | Aggregate peak 1.2 | Per-app limiting alone cannot guarantee headroom in the endpoint mix |
| Original plus a gain-2 copy, same phase | RMS becomes 3x original | Replaying a captured copy is not replacement |
| Original plus only an extra gain-1 copy, half-period delay | Near-total cancellation at that frequency | Adding only the extra gain is not a reliable latency workaround |

The reference limiter examines a whole 240-frame stereo block, 5 ms at 48 kHz, and scales the block together. It is deliberately **not production limiter code**: block transitions can click/pump; there is no true-peak oversampling, validated release envelope, real-time scheduling or hardware clock handling. Five milliseconds is its chosen analysis block, not measured playback latency. A limiter-off option cannot promise freedom from distortion.

No route prototype ran. Stream migration, output loss and recovery remain untested.

## Backend options and inference

| Route | Potential benefit | What remains unproved / product cost |
|---|---|---|
| Process-loopback + original-path mute/attenuation + replay | Candidate with no new driver or APO | Need to prove useful capture survives suppression, then isolation, synchronization, mute semantics and failover. No claim that muting works was established here |
| Dedicated virtual render bus + user-mode processing | Original app can render away from the physical speaker; app-specific loopback may retain isolation | Driver/endpoint choice, stream migration, licensing/package approval, x64/ARM64 runtime validation, crash recovery and added latency. Per-process isolation must still be measured |
| Native render SFX APO | In-place processing can avoid a second capture/render path | Per-app identity/control channel, deployment, raw/exclusive/offload coverage, coexistence with vendor effects, native ARM64 implementation. Not ordinary per-user app installation |
| Endpoint-wide gain or loudness enhancement | Simpler control of one device's combined output | Different feature: affects other apps and dynamics, so cannot silently replace the approved per-app boost |
| Target-app player/plugin gain | No interception needed when the app already controls its buffers | Only cooperating apps; not the general Windows feature in the mockup |

### Why the existing Mic processing cable cannot simply be reused

Chippi's Mic processing writes cleaned microphone audio into CABLE Input and exposes CABLE Output to conferencing apps (`MicPipelineHost.cs:123-135`). **Inference from that topology:** also routing boosted app playback into the same shared cable can put playback into the conferencing microphone, and capturing the cable's combined signal loses app separation. It can create feedback or privacy leaks. This was not tried on hardware. Use a distinct verified transport or demonstrate isolation; do not select the spare 16-channel endpoint contrary to the approved brief.

The earlier S4 spike found that setting a per-app preference does not necessarily migrate an already-open explicit-device stream. S4 also limits its undocumented policy to verified native x64 build 26300. A virtual-bus backend using that mechanism inherits both limitations; it cannot claim working ARM64 routing from the existence of an ARM64 cable driver. See `spikes\per-app-device.md` in the Phase 3 scratch folder and `IsVerifiedBuild` in `src/Chippi.Audio/PerAppDevicePolicy.cs`.

### Required proof before a GO

No row below is proven end to end by this read-only/offline spike:

1. Capture only the selected app across sessions/process trees, including browser/Electron restarts, while leaving an unselected app unchanged. Exclude Chippi's own replay from capture.
2. Suppress the original speaker path without destroying capture, or confirm actual migration to a dedicated sink. A successful preference write is insufficient.
3. Concurrent boosted apps, source mute/volume semantics and Mic processing coexistence without speaker audio entering the microphone cable.
4. Measured end-to-end latency, jitter/dropouts and A/V sync on representative USB, Bluetooth and display outputs, at 44.1/48 kHz and differing formats. Agree acceptable latency with Travis; this brief has no numeric acceptance threshold.
5. Transient/music quality, stereo image and true-peak/headroom behavior. Enforce safe bounds and show when the limiter reduces requested gain.
6. Output loss, unplug/replug, default changes, application restart, disable/uninstall and crash recovery. Restore only settings owned by Chippi; avoid duplicate playback, feedback or stranded silence.
7. Verified supported Windows builds and x64/ARM64 execution; explicit fallback when protected/exclusive/offloaded paths cannot be processed.

## Next action for Marge

Present Travis with deferral of Volume boost for Phase 3. Keep the existing placeholder and 0-100% mixer; no code or product decision was changed by this spike. If he prefers further work now, obtain approval for a disposable-system capture/suppression prototype first, with bounded synthetic streams, snapshots of affected settings and restoration. Driver/APO options must be approved before implementation. No request to install or change anything on Travis's PC is needed to accept this gate report.

Files: `offline/` (reproducible generated-sample math), `offline-results.log`, `inventory/` (read-only probe), `inventory-results.log`. The branch/worktree holds no production edits and no background process was left running.