# Close protection: brief

Confirmed by Travis (task 240). Wave 1. The bar line and Settings › Windows › Keyboard are approved in [wave-1.html](design/wave-1.html) (task 264). [Decisions](README.md); [reference evidence](behavior-spec-a.md#3-quit-and-close-protection).

## Job and surface

Prevent accidental keyboard close. Settings > Windows > Keyboard owns Close protection and the per-app Ctrl+W list. Its shared toggle appears inside the grouped Actions switches menu and through the common action registry. No home tile or hold ring.

## Behavior

When enabled, protect Alt+F4 in every eligible app. Swallow the first press and show "Press again to close" over "Alt+F4 · Notepad" (the app's name) on the bar without taking focus. Pass the second distinct physical press to the same window. Chippi sends no keys; the app keeps its normal close and save flow.

Interval (Travis, task 262): a second press counts for as long as the bar shows the line, 6 seconds, the bar's usual line time. This replaces the reference's 600 ms. Use an injected monotonic clock. A held key's repeat never counts as a second press. Pending state belongs to window identity, process identity and chord. Reset on expiry, focus change, target destruction, lock, disable or shared pause. The line folds whenever the pending press ends: the second press, expiry or any reset, so it never says "Press again" when a press would be swallowed. Handle down/up consistently so a swallowed press cannot leave keys logically held.

When the bar can't show (Travis, task 263): with the island off, or with Always on on while a fullscreen app fills the widget's display, the first press plays Windows' default beep (`MessageBeep(0)`, as Plain paste's failures do) instead. The press is still swallowed, and the second press counts for the same 6 seconds.

Ctrl+W is protected only for selected apps and uses "Press again to close" over "Ctrl+W · Chrome". Its pending state is independent. The app may close a tab or document.

Title-bar X, taskbar Close window, menus, shutdown, crashes and Task Manager are outside coverage. Desktop Alt+F4 passes through. Elevated targets pass through. Remote desktop and VM hosts need exclusions until forwarding is verified.

Decided (Travis, task 226): off until switched on, even once installed. Once enabled, Alt+F4 scope is every eligible app. Always on off pauses protection in fullscreen apps, letting native input through. No per-feature fullscreen override.

## Reference bar

Match Vorssaint's distinct-press recognition while preserving Windows' normal save prompts. Improve visibility with feedback on the existing bar. Never substitute termination for closing.

## Acceptance and owner check

Deterministic tests cover interval boundaries (a press at 5.99 s closes, at 6 s it's the first press again), the line folding on every reset, the beep when the bar can't show, repeats, key-up balancing, focus changes, reused handles, exclusions, pause and disabling between presses. Assert zero synthetic input.

In Windows Sandbox with clipboard redirection and networking off, use a dev profile without Sound. Open Notepad, type "Unsaved close check", enable protection and press Alt+F4 once: text stays and the bar shows the line. Release F4 and press again within 6 seconds: exactly one save prompt appears. Click Cancel. Repeat after the line folds (6 s): that press is a first press again. Then hold F4 to exercise repeat. Then turn the island off (warn first: this step plays a sound) and press Alt+F4 once: the default beep plays and the text stays; press again within 6 seconds: the save prompt appears. Test opt-in Ctrl+W on a disposable browser tab, title-bar X and fullscreen pause.

Record feedback latency and bar screenshots. No input checks have run for this draft.

## Approval and dependencies

Shared hook, app identity, actions and pause belong to Kayla's architecture. Decided: the brief (task 240), the 6-second interval (task 262), the beep (task 263), the bar line and the Keyboard page (task 264), and the initial off state (task 226). Its app list shows each app's real icon (task 264, see [notes](design/notes.md#app-icons)).
