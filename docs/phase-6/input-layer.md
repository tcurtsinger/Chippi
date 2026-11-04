# Input layer, shared actions and the pause: brief

Draft by Kayla, 2026-11-04, for Travis to confirm. Wave 1's shared parts, which every other Phase 6 feature builds on. How and why: [architecture.md](architecture.md) sections 2 to 5. DESIGN.md wins where it speaks.

## Job

Give every Phase 6 feature one way to listen to the keyboard and mouse, one list of things Chippi can do, one place shortcuts are kept, and one rule for when they stand down in games. Done right, Travis sees three new Settings rows and Actions regrouped (task 224), and the Radial menu works exactly as it does today.

## What Travis sees

**Settings › General › Fullscreen apps** (shown once a Phase 6 input feature is installed):

| Row | Control | Default |
|---|---|---|
| Always on | Switch | Off |
| Pause in these apps | A list of apps, each with Remove; Add app under it | Empty |

- **Always on off:** while a fullscreen game or app is the window in front, every Phase 6 input feature pauses: Debounce, Close protection, Smooth scrolling, Shortcuts' bindings, Text snippets, Snap's Shift+drag zones, the App switcher's shortcut and Focus follows mouse. Clicking a window on the other monitor brings it to front, so they act there again.
- **Always on on:** they act everywhere, games included.
- **Pause in these apps:** apps that count as fullscreen even in a window, for games that don't cover the screen.
- **Never paused:** the Radial menu (its own switch, below), shortcuts that existed before Phase 6 (mic mute, the output switch, Paste as plain text, Quick actions, Cleaning mode, Search's Ctrl+Alt+Space), Cleaning mode's lock, and Search itself (the Command bar): a snippet picked in Search is typed into the window Search opened over even when that's a fullscreen app, since it was chosen on purpose (Marge's call).
- Always on is also a switch in Actions' Input menu; Lumen's mockup (task 212) names it there.
- **Every Phase 6 feature starts off,** even once installed, until Travis switches it on (task 226). Always on starts off too.

**Settings › Radial menu › Opening:** a new switch row, **Open over fullscreen apps**, on by default, which keeps today's behavior. Off, the triggers reach the game as plain clicks and keys while a fullscreen app is in front.

**Settings › Shortcuts:** looks the same as today. Its rows now come from one list that knows every shortcut Chippi holds, so a new feature's shortcuts show up there without changing the page. Herb's Shortcuts brief and Lumen's mockup reshape the page in wave 2.

