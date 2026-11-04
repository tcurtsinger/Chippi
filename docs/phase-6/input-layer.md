# Input layer, shared actions and the pause: brief

Draft by Kayla, 2026-11-04, for Travis to confirm. Wave 1's shared parts, which every other Phase 6 feature builds on. How and why: [architecture.md](architecture.md) sections 2 to 5. DESIGN.md wins where it speaks.

## Job

Give every Phase 6 feature one way to listen to the keyboard and mouse, one list of things Chippi can do, one place shortcuts are kept, and one rule for when they stand down in games. Done right, nothing changes that Travis can see except three Settings rows, and the Radial menu works exactly as it does today.

## What Travis sees

**Settings › General › Fullscreen apps** (shown once a Phase 6 input feature is installed):

| Row | Control | Default |
|---|---|---|
| Always on | Switch | Off |
| Pause in these apps | A list of apps, each with Remove; Add app under it | Empty |

- **Always on off:** while a fullscreen game or app is the window in front, every Phase 6 input feature pauses: Debounce, Close protection, Smooth scrolling, Shortcuts' bindings, Text snippets, Snap's Shift+drag zones, the App switcher's shortcut and Focus follows mouse. Clicking a window on the other monitor brings it to front, so they act there again.
- **Always on on:** they act everywhere, games included.
- **Pause in these apps:** apps that count as fullscreen even in a window, for games that don't cover the screen.
- **Never paused:** the Radial menu (its own switch, below), shortcuts that existed before Phase 6 (mic mute, the output switch, Paste as plain text, Quick actions, Cleaning mode, Search), Cleaning mode's lock.
- Always on is also a switch in Actions' menu of Phase 6 switches; Lumen's mockup (task 212) names it there.

**Settings › Radial menu › Opening:** a new switch row, **Open over fullscreen apps**, on by default, which keeps today's behavior. Off, the triggers reach the game as plain clicks and keys while a fullscreen app is in front.

**Settings › Shortcuts:** looks the same as today. Its rows now come from one list that knows every shortcut Chippi holds, so a new feature's shortcuts show up there without changing the page. Herb's Shortcuts brief and Lumen's mockup reshape the page in wave 2.

## Behavior

- **The Radial menu is unchanged:** same triggers, same 250 ms hold, the click still reaches the app, the same virtual pointer in games. Cleaning mode's lock and recording a shortcut in Settings are unchanged too.
- **Pausing never leaves anything stuck.** A key or button Chippi swallowed before a game came to front still has its release swallowed.
- **While paused, the game's input doesn't pass through Chippi at all** when nothing unpaused needs that device: the hook comes off.
- **Shortcuts that pause are released to the game:** while paused, Chippi gives the key back, so the game sees it.
- **Focus follows mouse** is Windows' own setting, so pausing it turns that setting off while the fullscreen app is in front and back on after.
- **If Windows drops Chippi's hook** (it does that silently to a hook it thinks is slow), Chippi puts it back within 5 seconds. Today the Radial menu would stop until Chippi restarts.
- **Admin windows:** while one is in front, hook features don't act on it, and Caps Lock is plain Caps Lock. Key shortcuts still work. Nothing in Chippi can change this without a signed installer (architecture §6).
- **Games and anti-cheat:** with Always on off, Chippi sends no input into a fullscreen game. Untested on Travis's games either way.

## Calls for Travis

1. **Always on lives in Settings › General,** under Fullscreen apps, since it covers features in three groups (Windows, Clipboard & text, Shortcuts). Recommended. The other option is a row on each Phase 6 page, all tied to one setting.
2. **Pause in these apps,** for windowed games: add it. Recommended; without it, a game that runs in a window never pauses anything.
3. **Shortcuts from before Phase 6 never pause** (mic mute in a game is their point). Recommended.

## How it's built

Three PRs, in order. All three change shared code (Core, App, Chippi.UI and the Radial menu), and the first adds two projects to `Chippi.slnx`.

1. **The hub:** new `Chippi.Input` and `Chippi.Input.Tests`. One keyboard and one mouse hook on one thread, filters in a fixed order, modifier masking, the watchdog. The Radial gesture, the key recorder and Cleaning mode's lock move onto it. The Windows Sandbox runner for real-hook checks.
2. **Shared actions and shortcuts:** the Radial menu's action kinds, runner and picker lifted to Core and `Chippi.UI`; window verbs take a target (under the pointer, in front, or in front before the island opened); `IsOn` on Chippi's commands; Actions accepts tiles other features add. `GlobalMicHotkey` becomes Core's shortcut service, and Settings › Shortcuts reads its list.
3. **The pause:** the fullscreen check lifted from the island to Core, Always on and Pause in these apps, the Radial menu's new switch.

## Done means

- **Radial menu regression:** its tests pass, and in the Sandbox a probe holds the middle button, flicks and lets go, clicks it, and holds Back, with the same results as on main. Travis's live check: hold, flick and a click on his real mouse.
- **Cost:** the hook's time per event, p99 under 50 microseconds, and Chippi's CPU over 60 s with the mouse moving, three readings, against main.
- **Pause:** in the Sandbox, with a borderless window covering the screen in front, a paused filter passes everything and the hook comes off; with Always on, it acts; a key held across the switch has no stuck release. Same for an app on Pause in these apps.
- **Watchdog:** a test unhooks the hub behind its back and sees it reinstalled.
- **Shots:** the three Settings rows, dark and light, next to Lumen's mockup if there is one.
- **Not run here:** admin windows (needs a UAC prompt) and anti-cheat.

## Left out, and why

- **Taking over Alt+Tab:** a later option once the App switcher has proven itself (task 211, #6).
- **A pause per feature:** one switch (Travis, task 198).
- **Running Chippi as admin, or `uiAccess`:** needs a signed installer under Program Files.
- **Moving the island's own hook onto the hub:** it only listens while the panel is open, and moving it gains nothing.
