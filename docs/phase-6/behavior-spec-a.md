# Phase 6 behavior research, part A

Karl, 2026-11-04. Tasks 199 and 215. Research updated for the settled decisions. Briefs await confirmation.

## Settled decisions

[README.md](README.md) records owner decisions and Marge's lead calls. These supersede the earlier proposals preserved below as research history.

- Shared actions work from Actions, Search, Radial menu and Shortcuts. Actions groups related commands in menus. No new home tile.
- Always on defaults off and pauses every Phase 6 input feature on the affected fullscreen display, Debounce included. Radial menu has a separate fullscreen switch, on by default.
- Snap lives in Actions. Shift+drag shows zones; plain top-edge drag keeps Windows Snap. Saved app arrangements with relaunch are a separate step later in Phase 6. No mouse-button drag gestures.
- App switcher starts with a separate shortcut. Alt+Tab takeover is a later option after validation.
- Close protection uses two distinct Alt+F4 presses in every app when enabled. The first shows "Press again to close" over "Alt+F4 · Notepad" on the bar, or plays Windows' default beep when the bar can't show. No synthetic keys; a second physical press while the line shows (6 s) passes through. Ctrl+W is opt-in per app. Keys only.
- Focus follows mouse uses native Windows tracking, 250 ms and raise enabled, restoring the original settings on disable and pause. The earlier custom wait-for-stop and per-app filtering proposal is not promised by this native implementation.
- Smooth scrolling includes per-app speeds and exclusions in the first build.
- Shortcuts combines keys, Caps Lock combos and mouse buttons. These five features use that binding page.

The five briefs specify proposed build scope: [Focus](focus-follows-mouse.md), [Close protection](close-protection.md), [Scrolling](smooth-scrolling.md), [Snap](snap.md), [App switcher](app-switcher.md). Any earlier proposal below that conflicts with this section is superseded.
## Evidence and limits

Vorssaint source inspected at commit `3857e560d6e19b3170d432c2038c60b542810852`. Source was read for observable behavior only. No code, strings, graphics or assets are proposed for reuse. This is source/document research on Windows, with no macOS runtime available. No references were installed, no input hooks ran, no real windows were moved, and no clipboard or audio was accessed. Chippi source is unchanged. Numeric source defaults below are evidence, not approved Chippi defaults.

Pinned source links use this base:
https://github.com/vorssaint/vorssaint-utils/blob/3857e560d6e19b3170d432c2038c60b542810852/

## 1. Window layouts and snapping

### Verified Vorssaint behavior

WindowLayoutSupport.swift defines halves (including centered half), horizontal and vertical thirds and two-thirds, strips of quarters, corner quarters, six cells in a three-column/two-row grid, maximize, maximize with margin, center, previous/next display and restore. Its full-screen action is separate from maximize. These are actions on windows, not proof of a saved multi-app workspace launcher.

WindowLayoutSettings.swift exposes edge snapping with individually disabled zones, modifier gestures, a raise-window option, separate screen and window gaps, shortcuts, a directional trigger, ignored apps and cycling side placements through thirds. WindowGestureSupport.swift distinguishes a click from a drag after six points of movement and returns a held press if it never becomes a gesture. Do not transplant that physical threshold without DPI testing on Windows.

