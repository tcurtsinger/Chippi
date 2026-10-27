# System monitor mockup: notes (2026-10-26)

Inside the Surfaces model (PR #52). Every reading in the mockup is a sample.

**Files**
- `index.html`: all seven boards. Add `?board=<id>` to see one board alone.
- `shots\*.png`: 2x renders of each board.
- `render.ps1`: renders the PNGs again with headless Edge.

| Board | What it shows |
|---|---|
| `grid-top` | All features, top edge |
| `page-top` | System monitor page, top edge (Travis's desktop) |
| `page-right` | The same page on the right edge |
| `docked` | Docked by the tray, island off |
| `states` | A laptop; after sleep, a failed read and an alert; logical processors open; the tile, tab and Windows notification during an alert |
| `settings-dark` | Settings › System monitor, dark (Travis's desktop) |
| `settings-light` | Settings › System monitor, light (a laptop) |
| `short` | The page and its round buttons fitting a 1280×720 laptop screen |

## Choices DESIGN.md doesn't make yet

### Tile
1. **Name and glyph:** "System monitor" with Lucide Activity (the catalog's icon).
2. **Grid place:** sixth, after File organizer, so the grid is two full rows. It joins Island & pet › Tiles and can be a round button like any tile.
3. **Status:** the one reading Settings › Tile status picks: "CPU 12%" (default), "Memory 39%", "GPU 31%" or "↓ 1.2 MiB/s". There's no dot normally, because monitoring isn't an activity.
4. **During an alert:** while an alert that's on is past its threshold, the status is that reading in Caution with the Caution dot ("Memory 93%"), as Sound does for "Mic muted". If several alerts hold, the first in the Alerts list shows.

### Page
5. **Layout:** 640 wide in two columns 16px apart, like Sound, with no column labels; each reading is its own card.
   - Left column: CPU, Memory, then Battery.
   - Right column: GPU, Network, then Thermal zone.
   - Disks spans the full width below.
6. **Header:** the right side shows "Last 4 min", the span the graphs cover (120 samples: 2, 4 or 10 min). DESIGN puts the tile's status there, but the page already shows that reading.
7. **Card header:**
   - The name is in section-label style (12 / 600 Caption).
   - The headline value is a new type role, **Reading** (20 / 400, tabular).
   - Only cards whose reading is a percentage or a single temperature get a headline value. Network has none.
8. **Graph:**
   - Size and scale: 36px tall, the card's full width, 120 samples. CPU, Memory and GPU are fixed at 0–100%. Network scales to a rounded maximum, shown top right in 11px Muted ("2 MiB/s").
   - Look: a 1.5px accent line over a flat 14% accent fill and a 1px baseline. The accent here works like Sound's level meter: a live level, not decoration. No gradient and no glow.
9. **Network's two lines:** Receive is solid accent with the fill; Send is dashed (3 2) in Secondary with no fill. Pattern and lightness tell them apart, as in Task Manager. Each fact label carries its line as a legend.
10. **Reading lines:** under the graph, one or two 12px lines in Chippi's "state · detail" style, instead of a grid of labeled facts.
    - Values are in Text and labels in Caption, joined by "·": "58 °C · Fan 1,120 RPM · 30%".
    - The units say most of it, so only memory figures and the fan get labels: Committed, Dedicated, Shared, Fan. The words follow Task Manager.
11. **Room for CPU temperature:** the CPU line reads "4.69 GHz". A later temperature joins it ("4.69 GHz · 62 °C") without adding a line. A CPU temperature alert would go after CPU load.
12. **Logical processors:**
    - A small button at the right end of the CPU line, "32 logical processors ⌄", opens them. Closed by default; it stays open for the session only.
    - Open, it's a strip 16 cells wide (two rows for 32), each cell 14px tall and filled from the bottom in accent to that processor's load, with its own accessible name.
    - Per-processor readings are sampled only while it's open.
13. **GPU:**
    - **Picker:** with more than one GPU, the name is a picker in Now Playing's source-picker style. With one GPU it's plain text. The Microsoft Basic Render Driver is never listed, and the card hides when no adapter reports load.
    - **Facts:** shown only when the driver reports them.
    - **Fan:** reads "Fan 1,120 RPM · 30%" (measured RPM, then NVIDIA's intended speed), or just the one value when only one is reported.
    - **Shared memory:** only the amount used. Its total is half of RAM and says little.
14. **Network adapter:**
    - The adapter's name always shows, so it's clear which one is measured. It's a picker only when there's more than one real adapter.
    - If the remembered adapter disappears and another real adapter exists, the card switches to it, and the new name shows the change.
    - If no real adapter is left, the name stays, the values read "Not connected" and the graph stops.
15. **Disks:** one table with 24px rows. The card's name, "Disks", heads the first column ("Disk 0 (C:)", Task Manager's name); then Active time, Read speed, Write speed and Free space.
    - Free space has one line per volume on that disk: the letter, a used-space bar, then the amount free.
    - The total size appears only in the accessible name, so each volume fits on one line.
    - There are no disk graphs.
16. **Battery and Thermal zone cards:** no graph, since they change slowly.
    - Battery's line: the state (On battery, Charging or Plugged in), the rate in W, and the time left on battery when Windows reports it: "On battery · 11.8 W · 2 hr 40 min left".
    - Several thermal zones are numbered "Thermal zone 1", "Thermal zone 2".
17. **Missing values:**
    - A reading that failed shows its status in Caption: "Temperature unavailable".
    - A reading the hardware doesn't support isn't shown.
    - Sleep and failed reads leave a gap in the graph.
    - A removed device reads "Not connected".
18. **Caution only while an alert is on:** the number in the card header, a volume's bar and its free amount turn Caution only when that alert is on and past its threshold. Graphs keep the accent, so a 93% memory graph doesn't become a yellow block. With the alert off, nothing turns Caution, not even a nearly full disk.
19. **Units:**
    - Above 1,024 GiB, sizes switch to TiB (the decided list stops at GiB).
    - Rates are KiB/s below 1 MiB/s, then MiB/s.
    - The bits option is Kbps below 1 Mbps, then Mbps.
    - Temperatures have a space ("58 °C"), and RPM has a thousands separator.
20. **Not on the page:** no settings link, no pause control and no time-range control.

### Docked
21. **Docked card:** the same page at the same width in DESIGN §5's docked card, with the All features / Settings footer.
22. **Short screens: the page is compact enough to fit** (Travis, 2026-10-26; board `short`).
    - The page is about 500px tall on Travis's PC (5 disks) and 450px on a laptop; it was 670px.
    - That fits with its round buttons on any edge of the smallest common laptop screen: 1920×1080 at 150% scaling, which apps see as 1280×720 (672px above the taskbar).
    - Budget for later rows: keep the page under 550px. There's no scrolling inside the island.

### Settings › System › System monitor
23. **Sidebar:** the System group lists Keep awake, then System monitor (catalog order).
24. **First card, no header:**
    - Update speed: 1 second, 2 seconds (default) or 5 seconds.
    - Tile status: CPU (default), Memory, GPU or Network, listing only readings that are on.
25. **Readings:**
    - A switch per reading, all on by default.
    - Battery and Thermal zone rows appear only on PCs that have them.
    - Turning a reading off removes its card and stops sampling it. If it was the tile's status, the tile falls back to the first reading still on.
26. **Units:**
    - Temperature: Celsius or Fahrenheit. This row shows only when some temperature is reported.
    - Network speed: Bytes per second or Bits per second.
27. **Alerts:** each alert is a switch row with a Threshold sub-row under it (indented like expander sub-rows), holding a slider and its value: "90%", "10% free", "15%".
    - The threshold dims while its alert is off, like Mic processing's levels.
    - Ranges and steps are as decided.
28. **GPU temperature:**
    - **Limit fields:** one sub-row per GPU that reports temperature, named for the GPU. Each is a °C number field that starts as "Not set". The field range, 40–110 °C, is my pick; there's no default limit.
    - **Switch:** the alert can be turned on once any limit is set. A GPU left "Not set" isn't watched.
    - **While off:** the fields stay editable, unlike the sliders, since the alert can't start without a limit.
29. **Rows only some PCs have:** Low battery only with a battery; GPU temperature only when a GPU reports temperature.
30. **Repeat interval:** the last row of Alerts, shared by every alert: 2, 5, 15 (default), 30 or 60 minutes.
31. **No on/off switch for the feature:** it samples only while something needs it, so there's nothing to switch off.
32. **Mockup size:** both windows are stretched to show the whole page; in the app the page scrolls. Sizes follow DESIGN.md and the shipped app (20px title, 272px sidebar), not the Surfaces mockup's larger title.

### Notices
33. **Notice kinds:** five under Notifications › System monitor: CPU load, Memory in use, Low disk space, Low battery and GPU temperature.
    - Proposed default destinations: Windows and Island both. The alerts are opt-in, so once someone turns one on it should be seen.
34. **Copy:** one line saying what happened: "CPU load 97%", "Memory in use 93%", "G: 8% free", "Battery 14%", "RTX 4080 86 °C".
    - No buttons.
    - A click opens the System monitor page, in the island, or docked while the island is off.
35. **Island:**
    - The closed tab adds the Activity glyph in Caution as a status activity, last in priority after media, timer, stash and agent, until the alert clears.
    - Gumdrop stays Idle; there's no new mood.

## Not drawn, by decision
CPU temperature, motherboard fans, fan control, network speed test, per-app rankings, SMART health and battery temperature.

## Found while reading
The catalog's description for `system-monitor` still says "in the tray". Marge's docs pass already lists this fix.
