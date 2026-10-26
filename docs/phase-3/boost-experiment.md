# Volume boost experiment: route 1 (process loopback, quiet the original, replay with gain)

> Copied from `X:\code\chippi-scratch\phase-3\boost-experiment\findings.md` (Kayla). The raw files it names (`out/`, `host-meter.txt`, `boost.wsb`) are in that folder, which moves to the Phase 3 leftovers zip in `X:\code\chippi-archive` at the end of the phase. The code is on branch `spike/boost-loopback`.

**NO-GO for route 1 as tested** (Windows Sandbox, build 26100 x64, suppression by session mute or session volume 0). Both ways of quieting the app's own output also silenced its process-loopback capture, so there was nothing left to replay. Capture isolation and a clean gain replay both work, but they don't help without suppression.

Date 2026-10-25. Branch `spike/boost-loopback` (code in `build/boost-spike`), off main `1e6341c`. Run in Windows Sandbox (Windows 10.0.26100 x64, one render endpoint "Remote Audio"), synthetic tones only. Raw output: `out/results.txt`, `out/replay-stats.txt`; host meter `host-meter.txt`.

## Safety

- Before any tone started, the experiment muted the sandbox's own output and set it to 0 (`Endpoint 'Remote Audio' muted=True volume=0.00`, still so at the end).
- A read-only meter on Travis's PC sampled every render session's peak every 50 ms for 5 minutes. The sandbox's session (`WindowsSandboxRemoteSession`) peaked at **0.0000** for the whole run. No setting on his PC was read for writing or changed.
- The sandbox was closed afterwards (`wsb list` shows none).

## Results

| Proof item (boost-gate findings) | Result |
|---|---|
| 1. Only the chosen app is captured | **Pass.** A's tree: 440 Hz 0.149, 1000 Hz 0.0000. B's tree: 1000 Hz 0.149, 440 Hz 0.0000. During replay, A's capture stayed at 1.01x its level alone, so the replay (another process) didn't leak into it |
| 2. The original path can be quieted without killing the capture | **Fail.** A's session muted: capture 0.0000 at every frequency (RMS 0.0000) over 4 s. A's session at volume 0: the same. The capture kept delivering frames (191,520, no discontinuities), all silent. Process loopback taps after the app's own session volume and mute |
| 4. Latency and dropouts | Dropouts: **none** in 60 s (6,282 replay reads, 0 short reads, 0 missing frames, 0 clipped samples, 0 capture discontinuities in either stream). Gain: replay/A at 440 Hz = **1.99** for gain 2. Latency: **not measured reliably.** The marker detector found 119 markers in the replay against 60 in the source, and the paired delays spread from 3 to 497 ms (sd 189 ms), which points to a fault in my measurement, not a real figure. I didn't chase it because item 2 already decides the route |

Run with A unquieted (item 2 failed), so the replay played alongside the original, which would be the "original plus a copy" case in the gate findings: louder, not a boost.

## What this means

- On this build, muting or zeroing the app's session can't be the suppression step. Other suppression methods weren't tried. The known alternative is to send A's original somewhere nobody hears, a dedicated virtual render endpoint (route 2 in the gate findings), which needs a driver; VB-CABLE is ruled out because it carries Mic processing. Other routes remain untested, not disproved.
- The sandbox's only endpoint is Remote Audio, so I couldn't test route 2 here. It would also need Travis's approval for a driver.
- Not tested: browsers/Electron process trees, protected content, exclusive or offloaded streams, Bluetooth/USB outputs, ARM64.

## Recommendation

Defer Volume boost, keep App volume at 0 to 100%. If Travis still wants it, the next experiment is route 2 or an APO, and both need a driver decision from him first.
