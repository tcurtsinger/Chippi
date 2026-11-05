# Phase 6 architecture: Windows and input

Kayla, 2026-11-04. Research (task 201) updated for Travis's decisions in [README.md](README.md) (tasks 197, 198, 208 to 211). Read against main `a97f858`. Behavior is in Karl's and Herb's specs and each feature's brief; this says what each feature needs from Windows, what it costs, what can't be done, how it's tested without the owner's real keyboard, mouse or clipboard, and the build order. Wave 1's shared parts are briefed in [input-layer.md](input-layer.md).

## 1. What Chippi already has

| Piece | Where | Phase 6 use |
|---|---|---|
| Mouse and keyboard low-level hooks on their own thread: Chippi's own input passes by an extra-info marker, click replay, a virtual pointer for games, the pure `Gesture` state machine | `RadialMenu/Input/WheelHook.cs` | Becomes the input hub (section 2) |
| Three more low-level hooks | `CleaningMode/KeyboardLock.cs`, `RadialMenu/Input/KeyRecorder.cs`, `Chippi.App/Island/IslandWindow.xaml.cs` (Esc and click-away while the panel is open) | The first two move onto the hub; the island's stays (only while the panel is open, observes only) |
| Global shortcuts: `RegisterHotKey` on a message-only window | `Sound/Mute/MicShortcut.cs` (`GlobalMicHotkey`), used by Mic mute, Audio routing, Paste as plain text, Quick actions, Cleaning mode, Command bar and Radial menu | Lifted to Core as the shortcut service (section 4) |
| Settings › Shortcuts | `Chippi.App/Settings/Pages/SimplePages.cs`: each of seven hard-coded feature ids adds its own `shortcut-list` view | One list from the shortcut service |
| Chippi's commands: everything Chippi does, from every running feature (`IModuleUi.AddCommands`) | `Chippi.Core/Modules/ChippiCommand.cs`, `ModuleRegistry.Commands()` | Already the list Search and the Radial menu's Chippi slice read. Phase 6 verbs join it |
| The Radial menu's action kinds: App, File or folder, Link, Key shortcut, Chippi, Media, Window, Text (typed with `KEYEVENTF_UNICODE`), Command, Selected text, Settings page | `RadialMenu/Slices/SliceCatalog.cs`, `SliceRunner.cs`, `Pc/IRadialPc.cs`, the slice editor in `RadialMenuSettingsPage.cs` | Lifted to Core as the shared action model (section 3) |
| Window verbs: Snap left and right, Maximize, Minimize, Close, Keep on top, Move to other screen | `RadialMenu/Pc/WindowsRadialPc.cs` | Start of Snap's window code |
| Actions gallery: a fixed `QuickActionId` list; Plan, Eject, Quit and Power step into a list (`OpensList`) | `Chippi.Features.QuickActions` | Gains tiles other features add (section 3) |
| Windows' Alt+Tab list rules | `QuickActions/Windows/AltTabApps.cs` (Quit apps) | Start of the App switcher's window list |
| Fullscreen detection: D3D fullscreen, presentation mode, a foreground window covering its monitor, from WinEvent hooks | `Chippi.App/Island/ForegroundTracker.cs` | Its test lifted to Core as the pause check (section 5) |
| The app that was in front before the island opened | `ModuleContext.IslandPasteTarget` | Target of a window verb run from an Actions tile |
| Shared Settings pages built from several features' sections | `SettingsWindow.SharedPages` (Sound's Output and Microphone) | Settings › Mouse and Keyboard under Windows |

## 2. One input hub

One hub, a new WPF-free project `Chippi.Input`, owns a single `WH_KEYBOARD_LL` and a single `WH_MOUSE_LL` on one dedicated thread. Every feature that needs raw input plugs a synchronous filter into it. Features reach it through `ModuleContext`, the way they reach the clipboard hub.

