# Snap: brief

Draft for Travis's confirmation. Wave 3. Actions mockup is Lumen's task 212; zone/editor mockups follow. [Decisions](README.md); [reference evidence](behavior-spec-a.md#1-window-layouts-and-snapping).

Decided (Travis, task 226): Shift+drag zones are off until switched on, even once installed. The Snap menu works as soon as Snap is installed.

## Job and surface

Place the current window without hunting for edges. Snap is a menu inside Actions: Left, Right, Maximize, Center, Next monitor, Restore and named zones. No new home tile. Settings > Windows > Window layouts owns zone editing, gaps and exclusions. Shortcuts owns bindings.

Each placement is a shared action available to Search, Radial menu, keys, Caps Lock combos and mouse buttons. Actions groups the commands rather than filling its gallery with loose placements.

## Behavior

Include halves, thirds, two-thirds, quarters/corners and custom named zones. Common commands appear in the Snap menu; other placements remain discoverable in the action picker and named layouts. The approved mockup settles menu hierarchy.

Capture the intended app window before opening Chippi. Never move the panel or an unrelated window that gains focus during selection. Revalidate window/process identity at execution. Missing or denied targets fail safely with "Snap failed".

Use monitor work area and DPI, preserving taskbar space. Respect minimum and fixed sizes, negative coordinates and portrait displays. Keep a window reachable when exact fit is impossible. Next monitor wraps in stable display order. Restore returns the same window's pre-snap placement, including maximized state. Proposed restore history is one baseline across repeated snaps, replaced after a manual move. Revalidate after monitor removal.

Shift+drag shows zones and highlights the destination; release commits only a valid zone. Escape or releasing Shift cancels Chippi placement while preserving native drag. Plain top-edge drag keeps Windows Snap. No mouse-button drag gestures or changes to Windows Snap preferences. Excluded apps retain native dragging.

Always on off suppresses Phase 6 gestures and actions on qualifying fullscreen targets/displays. Elevated windows cannot be moved.

Saved multi-app arrangements with relaunch remain a separate step later in Phase 6. They need a separate brief; this Snap PR cannot claim that step is complete.

## Reference bar

Match FancyZones' editable zones and visible Shift+drag targets, Windows Snap's work-area handling and Vorssaint's placement families. Improve access through the shared action model, readable monitor names and Restore.

## Acceptance and owner check

Geometry fixtures cover mixed DPI, negative origins, portrait, taskbar edges, fixed/minimum sizes and monitor disconnect. Restore tests include repeated snaps, manual moves, maximize and reused handles.

In Windows Sandbox with clipboard redirection and networking off, use a dev profile without Sound. Open two Notepad windows with distinct text. Use Actions > Snap > Left, Right, Next monitor and Restore on one: the other never moves. In Settings create a zone named "Reading". Shift+drag into it and release; repeat cancelling with Escape and releasing Shift away from a zone. Plain top-edge drag still offers Windows Snap.

Record environments where Sandbox cannot supply mixed DPI or multiple monitors; require suitable isolated-desktop coverage before acceptance. Capture dark/light menu and zone screenshots at real panel size, cold/warm opening and DWM placement timings. No runtime checks have run for this draft.

## Approval and dependencies

Uses shared actions, original-window context, identity, pause and Shortcuts. Travis confirms restore behavior through the brief and approves Actions, zone and editor mockups before code.
