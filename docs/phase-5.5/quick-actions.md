# Quick actions: brief

Confirmed by Travis, 2026-11-01, and its mockup approved the same day: [Chippi Phase 5.5: Quick actions and Cleaning mode](https://claude.ai/artifact/HNpG8aciGLWyJecHFyfeeW). DESIGN.md wins where it speaks.

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

**Confirms,** each a short question with Cancel and a verb (DESIGN.md §3): "Empty Recycle Bin?", "Sleep?", "Restart?", "Shut down?", "Sign out?", "Quit all apps?", "Quit all but Chrome?", "End frozen apps?" and "Restart Explorer?". Both "all" buttons ask (Travis, 2026-11-01). Quitting one app from its row doesn't.

## Surfaces

1. **The home (changes for everything, Travis 2026-11-01):** 5 columns instead of 4, so 10 buttons fit, about 91 wide. Every name under a home button is one word: Microphone, Volume, Awake, Organizer, System, Clipboard, Scratchpad, Shelf, Actions. Their Settings pages keep the full names. Names and statuses stay centered under their buttons. Actions has no status line: it's a door to its page.
2. **The Actions page:** the home's grid, carried over: 5 columns, 10 between columns and 12 between rows, each button holding only its glyph, with the label centered under it and the status under that. With 16 or more picked, the four rows take 34-tall buttons so they fit; with fewer, the home's 44. The block sits in the body's vertical middle and never scrolls. On/off buttons take the accent fill while on, as the home's toggles do; the label and status under them keep their colors. A button that opens a list (Plan, Eject with two or more drives, Quit, Power) has a small chevron at its right.
3. **First open:** the page shows all 19 as buttons with a check in each corner, nothing picked, and "None picked" with a disabled Done under them. Picking lights a button on the accent tint with an accent edge and fills its check; Done turns on with the first pick.
4. **The small lists step into the page** (Travis chose this over a pop-over): the list takes the page's place, the header names it ("‹ Quit apps"), and Back returns to the buttons. Quit apps runs in two columns past 8 apps, each row an app's icon, its name, a "Front" or "Kept" badge, and an X to quit it; "Quit all but Chrome" and "Quit all" sit at the bottom right. Eject's rows have the drive, its size and an Eject button. Power plan's are radio rows, the active one filled and semibold. Power's are Sleep, Restart, Shut down and Sign out.
5. **Settings › Quick actions:** the pick as two sections of switch rows, Toggles and Actions, each row the glyph (accent while shown), the full name and On or Off; then **Kept apps**, with a Remove for each and Add app. **Settings › Shortcuts:** a shortcut for any of them, off by default, clashes flagged as today.

## Feedback

The button's state is the only feedback, from a click or a shortcut: no bar notice, and Chippi doesn't react.

## States

- What your PC can't do is left out of the pick: no Bluetooth adapter, one screen only.
- With nothing to act on, the button turns off and its status says so ("No drives", "None").
- A change made elsewhere (Windows Settings, a shortcut) shows on the button.
- Errors read "… failed" (DESIGN.md §3).

## Left out, and why

- **Night light** and **Do Not Disturb:** Windows has no supported way for an app to set them. Night light lives in an undocumented binary setting that community tools rewrite (reported working on 24H2, unknown on build 26300). Do Not Disturb has no setter at all: the documented Focus API is the Clock app's focus sessions, behind a Microsoft unlock token, and the other routes write undocumented settings that may need a sign-out. Travis chose neither.
- **HDR, mouse acceleration, the Sticky Keys prompt, transparency and clock seconds:** offered, not chosen.
- **Keep awake** and **Microphone** keep their own home buttons.