Why one hub and not one hook per feature:
- **Order.** Windows calls low-level hooks newest first, so separate hooks in one process run in whatever order they were installed. In the hub the order is fixed (below).
- **Cost.** Each hook is a cross-thread round trip on every mouse move, up to 1000 a second on Travis's mouse. Five features with their own hooks pay that five times.
- **Silent removal.** Windows drops a low-level hook that answers too slowly and doesn't say so. One hook gets one watchdog; none of today's four would notice.
- **One place for the shared rules:** Chippi's own input passes untouched, input other programs send counts as a hand's, a swallowed down always swallows its up (and its repeats, unless the filter that swallowed it lets one pass: then the key is the app's again, repeat and up), modifier masking, and the pause.

Filter order:

1. Cleaning mode lock (not Phase 6; never paused).
2. Debounce: drops chatter, sends nothing.
3. Close protection: swallows the first Alt+F4, passes the second.
4. Shortcuts' hook triggers (Caps Lock combos, mouse buttons) and the Radial gesture. They share buttons: a click goes to the shortcut, a 250 ms hold opens the wheel.
5. Smooth scrolling: swallows a wheel notch, sends a stream of small deltas.
6. The snippet watcher: reads typed characters, never swallows.
7. Later, only if Travis turns on the App switcher's Alt+Tab option: Alt+Tab.
8. The key recorder, while Settings records a shortcut: it sees what the filters above passed.

Rules for filters (the hub enforces what it can; review enforces the rest):
- A filter answers from memory in microseconds: no I/O, no locks shared with the UI thread, no COM or UI Automation, no process lookups. Per-app rules read a foreground-app snapshot the hub keeps from the foreground WinEvent.
- Actions run on the UI thread or a worker after the hook returns, as `WheelHook` does now.
- A hook is installed only while an active filter needs its device, and comes off once the last swallowed key or button is up.
- **Modifier masking:** after swallowing a chord that used Alt or Win, the hub sends one unassigned key (VK 0xE8) before the modifier's up, so Alt doesn't open the app's menu bar and Win doesn't open Start. PowerToys and AutoHotkey do the same. Shortcuts needs it.
- **One clock:** filters time events with `Stopwatch.GetTimestamp()` taken on entry to the hook, never the event's own `time` field, which is millisecond-coarse and may tick at the system timer's 10 to 16 ms (as long as Debounce's windows). Tests inject a clock that wraps `Stopwatch`.
- **Watchdog:** when `GetLastInputInfo` shows input the hub hasn't seen for 5 s while a hook should be in, reinstall it and log it, at most once a minute. Over an admin window the hub sees nothing by design (section 6), so a reinstall there costs nothing but a log line.
- **Nothing stays held.** The hub keeps the set of modifiers Chippi has sent down and not yet up (Shortcuts' held modifiers, section 7). It sends their ups, as one batch marked as Chippi's, when the key that held them goes up, the filter turns off, Chippi quits, the watchdog finds the hook gone, the session locks or the desktop switches (`WTS_SESSION_LOCK`, `EVENT_SYSTEM_DESKTOPSWITCH`), a pause begins, or no key event arrives for 5 s while one is held. A crash skips all of these; a modifier it leaves down clears the next time that key is pressed and released. (A held key keeps repeating, so the 5 s timeout never cuts a real hold short.)

Measure in the hub PR: the hook's time per event (target p99 under 50 microseconds) and Chippi's CPU over 60 s with the mouse moving, three readings, as AGENTS.md describes.

## 3. One action model, many doors

Travis (task 208): every verb is one shared action, and any door can fire it: an Actions tile, Search, a Radial slice, a key shortcut, a Caps Lock combo or a mouse button.

- **Chippi's commands stay the list of what Chippi does.** Phase 6 features add their verbs through `IModuleUi.AddCommands` (keys like `snap/left`, `app-switcher/open`, `snippets/<id>`) and their on/off as commands too. Search and the Radial menu's Chippi slice see them with no further work. A command that is an on/off gains an `IsOn` state, so a tile and a Search row can show it.
- **The Radial menu's action kinds become Core's action model** (`ChippiAction`: kind, value, name, glyph), with its runner and its `IRadialPc` behind a Core interface. Submenu stays the Radial menu's own. Shortcuts binds a key, Caps Lock combo or mouse button to a `ChippiAction` and uses the same picker, lifted out of the Radial menu's slice editor into `Chippi.UI`.
- **One home for window verbs.** The Window kind is the only place a window verb lives. Snap adds its verbs to it (Center, Restore, Next monitor, its zones), so "Snap left" exists once, whichever door fires it. Its target depends on the door: the window under the pointer (Radial, mouse buttons), the foreground window (keys, Caps Lock combos), or the window that was in front before the island opened (Actions tiles, Search).
- **Actions tiles from other features** (Travis: grouped into menus, like Plan, Power and Quit). Today's gallery holds a fixed `QuickActionId` list. It gains tiles that other features add: a tile is a command, a command with `IsOn`, or a menu of commands that steps into the page like Plan. Phase 6 adds three: a Snap menu, Input (a menu of Phase 6 switches), and the App switcher. A tile whose feature isn't installed is offered nowhere, and keeps its gallery position while it's gone, as Cleaning does now. Lumen's mockup (task 212) sets their look.
- **Actions regroups in wave 1** (Travis, tasks 224 and 225, option B). Lock, Turn off displays and Screen saver become rows in Power; End frozen apps moves into Quit apps: a frozen app's row shows "Not responding" and its X ends it, and the footer gains End beside Quit all, both confirmed first. Their four tiles leave the gallery and Lock keeps no tile of its own, so the gallery is 18 tiles with Snap, Input and Switcher. The four stay `QuickActionId`s with the same command keys (`quick-actions/lock` and the rest), so Search, Radial slices and their shortcuts keep working. `QuickActionsLayout` closes up the four freed slots once (the remaining tiles keep their order; `RenderGrid` draws an empty slot as a gap, and gaps Travis left himself stay) and appends a new tile after the last one when its feature is first installed, instead of reserving it a slot. The brief's Actions section has the details.

## 4. One shortcut service

`GlobalMicHotkey` moves from Sound to a Core shortcut service, still `RegisterHotKey` on a message-only window.

- It knows every shortcut Chippi owns, with its owner and name, so Settings › Shortcuts lists them from the service and flags clashes between Chippi's own, instead of seven hard-coded ids.
- A failed `RegisterHotKey` means another app holds it, and the row says so.
- `RegisterHotKey` works over admin windows and needs no hook.
- **Win combos can be bound** (Marge's call). The service keeps a list of the combos Windows itself uses (Win+L, Ctrl+Alt+Del, Alt+Tab, Alt+F4 and Windows' own Win combos, Herb's `shortcuts.md`): one saves, shows "In use by Windows" and doesn't act. A free Win combo goes through `RegisterHotKey` like any key. When `RegisterHotKey` refuses it, another app holds it: the row shows "In use by another app" and Chippi never takes it over with a hook (Marge, 2026-11-05).
- Clashes are found only with apps that also use `RegisterHotKey`; a game's own key binding isn't visible to Windows.
- Shortcuts (Herb's brief) adds custom bindings, Caps Lock combos and mouse buttons on top. The App switcher's own shortcut is one of its rows.

## 5. The pause and Always on

Travis (tasks 198, 211): one **Always on** switch covers every Phase 6 input feature, Debounce included, off by default. Off, they pause while a fullscreen game or app is in front. What acts on input in the background also starts off, even once installed, until Travis switches it on (task 226, the rule in [README.md](README.md)); what he triggers himself (the Snap menu, the App switcher's tile and Search, snippets picked in Search, shortcuts he binds) works once installed.

- **The check:** `ForegroundTracker`'s fullscreen test moves to Core: D3D fullscreen, presentation mode, or a foreground window covering its whole monitor (catches borderless games). The island keeps its own rule (it hides only on its display) on top of the same test.
- **Paused** means the foreground window is fullscreen and Always on is off. Keyboard input goes to the foreground app and a fullscreen game holds the mouse, so the foreground window is the right test on two monitors: click the browser on the Samsung and the browser is in front, so nothing pauses.
- **Windowed games** that don't cover their monitor aren't detected, so one shared list, "Pause in these apps", counts as fullscreen (Travis, task 227; Always on's place in Settings › General and pre-Phase 6 shortcuts never pausing were decided with it).
- **Pause only stops new gestures.** A key or button swallowed before the game came to front still has its up swallowed, so nothing sticks.
- **While paused,** a filter that's paused passes everything; when no unpaused filter needs a device, its hook comes off, so a game's input doesn't pass through Chippi at all.
- **Covered:** Debounce, Close protection, Smooth scrolling, Shortcuts (key shortcuts too: they're unregistered while paused, so the game gets the key), Text snippets' typed abbreviations, Snap's Shift+drag zones, the App switcher's shortcut, Focus follows mouse.
- **Focus follows mouse** uses Windows' own setting (section 7), so pausing it means Chippi turns that setting off while a fullscreen app is in front and back on after. Otherwise moving to the other monitor would pull focus out of the game.
- **Not covered:**
  - The Radial menu. Settings › Radial menu gets its own switch for opening over fullscreen apps, on by default (task 211, #2). Off, its gesture filter passes everything while a fullscreen app is in front.
  - Shortcuts that existed before Phase 6 (mic mute, output switch, Paste as plain text, Quick actions, Cleaning mode, Search's Ctrl+Alt+Space). Mic mute in a game is their point.
  - Search (the Command bar feature, the home's Search tile), and a snippet picked there (Marge's call): it's typed into the window Search opened over even when that's a fullscreen app, because it was chosen on purpose.
  - Cleaning mode's lock and the key recorder.

## 6. Limits every hook feature shares

- **Admin windows.** Chippi runs as a standard user. Windows doesn't call its hooks while an elevated window (Task Manager, an admin terminal, regedit, an installer) is in front, and drops input Chippi sends to one (seen in Phase 5). The way around it, a `uiAccess` manifest, needs a signed exe installed under Program Files; Chippi's installers are unsigned and install per user, so it's out. Running Chippi elevated isn't worth it. So over admin windows every hook feature does nothing, Caps Lock acts as plain Caps Lock, Snap can't move the window, and Close protection doesn't catch Alt+F4. `RegisterHotKey` shortcuts still work there. Each brief says this; PowerToys documents the same limit.
- **Anti-cheat** (public reports, not tested on Travis's games): low-level hooks are what Discord, OBS, PowerToys and AutoHotkey use and aren't treated as cheating by themselves. Some competitive games watch for injected input (the flag on every `SendInput` event) and ignore it or treat it as a macro. With Always on off, Phase 6 features send nothing into a fullscreen game; the Radial menu still can (key shortcut and text slices, its virtual pointer) while its Open over fullscreen apps switch is on, the default. With Always on on, Smooth scrolling, Shortcuts and Text snippets send input into the game; Debounce and Close protection only drop or pass input.
- **Other remappers.** Travis runs none. Where two tools at once would break each other: PowerToys Keyboard Manager or AutoHotkey remapping Caps Lock (the second sees the first's injected key as a hand's), FancyZones (two zone overlays, both move the window), an Alt+Tab replacement. Mouse vendor apps remap in the driver, before hooks, so they don't clash.
- **Windows' own Snap** keeps the plain drag to the top edge (task 211, #5); Chippi's zones show on Shift+drag only, so the two never show at once.

## 7. Per feature

Sizes: **S** about a day, **M** a few days, **L** a week or more, each one PR.

**Snap: M for the Actions menu and keys, L with the Shift+drag zones.** Placement with `SetWindowPos`, correcting for the invisible resize border (`DWMWA_EXTENDED_FRAME_BOUNDS`), restoring a maximized window first, and moving then sizing across monitors with different scales (the window rescales itself on `WM_DPICHANGED` in between). Zones: no hook for the drag, `EVENT_SYSTEM_MOVESIZESTART/END` WinEvents with Shift read at the time, and a topmost, non-activating, click-through overlay. Win+arrow keys belong to Windows' Snap, so Chippi's keys are the shortcut service's. Saved app arrangements with relaunch come later in the phase (task 211, #4): windows identified by app path, class and title, monitors by the `\\?\DISPLAY#...` id the widget already stores. Limits: admin windows don't move; some apps enforce their own minimum size; UWP apps move through their frame window.

**App switcher with previews: L.** Opened by its own shortcut from the shortcut service; taking over Alt+Tab is a later option (task 211, #6) and then needs the hub with Alt masked. The list follows Windows' Alt+Tab rules (`AltTabApps`: visible, not cloaked, which also hides other virtual desktops, owner and tool-window styles), ordered by Chippi's own most-recent list from the foreground WinEvent, falling back to z-order for windows older than the session. Previews are live DWM thumbnails. **Probe:** a DWM thumbnail draws fully inside a WPF `AllowsTransparency` window, as in a plain one (`DwmRegisterThumbnail` and `DwmUpdateThumbnailProperties` both S_OK, 69.3% of the window covered in both), so the switcher can look like the island. A thumbnail draws above WPF content in its rectangle and can't be clipped to rounded corners, so it's inset, not rounded; a minimized window has no live preview, so it shows its icon, like Windows. Switching uses the island's `ActivateWindow`; a spike first checks the hand-off never leaves focus nowhere.

**Close protection: S.** Keys only (task 211, #7). The first Alt+F4 (and Ctrl+W in the apps it's turned on for) is swallowed and the bar shows "Press again to close" over "Alt+F4 · Notepad" (task 264), or Windows' default beep plays when the bar can't show (task 263); a second press while the line shows (6 s, task 262) passes through untouched. On the first Alt+F4, Chippi sends one marked inert-key pair through the hub (VK 0xE8) to prevent Alt opening the menu bar (Travis, task 293). It sends no close chord. Not covered: the close button, the taskbar's Close window, an app's own menu, admin windows.

**Focus follows mouse: S.** Windows has it built in: `SPI_SETACTIVEWINDOWTRACKING`, with the window raised (`SPI_SETACTIVEWNDTRKZORDER`) after 250 ms (`SPI_SETACTIVEWNDTRKTIMEOUT`). No hook, and no fights with the foreground lock that an own version would have. Chippi records the setting it found and puts it back when the feature is off, paused, or uninstalled, and after a crash at the next start.

**Smooth and linear scrolling: M, plus an app matrix.** The hub swallows a hardware wheel notch and sends small wheel deltas, marked as Chippi's, over the scroll's duration from a high-resolution timer at about the display rate. Ctrl+wheel and Shift+wheel pass untouched. Apps that treat every wheel message as a whole notch scroll far too fast, and browsers already smooth-scroll, so smoothing stacks: per-app speeds and the excluded-apps list ship in the first build (task 211, #8). Linear is a curve inside Smooth scrolling, not its own feature. Only wheel mice; touchpads and pens pass.

**Debounce: S.** Second filter in the hub. Drops a down that follows the same button's or key's up within the threshold, and that down's up, timed on the hub's `Stopwatch` clock (section 2). Key auto-repeat (downs with no up) always passes. Sends nothing. Key debounce tops out at 200 ms.

**Shortcuts (Mouse buttons, Super key and Shortcut manager as one feature): L, in two PRs.**
- Key shortcuts on the shortcut service, plus the Settings › Shortcuts page with every binding and its clashes.
- Hook triggers: Caps Lock combos and mouse buttons (Middle, Back, Forward, wheel tilt, optionally with a held modifier, per app). The hub swallows Caps Lock (no toggle, no light) and its repeats, and a button's down and up; the binding runs a `ChippiAction`. A key with no binding passes with the held modifiers down (Ctrl+Alt+Shift by default, or the ones set in Hold modifiers), always while the Caps Lock door is on: the modifiers go down with the key and up with it, and the hub's release path (section 2) covers every other way out. A chord a binding sends is one `SendInput` batch, down and up together. No mouse-button drag gestures (task 211, #9). Right Alt isn't offered and Win isn't in the default set (#10). If a binding sends Ctrl+Alt+Shift+Win, that's Windows' "Office key", which opens Office apps with W, X, P and others; Herb's brief settles it. Over admin windows Caps Lock toggles as normal.

**Text snippets: M engine, M editor.** The hub's snippet watcher reads typed characters into a buffer as long as the longest abbreviation, using `ToUnicodeEx` with the flag that leaves keyboard state alone (Windows 10 1607 and later), so dead keys and accents keep working. It never swallows a key. On a match the UI thread checks the focused field isn't a password (UI Automation `IsPassword`), then sends backspaces and types the text with `KEYEVENTF_UNICODE` (the Text action), in chunks, since one long batch drops characters in some apps (Remote Desktop, some terminals). Never pasted, so the clipboard is untouched (task 211, #11). The buffer is memory only, never logged, and clears on a click, a focus change and after each expansion. Skipped while an IME is composing.

## 8. Testing without the owner's input

| Level | Runs where | Covers |
|---|---|---|
| Pure filters on an injected clock | CI | Debounce timing, Caps Lock tap and hold, snippet matching, the double Alt+F4, the scroll curve, button sharing with the Radial gesture, pause rules |
| The hub with a fake input source and a recording sender | CI | Order, swallowed down means swallowed up, Chippi's marker passes, masking, hooks coming off, the watchdog, the held-modifier release on every way out |
| Test-owned windows on the desktop | Local and CI | Snap's math and `SetWindowPos` on the tests' own windows, the switcher's list and thumbnails of its own windows, switcher shots |
| Windows Sandbox (`.wsb`, clipboard redirection and networking off; the hub PR builds the runner) | Local, by an agent | Real hooks: a probe sends `SendInput` without Chippi's marker, so it counts as a hand's, into a test app and reads what arrived. Snippets in Notepad, Caps Lock combos, debounce on fast synthetic double clicks, the scroll stream into a test view, Shift+drag to a zone, the Focus follows mouse setting |
| Travis's live checks | His desk, at a time he sets | How scrolling feels, his real buttons, both monitors and their scales, the app matrix, a game with Always on off and on |

Sandbox has one monitor and no real mouse feel, so mixed-scale snapping and scroll feel can only be proven in the live checks.

## 9. Build order

The waves are Marge's ([README.md](README.md)).

**Wave 1:**
1. The input hub: `Chippi.Input` and `Chippi.Input.Tests` (new in `Chippi.slnx`); the Radial gesture, the key recorder and Cleaning mode's lock moved onto it; the watchdog; the Sandbox runner. Shared code with Radial menu regression risk, so its tests come along.
2. Shared actions, the shortcut service, the pause and Always on, the Radial menu's fullscreen switch, and the Actions regroup.
3. Debounce (S), Focus follows mouse (S), Close protection (S).

**Wave 2:** Shortcuts (L, two PRs), Smooth scrolling (M, the app matrix in its live check).

**Wave 3:** Text snippets (M and M), Snap (M, then L with zones; saved arrangements after), the App switcher (L, the focus spike first).

## 10. Not verified

- Hooks going quiet over admin windows and those windows not moving: from Microsoft's UIPI docs and PowerToys' documented limits, not tried here (it needs a UAC prompt on Travis's PC).
- Anti-cheat: public reports only.
- Whether DWM thumbnails of admin windows show in the switcher.
- The switcher's focus hand-off: spike.
- Both displays' scales: not read; matters only for Snap's live check.

Sources: [UAC and uiAccess locations](https://learn.microsoft.com/en-us/windows/security/threat-protection/security-policy-settings/user-account-control-only-elevate-uiaccess-applications-that-are-installed-in-secure-locations), [LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc), [ToUnicodeEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-tounicodeex), [SystemParametersInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow), [DwmRegisterThumbnail](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmregisterthumbnail), [DWM thumbnail overview](https://learn.microsoft.com/en-us/windows/win32/dwm/thumbnail-ovw).
