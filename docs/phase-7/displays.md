# Displays and brightness: brief

Wave 1. Owner: Kayla (engine and wiring). Screens: Lumen (`design/wave-1.html` in #284, boards `sheet-displays` and `settings-displays`). Reviewer: Karl. Task 490. Status: for Travis's confirmation with the wave 1 mockups. Nothing is built yet.

[Decisions](README.md) override the research. References: [behavior spec B §5](behavior-spec-b.md#5-displays-and-brightness), [architecture §8](architecture.md#8-displays-and-brightness), [surfaces §5](surfaces.md#5-displays-gets-a-tile).

Decided (Travis, tasks 486 and 487, 2026-11-06): Displays gets the 12th home tile. Brightness keys act on the monitor under the pointer, 10% a press, changeable in Settings. DDC/CI writes are limited to brightness and contrast.

## Job and doors

Set each monitor's brightness from one click on the home, or with a key, without the monitor's own buttons.

| Door | What it does |
|---|---|
| Home | **Displays** tile, Order 65 (after System). A saved order in Settings › Island & pet is kept; the new tile lands by its default order. |
| Displays panel | Header: Back, "Displays", a Brightness / Contrast switch on the right. An All displays row (left out with one display), then a 44-tall card per display: its name, "· Main" on the main one, a slider and the level. Up to five rows: four displays and All displays. |
| Search | "Brightness up", "Brightness down" and "Displays" as Run rows. "brightness 40" gives value rows (below). |
| Radial menu, action picker | Brightness up, Brightness down, Open Displays. |
| Shortcuts | Three slots, all unassigned: Brightness up, Brightness down, Open Displays. |
| Bar | Only "Brightness control failed" after a key press that couldn't change the display. A change that works shows on the monitor itself. |
| Settings › System › Displays | The shortcut rows read-only with Edit; Brightness keys (Act on, Step); each display with its level and what it supports, and for one that doesn't answer, the likely cause and Try again. |
| Actions | Nothing. Extend and Turn off displays stay where they are; they're Windows display modes and power, not brightness. |

The tile's tooltip names the levels ("OMEN 34c 54% · Samsung 60%").

## Behavior

**Reading.** External monitors use DDC/CI through `dxva2`: `GetPhysicalMonitorsFromHMONITOR`, then `GetVCPFeatureAndVCPFeatureReply` for VCP 0x10 (brightness) and 0x12 (contrast). The level shown is `current × 100 / max`, using the maximum the monitor reports. A laptop panel uses WMI (`root\wmi`, `WmiMonitorBrightness` to read, `WmiMonitorBrightnessMethods.WmiSetBrightness` to write) through CsWin32's WMI COM interfaces, with no new package. A laptop panel has no Contrast.

- **Support is a read that answers.** A display supports brightness when a read of 0x10 succeeds, and contrast when a read of 0x12 does. The capabilities string isn't read at all: it took 1.3 to 6.6 s on Travis's monitors, and a direct read answers the same question in about 60 ms. This replaces architecture §8's cached capabilities.
- **One worker per monitor.** Every DDC call for a monitor runs on its own background worker, never the UI thread. The two monitors are read in parallel.
- **When it reads:** at feature start, when the panel opens, and after a display change (`WM_DISPLAYCHANGE`, which also reopens the physical monitor handles). Never on a timer. The panel opens filled with the last levels read and updates the rows when the fresh reads land.
- **Displays come from `DisplayCatalog`**, with their names, the main display and their ids. A display that's turned off or unplugged (Win+P to one screen) leaves the list.

**Writing.**

- **Latest value wins.** A slider sends only its latest value: while a write is in flight, a newer value replaces the queued one. When the write returns, the queued value goes next. This paces writes to the monitor's own speed (a write took 50 ms in Microsoft's docs) with no fixed throttle.
- **Skip writes that change nothing.** A value equal to the last one written or read isn't sent, since many monitors store each write in their own memory.
- **Release always lands.** The value under the thumb at release is always written, after any write in flight.
- **Only VCP 0x10 and 0x12** are ever written. No input switching, power, volume, color or factory reset codes.

**Brightness up and down.** Each press moves the target display by the step (10% by default) and stops at 0 and 100. The target is the display under the pointer when the key is pressed (`MonitorFromPoint`), or every display when Settings says All displays. Holding the key steps once: the shortcut service registers with `MOD_NOREPEAT`, like every Chippi shortcut. A press while a write for that monitor is in flight adds to the queued value, so three fast presses move 30%. The keys follow the shortcut service's rule for fullscreen apps, like every other shortcut.

**All displays.** Its slider moves every display together and keeps their differences: dragging it up 10 moves each display up 10. A display that reaches 0 or 100 stops there while the others go on. The differences are taken when the drag starts, so a display that stopped at a limit stays there. The row shows the main display's level. (Proposed: this is Monitorian's "in unison". The alternative is to set every display to the same level, which loses the offset between two different panels.)

