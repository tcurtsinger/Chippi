# Mac-style desktop

Requested by Travis, 2026-11-25. Answers approved the same day: every recommended default, except that the notch follows the theme and can be dragged along the top bar. Mockup: [design/mockup.html](design/mockup.html) (dark and light, clickable). Nothing is built until the mockup is approved.

An optional mode that gives Windows a top menu bar, a bottom dock and turns the widget into a notch at the top of the screen. References: [Muelle](https://github.com/crafter-station/Muelle) (MIT) for the Windows shell parts, [vorssaint-utils](https://github.com/vorssaint/vorssaint-utils) for the notch's behavior. Neither's code or assets are copied; Muelle's approach is followed and credited in `THIRD-PARTY-NOTICES.md` if any code is ported.

## Boundaries

- Chippi's own name, mark, icons (Lucide), pet and Segoe UI Variable. No Apple logo, no SF Pro (its license forbids use off Apple hardware), no Vorssaint name, icon or screens (its TRADEMARKS.md covers trade dress).
- Every feature page is the one that exists today. The mode adds three surfaces around them: the menu bar, the dock and the notch. Nothing is rebuilt per surface.
- DESIGN.md gets a "Mac-style desktop" section once the mockup is approved. Outside the mode, nothing changes.

## Turning it on

- A feature, **Mac-style desktop**, in Add features (Windows group). Not in Essentials; in Power user and Everything.
- Settings › Mac-style desktop: **Desktop style** Windows / Mac-style, then switches for **Menu bar**, **Dock** and **Notch**, all on by default.
- Turning it off, quitting, uninstalling the feature, a crash, or `Chippi.exe --restore` puts the taskbar and the work area back.

## The taskbar

- Hidden on every display while the menu bar or dock is on: auto-hide turned on, then the taskbar windows (`Shell_TrayWnd`, `Shell_SecondaryTrayWnd`) made invisible and click-through. `ShowWindow(SW_HIDE)` doesn't last on Windows 11.
- The user's own auto-hide setting is saved to a file before the first change and deleted only after it is restored, so a crash and restart never mistakes Chippi's auto-hide for the user's. Explorer restarting is detected (`TaskbarCreated`) and the takeover is repeated.
- The Windows key, Win+A (Quick Settings), Win+N (notifications) and Win+Tab keep working.

## Menu bar

- 28 DIPs, on top of every display, registered as an AppBar so maximized windows start below it. Hidden while a fullscreen app is in front on its display.
- **Surface:** frosted blur (DWM host backdrop) with the island's text colors, following the app theme. Solid island fill when Windows transparency effects are off.
- **Left, from the left:**
  - **Chippi mark** (the pet's face at 16): About this PC, Chippi settings, Task Manager, then Lock, Sleep, Restart, Shut down, Sign out.
  - **App name** of the foreground window, 13 / 600. Its menu: About, Show in Explorer, Hide, Quit (closes the app's windows), End task.
  - **The app's real menus** when it has a classic Win32 menu, read from `GetMenu` and invoked with `WM_COMMAND`, or a UI Automation menu bar. Most modern apps (Edge, Chrome, Teams, Electron, Settings) have none, and then nothing is shown in their place.
  - **Window:** Minimize, Maximize, Snap (left, right, the saved layouts), Move to (each display), Bring all to front, Close window. Runs through the shared action PC.
- **Right, from the right:** date and time (`Wed Nov 25  10:02 AM`, opens Win+N); Controls (opens the notch's home); Wi-Fi, volume and battery (open Quick Settings); input language; the tray chevron (opens Windows' own hidden-icons flyout); Chippi's status badges (Mic muted, Keep awake, Agents). CPU, RAM and network readouts are optional, off by default.
- Menus are the island's flyout style: 8 corners, 30-DIP rows, 13 text, shortcut hints right-aligned in tertiary.

## Dock

- Main display, bottom, centered. 48-DIP icons on a 64-DIP glass shelf with 16 corners, 6 under each icon for the running dot. Registered as an AppBar (maximized windows stop above it).
- **Hide:** Always shown (default), Auto-hide, or Hide when a window touches it. **Position:** bottom (default), left, right.
- **Contents:** Start (opens the Windows Start menu), pinned apps (brought over from the taskbar's pins on first use), running apps not pinned, a divider, Downloads (a stack menu of the newest files) and Recycle Bin.
- **Magnification** (on by default, size adjustable): icons near the pointer grow to 72 with a cosine falloff over 3 icons.
- **Clicks:** an app not running opens; a running one comes to the front; the one in front minimizes. Several windows show the App switcher's live previews above the icon on hover.
- **Right click:** Open, New window, Keep in Dock / Remove from Dock, Show in Explorer, Close windows, Quit.
- **Drag:** reorder, drag off to remove, drop files on an app to open them with it, on Downloads to move them there, on Recycle Bin to delete them.
- Later, not in this project: jump lists, the minimize animation, app groups.

## Notch

The widget's form while the mode is on. Same activities, same panel, same pages.

- **Rest:** centered on the main display's menu bar, attached to the top edge: 28 tall, 184 wide, square top, 10 bottom corners. A tiny Chippi sits in the left ear when nothing is going on.
- **Color:** dark theme, pure black like a camera notch. Light theme, frosted glass like the menu bar but lighter: white at 50% over a blur (80% once open, so pages stay readable), a 1px white edge and a soft shadow. Solid `#F5F5F6` when Windows transparency effects are off (Travis, 2026-11-25).
- **Ears:** the highest activity, as on the bar today: the left ear shows its glyph, cover or provider mark, the right ear its value (the agent's elapsed time, the equalizer, the time left). The notch widens to 280 to make room and narrows back when it ends.
- **Opening:** hover 300 ms or click. The notch grows down into the 520 × 360 panel, keeping its top edge; it closes 400 ms after the pointer leaves, or on Esc or a click outside. Click only is an option.
- **Home:** the Tool gallery in 5 columns, each tile with its shortcut under its name when one is set. Pages open inside the notch with Back, as today.
- **Dragging** (Travis, 2026-11-25): press and move 4 DIPs to pick it up. While carried it is the **pill**: 22 tall, fully round, hugging what it shows (Chippi alone, 38 wide, or the activity's glyph and value side by side). Dragged sideways it rides 3 below the top edge. Pulled 12 DIPs below the bar it detaches and follows the pointer anywhere, lifted (1.06 scale, −3° tilt, deeper shadow), with a 3px accent-fill line on the top edge of the display under the pointer showing the notch's width where it will land (the 2026-10-23 movable tab's landing line). On release it slides up and **lands as the notch** at that place, on any display's bar. Within 40 DIPs of the center it snaps to the center.
- **Placement:** saved as a display and a fraction of the bar's width, like the puck's place today. The menu bar's items keep clear of it: the left group's app menus collapse into a » menu before they would touch it, and a notch on the right side moves the status items to its left. The items make room during the drag, before the drop.
- **Opening away from the center:** the panel grows down from the notch, centered on it and kept 8 inside the work area.

## Delivery

1. This brief and the mockup, for approval (this pull request).
2. Taskbar takeover and the AppBar groundwork: crash-safe restore, `--restore`, Explorer restarts, displays coming and going.
3. Menu bar.
4. Dock.
5. Notch and pill.

Each is one pull request with its screens, offscreen shots in dark and light and timings. Hiding the taskbar runs only in Windows Sandbox or an owner-approved away session, never on the owner's desktop while he's at it.
