# Close protection: brief

Draft for Travis's confirmation. Wave 1. Bar feedback mockup required. [Decisions](README.md); [reference evidence](behavior-spec-a.md#3-quit-and-close-protection).

## Job and surface

Prevent accidental keyboard close. Settings > Windows > Keyboard owns Close protection and the per-app Ctrl+W list. Its shared toggle appears inside the grouped Actions switches menu and through the common action registry. No home tile or hold ring.

## Behavior

When enabled, protect Alt+F4 in every eligible app. Swallow the first press and show "Press Alt+F4 again to close" on the bar without taking focus. Pass the second distinct physical press to the same window. Chippi sends no keys; the app keeps its normal close and save flow.

Proposed interval: 600 ms, using an injected monotonic clock, matching the reference default. A held key's repeat never counts as a second press. Pending state belongs to window identity, process identity and chord. Reset on expiry, focus change, target destruction, lock, disable or shared pause. Handle down/up consistently so a swallowed press cannot leave keys logically held.

Ctrl+W is protected only for selected apps and uses "Press Ctrl+W again to close". Its pending state is independent. The app may close a tab or document.

Title-bar X, taskbar Close window, menus, shutdown, crashes and Task Manager are outside coverage. Desktop Alt+F4 passes through. Elevated targets pass through. Remote desktop and VM hosts need exclusions until forwarding is verified.

Decided (Travis, task 226): off until switched on, even once installed. Once enabled, Alt+F4 scope is every eligible app. Always on off pauses protection in fullscreen apps, letting native input through. No per-feature fullscreen override.

## Reference bar

Match Vorssaint's distinct-press recognition while preserving Windows' normal save prompts. Improve visibility with feedback on the existing bar. Never substitute termination for closing.

## Acceptance and owner check

Deterministic tests cover interval boundaries, repeats, key-up balancing, focus changes, reused handles, exclusions, pause and disabling between presses. Assert zero synthetic input.

In Windows Sandbox with clipboard redirection and networking off, use a dev profile without Sound. Open Notepad, type "Unsaved close check", enable protection and press Alt+F4 once: text stays and the bar shows the line. Release F4 and press again within the interval: exactly one save prompt appears. Click Cancel. Repeat after interval expiry, then while holding F4 to exercise repeat. Test opt-in Ctrl+W on a disposable browser tab, title-bar X and fullscreen pause.

Record feedback latency and bar screenshots. No input checks have run for this draft.

## Approval and dependencies

Shared hook, app identity, actions and pause belong to Kayla's architecture. Travis confirms the proposed interval with this brief; the initial off state is decided, and the bar mockup needs approval before code.