**Contrast view.** The header's switch shows Contrast sliders for the displays that answered 0x12. A display without contrast isn't listed in that view. With no display answering 0x12, the switch isn't shown.

**Search values.** "brightness 40" (or "bright 40", with or without %) gives "Brightness to 40%", which acts on the keys' target, then a row per display ("OMEN 34c to 40%"). Values over 100 are 100. "brightness" alone gives a row per display with its level, like "output" alone.

**Try again** (Settings, on a display that doesn't answer) reopens that display's handle and reads it again; on success the display joins the panel. There's no Reset: a display left too dark comes back with Brightness up or the panel.

## Edge cases

- **A display that doesn't answer** (DDC/CI off in the monitor's menu, a dock or adapter that doesn't pass it, VGA or DVI, a model without DDC/CI): its card shows the name and "Brightness control failed", with no slider. A first failed read is retried once after 500 ms, since DDC/CI fails now and then. Chippi can't tell the causes apart, so Settings › Displays names the likely ones under that display with Try again; the panel doesn't.
- **A write that fails mid-drag:** the card shows "Brightness control failed" and the slider goes back to the last level read. The next drag tries again.
- **A key press on a display that doesn't answer:** the bar shows "Brightness control failed" for 6 seconds; nothing else changes.
- **The monitor changed by its own buttons:** Chippi learns it at the next read (panel open). A key press reads first if the last read is more than 10 seconds old, so it steps from the real level, not a stale one.
- **Monitors added, removed or rearranged while the panel is open:** the rows follow `WM_DISPLAYCHANGE`. A drag on a display that leaves ends with nothing written.
- **Two monitors of the same model:** names come from `DisplayCatalog`; same names get " 1" and " 2" in display order.
- **Six or more displays:** the panel shows the first four by display order plus All displays; Settings lists every display.
- **Sleep and resume:** monitor handles are reopened after resume (`WM_POWERBROADCAST`), then read.
- **Stopping the feature** waits for writes in flight (at most one per monitor) and drops queued ones.

## Shortcuts

Under the README's starter rule (task 482):

| Verb | Slot | Default |
|---|---|---|
| Brightness up | Global, `displays/brightness-up` | Unassigned |
| Brightness down | Global, `displays/brightness-down` | Unassigned |
| Open Displays | Global, `displays/open` | Unassigned |
| Move a slider, switch Brightness and Contrast | In context: the panel's own keys (arrows and Page up/down on a focused slider, Tab) | Fixed |
| Try again | In context: Settings button on a display that doesn't answer | Fixed |

The feature adds the three slots to `ModuleContext.Shortcuts` when it starts and disposes them when it stops. The panel and Settings show them read-only with Edit.

## What we took

| Tool | Taken | Left out |
|---|---|---|
| Twinkle Tray | One click to a slider per monitor; hotkeys that step by an amount (its Offset); the list of causes for a monitor that doesn't answer | Time-of-day schedules, idle dimming, a command line, normalizing different panels |
| Monitorian | All displays moving together, keeping each display's offset; plain causes ("Turn on DDC/CI in the monitor's menu") | Input source, color temperature |
| ClickMonitorDDC | Nothing: its documentation couldn't be confirmed (spec B) | |
| Windows' own slider (laptops) | The same WMI path, so a laptop panel works like a monitor | |

## Copy

Every string on screen. Marge runs them through Decide before the installer.

- **Tile:** Displays. Tooltip: "<name> <level>%" for each display, joined with " · ".
- **Panel:** Displays; Brightness; Contrast; All displays; Main (as "· Main"); "<level>%"; Brightness control failed.
- **Bar:** Brightness control failed.
- **Search:** Displays; Brightness up; Brightness down; Open Displays; Brightness to <n>%; <name> to <n>%; "<name> · <level>%" rows. Verb: Run.
- **Settings › System › Displays** (as Lumen's `settings-displays` board): Displays (page title); Shortcuts, with Brightness up, Brightness down, Open Displays, Not set and Edit; Brightness keys, with Act on (Display under the pointer, All displays) and Step (5%, 10%, 20%); Displays, with each display's name and "<level>%", and "Main display · Brightness and contrast", "Brightness and contrast" or "Brightness" under it; for a display that doesn't answer, "Brightness control failed · Turn on DDC/CI in the monitor's menu, or connect it without a dock" and Try again.

## Shared code

- **New projects** (approved by Marge, 2026-11-06): `Chippi.Features.Displays` and `Chippi.Features.Displays.Tests`, both added to `Chippi.slnx`.
- **Chippi.UI:** `DisplayCatalog` gains a `Changed` event raised on `WM_DISPLAYCHANGE` and resume, from one hidden window the shell owns, so Displays, Capture and Snap share it rather than each listening. This is the only shared change.
- **Island:** the Displays tile and page are Lumen's, through the existing `IslandTileSpec` and page host. No island code changes beyond the catalog order.
- **App:** the module registration and the Settings page. Displays is already in the catalog (`displays`, System group, Essentials preset).
- **Core and the shortcut service:** no change.

## Tests

- **CI, with a fake monitor bus** (`IMonitorBus`: open, read, write, close per monitor, with scripted delays and failures, and an injected clock):
  - latest value wins while a write is in flight; release always lands; equal values aren't sent; nothing runs on the calling thread
  - All displays keeps offsets, stops a display at 0 and 100, and takes the offsets at drag start
  - keys step by the setting, add to a queued value, read first after 10 seconds, stop at 0 and 100, act on the display under a given point or on all
  - a failed read retries once after 500 ms; a failed write puts the slider back; Try again reopens and reads only that display
  - a display removed mid-drag gets no write; handles reopen after a display change and after resume
  - same-name displays get " 1" and " 2"; four displays plus All displays fit, a fifth goes to Settings
  - "brightness 40", "bright 40%", "brightness 140" and "brightness" alone give the right rows
  - writes are only ever VCP 0x10 and 0x12 (the bus refuses any other code, and the test proves nothing asks for one)
- **Offscreen shots** (`CHIPPI_ISLAND_SHOTS`, both themes) beside Lumen's boards: two displays and All displays, one display, four displays, the Contrast view, a display that doesn't answer, Settings at full and 860 width with a failing display's causes.
- **Read-only check on Travis's PC** (a dev profile with only Displays): reads both monitors' brightness and contrast and times the reads and the panel's open. No write.
- **Write check on Travis's PC, once, at the end:** I post a warning in the room first, run `build/fullscreen-check.ps1` and check he's idle, read both levels, then drive the panel's sliders through UI Automation (no real clicks): each monitor up 10 and back, All displays up 10 and back, two key presses through the shortcut path. The levels read at the start are written back, and read again to confirm. No Sandbox run: the Sandbox's virtual display has no DDC/CI.

## Done means

One feature PR with Lumen's screens and the engine, CI green on x64 and ARM64 and the test job, Karl's review, then the write check above with these timings:

| What | Target | How |
|---|---|---|
| Displays panel open | Opens filled from the last levels read, with no DDC call before its first frame; UI-thread work under 16.7 ms; the island's page-switch frames no worse than Audio's | Frame probe on page switch, 10 cold and 10 warm |
| Fresh levels after open | Rows updated within 300 ms (two monitors read in parallel, about 60 ms per read) | Stopwatch from open to the last row updated |
| Slider drag to the monitor changing | First write starts within 16.7 ms of the first drag move; each write returns within 100 ms at p95; the release value is written within 200 ms of release; UI-thread work per drag move under 2 ms | Timestamps around `SetVCPFeature` during the UI Automation drag; Travis judges the monitor's own response by eye |
| Key to the monitor changing | Write starts within 16.7 ms of `WM_HOTKEY`; returns within 100 ms at p95 | Timestamps in the write check |

Screenshots of the panel, Contrast view and Settings next to the boards, and the timings report, go to the room. Travis's live check covers both monitors by eye, the keys over each monitor, and Win+P to one screen and back.

## Not verified

- DDC writes on Travis's monitors, and how fast each monitor visibly responds (reads only so far: 55 to 65 ms).
- That a read of 0x10 or 0x12 is a reliable support test on every monitor; some monitors answer reads and ignore writes (inference from Twinkle Tray's notes).
- The laptop WMI path from a standard user: no laptop here, so it's tested only with the fake.
- Whether brightness 0 is dark enough on some panels to be hard to read; Brightness up is the way back.
- Displays behind docks and adapters: Travis's monitors are connected directly.
