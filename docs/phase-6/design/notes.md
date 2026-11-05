# Phase 6 mockups: notes (2026-11-04)

Every app, count and setting in these mockups is invented. Nothing read or changed the real mouse, keyboard, clipboard or Windows settings.

## Files

- `actions-shortcuts.html`: task 212, the Actions menus and Settings › Shortcuts. Travis approved it on 2026-11-04 (task 240) with option B (task 224).
- `wave-1.html`: wave 1's Settings screens and Close protection's bar line. Travis approved it on 2026-11-04 (task 264), with real app icons in every app list ([App icons](#app-icons)).
- `shortcuts.html`: wave 2's Settings › Shortcuts, revised for Travis's caveats when he approved it on 2026-11-05 (tasks 341 and 344, [Wave 2](#wave-2-shortcuts-task-344)). It replaces the Settings › Shortcuts boards in `actions-shortcuts.html`.
- `before\`: real renders from `main` at `b9b6d05`: the Actions gallery, Settings › General and Radial menu (`--settings-snapshot`, a dev profile with only Radial menu and every hold off), and the agent's 6-second bar line (`AgentBarTests.Render`).
- `shots\<board>-<theme>.png`: 2x renders of every board, dark and light.
- `render.ps1`: renders the PNGs again with headless Edge. Use `-Only <board>,<board>` to render only some boards.

Add `?board=<id>&theme=dark|light` to any page to see one board.

| Page | Board | What it shows |
|---|---|---|
| actions-shortcuts | `sheet-actions` | Today's gallery, option A, option B |
| actions-shortcuts | `sheet-menus` | Snap, Input, Power and Quit apps |
| actions-shortcuts | `sheet-snap-states` | Snap over an admin window, a move failed |
| actions-shortcuts | `settings`, `settings-narrow` | Settings › Shortcuts at 1120 and 860 |
| actions-shortcuts | `sheet-shortcut-states` | Adding a shortcut, a clash, saved, the row menu, empty, the hook down |
| wave-1 | `settings-general` | General with **Fullscreen apps**: Always on, Pause in these apps |
| wave-1 | `settings-radial` | Radial menu › Opening with **Open over fullscreen apps** |
| wave-1 | `settings-mouse` | Windows › Mouse: Focus follows mouse, Scrolling (wave 2), Clicks |
| wave-1 | `settings-keyboard`, `settings-keyboard-narrow` | Windows › Keyboard: Close protection and its Ctrl+W list, Key debounce, at 1120 and 860 |
| wave-1 | `sheet-settings-states` | Always on (list dims), Add app, empty list, first install, Focus change failed, a hook down, Add key, Close protection off |
| wave-1 | `sheet-bar` | "Press again to close" on the bar, Ctrl+W, the widget on the left, the line that doesn't fit, the two presses |
| shortcuts | `shortcuts`, `shortcuts-narrow` | Settings › Shortcuts at 1120 and 860: a dozen shortcuts, Caps Lock, Mouse buttons |
| shortcuts | `shortcuts-empty` | First install: no shortcuts, both switches off |
| shortcuts | `sheet-shortcuts-states` | Add shortcut's steps, the three refusals, Record, a shortcut that can't work, the row menu, Caps Lock off, paused, the Win confirm, a hook down |

## Wave 2: Shortcuts (task 344)

Travis approved with three caveats (2026-11-05): the keycaps weren't uniform down the column, he didn't want warning text on the rows, and "Off in these apps" was too verbose. The revision:

1. **One keys column.** Every row's keycaps (or its Record button) start at the same point, in a 200-wide column before the row's More button. A row without More keeps the space, so the column doesn't move.
2. **No state lines on rows.** A saved shortcut that can't work (another app or Windows holds it, its app is gone, the Radial menu holds the button, Caps Lock combos are off) shows only dimmed keycaps. The reason is the keycaps' tooltip and their automation name, so a screen reader hears it.
3. **Clashes are refused in the editor.** Add shortcut, Record and Change all open the same editor. A clash keeps the pressed keys in the field with a red border, leaves Save off, and says why on the Save row: "Snap left already uses this", "The Radial menu uses this", "Windows uses this", "Another app uses this". Pressing again replaces it.
4. **"Excluded apps"** names both sections' app lists.
5. **The Caps Lock switch says "Paused"** in place of "On" while an excluded app is open, with no caption.
6. **The Win warning is a confirm** (Travis's task 211 #10 asked for a warning): turning on Win asks "Add Win to Caps Lock?", "With Ctrl, Alt and Shift, most keys open Office.", with Add and Cancel. That claim is still unconfirmed (`shortcuts.md`).

## Option B's tile order

`sheet-actions` B shows Input, Snap and Switcher partway through the gallery. The rule in `input-layer.md` replaces that order: remaining tiles keep their order, and a new tile goes after the last tile once its feature is installed (Kayla, Lumen and Marge, #223). The tiles themselves are as approved.

## App icons

Travis (task 264): every app list shows the app's real Windows icon, never the two-letter squares ("Ch", "VS") the mockup draws. That covers Pause in these apps, its Add app menu, Ctrl+W in these apps and Scrolling per app. Use the public `AppIcon` in Chippi.UI (`WindowsAppIcons` is internal to it): `AppIcon.View(exePath, 20, monogram)` for a row, which loads through the cached `AppIcon.LoadAsync` and keeps the monogram until the icon arrives, so the row never changes size. An open app is listed by its exe's path too. `AppIcon.Read` and `AppIcon.ReadWindow` are only for an STA worker thread, never the UI thread. A letter shows only when Windows has no icon for the app, as in Search and Agents.

The mockup keeps the squares because this repo is public and the apps are other companies' products, so their logos aren't drawn into it. Read each square as that app's icon at the same size (20 in lists and the menu).

## Wave 1: choices Travis approved (task 264)

1. **The bar line is "Press again to close" over "Alt+F4 · Notepad".** The brief's "Press Alt+F4 again to close" measures 159 DIPs at the bar's 13 / 600, and the title line has 148. It would scroll after its 2-second hold, so half of it would still be unread when the line folds. The keys and the app fit in the second line (91 DIPs). Ctrl+W uses the same line with "Ctrl+W · Chrome".
2. **The lead is Close protection's ShieldX glyph** in the 34 accent-tint circle, the same as other activities, not the app's icon. The second line names the app.
3. **The line folds when the second press closes the window,** and whenever the pending press ends otherwise (6 s, focus moving, a pause). The app's own save prompt takes over from there.
4. **Setting labels are noun phrases** (DESIGN.md §3): "Click debounce" and "Key debounce" replace the brief's "Ignore accidental double clicks" and "Ignore key chatter". "Debounce time" replaces "window", since "window" on the Windows pages would read as an app window.
5. **The ignored count is the switch's caption** ("12 ignored"). It shows only while the switch is on, and it's kept in memory only.
6. **Lists inside a section start with a header row** (the list's name, its count as caption and Add app or Add key), with items indented 50 below it, as dependent rows are. Pause in these apps, Ctrl+W in these apps, Keys with their own time and Scrolling per app all work this way.
7. **A list dims while it does nothing:** Pause in these apps while Always on is on, and Ctrl+W in these apps while Close protection is off, as Hide tray icon dims while the island is off. The list is kept.
8. **Add app opens a menu of open apps**, then Browse… for an app that isn't running. A windowed game is usually open when you notice it needs pausing.
9. **Open over fullscreen apps is the last row of Opening**, since it applies to every trigger above it. It's on by default.
10. **A hook that stays down is an infobar naming the features that failed** ("Click debounce and Smooth scrolling failed", Retry), the same as the Shortcuts page's. It shows only after the watchdog's 5-second reinstall has failed.
11. **Scroll speed and leaving an app alone share one list,** "Scrolling per app", with a combo per app: a speed, or Leave alone. A second list of the same apps would be redundant. Smooth scrolling is wave 2; it's drawn here so the Mouse page is designed once.
12. **No row for fixed values:** Focus follows mouse's 250 ms delay and Close protection's 6-second interval aren't settings (DESIGN.md §3, Setting labels).

## Travis's answers

1. **How long a second press counts (task 262): while the line shows.** A second Alt+F4 or Ctrl+W closes the app for as long as the bar shows "Press again to close", 6 seconds, and the line folds when that runs out. This replaces the 600 ms interval, which would have left the line saying "Press again" for 5.4 s while a press was only swallowed again. Focus moving to another window still resets the count, so closing one window and then the next isn't affected.
2. **When the bar can't show (task 263): the first press beeps.** With the island off, or Always on on while a fullscreen app fills the widget's display, the first press plays Windows' default beep, the same as Plain paste's failures. The second press still counts for 6 seconds.

## Not drawn, and why

- **"Not working in admin windows"** (debounce brief): Settings is always the window in front while you read it, so this status could never be seen there.
- **Settings › General with no Phase 6 input feature installed:** today's page, unchanged (`before\*-general.png`). The Fullscreen apps section appears once one is installed.
- **Phone width:** Settings is a desktop window, and its narrowest size is 860 (`settings-keyboard-narrow`).
