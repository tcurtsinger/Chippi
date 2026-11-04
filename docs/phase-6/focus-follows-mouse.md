# Focus follows mouse: brief

Draft for Travis's confirmation. Wave 1. Settings design needs confirmation. [Decisions](README.md); [reference evidence](behavior-spec-a.md#4-focus-follows-mouse).

## Job and surface

Give keyboard focus to the window under the pointer without clicking. Settings > Windows > Mouse owns the enable switch. Its shared toggle appears inside the grouped Actions switches menu and is available through Search, Radial menu and Shortcuts. No home tile.

## Behavior

Decided (Travis, task 226): off until switched on, even once installed. Use native Windows active-window tracking with 250 ms delay and raising enabled, as Marge specified. Do not build a competing activation engine or promise per-app exclusions and wait-for-stop behavior that native tracking cannot supply.

Capture the original tracking, raise and delay values before changing them. Restore them on disable, feature removal, orderly exit and fullscreen pause. Resume reapplies Chippi values without replacing the original snapshot. Failures appear as "Focus change failed" and in logs.

Proposed crash recovery: persist ownership and recover on next startup. Compare current values before restoring so a newer external change is not overwritten. Pause follows the window in front, on every display, as settled in the [decisions](README.md) and [shared pause architecture](architecture.md#5-the-pause-and-always-on). With Always on off, a fullscreen foreground app pauses native tracking globally; resume reapplies Chippi values. Native tracking is system-wide.

Always on defaults off. Pause on lock too. Installing the feature does not enable it automatically.

## Reference bar

Match Windows' tracking and raise behavior. Chippi adds a visible switch, shared fullscreen policy and original-preference restoration. Vorssaint's custom dwell and exclusion engine is reference evidence, outside this native-engine brief.

## Acceptance and owner check

Injected setting-access tests cover originals on/off, repeated pause/resume, external changes, partial failure and crash recovery.

In Windows Sandbox with clipboard redirection and networking off, use a dev profile without Sound. Open two Notepad windows, type "Left" and "Right" respectively. Enable Settings > Windows > Mouse > Focus follows mouse. Hover without clicking, then type a distinct letter after the dwell: it goes into the hovered window, which raises. Pass quickly over a third window and observe native behavior. Disable and confirm the original Windows settings return.

Check menus, dragging and modal dialogs, fullscreen pause with Always on off/on, exit and restart. Record original/final values, dwell-to-focus timings and screenshots. A second display needs an isolated environment that supports it. No runtime checks have run for this draft.

## Approval and dependencies

Uses Kayla's [architecture](architecture.md#7-per-feature) and [input-layer brief](input-layer.md) for shared pause and ownership policy. The global pause follows the settled shared rule above. Travis confirms the brief; the initial off state is decided. Settings rows need design confirmation before code.