**Actions** regroups (tasks 224 and 225, Lumen's option B and menus, task 212):

- **Power** gains Lock, Turn off displays and Screen saver as rows, beside Sleep, Restart, Shut down and Sign out. Lock is no longer a tile of its own.
- **Quit apps** gains End frozen apps: an app that isn't responding shows "Not responding", and its row's button is End instead of Quit. Ending still asks first, as End frozen apps does today, now naming the app ("End Outlook?", Cancel and End).
- **The gallery** drops the Lock, Displays, Screensaver and Frozen tiles. Phase 6 adds Snap (a menu), Input (a menu of the Phase 6 switches) and Switcher, each shown once its feature is installed; Input shows once any Phase 6 input feature is. With all three, that's 18 tiles.
- **Travis's saved positions stay.** Every tile that remains keeps its slot in his saved layout; the four slots freed go empty, and a new tile takes the first empty slot when its feature is installed. He can move them in the editor as today.
- **Nothing bound to the four moved actions breaks.** Their commands (`quick-actions/lock`, `displays`, `screensaver`, `frozen`) keep their keys, so Search, Radial slices and their shortcuts in Settings › Quick actions still run them.

## Behavior

- **The Radial menu is unchanged:** same triggers, same 250 ms hold, the click still reaches the app, the same virtual pointer in games. Cleaning mode's lock and recording a shortcut in Settings are unchanged too.
- **Pausing never leaves anything stuck.** A key or button Chippi swallowed before a game came to front still has its release swallowed.
- **No modifier stays held.** Any modifier Chippi has sent down (Shortcuts' Caps Lock combos, wave 2: with Caps Lock held, a key with no binding passes with the held modifiers, Ctrl+Alt+Shift by default) is released with its key, and also when the feature turns off, Chippi quits, the hook is found gone, the session locks or the desktop switches, a pause begins, or no key event arrives for 5 seconds. Wave 1 builds and tests this release path; the hub owns it ([architecture.md](architecture.md) §2).
- **Timing:** filters time each key and button on Chippi's own high-resolution clock, taken as the hook is called, not the coarse timestamp Windows attaches. Debounce depends on it.
- **While paused, the game's input doesn't pass through Chippi at all** when nothing unpaused needs that device: the hook comes off.
- **Shortcuts that pause are released to the game:** while paused, Chippi gives the key back, so the game sees it.
- **Focus follows mouse** is Windows' own setting, so pausing it turns that setting off while the fullscreen app is in front and back on after.
- **If Windows drops Chippi's hook** (it does that silently to a hook it thinks is slow), Chippi puts it back within 5 seconds. Today the Radial menu would stop until Chippi restarts.
- **Admin windows:** while one is in front, hook features don't act on it, and Caps Lock is plain Caps Lock. Key shortcuts still work. Nothing in Chippi can change this without a signed installer (architecture §6).
- **Games and anti-cheat:** with Always on off, Phase 6 features send no input into a fullscreen game. The Radial menu still can (key shortcut and text slices, its virtual pointer) while Open over fullscreen apps is on, its default. Untested on Travis's games either way.

## Decided (Travis, task 227)

1. **Always on lives in Settings › General,** under Fullscreen apps, since it covers features in three groups (Windows, Clipboard & text, Shortcuts).
2. **Pause in these apps,** for windowed games: without it, a game that runs in a window never pauses anything.
3. **Shortcuts from before Phase 6 never pause** (mic mute in a game is their point).

## How it's built

Three PRs, in order. All three change shared code (Core, App, Chippi.UI and the Radial menu), and the first adds two projects to `Chippi.slnx`.

1. **The hub:** new `Chippi.Input` and `Chippi.Input.Tests`. One keyboard and one mouse hook on one thread, filters in a fixed order, one `Stopwatch` clock (injected in tests), modifier masking, the held-modifier release path, the watchdog. The Radial gesture, the key recorder and Cleaning mode's lock move onto it. The Windows Sandbox runner for real-hook checks.
2. **Shared actions and shortcuts:** the Radial menu's action kinds, runner and picker lifted to Core and `Chippi.UI`; window verbs take a target (under the pointer, in front, or in front before the island opened); `IsOn` on Chippi's commands; Actions accepts tiles other features add, and regroups (Power, Quit apps, the gallery as above). `GlobalMicHotkey` becomes Core's shortcut service, and Settings › Shortcuts reads its list.
3. **The pause:** the fullscreen check lifted from the island to Core, Always on and Pause in these apps, the Radial menu's new switch.

## Done means

- **Radial menu regression:** its tests pass, and in the Sandbox a probe holds the middle button, flicks and lets go, clicks it, and holds Back, with the same results as on main. Travis's live check: hold, flick and a click on his real mouse.
- **Cost:** the hook's time per event, p99 under 50 microseconds, and Chippi's CPU over 60 s with the mouse moving, three readings, against main.
- **Pause:** in the Sandbox, with a borderless window covering the screen in front, a paused filter passes everything and the hook comes off; with Always on, it acts; a key held across the switch has no stuck release. Same for an app on Pause in these apps.
- **Watchdog:** a test unhooks the hub behind its back and sees it reinstalled.
- **Release path:** hub tests with a fake source and a recording sender hold a modifier, then trigger each way out (its key up, the filter off, quit, the hook gone, a lock, a desktop switch, a pause, 5 s without key events) and see exactly one up for it each time.
- **Actions regroup:** a test loads a saved layout from main (all 19 tiles, in a custom order) and sees every remaining tile in its old slot, the four moved ones gone from the gallery, and a new tile in the first empty slot. Lock, Turn off displays and Screen saver run from Power, a test app that stops responding shows "Not responding" in Quit apps and End ends it after the confirm, and `quick-actions/lock` still runs Lock from Search.
- **Shots:** the three Settings rows, the Actions gallery, Power and Quit apps, dark and light, next to Lumen's mockup (task 212, option B).
- **Not run here:** admin windows (needs a UAC prompt) and anti-cheat.

## Left out, and why

- **Taking over Alt+Tab:** a later option once the App switcher has proven itself (task 211, #6).
- **A pause per feature:** one switch (Travis, task 198).
- **Running Chippi as admin, or `uiAccess`:** needs a signed installer under Program Files.
- **Moving the island's own hook onto the hub:** it only listens while the panel is open, and moving it gains nothing.
