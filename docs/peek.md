# Peek

Approved 2026-11-11. Peek is a shell setting under **Island & pet**, not another gallery tile. It opens Chippi on a temporary destination through an editable global shortcut.

## Behavior

- **Shortcut:** unassigned initially. Read-only keys and Edit in Peek settings open the central Shortcuts editor at owner `peek`, name `Peek`.
- **Default panel:** Home, Now Playing, Audio, or an installed feature page, including shared Capture and hidden result pages. Home is the default. A removed concrete page falls back to Home; its saved choice remains so reinstalling restores it. Audio's generic alias resolves an installed Audio view.
- **Hold:** open while the shortcut is down; release the trigger key or its required modifiers to collapse. Releasing one Ctrl while the other remains down keeps the chord held. A release that reaches the UI before its press cannot leave Peek stuck open.
- If an owned file picker or dialog is open, the collapse waits for its last open-hold lease to end, keeping that dialog usable. Opening a fresh Peek visit cancels an older pending collapse.
- **Toggle:** press once to open, again to collapse. Click-away leaves it open. Esc and the puck can close it. Typing-page selection does not automatically take the foreground; a deliberate click can.
- Peek uses the widget's screen, including over fullscreen apps, and respects Island off. Locking or turning off the display closes it. The ordinary puck destination and remembered Audio view remain unchanged by Peek navigation.
- Navigation to a subpage stays inside the Peek visit. Rebinding/removing the shortcut, changing Peek's mode/target, disabling the island, or stopping the host ends the visit.

## Direction contract

**THESIS:** One shortcut makes the owner's chosen panel available for a glance, with predictable release or toggle dismissal.

**OWN-WORLD:** Existing native Settings cards, Segoe hierarchy, neutral dark/light brushes, the house ComboBox, segmented choice, and read-only shortcut row.

**STORY:** Choose the destination, edit the shortcut centrally, choose Hold or Toggle, then use it from the current app.

**FIRST VIEWPORT:** A Peek section follows the island's opening/display settings. Three rows contain Shortcut/Edit, Default panel, and Mode, with a concise mode hint and inline failure/fallback feedback.

**FORM:** Local extension of Island & pet, using its existing components. The accepted written plan is the reference; no new visual world or concept roll.

**SIGNATURE:** The existing widget expands into the chosen panel without changing its ordinary destination or taking the keyboard before interaction.

## Implementation and checks

- `PeekController` owns the central shortcut slot and observes Hold releases through an `IInputHub` filter. Toggle adds no observer. Nothing hooks input for Peek until a Hold shortcut is assigned successfully. No feature-owned hook or input injection is added.
- Key-repeat suppression comes from the existing `HotkeyWindow` registration. Release generations reject stale callbacks; reset/rebind/shutdown remove ownership. A failed Hold observer refuses the assignment rather than holding a shortcut that cannot close safely.
- `IslandWindow` handles the temporary/pinned visit and fullscreen visibility; `IslandPanelWindow` suppresses remembered navigation and automatic keyboard acquisition during it.
- Windowless controller fixtures cover hold/release, short taps, stale release, modifiers/reset, toggle, config changes, rebind/refusal, failed saves and observer failure. Settings fixtures cover choices, central Edit, persistence, removed targets and failure recovery. Dark/light settings render at 800 and 460 DIPs with `CHIPPI_ISLAND_SHOTS`.
- CI-only `PeekWindowTests` exercise native panel routing, click-away/Esc, remembered normal/Audio destinations, removed pages, fullscreen/passive Search, modal holds and a queued old click-away. These tests show windows and are not run locally while the owner is at the PC.

Completion evidence, 2026-11-11 (`b2da53b`): local checks passed 9 controller, 6 Peek settings, 4 Island & pet page and 188 Core tests. Release application builds passed for x64 and ARM64 with no warnings or errors. Four native offscreen Settings renders cover dark/light at 800 and 460 DIPs; the three-row section retains the existing Settings cards, ComboBox, segmented control and read-only shortcut component, so root `DESIGN.md` and the global sidecar are unchanged. Review resolved subpage dismissal, concrete Audio fallback and transactional recovery after release-tracking failure; the final modal deferred-close and queued-click review found no material issues. The 8 native window tests remain CI-only; this evidence establishes no live fullscreen, hardware, DWM timing or Sandbox result.

## Owner check

1. In Settings > Island & pet > Peek, choose System (or another installed page). Select Edit and record an unused chord in Shortcuts; Save.
2. Choose Hold. From another app, hold that chord: Chippi should show the chosen page without taking focus. Release: it collapses. Open the puck normally: its previous destination remains.
3. Choose Toggle. Press once, click another app, and confirm Peek stays open. Press again to close. Repeat and close with Esc.
4. Navigate to a subpage during Peek; it remains a Peek visit. Try while a fullscreen app is in front and check that the app stays fullscreen.

Live key delivery, fullscreen/game behavior, DWM frame timing, Windows Sandbox and native ARM64 execution remain owner/isolated-session checks. Offscreen fixtures do not establish those results.
