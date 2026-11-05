# Saved app arrangements: brief

For Travis to confirm after Kayla's review (task 435). Snap's last step in Phase 6, separate from the wave 3 placement work. [Decisions](README.md), [Snap](snap.md) and [architecture](architecture.md). Everything below is proposed unless attributed to a settled decision. Lumen designs the screens after confirmation; DESIGN.md wins where it speaks.

## Job

Save the apps and window positions for a task, then launch that arrangement and return each window to its saved display and place.

The reference is [PowerToys Workspaces](https://learn.microsoft.com/en-us/windows/powertoys/workspaces), checked 2026-11-05. It captures a desktop, lets the person remove apps and edit launch arguments, and reports each app's launch result. Its documentation describes differing app behavior when reusing windows, a visible move after launch, and limits on elevated windows. Those are reference facts; the rules below are Chippi's proposal.

## Doors

- Settings > Windows > Window layouts gains an Arrangements section alongside zones. Arrangements belong to Snap, with no separate installed feature or home tile.
- Actions > Snap gains an Arrangements submenu containing saved names and Manage. Each saved arrangement is one shared action, also available in Search, the Radial menu and the action picker.
- Bind Launch {name} only in Settings > Shortcuts. The arrangement editor shows its binding read-only with Edit, using the existing shortcut service. No default binding.
- Launch works when Snap is installed. It obeys the shared Phase 6 fullscreen pause and Pause in these apps policy. A refused launch starts no apps and says why. Always on does not permit moving a fullscreen or elevated window.

## Capture and edit

1. Choose New arrangement. Arrange the apps normally, using Snap if useful, then choose Capture. Chippi remains usable while preparing the desktop.
2. Capture eligible top-level windows on the current virtual desktop, across connected displays, reusing the App switcher's window list through shared code rather than adding another enumerator. Exclude Chippi, desktop/taskbar, tool windows, dialogs, fullscreen and elevated windows. Show a count of skipped windows and their reasons.
3. Review before saving: name, a preview grouped by readable display names, and one row per window with the app's real icon, title, display and position. Remove excludes that row from the saved arrangement; it never closes the app. Cancel discards the draft.
4. Each row stores the same target value as the shared App action. Resolve it through that existing model, including registered packaged-app targets. Arguments and Working folder are offered only for executable path targets; Start menu app-list targets cannot accept them. Prefer a stable target over a versioned executable path. If no supported launch target can be resolved, the row cannot be saved until removed or given a valid desktop app target.
5. Save stores the selected rows and geometry. Edit supports rename, removing rows, launch details and Recapture. Recapture opens a replacement draft; Cancel keeps the saved arrangement. It requires review before Save and does not silently erase edited launch details for rows that can be matched unambiguously.

Name is required and unique ignoring case and surrounding spaces. At least one eligible row is required. Delete asks "Delete {name}?" with Cancel and Delete. Deleting removes its shared action; existing bindings become unavailable under the shortcut service's rules.

Capture stores app identity, window class and a title hint, normal bounds, maximized/minimized state, display identity, saved work area and DPI. Handles and process IDs are temporary launch-run data, never persistent identity. Titles and paths stay local and are not written to diagnostic logs. Do not capture process command lines automatically: they can contain secrets. Arguments are entered deliberately and saved locally with the arrangement.

Saving an arrangement saves window placement and launch instructions. Browser tabs, unsaved documents, app session state, Windows Snap groups and virtual desktop creation are outside this first build. An explicit file or URL in Arguments can reopen content if that app supports it. Capturing two windows of one app does not guarantee that app can reopen two distinct windows.

## Launch and window matching

Reuse existing eligible windows first, then launch missing rows. This is the proposed default, with no switch in the first build. Never close unrelated windows or apps to make the desktop match.

- Match exact app identity first, then window class and title hints. Assign each live window to at most one saved row. A unique match can move. When class and title hints still tie, choose the window whose current bounds are closest to the saved destination bounds, without asking (Marge, 2026-11-05). Compare the sum of squared differences of the four rectangle edges in physical desktop coordinates, after resolving the saved display and fitting the destination. Resolve exact ties by stable enumeration order within that run. Record these guessed matches in the run summary so Travis can correct placement with Snap. Reserve each chosen window before matching the next row.
- Existing windows on another virtual desktop are left alone. Mark the affected row Skipped with the reason instead of pulling it across or launching a likely duplicate.
- Launch each missing row once through the shared PC/action boundary. Existing `IActionPc.Open` accepts only a target. Add a shared `IActionPc` member for executable path, arguments and working folder, with structured fields, never a shell command assembled from the title. It must never request elevation or trigger a UAC prompt; targets requiring elevation fail without launching. Registered app-list targets remain argument-free, but their existing shell activation path needs a new shared no-elevation check. A Start menu app that needs admin must not show a prompt: mark its row Skipped with "Needs admin" and continue the rest of the run. Packaged activation and single-instance redirection must match the resulting eligible window, not assume the returned process owns it.
- Watch for a usable window until a bounded deadline, 60 seconds per row from its launch, fixed with no setting (Marge, 2026-11-05). A splash screen or dialog is not success. Do not keep moving a window after it is placed. A late window after timeout stays where the app opens it.
- If an app reuses a window already assigned to another row, mark that row Skipped with the reason. Retry is explicit and applies only to failed rows, so successful rows are not launched or moved again.
- One arrangement run at a time. A second invocation opens its progress view. Cancel stops pending launches and moves, removes watchers and leaves already opened apps in place. It never kills them or rolls back the desktop.

Revalidate window/process identity, eligibility, display availability and pause immediately before every move. A fullscreen window belonging to a row launched by this run marks only that row Skipped; it does not cancel the remaining rows. An unrelated fullscreen window that qualifies for the shared pause stops pending work while Always on is off. A session lock always stops pending work. Report remaining rows as cancelled; resuming requires Launch again. Classify ownership using run-tracked process/window identity, not the window title.

## Placement and limits

Restore each row to its saved display by stable monitor identity. Add a shared placement member accepting saved bounds and saved work area, even when the window is already on the destination display. `MoveToDisplay` only transfers a window from its current position and cannot restore an arrangement. Use `DisplayMove.Fit` to fit the saved rectangle into the destination work area: it preserves relative position and shrinks when necessary, never enlarges a window and does not scale by DPI. Record DPI for context, without promising DPI-based size preservation. Respect taskbars and app minimum/fixed sizes, and keep the window reachable when exact fit is impossible.

A missing display falls back to the invocation display: the Chippi surface's display for a panel or Settings click, or the display under the pointer for a shortcut or Search (Marge, 2026-11-05). Capture that display at invocation, before Chippi opens or focus changes. If it also disappears, use the current primary display. Report substitutions without rewriting the saved arrangement. A display disconnected during launch uses the same fallback after revalidation.

Normal windows arrive normal, maximized windows arrive maximized, and minimized windows are placed using their restored bounds then minimized again. A minimized-from-maximized row retains its maximized restore state. Recheck for fullscreen after any restoration, before moving, as the App switcher does. Fullscreen, elevated, denied and identity-changed windows are skipped with a reason. No elevation prompt or administrator launch option in this build.

Progress shows each app as Waiting, Opening, Placing, Ready, Skipped or Failed. Errors use "Launching failed" or "Placement failed" with a short reason and Retry where useful. A run summary retains guessed matches and display substitutions. Successful completion with no guesses or substitutions dismisses progress; partial completion remains available until dismissed. Dismiss hides progress; Cancel stops work. Launch may visibly open a window before moving it. No promise of atomic placement, exact stacking order or automatic rollback. Chippi does not repeatedly activate each app during placement.

## The page

Lumen settles layout and copy after approval. Required states: empty, saved arrangements, capture/review, edit, delete confirmation, launch progress, partial failure, guessed-match summary and missing display. No Choose window screen is needed. A saved row offers Launch, Edit and Delete. Use app icons, readable display names and bare-verb buttons. Avoid permanent explanatory lines where a control or refusal can carry the meaning.

## Tests

- Fixtures cover capture exclusions, duplicate names, persistence, missing launch targets, recapture/cancel and deleted actions.
- Matching fixtures cover duplicate apps/titles, reused handles, single-instance redirection, splash windows, ambiguous matches, other virtual desktops, timeouts, retry and cancellation with injected clocks.
- Geometry fixtures cover mixed DPI, negative origins, portrait, changed work areas, fixed/minimum sizes and monitor removal. Include minimized-from-maximized and minimized-fullscreen safety paths.
- Shared action and shortcut fixtures cover deleted arrangements, pause before launch and pause during a run, distinguishing a row opening fullscreen from an unrelated fullscreen window. No feature-owned hooks or hotkeys.
- Shared launch fixtures cover executable arguments and working folder, argument-free app-list activation, and refusing elevation without a UAC prompt, including a Start menu shortcut that requires admin: Skipped, "Needs admin", with the remaining rows continuing. Placement fixtures cover restoring saved bounds on the same display and fitting without DPI scaling or enlargement. Invocation fixtures cover pointer-display selection for shortcuts and Search.
- One final Sandbox run with networking and clipboard redirection off: capture multiple standard editor windows, close some, launch, verify reuse and relaunch, positions, timeout and Cancel. No Sound modules or real clipboard access.

## Done means

Run a dev profile with at least three arrangements and twelve windows across several apps, including repeated windows of one app. Capture dark/light screenshots of every required state next to Lumen's approved mockup. Measure cold/warm page opening and progress response, time from Launch to each eligible window's first placement, and total completion separately from app startup time. The page must open filled without waiting for launch discovery.

Travis's live-check file must give exact app setup, clicks and expected results. Include a cold launch after closing the captured apps, reuse without duplicates, two same-app windows, maximized and minimized windows, a missing app, cancellation, fullscreen pause with Always on off/on, an elevated refusal, mixed-DPI placement, and a missing display. Use disposable files and never close unsaved work for a test. Sandbox's single display cannot prove the real monitor cases.

## Not verified

This is a brief, with no implementation or runtime check. App launch discovery, packaged app matching, multiple windows of one app and virtual desktop detection need implementation probes before their behavior can be promised. The reuse-first rule, missing-display fallback and minimized-state preservation need Travis's confirmation along with Marge's nearest-window matching and fixed 60-second deadline. No PowerToys runtime was exercised; reference behavior above comes from Microsoft's documentation.

## Approval and dependencies

Kayla reviews this brief first. Travis then confirms its behavior before Lumen mocks the capture/editor/progress screens for approval. Engine work uses the shared action runner; shortcut registration stays in the shared service. Required shared changes are: move stable display IDs and readable names from `Chippi.App/Island/Displays.cs` into shared code usable by features; expose/reuse the App switcher's window list; extend `IActionPc` and its Windows implementation with no-elevation argument-aware path launching, a no-elevation check for Start menu/app-list activation (skip admin-required targets with "Needs admin" and continue), and saved-bounds placement using `DisplayMove.Fit`. These are new shared contracts, not existing feature APIs, and require shared-code review and tests. If a probe cannot support a promised behavior, return that specific limit for a decision before reducing scope. Phase 6 stays open until this step passes Travis's live check.
