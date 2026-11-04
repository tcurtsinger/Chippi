# Quick actions: brief

Confirmed by Travis, 2026-11-01, and its mockup approved the same day: [Chippi Phase 5.5: Quick actions and Cleaning mode](https://claude.ai/artifact/HNpG8aciGLWyJecHFyfeeW). DESIGN.md wins where it speaks. The one-page gallery, in-widget editor and drag-and-drop positions were approved on 2026-11-03.

**Mode:** Operate.

**Name:** the feature was planned as Quick toggles. It's **Quick actions** everywhere (Settings, the catalog, Add features, the docs), and **Actions** on the island.

## Job

Flip Windows settings and run system actions in one click from the island, without opening Settings or Explorer: mid-work, before a game, before a screen share.

## What it holds

Each has a one-word label on the island (Travis: every island label is one word) and its full name in Settings.

**On/off (9):**

| Island | Settings | What it does |
|---|---|---|
| Dark | Dark mode | Windows and apps, light or dark |
| Hidden | Hidden files | Hidden files and file extensions in Explorer |
| Icons | Desktop icons | Show or hide everything on the desktop |
| Auto-hide | Taskbar auto-hide | The taskbar slides away until the pointer reaches the edge |
| Extend | Second screen | Both screens extended, or the main screen only (Win+P's choices) |
| Bluetooth | Bluetooth | The radio |
| Wi-Fi | Wi-Fi | The radio |
| Camera | Camera | Lit while apps can use the camera, like Windows' own switch; off blocks every app, with a crossed-out camera |
| Plan | Power plan | Picks from your plans; its status is the active one |

**Actions (10):**

| Island | Settings | What it does |
|---|---|---|
| Lock | Lock | Win+L |
| Displays | Turn off displays | The screens go dark; the PC stays awake |
| Screensaver | Screen saver | At once, on every screen |
| Recycle | Empty Recycle Bin | Empties it |
| Eject | Eject | Pick a drive from a list; with one drive, it ejects at once. Status: "2 drives", the drive, or "No drives" (off) |
| Quit | Quit apps | A list of open apps: quit one, all, or all but the front one. Apps on the keep-list are never closed by "all". Apps with unsaved work still ask to save. Status: "9 open" |
| Frozen | End frozen apps | Ends every app that has stopped responding. Status "None" (off) when nothing has |
| Explorer | Restart Explorer | For a stuck taskbar or Start menu |
| Power | Power | Sleep, restart, shut down or sign out, from a list |
| Cleaning | Cleaning mode | Starts it ([cleaning-mode.md](cleaning-mode.md)); offered while that feature is installed |

**Confirms,** each a short question with Cancel and a verb (DESIGN.md §3): "Empty Recycle Bin?", "Sleep?", "Restart?", "Shut down?", "Sign out?", "Quit N apps?", "End frozen apps?" and "Restart Explorer?". Both "all" buttons ask (Travis, 2026-11-01). The count and exceptions describe the exact app set captured when the question opens; a newly opened app cannot be added after confirmation. Quitting one app from its row doesn't ask.

## Surfaces

1. **The home (changes for everything, Travis 2026-11-01):** 5 columns instead of 4, so 10 buttons fit, about 91 wide. Every name under a home button is one word: Microphone, Volume, Awake, Organizer, System, Clipboard, Scratchpad, Shelf, Actions, and Search for the Command bar ([command-bar.md](command-bar.md)). Their Settings pages keep the full names. Names and statuses stay centered under their buttons. Actions has no status line: it's a door to its page.
2. **The Actions page (revised 2026-11-03):** one gallery, five columns, all selected actions visible together without scrolling or paging. Each whole tile is clickable, including its glyph, short label and state. Only true Windows toggles use toggle semantics and an active accent. Commands are ordinary buttons; list openers (Plan, Eject with two or more drives, Quit, Power) have a small chevron. Full names appear in tooltips and accessibility labels. Intentional empty positions remain empty.
3. **Choose and edit:** first open is "Choose actions", with nothing selected. A configured gallery has a pencil-only header button named "Edit actions", opening the same editor in the widget. All available actions sit in a five-column, 20-position grid. Click to show or hide; drag to an empty position to move, or onto another tile to swap. Alt+arrow keys provide the same placement with focus following the tile. Selected tiles take the accent tint and check. The count reads "N shown". Done is disabled with no selection; otherwise it saves the selection and exact positions, including gaps. Back or Esc discards the draft. Hidden and temporarily unavailable actions keep their positions.
4. **The small lists step into the page** (Travis chose this over a pop-over): the list takes the page's place, the header names it, and Back returns to the gallery. Quit apps uses at most two readable columns of five 42-tall rows, paging past ten so every app is reachable. A range with previous/next buttons sits above the list. Each row has the app icon, name, visible Front and Kept markers when applicable, and an X to quit it. "Quit all but Chrome" and "Quit all" sit at the bottom. Both review a precise app count and name the exceptions before proceeding. A kept app is excluded from bulk Quit but can still be quit individually. Eject lists every drive, its size and Eject control, paging as needed. Power plan uses radio rows, active filled and semibold. Power lists Sleep, Restart, Shut down and Sign out.
5. **Settings › Quick actions:** **Show in Actions** lists the available actions with their full names, glyphs and **Shown / Hidden** switches. These switches only control gallery visibility. **Keep open during Quit all** lists the bulk-quit exceptions, with Remove for each and Add app. **Settings › Shortcuts:** a shortcut for any action, off by default, with conflicts flagged.

## Feedback

Feedback stays on the affected tile or list row, from a click or a shortcut. No bar notice, and Chippi doesn't react. A failed operation names its action and leaves its control available to retry. An Eject failure never appears in Power, or under a different drive.

## States

- What your PC can't do is left out of the pick: no Bluetooth adapter, one screen only.
- With nothing to act on, the button turns off and its status says so ("No drives", "None").
- A change made elsewhere (Windows Settings, a shortcut) shows on the button.
- Errors read "… failed" (DESIGN.md §3), remain with the affected action, and clear when that operation succeeds.
- Editor changes never run a Windows action. Done commits the draft; Back and Esc leave the saved gallery intact.

## Left out, and why

- **Night light** and **Do Not Disturb:** Windows has no supported way for an app to set them. Night light lives in an undocumented binary setting that community tools rewrite (reported working on 24H2, unknown on build 26300). Do Not Disturb has no setter at all: the documented Focus API is the Clock app's focus sessions, behind a Microsoft unlock token, and the other routes write undocumented settings that may need a sign-out. Travis chose neither.
- **HDR, mouse acceleration, the Sticky Keys prompt, transparency and clock seconds:** offered, not chosen.
- **Keep awake** and **Microphone** keep their own home buttons.
