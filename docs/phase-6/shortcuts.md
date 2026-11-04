# Shortcuts: brief

For Travis to confirm. Mouse buttons, Super key and Shortcut manager are one feature (tasks 210 and 211 #3), with one page, Settings › Shortcuts, which grows the existing Chippi › Shortcuts page. Research: [behavior-spec-b.md](behavior-spec-b.md) sections 1, 3 and 5. Shared hub, actions, shortcut service and pause: `input-layer.md` (Kayla). Mockup: Lumen, task 212. DESIGN.md wins where it speaks.

## Job

Bind any action to a key, a Caps Lock combo or a mouse button, in one place, and see every clash.

## Doors

Each door runs one shared action (task 208): any verb the Radial menu or Actions has, plus Phase 6's (Snap, Insert a snippet, the switches).

- **Key.** A global shortcut through `RegisterHotKey` (the shortcut service). Works over admin windows. Windows-reserved combinations are refused up front (any Win combination, Ctrl+Alt+Del, Alt+Tab, Alt+F4).
- **Caps Lock combo (the Super key).** Hold Caps Lock, press a key: the bound action runs and the key is swallowed. Caps Lock itself never toggles while this is on (the hub swallows its down, up and repeats).
- **Mouse button.** Middle, Back, Forward, side wheel left and right (one press per notch). Left, right and wheel up and down are never offered. A button with a binding no longer does its old job. Buttons 6 and up aren't visible to a Windows hook, so they're out.

## The Caps Lock combo, in detail

- **Held modifiers (vorssaint's Super key).** Always on while the Caps Lock door is on. With Caps Lock held, any key with no binding passes through as if the held modifiers were down. The setting "Hold modifiers" only chooses which ones (Ctrl, Alt, Shift; default Ctrl+Alt+Shift). A key with a binding runs its action instead and is swallowed. If every modifier is turned off, an unbound key passes unchanged. They are sent only when a key goes down during the hold, and released with the key. A solo tap sends none, so Win and Alt can't wake Start or a menu bar; if modifiers go down with no key between, the hub masks with its unassigned key.
- **Not offered:** Right Alt (AltGr sends Ctrl+Alt on many layouts and breaks `@`). **Win is off by default** and carries a warning: Ctrl+Alt+Shift+Win with most keys opens Office. Both from Travis's answer (task 211 #10).
- **A tap on its own** ("What a quick tap does"): Nothing (default), Turn capitals on and off (sends a tagged Caps Lock press), Press Escape, Switch input language (Win+Space). A hold longer than 500 ms with no other key counts as no tap.
- **Held key:** Caps Lock by default; Right Ctrl, Right Shift and Right Win are offered too, as in vorssaint.
- **Safety.** Held modifiers release on the key's up, the feature turning off, Chippi quitting, the hook dying, a session lock or desktop switch, and a timeout when no key events arrive. A chord is one `SendInput` batch where possible. Kayla designs the release.
- **Over an admin window** Caps Lock is plain Caps Lock.
- **Status:** "Caps Lock holds Ctrl+Alt+Shift." and "Working now", "Paused while a selected app is open", "Not working in admin windows", "Chippi cannot watch the keyboard right now."

## The page

Settings › Shortcuts, top to bottom:

1. **Every binding, one list.** Each row: the action (name and icon), its door (a key as keycaps, "Caps + K", or "Back side button"), a state, and Change and Remove. Grouped like Actions: Chippi, Window, Text and so on. Rows for Chippi's own features (Mic mute, Command bar, Plain paste, Radial menu, Cleaning mode, Audio output, each Quick action) come from the shortcut service, not a hard-coded list.
2. **Add shortcut.** Pick an action (the Radial menu's picker), then a door: "Press a shortcut" (recorder), "Caps +" then a key, or "Press a mouse button" ("Now press an extra button or move the side wheel." with Cancel; the press is taken and swallowed). Save and Cancel.
3. **Caps Lock section:** the held key, Hold modifiers and its checkboxes, the tap action, apps to leave alone.
4. **Mouse buttons section:** Apps to leave alone ("Your extra mouse buttons stay quiet in these apps, and the press reaches them instead."), Add an app, Remove.
5. A section's switch is off by default. Always on (Kayla's brief) is not repeated here.

Empty state: "No shortcuts yet. Add the first one."

### States and clashes

- **Active**, **Inactive** (its feature is off).
- **Used twice:** two Chippi bindings on one door. The second doesn't save: "{action} already uses this." For a mouse button: "That button or direction is already on the list."
- **In use by another app:** `RegisterHotKey` failed. This is the only way to learn another app holds a combination.
- **Held by the Radial menu:** "That button already opens the Radial menu. Pick another one, or free it there first." A button the Radial menu holds shows "This button opens the Radial menu now, so the shortcut waits." Radial wins until it's freed; both live in the hub (click for the binding, hold for the wheel).
- **Windows keeps this one:** the reserved combinations.
- Honest scope: Windows has no call that lists other apps' global shortcuts, so the page says "Shortcuts Chippi uses, with clashes flagged". The catalog blurb is reworded from "Every shortcut Chippi and your apps use".

## Edge cases

- Back and Forward belong to browsers and Explorer: the apps list is the way out.
- A shortcut with no action yet passes through.
- A key bound twice, once through `RegisterHotKey` and once as a Caps combo, is two different doors and is fine.
- An action unavailable (a removed app, a feature not installed) shows "Unavailable" and does nothing.
- Press shape: the action fires on down; holding doesn't repeat.
- Another remapper (PowerToys Keyboard Manager, AutoHotkey) on Caps Lock fights this. Not detectable; the section's caption says so.

## Tests

- Pure state machines on an injected clock: tap and hold, hold modifiers, repeats, the 500 ms solo hold, release on every safety path, the button-versus-Radial arbitration, the clash rules.
- Shortcut service tests with a fake registrar: registration failure shows "In use by another app", twice-used is refused, reserved combos refused.
- Hub tests with a fake source: Caps Lock swallowed with its repeats, swallowed down means swallowed up, tagged input passes.
- Windows Sandbox probes: Caps+key reaches a test window as the bound action and the plain key does not; held modifiers arrive as a chord; Caps Lock's light never changes; middle, Back and Forward presses (`SendInput`) run the action and the target sees no click.
- Screenshots of the page: dark and light, empty and with a dozen bindings including each state.

## Done means

- All three doors used in a dev profile with real-size data (a dozen bindings, both apps lists).
- The page's p99 time to open measured; shots next to Lumen's mockup.
- Sandbox probe results attached.
- Travis's live check: his real mouse's extra buttons, Caps Lock with his keyboard, and a game with Always on off and on.

## Not verified

- Hooks seeing Caps Lock down and up, and a swallowed one not toggling it: standard hook behavior, not tried.
- Stuck modifiers after a lock or desktop switch: inference, not tried.
- Which of his buttons a hook can see: depends on his mouse and its driver.
- The Office key shortcut (Ctrl+Alt+Shift+Win) and AltGr sending Ctrl+Alt: from memory, no source found.
- Anti-cheat on his games: untested. With Always on off, Chippi sends nothing into a fullscreen game; with it on, chords and bound keys are input a game may treat as a macro.
