# Quick toggles: brief

Confirmed by Travis, 2026-11-01. The mockup is built from this; DESIGN.md wins where it speaks.

## Job

Flip Windows settings and run system actions in one click from the island, without opening Settings or Explorer: mid-work, before a game, before a screen share.

## What it holds

**On/off (9):**

- **Dark mode:** Windows and apps, light or dark.
- **Hidden files:** hidden files and file extensions in Explorer.
- **Desktop icons:** show or hide everything on the desktop.
- **Taskbar auto-hide.**
- **Second screen:** main screen only, or both extended (Win+P's choices).
- **Bluetooth** and **Wi-Fi:** the radios.
- **Camera block:** every app off the camera, and back.
- **Power plan:** picks from your plans and marks the active one.

**Actions (9):**

- **Lock**, **Turn off displays** (the PC stays awake) and **Start screen saver** (at once, on every screen).
- **Empty Recycle Bin.**
- **Eject:** pick a drive from a short list; with one drive, it ejects at once.
- **Quit apps:** a list of open apps. Quit one, all, or all but the front one. Apps on a keep-list (Settings) are never closed by "all". Apps with unsaved work still ask to save.
- **End frozen apps:** closes every app that has stopped responding.
- **Restart Explorer:** for a stuck taskbar or Start menu.
- **Power:** sleep, restart, shut down or sign out.

Confirms, each a short question with Cancel and a verb (DESIGN.md §3): Empty Recycle Bin, every Power action, Quit all, End frozen apps and Restart Explorer.

**Cleaning mode** joins the actions you can pick while it's installed too ([cleaning-mode.md](cleaning-mode.md)).

## Surfaces

1. **Home tile:** a Quick toggles tile on the island's home grid opens its page.
2. **The page:** the ones you picked, as tiles. It never scrolls: the tiles shrink to fit. On/off tiles show their live state; actions run on a click, after their confirm.
3. **First open:** the first time the page opens, it asks which ones you want.
4. **Small lists in the page:** Quit apps, Eject, Power plan and Power.
5. **Settings › Quick toggles:** the pick, and Quit apps' keep-list. **Settings › Shortcuts:** a shortcut for any toggle or action, off by default, clashes flagged as today.

## Feedback

The tile's state is the only feedback, from a click or a shortcut: no bar notice, and Chippi doesn't react.

## States

- What your PC can't do is left out of the pick: no Bluetooth adapter, one screen only.
- With nothing to act on, the tile says so: "No drives", "None frozen".
- A change made elsewhere (Windows Settings, a shortcut) shows on the tile.
- Errors read "… failed" (DESIGN.md §3).

## Left out, and why

- **Night light** and **Do Not Disturb:** Windows has no supported way for an app to set them. Night light lives in an undocumented binary setting that community tools rewrite (reported working on 24H2, unknown on build 26300). Do Not Disturb has no setter at all: the documented Focus API is the Clock app's focus sessions, behind a Microsoft unlock token, and the other routes write undocumented settings that may need a sign-out. Travis chose neither.
- **HDR, mouse acceleration, the Sticky Keys prompt, transparency and clock seconds:** offered, not chosen.
- **Keep awake** and **Microphone** keep their own home tiles.

## For the mockup to settle

- The tiles' layout and sizes, for a few and for all 19.
- How the small lists open: a step into the page, or a pop-over.
- The first-open picker.
- What the home tile's status line says.
