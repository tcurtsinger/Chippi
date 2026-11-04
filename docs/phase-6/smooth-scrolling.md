# Smooth and linear scrolling: brief

Draft for Travis's confirmation. Wave 2. Settings design needs confirmation. [Decisions](README.md); [reference evidence](behavior-spec-a.md#5-smooth-and-linear-scrolling).

## Job and surface

Make wheel motion smooth and predictable, with speed suited to each app. Settings > Windows > Mouse has independent Smooth and Linear switches, distance/response/coast controls, per-app speeds and exclusions. Smooth's toggle appears in the grouped Actions switches menu; both toggles are shared actions.

## Behavior

Both start off (proposed). Smooth interpolates wheel movement. Linear removes speed-dependent acceleration so equal notch counts give equal distance at different spin rates. Either can run independently. Per-app speeds and exclusions ship in the first build.

Preserve horizontal input, fine deltas and Ctrl+wheel zoom. Avoid double animation of touchpad gestures. Small deltas alone cannot identify touchpads because precision wheels produce them too. Kayla's implementation must define and verify device classification.

Ignore Chippi's injected events. Cancel queued motion on target change, exclusion, reversal, pause, lock, disable or target destruction. Never deliver an old tail into another window. Preserve nested-pane targeting. Unsupported and elevated targets retain native input without duplicate delivery.

Always on off pauses on the qualifying fullscreen display and original input passes through. Feature removal leaves native scrolling and system preferences intact.

Proposed tuning starts with zero coast and system-relative distance. Vorssaint's 40 pixels and 65 response are macOS reference values, not approved Windows defaults. Calibrate units, limits and defaults in isolated Windows fixtures and report the selected values in the implementation PR. Do not promise identical pixels in apps with different wheel-message handling.

## Reference bar

Match SmoothScroll's animation, app speeds and exceptions. Improve predictability through separate distance and animation controls, explicit Linear behavior and a sample scroll region in Settings. Pointer acceleration, inversion and mouse-button drag gestures are outside this brief.

## Acceptance and owner check

Fake-clock checks cover conserved deltas, equal-notch slow/fast sequences, fine deltas, both axes, reversal, bursts, cancellation and injection filtering. Check persisted overrides and exclusion matching.

In Windows Sandbox with clipboard redirection and networking off, create a long document in PowerShell: 1..2000 | Set-Content "$env:TEMP\scroll-check.txt". Open it in Notepad and a browser. In a dev profile without Sound, toggle Smooth and Linear separately and together. Scroll equal detent counts slowly/quickly, reverse direction, use Ctrl+wheel and switch windows mid-glide. Apply an app speed override and exclude the other app.

Check nested panes, horizontal wheel and a touchpad where hardware permits. Record first-response, settling and reversal timings and screenshots. Verify no tail after fullscreen pause. State hardware limits. No runtime checks have run for this draft.

## Approval and dependencies

Uses Kayla's shared mouse hook, injection marker and pause policy. Travis confirms the brief and Settings design before code. Windows tuning is an explicit implementation acceptance requirement.
