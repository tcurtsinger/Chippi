# Phase 6 mockups: notes (2026-11-04)

Every app, count and setting in these mockups is invented. Nothing read or changed the real mouse, keyboard, clipboard or Windows settings.

## Files

- `actions-shortcuts.html`: task 212, the Actions menus and Settings › Shortcuts. Travis approved it on 2026-11-04 (task 240) with option B (task 224).
- `wave-1.html`: wave 1's Settings screens and Close protection's bar line. Travis approved it on 2026-11-04 (task 264), with real app icons in every app list ([App icons](#app-icons)).
- `shortcuts.html`: wave 2's Settings › Shortcuts, revised for Travis's caveats when he approved it on 2026-11-05 (tasks 341 and 344, [Wave 2](#wave-2-shortcuts-task-344)). It replaces the Settings › Shortcuts boards in `actions-shortcuts.html`.
- `wave-3.html`: wave 3's screens (task 375): Settings › Text snippets, Settings › Snap with its zone editor, Shift+drag zones, the App switcher overlay and Settings › App switcher ([Wave 3](#wave-3-text-snippets-snap-and-the-app-switcher-task-375)). Travis approved it on 2026-11-05 as drawn (task 384).
- `arrangements.html`: saved app arrangements (task 439): Settings › Snap › Arrangements with its review and editor, and the island's Arrangements list, run page and bar lines ([Saved app arrangements](#saved-app-arrangements-task-439)). Not approved yet.
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
| wave-3 | `snippets`, `snippets-narrow` | Settings › Text snippets at 1120 and 860: twelve snippets in four folders, one turned off |
| wave-3 | `snippets-empty` | First install: no snippets, Snippet expansion off |
| wave-3 | `sheet-snippets-states` | Add and Edit, the two refusals, a trigger that starts another, Insert date or time, Delete, search, a missing sound, the hook down, a snippet in the island's Search |
| wave-3 | `snap`, `snap-narrow` | Settings › Snap at 1120 and 860: Shift+drag per display, Layouts, Gaps |
| wave-3 | `sheet-snap-editor` | The zone editor: a zone selected, a divider dragged, a new layout, a name refused; a layout's menu; Shift+drag off |
| wave-3 | `sheet-snap-zones` | Shift+drag over the main display and the laptop's, the pointer in a gap, "Snap failed" on the bar |
| wave-3 | `switcher` | The App switcher over the desktop: twelve windows, a minimized one |
| wave-3 | `switcher-settings` | Settings › App switcher |
| wave-3 | `sheet-switcher-states` | Search, This display, no preview, 50 windows, no results, "Switch failed" on the bar |
| arrangements | `arrangements`, `arrangements-narrow` | Settings › Snap at 1120 and 860 with three arrangements |
| arrangements | `arrangements-review`, `arrangements-review-narrow` | New arrangement's review: twelve windows on two displays, a row pointed at |
| arrangements | `sheet-arrangements-states` | Empty, a row's menu, launched from Settings, Delete, launch details, the three refusals, Edit, Recapture |
| arrangements | `sheet-arrangements-island` | Actions › Snap and its Arrangements list (and empty), Search, a run: launching, partly done (pages 1 and 2), guessed, a missing display, cancelled, stopped; four bar lines |
| shortcuts | `sheet-shortcuts-states` | Add shortcut's steps, the three refusals, Record, a shortcut that can't work, the row menu, Caps Lock off, paused, the Win confirm, a hook down |

## Wave 2: Shortcuts (task 344)

Travis approved with three caveats (2026-11-05): the keycaps weren't uniform down the column, he didn't want warning text on the rows, and "Off in these apps" was too verbose. The revision:

1. **One keys column.** Every row's keycaps (or its Record button) start at the same point, in a 200-wide column before the row's More button. A row without More keeps the space, so the column doesn't move.
2. **No state lines on rows.** A saved shortcut that can't work (another app or Windows holds it, its app is gone, the Radial menu holds the button, Caps Lock combos are off) shows only dimmed keycaps, with no tooltip (Marge on #247: no explaining text). Change shows the reason when it's recorded again (3).
3. **Clashes are refused in the editor.** Add shortcut, Record and Change all open the same editor. A clash keeps the pressed keys in the field with a red border, leaves Save off, and says why on the Save row: "Snap left already uses this", "The Radial menu uses this", "Windows uses this", "Another app uses this". Pressing again replaces it.
4. **"Excluded apps"** names both sections' app lists.
5. **The Caps Lock switch says "Paused"** in place of "On" while an excluded app is open, with no caption.
6. **The Win warning is a confirm** (Travis's task 211 #10 asked for a warning): turning on Win asks "Add Win to Caps Lock?" with Cancel and Add, in that order like the app's other confirms, and no body line (Marge on #247).

## Wave 3: Text snippets, Snap and the App switcher (task 375)

Drawn from `text-snippets.md`, `snap.md` and `app-switcher.md`. The Snap menu itself is the approved `sheet-menus` board and isn't redrawn. Choices for Travis to check:

**Text snippets**
1. **"Snippet expansion"**, not the brief's "Expand snippets while typing": setting labels are noun phrases (DESIGN.md §3), as "Click debounce" was in wave 1. Off on first install. Its sound is the dependent row "Expansion sound" (None by default); a missing one says "Sound unavailable" in Caution.
2. **The switch and its excluded apps come first, the list after.** With fifty snippets the list is long, and the switch decides whether any of them works.
3. **The list's first row is the search, the count and Add snippet**, so Add is in reach with fifty snippets. Folders are group headers, as in Shortcuts. Each row: the name over its text (variables as typed), the trigger in the code font, how it expands, its switch and More. A turned-off snippet dims everything but its switch.
4. **Row switches have no On/Off beside them:** in a list of fifty that word repeats on every row, and the dimmed row already says it.
5. **The editor opens in place**, like the Shortcuts editor: in the row's place for Edit, under the search row for Add. Delete is on its title row and asks "Delete this snippet?" with Cancel and Delete.
6. **Refusals sit on the Save row** in Caution, with the field's amber border and Save off, as in Shortcuts: "The trigger needs at least 2 characters", "Another snippet already uses this trigger". "This trigger starts another one" doesn't block, so it's a neutral status with Save still on.
7. **Insert date or time** is a flyout from its button under Text, with the brief's rows and a live Preview; Insert writes the tag where the cursor was. **Insert clipboard** writes `{{clipboard}}`.
8. **Empty is "No snippets" with Add snippet**, the same as Shortcuts' "No shortcuts", not the brief's "No snippets yet. Add the first one."
9. **In the island's Search**, a snippet is a result with the Snippet glyph, "Snippet · ;sig" as its detail and **Insert** as its verb. "Snippet not inserted" and "Not working in admin windows" use Search's line under the results.
10. **Not drawn:** "Not working in admin windows" on the Settings page, for the reason wave 1 gives: Settings is the window in front while you read it.

**Snap**
11. **The page is "Snap"**, as the approved Shortcuts sidebar names it, not the brief's "Window layouts".
12. **Shift+drag picks a layout per display**, each named as Windows names it with its role and resolution as the caption. FancyZones works the same way, and a laptop screen rarely wants the desk monitor's layout.
13. **Layouts are one list with grips.** The five built-in ones can be duplicated but not edited or deleted. The Snap menu shows six pictures, so it shows the first six; a layout past them is captioned "not in the Snap menu" and stays in the action picker.
14. **The zone editor opens in place of the list**, like the Radial menu's wheel: the layout's name, then the canvas at the main display's shape beside the selected zone's rows (Zone name, Split: Side by side or Stacked, its size in pixels on that display, Remove). Dividers drag; while one moves it shows the split ("60% · 40%"). A new layout starts as one zone to split. A name another layout has is refused like a trigger.
15. **Gaps** are two sliders: Window gap (8 px) and Screen edge gap (0 px).
16. **The zones on Shift+drag** fill the display's work area at the gaps, on the island's fill, each named; the one under the pointer takes the accent tint and border. In a gap nothing is lit and release leaves the window where it is. A placement that fails says "Snap failed" over the app on the bar.

**App switcher**
17. **The overlay** is the island's fill and shadow, centered on the display it opened on: the search field ("Search windows") with All displays and This display beside it, then cards five across (a preview, the app's icon and the title). Typing goes to the search at once.
18. **Selection starts on the second card,** the window you were in before the one in front, as Alt+Tab does, so opening then Enter goes back.
19. **Without a preview** (a minimized window, an app in "Apps without previews", or one Windows can't capture) the card shows the app's icon in the preview's place, with "Minimized" under it for a minimized one. The card's size doesn't change.
20. **More than three rows scroll inside the grid,** with the selection kept in view. The island's no-scroll rule is for its pages; this is an overlay, like the Radial menu.
21. **"Switch failed"** shows on the bar over the window's title, since the overlay has closed by then.
22. **Settings › App switcher:** Shortcut (Not set, Record), as the Radial menu has; Windows from (All displays or This display, which the overlay's switch also changes); Previews; and Apps without previews.

**Not drawn:** reduced motion (the overlay and zones only fade in), several virtual desktops, and the switcher on a portrait display. Every app, title, monitor and snippet here is invented, and the squares stand for each app's real icon ([App icons](#app-icons)).

## Saved app arrangements (task 439)

Drawn from `arrangements.md` as Travis confirmed it (task 438). Every window, app and display is invented. Choices for Travis to check:

1. **Arrangements is the last section of Settings › Snap,** after Gaps. The brief puts it below Layouts; Gaps stays next to the layouts it spaces.
2. **A saved row:** up to four of its apps' icons, the name over "12 windows · 2 displays", its shortcut as caps when one is set, Launch, and More (Edit, Delete), like a layout's menu. Rows are sorted by name: the Snap menu lists them in that order.
3. **New arrangement captures what's open right away** and opens the review in place, under the list. The brief had a separate Capture step after New. Most of the time the desktop is already arranged when you think "save this", and Recapture on the review's title row covers the times it isn't.
4. **The review:** Name, then a preview with each display where it sits and the windows that show on it (minimized ones are only in the list), then one row per window, grouped by display. Each row has the app's icon, its title over its size and place on that display ("1536 × 2088 at 0, 0", "Maximized", "Minimized · …"), Remove, and a chevron that opens its launch details. Pointing at a row lights its window in the preview. Windows that weren't captured are one row at the end: "3 windows not captured" over each one and why.
5. **Launch details:** a program shows its path with Browse…, Arguments and Working folder. A Start menu app shows its name over "Start menu" and nothing else, since it can't take either.
6. **Refusals sit on the Save row** in Caution with Save off, as in Snippets: "Fan Control needs an app to launch", "Another arrangement already uses this name", "No windows to save".
7. **Edit** is the same editor with Delete beside Recapture and the shortcut read-only with Edit ↗ (Settings › Shortcuts). Recapture replaces the rows, marks new windows "New" and says "Recaptured · 2 new · 1 gone" on the Save row. Cancel keeps what was saved. Delete asks "Delete Coding?" with Cancel and Delete.
8. **On the island, Actions › Snap has Arrangements in its header.** Its four commands already fill their row. The page lists the arrangements two across, as Actions lists do. Pointing at one shows Launch, and Manage opens Settings › Snap. Search finds an arrangement as "Arrangement · 12 windows", with Launch as its verb.
9. **A run is an island page named after the arrangement,** with Actions' two columns of five rows and paging. Each row shows the window's title over its state: Waiting, Opening, Placing, Ready, Skipped or Failed, with the reason. A problem gets a Caution glyph as well as the word, so it isn't carried by color alone. Once the run ends, problems and guesses sort first, so they're on page 1.
10. **The run is also an activity on the bar:** a progress ring with "5 of 12 ready" over the name. A clean run folds once it's done. A run that's only partly done, or that guessed a window or moved one to another display, stays on the bar until Dismiss, and clicking it opens the page. Partly done is Caution and offers Retry for the failed rows. Cancelled and Stopped (a fullscreen app came in front) offer Launch.
11. **A refused launch** (a fullscreen app or Pause in these apps) shows "Not launched" over "Coding · Fullscreen app" on the bar. If the widget is hidden because of that fullscreen app, the launch beeps once, as Close protection does (task 263).
12. **Launched from Settings,** the row's caption shows the run ("Opening · 5 of 12 ready") and Cancel takes Launch's place. The other Launch buttons are off until it ends, since only one run happens at a time.

**Not drawn:** the action picker and the Radial menu (an arrangement is an ordinary action there), reduced motion, a third display, and the widget on the left.

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