Sources: [actions and geometry](https://github.com/vorssaint/vorssaint-utils/blob/3857e560d6e19b3170d432c2038c60b542810852/Sources/Vorssaint/Services/WindowLayout/WindowLayoutSupport.swift), [settings](https://github.com/vorssaint/vorssaint-utils/blob/3857e560d6e19b3170d432c2038c60b542810852/Sources/Vorssaint/UI/Settings/WindowLayoutSettings.swift), [gesture contract](https://github.com/vorssaint/vorssaint-utils/blob/3857e560d6e19b3170d432c2038c60b542810852/Sources/Vorssaint/Services/WindowLayout/WindowGestureSupport.swift).

### Best Windows references and Chippi proposal

Windows Snap offers maximize-hover and Win+Z layouts tailored to screen size and orientation. FancyZones adds editable zones, a visible target highlight and Shift-drag placement. Workspaces captures an arrangement and launches applications into it. AltSnap adds modifier-drag movement and resizing from inside a window. Match those interaction qualities; distinguish single-window placement from launching a saved arrangement. [Windows Snap](https://learn.microsoft.com/windows/apps/desktop/modernize/apply-snap-layout-menu), [FancyZones](https://learn.microsoft.com/en-us/windows/powertoys/fancyzones), [Workspaces](https://learn.microsoft.com/en-us/windows/powertoys/workspaces), [AltSnap](https://github.com/RamonUnch/AltSnap/wiki).

Proposed full scope: built-in placements, custom named zones, shortcuts, edge targets, modifier move/resize, monitor transfer and restore. Keep saved multi-app launch arrangements as an explicit decision because it extends beyond the verified Vorssaint placement model. Chippi can improve the experience with the same layout picker available from its panel and Actions, readable monitor names, a preview before committing and Restore directly after a move.

Behavior requirements for the brief: use the monitor work area, preserve taskbar space, respect minimum sizes and fixed-size windows, and keep an accessible part of a window visible. Restore refers to the target window's earlier placement, never a different window with a reused handle. Define restore across repeated snaps, maximize and monitor disconnect. A disappearing target fails safely. Excluded apps retain native dragging and shortcuts. Do not silently disable Windows Snap or claim its shortcuts.

Earlier decision proposal (see settled decisions): Does Window layouts also include saved app arrangements and relaunch, or window placement only? Recommend the full placement system first, with arrangement launch separately identified in the phase plan so it cannot disappear through ambiguity. Choose the default gesture chord and whether edge snapping begins enabled after installation.

## 2. App switcher with previews

### Verified Vorssaint behavior

SwitcherSettings.swift offers window-preview, icon-row and simple layouts. Simple mode avoids screen capture. There are app/window/all-window shortcut roles, optional system-shortcut takeover, current-display and current-Space filters, minimized placement, fullscreen inclusion, preview exclusions and configurable overlay placement. Settings also expose appearance delay and instant selection.

AppSwitcher.swift handles arrow navigation, Enter to commit, Escape to cancel and typed search. With an empty unpinned query, W and Q are close/quit commands and S can pin search; once searching, letters go into the query. Repeated key-down does not repeatedly close windows. These macOS letter bindings are evidence, not a recommendation for Chippi. Cancellation and activation are distinct paths.

Sources: [settings](https://github.com/vorssaint/vorssaint-utils/blob/3857e560d6e19b3170d432c2038c60b542810852/Sources/Vorssaint/UI/Settings/SwitcherSettings.swift), [switcher behavior](https://github.com/vorssaint/vorssaint-utils/blob/3857e560d6e19b3170d432c2038c60b542810852/Sources/Vorssaint/Services/Switcher/AppSwitcher.swift).

### Best Windows references and Chippi proposal

Native Alt+Tab provides previews and fast modifier-release selection. Ctrl+Alt+Tab leaves the selector open. PowerToys Run's Window Walker supplies text search for running windows. Match native speed and keyboard predictability while adding search and display filtering. [Windows shortcuts](https://support.microsoft.com/en-gb/accessibility/windows/keyboard-shortcuts-in-windows), [Window Walker](https://learn.microsoft.com/en-us/windows/powertoys/run#window-walker).

Proposed behavior: recent-window ordering, one entry per eligible window, title plus app identity, minimized-window recovery, optional grouping by app, display filtering, keyboard and pointer selection, search and an icon-only fallback for unavailable previews. Provide a persistent invocation alongside hold-and-release switching. Escape changes nothing; Enter or modifier release activates only the selected live window. If it disappears, refresh selection without activating an unrelated window. Respect virtual-desktop boundaries unless an explicit all-desktops mode is approved and feasible.

Chippi can improve discoverability by sharing its app identity and search conventions, making the active display filter visible and offering preview privacy exclusions without hiding the corresponding window. No blank tile while waiting indefinitely for capture. Do not persist thumbnails to disk.

Earlier decision proposal (see settled decisions): opt-in replacement for Alt+Tab versus a separate shortcut? Recommend a separate configurable shortcut initially, with explicit takeover available after its recovery path is proven. Decide app grouping, minimized ordering and whether close belongs in the overlay. Recommend explicit close control with the shared close guard, no single-letter destructive commands competing with search. Native switching must remain available when Chippi is paused or exits.

## 3. Quit and close protection

### Verified Vorssaint behavior

Quit and close have separate enable switches and settings. Modes are hold, double press and extra modifier; scopes are all apps, selected apps and all except selected. Both protection switches default off. Hold defaults to 800 ms within 250 to 2,000 ms; double press defaults to 600 ms within 200 to 1,500 ms. Feedback is configurable. These guard Command-Q and Command-W, not every possible termination path. The support code explicitly considers remote-desktop and VM key forwarding, where closing the local application is not equivalent to forwarding a shortcut.

Sources: [settings](https://github.com/vorssaint/vorssaint-utils/blob/3857e560d6e19b3170d432c2038c60b542810852/Sources/Vorssaint/UI/Settings/QuitProtectionSettings.swift), [modes, timing and routing](https://github.com/vorssaint/vorssaint-utils/blob/3857e560d6e19b3170d432c2038c60b542810852/Sources/Vorssaint/Core/QuitProtectionSupport.swift).

### Windows baseline and Chippi proposal

Windows documents Alt+F4 as close-active-window, with a different desktop action when no app is active. Ctrl+W is application-specific tab/document closing. Windows has no universal Command-Q equivalent. Task Manager termination and application crashes are outside a keyboard guard's guarantee. The Windows reference to match is the app's own normal close/save flow, preserved after confirmation. [Windows shortcuts](https://support.microsoft.com/en-gb/accessibility/windows/keyboard-shortcuts-in-windows).

Propose independently configurable Alt+F4 and Ctrl+W guards, all three confirmation modes, app scopes and visible hold progress. Chippi can improve safety with selected-app setup, a way to try the shortcut in Settings, and feedback identifying the app and action. Never substitute process termination for a confirmed close. Never claim to preserve unsaved work against Task Manager, shutdown or crashes.

Pending confirmation belongs to one app/window/action. Cancel on focus change, pause, lock or disable. A second press must target the same window, and autorepeat is not a second press. Deliver the confirmed original action once and preserve app save prompts. Decide title-bar X and menu Exit separately: they are not automatically covered by shortcut interception.

Earlier decision proposals (see settled decisions): recommend selected-app scope by default, with Alt+F4 and Ctrl+W separately opt-in. Ask whether mouse X/menu Exit protection is required. Recommend documenting keyboard-only coverage for the first build rather than promising universal prevention. Remote desktop and VM apps need explicit handling and isolated verification.

## 4. Focus follows mouse

### Verified Vorssaint behavior

MouseSettings.swift defaults this feature off, with raise-window and wait-for-pointer-stop enabled. Delay defaults to 250 ms and is bounded from 100 to 3,000 ms. There is a shared app-exclusion check. The service does not evaluate while a mouse button or Command/Control/Option/Shift is held and checks again before acting. Its modal handling protects attached sheets and app-modal windows. The support state distinguishes dwell over a window from waiting for the pointer to stop.

Sources: [settings](https://github.com/vorssaint/vorssaint-utils/blob/3857e560d6e19b3170d432c2038c60b542810852/Sources/Vorssaint/UI/Settings/MouseSettings.swift), [timing state](https://github.com/vorssaint/vorssaint-utils/blob/3857e560d6e19b3170d432c2038c60b542810852/Sources/Vorssaint/Services/FocusFollowsMouse/FocusFollowsMouseSupport.swift), [activation safeguards](https://github.com/vorssaint/vorssaint-utils/blob/3857e560d6e19b3170d432c2038c60b542810852/Sources/Vorssaint/Services/FocusFollowsMouse/FocusFollowsMouseService.swift).

First-build scope: the [Focus follows mouse brief](focus-follows-mouse.md) uses native Windows tracking. Per-app exclusions and wait-for-pointer-stop behavior described below are research proposals, outside the first build.

### Windows baseline and Chippi proposal

Windows already supports active-window tracking, separate raise behavior and a delay through SystemParametersInfo. Match these choices. Chippi can improve control with per-app exclusions, the shared fullscreen pause and explicit wait-for-stop behavior. This is keyboard focus transfer, not simply scrolling an inactive window. [Windows tracking parameters](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow).

Propose off until enabled, adjustable delay, separate Raise and Wait for pointer to stop switches, and exclusions. A pending activation must be invalidated when the target disappears, a drag starts, a menu opens, the session locks or shared pause applies. Do not activate through the taskbar, Chippi overlays or a modal dialog. A failed foreground request must not lead to repeated focus attempts.

Earlier decision proposal (see settled decisions): recommend matching Vorssaint's 250 ms, raise and wait-for-stop defaults after opt-in, subject to a hands-on trial. Kayla must decide whether native tracking can meet exclusions and pause without permanently changing the owner's system preferences. If system settings are owned temporarily, the brief needs explicit restoration behavior.

## 5. Smooth and linear scrolling

### Verified Vorssaint behavior

These are separate toggles, both off by default. Smooth scrolling controls distance (20 to 100 pixels, default 40), response (0 to 100, default 65) and coast (0 to 100, default zero). Linear scrolling normalizes wheel distance independently of how fast the wheel is spun and can combine with smoothing. These units and defaults are macOS reference values, not Windows line counts.

SmoothScrollService.swift distinguishes wheel input from trackpad gesture/momentum, stops its glide for excluded targets, ignores its own synthetic events and treats Control-wheel separately. It accounts for continuous-reporting wheels rather than assuming continuous events always mean trackpad. Pointer acceleration, inversion and hold-to-scroll-sideways are adjacent features, not synonyms for linear scrolling.

Sources: [controls](https://github.com/vorssaint/vorssaint-utils/blob/3857e560d6e19b3170d432c2038c60b542810852/Sources/Vorssaint/UI/Settings/MouseSettings.swift), [distance and response](https://github.com/vorssaint/vorssaint-utils/blob/3857e560d6e19b3170d432c2038c60b542810852/Sources/Vorssaint/Services/SmoothScrollSupport.swift), [routing](https://github.com/vorssaint/vorssaint-utils/blob/3857e560d6e19b3170d432c2038c60b542810852/Sources/Vorssaint/Services/SmoothScrollService.swift).

### Best Windows reference and Chippi proposal

SmoothScroll for Windows offers animated wheel scrolling, acceleration, configurable animation and per-app speed/disable settings. Match smoothness and exceptions; Chippi can improve predictability with clearly separate distance and animation controls and a sample document beside the settings. Vendor claims do not prove compatibility in every Windows app. [SmoothScroll](https://www.smoothscroll.net/win/).

Propose independent Smooth and Linear controls, per-app exclusions, distance plus response/coast tuning, and precise-wheel support. Linear must mean equal physical notch distance regardless of spin speed. Preserve horizontal input, fine deltas and Ctrl-wheel zoom; avoid double-smoothing precision touchpads or apps already doing their own interpolation. Reversal should respond promptly without an old-direction tail. Never deliver queued motion to another window after switching or pausing.

Earlier decision proposal (see settled decisions): recommend opt-in toggles and the full tuning surface, with conservative coast initially. Decide whether per-app speed profiles are in the initial brief, in addition to exclusions; recommend yes to match the reference. Direction inversion and sideways modifier behavior need explicit phase ownership if requested, since PLAN names smooth and linear rather than every neighboring Vorssaint mouse tool.

## Checks to put in the feature briefs

These are proposed acceptance checks, not checks already run.

- Layouts: multiple mixed-DPI monitors, portrait work area, taskbar edges, every placement family, min-size and fixed-size apps, restore after repeated moves and disconnect, ignored apps, gesture click pass-through. Include named zones and hundreds of repeated placements in isolated fixtures, without CPU stress.
- Switcher: 30 to 50 windows across several apps and displays, long titles, unavailable previews, minimized/closed targets, app grouping, fast invocation/release, search cancellation and keyboard-only use. Measure cold/warm open and key-to-selection latency. No stale thumbnail from a closed target.
- Close guard: each mode and scope, repeat keys, focus change during confirmation, denied elevated targets, remote apps, save prompts and shared fullscreen pause. Verify exactly one confirmed action. Use Windows Sandbox with clipboard redirection and networking off for input injection.
- Focus: pass over windows quickly, dwell and stop modes, dragging, menus, modal save dialogs, exclusions and fullscreen pause. Measure actual dwell-to-activation, and prove disable leaves normal activation intact.
- Scroll: long document in browser, editor, Explorer and PDF reader, nested scroll regions, both axes, zoom, high-resolution wheel and trackpad. Compare equal notch counts at slow/fast rates. Measure first response, settling and reversal. Switch targets mid-glide and prove no leaked motion. Input probes run in Sandbox only.

## Handoff

Marge owns the consolidated decision list; this document creates no duplicate owner questions. Kayla owns feasibility, hook ownership, elevation and the shared fullscreen policy. Lumen owns surfaces and mockups. No implementation or runtime validation is claimed by this research.
