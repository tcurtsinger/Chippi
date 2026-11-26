# Mac-style desktop

Requested by Travis, 2026-11-25. Answers approved the same day: every recommended default, except that the notch can be dragged along the top bar (its color settled later as pure black in both themes). Mockup: [design/mockup.html](design/mockup.html) (dark and light, clickable). Mockup approved by Travis the same day ("Ok great. Let's do it"); the menu bar is final as no fill and no blur, its ink following the wallpaper.

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
- The Windows key, Win+A (Quick Settings), Win+N (notifications) and Win+Tab keep working. Chippi never moves Windows' own flyouts: they open where Windows opens them.

## Menu bar

- 28 DIPs, on top of every display, registered as an AppBar so maximized windows start below it.
- **Fullscreen:** a display's bar hides when a fullscreen app comes to the front on it, and stays hidden while that app still covers the display, whichever display the keyboard is on: a film on one display isn't drawn over because of a click on the other. It comes back when the app stops covering the display or another app comes to the front there (`FullscreenHolds`).
- **Surface** (Travis, 2026-11-25, matching macOS 26): no fill and no blur. The wallpaper shows through untouched, the same in both themes.
- **Ink follows the wallpaper, not the theme:** white text and glyphs with a soft dark shadow over a dark wallpaper, `#1A1A1A` with a soft light halo over a light one. Chippi reads the current wallpaper of each display (`IDesktopWallpaper`) as a strip of luminance under the bar, placed as Windows fits the picture (Fill, Fit, Stretch, Tile, Center, Span; the background color where the picture doesn't reach), and each group picks white below 0.5 from the part of the strip it sits over, so a picture dark on the left and light on the right gets white menus and a dark clock. A group keeps its ink within 0.04 of the threshold. It reads again when the wallpaper or a display changes; a slideshow is read on each change. A solid-color background uses that color. Dropdown menus follow the app theme. A window dragged under the bar isn't read.
- **Left, from the left:**
  - **Chippi mark** (the pet's face at 16): About this PC, Chippi settings, Task Manager, then Lock, Sleep, Restart, Shut down, Sign out.
  - **App name** of the foreground window, 13 / 600, as the Start menu calls the app (`AppCatalog`: by the window's taskbar identity, then its program, then the one app installed in a folder above it, so Steam's helper is "Steam"); the program's own description only when the Start menu doesn't list it. Its menu: About, Show in Explorer, Hide, Quit (closes the app's windows), End task. With nothing in front but the desktop it reads **Desktop** (Travis, 2026-11-25), with New File Explorer window, Display settings and Personalize.
  - **The app's real menus** when it has a classic Win32 menu, read from `GetMenu` and invoked with `WM_COMMAND`, or a UI Automation menu bar. Most modern apps (Edge, Chrome, Teams, Electron, Settings) have none, and then nothing is shown in their place. An app running as administrator takes no commands from Chippi, so its menus aren't shown and the rows that act on its window are greyed out. Entries the app draws itself have no words and are left out.
  - **Window:** Minimize, Maximize, Snap (left, right, the saved layouts), Move to (each display, by its own name; two of the same monitor by their sizes), Bring all to front, Close window. Runs through the shared action PC. It sits before the app's Help; an app with a Window menu of its own gets these items at the top of that menu, not a second "Window".
- **Right:** Chippi's own items, then the date and time (`Wed Nov 25  10:02 AM`). See Menu bar items below.
- Menus are the island's flyout style: 8 corners, 30-DIP rows, 13 text, shortcut hints right-aligned in tertiary. One longer than its display scrolls.
- **After the audit** (Travis, 2026-11-25, "Fix everything"):
  - **Menus hold the keyboard** while they're open, as the dock's do (`MenuFocus`, one clear pixel, through `BarMenu`): Esc, the arrow keys and a click elsewhere work. A row that acts in the app in front (its own command, Minimize, Close window) gives that app the keyboard back before it runs, so a dialog it opens comes up with the keyboard. The app's title bar shows inactive while a bar menu is open.
  - **The app in front is read again when a menu closes**, so the bar never names an app whose window the menu just minimized or closed.
  - **Questions** are Chippi's own, under the title that asked: "Restart?", "Shut down?", "Sign out?", "End chrome.exe?" with Cancel and the verb (`BarQuestion`, shared with the dock's Empty Recycle Bin).
  - **Edges:** an item takes a press over the bar's whole height, and the mark and the clock reach to the ends of the bar, so the corners and the top row of pixels work.
  - **Narrow bars:** when both groups don't fit, the app's menus go into », then the right group's items go from the left, then the app's name is cut short. The clock never goes, and nothing is drawn over anything else.
  - **A second press on an open title closes its menu.**
  - Left as they are: an item's page opens in the notch where the notch is, and moving along the bar with the keyboard needs a shortcut of its own.

### Menu bar items

Travis, 2026-11-25, after trying Windows' flyouts moved under the bar ("instead of messing with the default windows behavior, we change the formula"): the right side is Chippi's own buttons, each doing one thing at once. Mockup: [design/menu-bar-items.html](design/menu-bar-items.html), approved the same day ("Build it").

- **Chosen in Settings › Mac-style desktop › Menu bar:** a row per item, top to bottom as they sit left to right, each with a grip (drag, or Up and Down on it) and ⋯ (Move up, Move down, Remove); **Add item** lists the rest, Chippi's first, then Windows' own, and a pick goes next to the clock. The clock's row is last and locked. Only what this PC has is offered: an item whose feature isn't installed, Battery without a battery and Language with one input language are left out, saved or not, and come back with what they need.
- **To start:** Search, Capture, Clipboard, Microphone, Sound, Battery, Language, Hidden icons, Controls.

| Item | A press | Shows |
|---|---|---|
| Search | Opens Search with the keyboard in its field | |
| Capture | Starts a region capture | |
| Clipboard, Scratchpad, Shelf, Displays, Actions | Opens its page | |
| Microphone | Mutes, unmutes | A red disc while muted |
| Sound | Opens Chippi Settings › Output (App volume without Output; Windows' sound settings without either) | The speaker, crossed while muted or at 0; read once a second, never set |
| Controls | Opens the notch's home | |
| Record | Opens Screen recording; while recording, stops | A red dot and the time while recording |
| Copy text, Pick color | Starts it | |
| Keep awake | Turns it on, off | An accent disc while on |
| System | Opens its page | The processors' load, read every 2 seconds only while the item is on the bar |
| Agents | Opens its page | An orange disc while a session works |
| Hidden icons | Opens Windows' own flyout, where Windows opens it; a second press closes it and the app in front gets the keyboard back | |
| Network | Opens Windows Settings › Network & internet | Wi-Fi's signal, or the wired glyph |
| Battery | Opens Windows Settings › Power & battery | The charge |
| Language | Switches input language (Win+Space) | The language in use |
| Clock | Opens Windows' notifications, where Windows opens them | Always last |

- **A page opens in the notch,** and a second press on its item closes it. The press itself closes the panel first, as any press off the panel does, so the panel remembers what a press just closed (`PageToggle`).
- **Names:** each item is named for what a press does now ("Mute microphone", "Stop recording", "Volume 45%"), in its tooltip and for screen readers.
- **What went:** the Wi-Fi, volume and battery group that opened Quick Settings, and the badges that appeared by themselves (the notch's ears show those activities).

## Dock

- Main display, bottom, centered. 48-DIP icons on a 64-DIP glass shelf with 16 corners, 6 under each icon for the running dot. Registered as an AppBar (maximized windows stop above it).
- **Hide:** Always shown (default), Auto-hide, or Hide when a window touches it. **Position:** bottom (default), left, right.
- **Contents:** Start (opens the Windows Start menu), the apps in one row, a divider, Downloads (a stack menu of the newest files) and Recycle Bin.
- **Magnification** (on by default, size adjustable): icons near the pointer grow to 72 with a cosine falloff over 3 icons.
- **Clicks:** an app not running opens; a running one comes to the front, un-minimized if it was; its window in front minimizes, as on the taskbar. An app whose windows are all on another desktop is gone to, not opened again. Several windows show the App switcher's live previews above the icon on hover.
- **Right click:** Open, New window, Keep in Dock / Remove from Dock, Show in Explorer, Close windows, Quit. Empty Recycle Bin asks "Empty Recycle Bin?" with Cancel and Empty. A clear one-pixel window (`MenuFocus`, shared with the menu bar) takes the keyboard before a menu or the question opens and gives it back to the app in front when it closes, so Esc, the arrow keys and a click elsewhere work as in any menu. The menu opens 8 above the icon as it is, grown or not, and the icons stay as they are while it's open. Quit closes the windows the dock shows under the icon, on every desktop. A file dropped on the Recycle Bin that can't be recycled is never deleted for good without Windows asking.
- **The row of apps** (Travis, 2026-11-25): pinned apps (brought over from the taskbar's pins on first use) and running apps that aren't pinned share one row. At a start it's the pins, then the running apps in the order they opened; an app keeps its place from then on, so bringing one forward doesn't move it. Any app can be dragged to any place in the row. A running app that's moved is kept from then on, as on a Mac; the others stay as they were, so a pin can sit to the right of a running app until the dock starts again.
- **Drag:** an icon is picked up where the pointer holds it and lands in the place its middle is over; drag off to remove, drop files on an app to open them with it, on Downloads to move them there, on Recycle Bin to delete them.
- **Keys:** Win+1 to Win+9 open the dock's apps in its order (the hidden taskbar would answer them in its own). Each tile is a UI Automation button with its name.
- **Polish after the critique** (Travis, 2026-11-25): the glass is Windows' acrylic blur behind the shelf, the Start tile keeps its gradient, taskbar pins come over in the taskbar's order, Store apps and web apps match their windows by AppUserModelID, and the menus are sentence case.
- **After the audit** (Travis, 2026-11-25, "Fix everything"): every size of an icon is one drawing, its largest artwork trimmed of its clear margins and scaled once to the resting and the magnified size, so icons drawn small in their frame match the ones that fill it; icon pixels are premultiplied before WPF draws them (Windows hands them over straight, which made every edge jagged); the row is put together again only when an app comes, goes or moves; while the icons are magnified the strip they grow into takes the pointer; and a shelf with more apps than the display is wide shrinks to fit.
- Later, not in this project: jump lists, the minimize animation, app groups, moving along the dock with the keyboard (it needs a shortcut of its own in Settings › Shortcuts).

## Notch

The widget's form while the mode is on. Same activities, same panel, same pages.

- **Rest:** centered on the main display's menu bar, attached to the top edge: 28 tall, 184 wide, square top, 10 bottom corners. A tiny Chippi sits in the left ear when nothing is going on.
- **Color** (Travis, 2026-11-25, final): pure black like a camera notch in both themes, at rest, carried and open, with no edge, shadow or blur. Its text and controls always use the dark island ink, whatever the app theme.
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
