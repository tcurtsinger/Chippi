# App switcher: brief

Draft for Travis's confirmation. Wave 3. Overlay mockups follow. [Decisions](README.md); [reference evidence](behavior-spec-a.md#2-app-switcher-with-previews).

Decided (Travis, task 226): the App switcher's tile and Search entry work as soon as it is installed. Its shortcut is unassigned until you set one.

## Job and invocation

Find and activate an open window quickly. One App switcher tile in Actions opens its overlay. Settings > Windows > App switcher owns presentation and filters. No home tile.

Open App switcher is one shared action for Search, Radial menu and Shortcuts. Start with a separate configurable shortcut. Proposed default: unassigned until chosen in Settings > Shortcuts. Alt+Tab takeover is a later option after cancellation, pause and exit recovery prove reliable.

Tile, Search and mouse invocation stay open until selection or dismissal. Modifier-based invocation may support hold/release switching when the binding explicitly expresses it. Ordinary commands must not commit merely because the invoking keys were released.

## Selection and previews

Proposed first-build scope: one entry per eligible window, recent-activation order, icon, app and title, minimized windows included. Selecting a minimized window restores it. Search matches title and app. Offer all-displays/current-display filtering, with visible state. Current-display follows the captured invocation context.

Arrows navigate, Enter or click commits, Escape cancels. Letters enter search, including W and Q. Proposed first build has no overlay close button or destructive letter shortcuts.

Cancel preserves the original active window. Commit validates window/process identity and activates only that target. Remove vanished selections without activating the replacement. Foreground refusal shows "Switch failed"; do not retry in a focus-stealing loop.

Use previews when supported, icon/title fallback otherwise. Preview-excluded apps remain searchable. Never persist thumbnails or show stale pixels from closed/reused targets. Prioritize visible candidates so 50 windows do not block opening on capture.

Proposed scope is the current virtual desktop. App grouping and all-desktops mode need a later decision. Skip Chippi utility and non-switchable helper windows.

Always on off pauses invocation on qualifying fullscreen displays. Cancel on lock or shared pause. Native Alt+Tab remains available. Elevated-window activation and preview restrictions need direct verification, separate from hook restrictions.

## Reference bar

Match native Alt+Tab's speed and cancellation, and Window Walker's search. Improve discoverability with a persistent action, visible display filtering and privacy exclusions that retain searchable windows. Vorssaint's simple capture-free mode informs fallback.

## Acceptance and owner check

Fixtures cover 50 windows, long/duplicate titles, minimized windows, missing icons/previews, target closure, reused handles, filters, search, rapid invoke/release and foreground refusal.

In Windows Sandbox with clipboard redirection and networking off, use a dev profile without Sound. Open several Notepad windows and type distinct title lines. Minimize one. Open Actions > App switcher, search it, press Enter and confirm restoration. Reopen, change selection, press Escape: original focus stays. Destroy the selected fixture from a controlled second process and verify no unrelated activation. Test the chosen shortcut, click selection and native Alt+Tab.

Use 30-to-50-window fixtures for cold/warm opening and key-to-highlight timings, with missing previews and long titles. Capture dark/light overlays and reduced motion. Record multi-display and virtual-desktop checks unavailable in the isolated environment. No runtime checks have run for this draft.

## Approval and dependencies

Uses Kayla's shared action context, app identity, shortcuts and pause. Travis confirms proposed binding, window scope and interaction through this brief and approves overlay mockups before code.
