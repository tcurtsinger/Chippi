# Phase 6 behavior spec, part B (Herb, 2026-11-04)

Mouse button shortcuts, Click and key debounce, Super key, Text snippets, Shortcut manager.
Spec source: vorssaint-utils (strings and `*Support.swift` files, read 2026-11-04; keys like [V1] point to the Sources list at the end). Where Windows differs, the
text says how. Defaults and ranges are vorssaint's unless a line says otherwise. Nothing here was run: this is
research, no code.

**Updated 2026-11-04 for Travis's answers** (tasks 197, 198, 208 to 211). What changed since the first version:
Debounce pauses with Always on like the rest; Mouse buttons, Super key and Shortcut manager are one feature,
Shortcuts, on one Settings page, binding shared actions; no drag gestures; no Right Alt and no Win in the default
set; snippets are typed, never pasted, with no import or export. The briefs (`debounce.md`, `shortcuts.md`,
`text-snippets.md`) say what gets built. This file is the research behind them.

## Shared rules (all five)

- One setting per feature, off by default. Each has a Settings page and no home button (they are switches that run
  in the background). Text snippets' quick menu is the one overlay in vorssaint; Chippi's first build leaves it out (see section 4).
- **Fullscreen (Travis, tasks 198 and 211 #1):** one shared **Always on** setting for all of Phase 6's input
  features, off by default. Off: every feature here pauses while a fullscreen game or app is in front on that
  display, **Debounce included** ("No if the Always on switch is off, then it shouldn't"). On: they keep acting
  everywhere, games included. A single switch, not one per feature. The shared fullscreen check is Kayla's. The
  Radial menu is the one exception, with its own switch.
- **Actions (task 208):** every verb a shortcut, button or Caps Lock combo runs is a shared action (the Radial
  menu's list, lifted). These features bind doors to actions; they don't own verbs. Phase 6 adds actions of its own
  (Snap, Insert a snippet, switches) in Karl's and my briefs.
- Input Chippi sends is tagged (`ExtraInfo == Native.Ours`, as the Radial menu does). Every feature here passes
  tagged input and other tools' injected input through untouched, so features never chew each other's output.
- Fail open: if a hook stops, throws or is disabled, input passes. Nothing may leave a key or button stuck down.
- Per-app exclusions ("Apps to leave alone", Add an app, Remove) for Mouse buttons and Super key, matched by process
  name. Super key's rule is stricter, as in vorssaint: it pauses while any listed app is open, even in the
  background.
- Hooks don't see elevated windows (UIPI) or the secure desktop. Status says "Not working in admin windows"
  rather than failing silently. (Feasibility is Kayla's.)

## 1. Mouse button shortcuts

Sources: vorssaint [V1] [V2]; Windows [W3] [W7] [W8]. Back and Forward are button numbers 3 and 4, side wheel is its own input, and buttons 3 to 31 are allowed [V2]; the Radial menu claim check is in the same file [V2]. Unverified: that buttons 6 and up are invisible to a Windows hook (inference from the hook only reporting X1 and X2 [W3]).

An extra mouse button, or a side-wheel direction, presses a key combination you choose. While a button has a
shortcut it no longer does what it did before (its press and release are swallowed).

- **Buttons (Windows):** Middle, Back (XBUTTON1), Forward (XBUTTON2), Side wheel left and right (WM_MOUSEHWHEEL,
  one shortcut per notch). Buttons 6 and up are invisible to a hook (vendor drivers remap them), so out of scope.
  Left and right are never offered.
- **Adding one:** "Add a button or side wheel" starts capture: "Now press an extra button or move the side wheel."
  with Cancel. The next press is taken and swallowed. Messages:
  - not allowed (left, right, wheel up/down): "That input cannot take a shortcut. Use an extra button or a side-wheel direction."
  - held by the Radial menu: "That button already opens the Radial menu. Pick another one, or free it there first."
  - already listed: "That button or direction is already on the list below."
  - after a wait: "If nothing happens, your mouse's own software may already be using that control."
  - hook unavailable: "Chippi cannot watch the mouse right now."
- **List row:** the button's name (Middle button, Back side button, Forward side button, Side wheel left/right),
  its shortcut with Set shortcut, Remove. Empty: "No shortcuts yet. Add a button or side-wheel direction."
- **Recorder:** single keys and combinations allowed here (unlike the global hotkey rule), including media keys and
  F13 to F24. A button with no shortcut yet passes through.
- **Radial menu conflict:** a button the Radial menu holds shows "This button opens the Radial menu now, so the
  shortcut waits." Radial wins until the button is freed. Both need the one hook thread (Kayla's call).
- **Press shape:** the shortcut fires on button down (keys down then up, tagged). Holding doesn't repeat.
- **Exclusions:** "Your extra mouse buttons stay quiet in these apps, and the press reaches them instead."
- **Drag gestures:** vorssaint's Spaces drag would be virtual desktops (Ctrl+Win+Left/Right) and Task view
  (Win+Tab). Travis (task 211 #9): not now.
- **Update for the action model:** a button runs any shared action, not only a key combination. "Press keys" stays
  one of the actions (the Radial menu's Key shortcut).
- Risk: Back and Forward are used by browsers and Explorer; the exclusion list is the only escape.

## 2. Click and key debounce

Sources: vorssaint [V1] [V3] [V4] (5 s stale-state gap; an accepted down's up is never suppressed); Windows [W2] [W5]. Defaults and ranges come from vorssaint's strings and defaults [V1]. The Windows hook's flag list (extended, injected, lower-integrity injected, alt down, up) has no auto-repeat flag [W2], so the repeat rule is my inference. The system timer is typically 10 to 16 ms [W5]; that the hook's `time` field ticks at that rate is my inference, not stated in the docs [W2]. The `Stopwatch` fix has not been measured.

One page, two switches: Extra click filter (mouse) and Key debounce (keyboard). Each ignores accidental repeats
from worn switches. A healthy click or key passes with no delay.

**Mouse**
- Left, right, middle only. Back and Forward belong to Mouse buttons.
- Window: default 25 ms, range 5 to 100. Caption: "A repeated click inside this interval is treated as an
  accidental duplicate."
- Rule: after an accepted release, a new press inside the window is dropped together with its own release (and
  drag packets between). The release of an accepted press is never dropped, so a stop can't leave a button held.
  A release with no matching press passes. Time going backwards resets the state.

**Keyboard**
- Global window default 5 ms, range 0 to 500 (0 = off), step 1. Optional per-key windows for one chattering key.
- Rule: a second key-down of the same key is dropped if (a) the key is still down and it came inside the window of
  the accepted press, or (b) it was the last accepted key and was released less than a window ago. Key-ups always
  pass. A key's state resets after 5 s of silence or when time goes backwards.
- Auto-repeat: macOS flags it, Windows doesn't. A key-down on an already-down key and past the window counts as a
  repeat and passes. Windows auto-repeat delay is about 250 to 1000 ms, so cap the slider at 200 ms (decision
  below).
- Never applies to Chippi's own tagged input or other tools' injected input.
- **Timing:** the hook's timestamp is millisecond-coarse and can tick at about 15 ms, as big as the 5 and 25 ms
  windows. Measure with `Stopwatch.GetTimestamp()` on entry to the hook instead.

## 3. Super key (Caps Lock as a modifier)

Sources: vorssaint [V5] (hidutil remap, tap and hold, 500 ms solo hold threshold, default modifiers); Windows [W1] [W2] [W4]. That a low-level hook sees Caps Lock down and up and a swallowed one doesn't toggle is standard hook behavior [W1] but was not tried here. The Ctrl+Alt+Shift+Win Office shortcut [W9] and the AltGr claim [W10] are unconfirmed, from memory. Lost key-ups on lock or desktop switch are inference; a session-change notification exists for the safety release [W6].

Holding one chosen key makes every other key you press act as if the chosen modifiers were held. A tap on its own
does a separate action.

- **Keys to hold:** Caps Lock (default), Right Ctrl, Right Shift, Right Win. Right Alt only with a note: on layouts
  with AltGr it also sends Ctrl+Alt, which breaks typing `@` and similar. Recommend leaving it out.
- **Modifiers:** one or more of Ctrl, Alt, Shift, Win. Default Ctrl+Alt+Shift. Win in the set gets a warning:
  Ctrl+Alt+Shift+Win plus most keys opens Office. Win off by default.
- **Mechanism:** no mapping table, no registry, no restart. A low-level keyboard hook sees Caps Lock go down and up;
  swallowing both stops the real Caps Lock toggling (its light never changes while on), and the auto-repeat downs
  are swallowed too. (vorssaint needs a hidutil remap because macOS never reports Caps Lock down and up.)
- **A tap on its own** ("What a quick tap does when no other key is pressed."): Nothing (default), Turn capitals on
  and off (inject a tagged Caps Lock press), Press Escape, Switch input language (Win+Space).
- **Holding:** the modifiers are pressed only when another key goes down during the hold, and released when the held
  key goes up. A solo tap injects no modifiers, so Win and Alt can't wake Start or the menu bar by themselves. If
  modifiers were pressed with no key between, send a throwaway key to mask Win and Alt.
- **Safety:** held modifiers are released on the key's release, the feature turning off, Chippi quitting, the hook
  dying, and a session lock or desktop switch (Ctrl+Alt+Del, UAC) that eats the key-up. Vorssaint checks the key's
  physical state; on Windows that isn't readable while the hook swallows the key, so use a timeout on injected
  modifiers when no key events arrive. Kayla to confirm.
- **Status:** "{key} holds {modifiers}." (Caps Lock holds Ctrl+Alt+Shift.), "Working now", and with an excluded app
  open "Paused while a selected app is open".
- **Failure:** "Chippi cannot watch the keyboard right now." (vorssaint's other two failures are macOS only.)
- **Other tools:** PowerToys Keyboard Manager, AutoHotkey or a keyboard driver that remaps Caps Lock will fight
  this. Not detectable; the page caption says it. Travis's task 197 answer matters most here.

## 4. Text snippets

Sources: vorssaint [V6] (64-character buffer, 40-character trigger, delimiters, `{{...}}` variables); Windows [W4] [W11] [W12]. The Unicode insert is documented [W4]; that every app accepts it is unverified, hence the app list to test. Password detection by UIA IsPassword [W12] works only where the app exposes it.

Type a short trigger anywhere and it becomes its text. Everything stays on this PC.

- **Fields:** Name, Trigger (e.g. `;email`), Text, Expand (Right away, or After space, Tab or Enter), Enabled,
  Ignore capitalization (off), Folder (plain text; folders exist only because snippets name them), Show in the
  quick menu (on).
- **Trigger rules:** 2 to 40 characters, no spaces or tabs. Errors: "The trigger needs at least 2 characters." and
  "Another snippet already uses this trigger."
- **Matching:** the engine keeps the last 64 typed characters. Longest trigger wins (`;email2` beats `;email`).
  After-delimiter snippets fire when Space, Tab or Enter follows; the delimiter is typed after the text.
  Right-away snippets fire as the last trigger character is typed.
- **The buffer resets on:** Enter or Tab with no match, arrows, Home, End, Esc, any Ctrl, Alt or Win combination, a
  mouse click, a change of foreground window. Backspace removes the last character.
- **Page:** switch "Expand snippets while typing" ("Type a trigger anywhere and it becomes its text. Everything
  stays on this PC."), the list, "Add snippet", an editor (New snippet, Edit snippet) with Save, Delete, Cancel.
  Empty: "No snippets yet. Add the first one."
- **Variables** (unknown tags stay as typed, so a typo is visible): `{{date}}`, `{{time}}`, `{{datetime}}`,
  `{{clipboard}}`. A format after a colon: `{{date:yyyy-MM-dd}}`. A timezone: `{{date-tz(America/New_York):yyyy-MM-dd}}`.
  - Formats are .NET patterns, not ICU (`dddd` weekday, `tt` AM/PM, `MM` month vs `mm` minutes). An "Insert
    date/time" helper (Type Date/Time/DateTime, Style Short/Medium/Long/Full/ISO 8601/Custom, Timezone with
    search, Pattern, live Preview, Insert or Update) writes the pattern so nobody has to know that. IANA ids work
    (.NET resolves them on Windows).
  - `{{clipboard}}` reads the real clipboard at expansion with a time limit (an app that never answers must not
    hang typing). A snippet without it never touches the clipboard. Tests use a fake clipboard.
- **Inserting:** Backspaces delete the typed trigger, then the text is typed as Unicode key events
  (`KEYEVENTF_UNICODE`), not pasted, so the clipboard is untouched. New lines go in as Shift+Enter (Enter would
  send a chat message). Needs checking in Notepad, a browser field, Word, a terminal and a chat app.
- **Not buffered:** password fields (UIA `IsPassword`) and anything typed while an excluded app is in front
  (an exclusion list like the others). Elevated windows aren't seen: shown in the status.
- **Reading keys:** `ToUnicodeEx` with the no-state-change flag, so dead keys aren't broken. Layout changes
  mid-typing are followed.
- **Quick snippet menu** ("Open snippets from a menu"): its own shortcut (proposal: Ctrl+Alt+;), a small overlay
  with "Search snippets", grouped by folder, Enter inserts at the cursor, Esc closes. Footer "Enter inserts · Esc
  closes". Empty: "No snippet matches the search." and "Nothing to show yet. Add snippets, or turn on Show in
  the quick menu for the ones you use most." It must not take focus from the target app.
- **Sound on expansion:** off by default, a picker of Windows system sounds, "Sound unavailable" if missing.
- **Storage:** one JSON list in Chippi's settings; stable ids; unknown fields read as defaults. Import and export
  isn't in vorssaint; out of scope unless Travis wants it.

## 5. Shortcut manager
(Now part of the one Shortcuts feature, task 210. See `shortcuts.md`.)

Sources: vorssaint [V7]; Windows [W13]. RegisterHotKey fails when another app holds the combination [W13]. I found no API that lists other apps' hotkeys; that is absence of evidence, not proof.

Vorssaint: one place listing every feature shortcut with Active or Inactive, edit in place, and the Super key
alternative on a row. Its takeover of macOS system shortcuts has no Windows twin, so here it is clash flags.

- **Where:** Settings › Shortcuts exists already (`SimplePages.cs:14`) and lists only some features. Extend it.
- **Lists today:** Mic mute (Ctrl+Shift+M), Command bar (Ctrl+Alt+Space), Plain paste, Radial menu, Cleaning mode,
  Audio output, each Quick action. Phase 6 adds Window layouts and the rest as they land.
  Mouse button shortcuts show as read-only rows ("Back button: Ctrl+W").
- **Row:** feature, shortcut (recorder to change, Remove to clear), and a state: Active, Inactive (feature off),
  "In use by another app" (registration failed), "Used twice" (two Chippi features), "Windows keeps this one".
- **Clashes:** two Chippi features can't hold one shortcut: "{feature} already uses this." and it doesn't save.
  Windows-reserved combinations are refused up front (all Win combinations, Ctrl+Alt+Del, Alt+Tab, Alt+F4; the
  recorder already blocks Win). An app that grabbed it first shows only when our registration fails.
- **Honest scope:** Chippi can't list other apps' global shortcuts, so the catalog blurb ("Every shortcut Chippi and
  your apps use") overpromises. Reword to "Every shortcut Chippi uses, with clashes flagged".
- **Super key alternative:** show "or Caps+<key>" on a row when the shortcut's modifiers are a subset of the super
  key's. Low value on Windows; skip if it adds noise.

## Best reference per feature, and where Chippi can beat it

Travis (task 197): he relies on none of PowerToys, AutoHotkey or a mouse app, and wants Chippi's version to do it
all better. so none of the five is cut back. The reference links are in Sources (R1 to R6). I confirmed the pages exist but did not read them in depth: "bar to match" is from my knowledge of the tools, so confirm before a brief quotes it. "Where Chippi can do better" is my opinion, not a measurement.

| Feature | Bar to match | Where Chippi can do better |
|---|---|---|
| Mouse button shortcuts | X-Mouse Button Control: per-app profiles, any button to key combos, text or scripts; the mouse makers' apps (Logitech Options+) | Same per-app exclusions, plus one list that shows Radial menu and shortcut owners together so a button is never double-booked. Capture with live feedback and a plain reason when a button can't be taken. No separate profile files. |
| Click and key debounce | Open-source "chatter" fixers and AutoHotkey scripts: a fixed delay per key, no UI | A slider with the numbers explained, per-key windows for the one bad key, never delays a healthy press, fails open, and a test area ("press the key") that shows how many presses were ignored. |
| Super key | PowerToys Keyboard Manager (remap Caps Lock to a modifier, no tap vs hold) and AutoHotkey Hyper-key scripts (tap and hold, but you write the script) | Hold and tap in one page with no script, a safe default that avoids the Ctrl+Alt+Shift+Win Office trap, a release on lock or desktop switch so modifiers never stick, and an "apps to leave alone" list. |
| Text snippets | Espanso and TextExpander (variables, date maths, forms, folders, sync), Beeftext, AHK hotstrings | Matches the common 90%: triggers, folders, date/time with a builder and preview, clipboard, quick menu. Better: never touches the clipboard to insert, skips password fields, no scripting or cloud needed. Skip forms and sync. |
| Shortcut manager | PowerToys Shortcut Guide (Win-key cheat sheet) and tools that list registered hotkeys | One page for Chippi's own shortcuts with clash and "in use by another app" flags, editable in place. Windows has no call that lists other apps' hotkeys, so listing every app's is not possible. |

## Small decisions for Travis

All answered "Yes" (task 211) as recommended:
1. Mouse buttons: no drag gesture for now (#9).
2. Super key: Right Alt not offered, Win not in the default set (#10).
3. Snippets: typed, never pasted; no paste mode, no import or export (#11).
4. Shortcut manager: the catalog blurb is reworded (part of #3).
5. Key debounce slider capped at 200 ms (lead call).
6. Task 197: he uses none of the references, so each feature gets a full version.

## Sources

Links opened 2026-11-04 (HTTP 200). "Text checked" means I searched the page text for the cited fact.

**vorssaint-utils** (`https://github.com/vorssaint/vorssaint-utils/blob/main/` + path). Files confirmed to exist; the numbers cited (buttons 3 to 31, 5 s stale gap, 64 buffer, 40 trigger, 500 ms solo hold) were found in them by search.
- [V1] `Sources/Vorssaint/Core/{MouseButton,MouseClickDebounce,MouseException,SuperKey,Snippet,ShortcutSettings}Strings.swift` (copy and defaults)
- [V2] `Sources/Vorssaint/Services/MouseButtons/MouseButtonShortcutSupport.swift`
- [V3] `Sources/Vorssaint/Services/MouseClickDebounce/MouseClickDebounceSupport.swift`
- [V4] `Sources/Vorssaint/Services/KeyboardDebounce/KeyboardDebounceSupport.swift`
- [V5] `Sources/Vorssaint/Services/SuperKey/SuperKeySupport.swift`
- [V6] `Sources/Vorssaint/Services/Snippets/TextSnippetSupport.swift`
- [V7] `Sources/Vorssaint/Services/SystemShortcutTakeover.swift`, `SystemShortcutTakeoverSupport.swift`

**Windows** (`https://learn.microsoft.com/en-us/windows/` + path)
- [W1] `win32/winmsg/lowlevelkeyboardproc` (hook timeout, text checked)
- [W2] `win32/api/winuser/ns-winuser-kbdllhookstruct` (flags list, no repeat flag, text checked)
- [W3] `win32/api/winuser/ns-winuser-msllhookstruct` (mouseData)
- [W4] `win32/api/winuser/ns-winuser-keybdinput` (`KEYEVENTF_UNICODE`, text checked)
- [W5] `win32/api/sysinfoapi/nf-sysinfoapi-gettickcount` (10 to 16 ms, text checked)
- [W6] `win32/api/wtsapi32/nf-wtsapi32-wtsregistersessionnotification` (page opened only)
- [W7] `win32/inputdev/wm-xbuttondown` (page opened only)
- [W8] `win32/inputdev/wm-mousehwheel` (page opened only)
- [W9] Ctrl+Alt+Shift+Win opens Office: no source found, unconfirmed.
- [W10] AltGr sends Ctrl+Alt: no source fetched, unconfirmed.
- [W11] `win32/api/winuser/nf-winuser-tounicodeex` (flag bit 2 keeps keyboard state, text checked)
- [W12] `win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationelement-get_currentispassword` (page opened only)
- [W13] `win32/api/winuser/nf-winuser-registerhotkey` (fails if hotkey taken, text checked)
- UIPI (hooks don't see elevated windows): `win32/winmsg/about-hooks`, page opened, specific sentence not checked.

**Reference tools** (pages opened only)
- [R1] X-Mouse Button Control `https://www.xmousebuttoncontrol.com/`
- [R2] PowerToys Keyboard Manager `https://learn.microsoft.com/en-us/windows/powertoys/keyboard-manager`
- [R3] PowerToys Shortcut Guide `https://learn.microsoft.com/en-us/windows/powertoys/shortcut-guide`
- [R4] AutoHotkey hotstrings `https://www.autohotkey.com/docs/v2/Hotstrings.htm`
- [R5] Espanso `https://github.com/espanso/espanso`
- [R6] TextExpander `https://textexpander.com/`
- Not looked up: Logitech Options+, Beeftext (its GitHub URL returned 404), "chatter fixer" scripts. Named as examples only.

**Chippi:** Radial menu `WheelHook.cs` (shared hook, tagged input), Sound `MicShortcut.cs` (hotkey rules), `SimplePages.cs:14`.
