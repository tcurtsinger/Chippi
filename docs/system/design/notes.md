# System page (proposed 2026-10-31, revised 2026-11-01)

Replaces the System monitor tables (the 2026-10-31 critique scored them 24/40). Open `index.html` to click through it; `render.ps1` writes `shots/`. Colors and sizes are the island and Settings tokens from `Dark.xaml` and `Light.xaml`.

Owner's choices (2026-10-31): clicking a widget opens a detail page in the island. The CPU, GPU and Memory pages list the top 3 apps. The feature is called "System" and the Settings group keeps its name "System".

Bolder and layout pass (2026-11-01; the owner took the recommended set): the detail pages' graph comes to the overview, the graphs fit their history, and the rows are sized by how fast their readings change. "Your PC" in the mock is the owner's PC as the 2026-11-01 renders read it.

## Overview (496 × 304, never scrolls)

- **Rows by meaning:** loads first, then Network, Battery and Temperature, then Drives. The graph rows share what Drives leave.
  - **Loads:** CPU, GPU and Memory at 160 each, or 244 each when there's no GPU. 116 tall on a desktop with 6 drives.
  - **Network alone:** 72 tall at full width: download and upload side by side on the left, their graph across the rest under the top line, its oldest readings fading in over 40 px.
  - **Network, Battery and Temperature together:** two or three side by side, as tall as the load row.
  - **Drives:** always the bottom row, at full width, in rows of 20, two drives to a row: the letter, a meter and "704 GiB free". Up to 3 rows (6 drives), giving one up while a graph row would be under 104. When they don't all fit, the fullest show (an alerting drive first) in letter order, and the last place reads "+N more". A single drive is one row with "455 GiB free of 953 GiB".
- **Widget anatomy:** each widget is a button.
  - **Top line:** a 14 glyph and the name in 12 tertiary, with one fact on the right in 11 tertiary (GPU temperature, the adapter, the drive count).
  - **Value:** 24 / 600 in tabular figures, with the unit at 14 in secondary.
  - **Context line:** 11 tertiary: clock, the GPU's name, "21.4 of 32.0 GiB", the upload rate, time left, "Hottest of 3 zones".
  - **Graph:** CPU, GPU, Memory, Network and Temperature draw their last 120 readings under the context line, out to the card's sides and bottom (inside its border): the detail page's accent line over its accent fade, at the detail page's scale, with no midline or scale label. Network adds send as a dashed line.
  - **Meter:** 4 px, gray on the track, for Battery and each drive: levels that change by the hour, where 4 minutes of graph would be a flat line.
  - **Alerts:** a widget whose alert holds turns its value, line and fade (or meter) Caution, so what needs you is the loudest thing on the page.
- **History:** the sampler keeps the last 120 readings of every reading the overview shows, all the time, so the overview opens with full graphs. On the owner's PC a read costs GPU load 2.8 ms and each other reading under 1 ms, about 5 ms every 2 seconds, added to the wake the Home status already has.
- **Hover:** a wash and a brighter border. A click opens the widget's page.

## Detail pages

- **Navigation:** the header reads "‹ CPU". Back or Esc returns to the overview, sliding in from the left; opening a page slides in from the right, using the island's page slide (12 px, 180 ms). The island reopens on the overview.
- **Top card:** whatever the cards under it leave (174 on CPU, GPU and Memory; the whole body on Network with one adapter).
  - The value at 32 / 600 with one secondary reading on the right.
  - A 120-sample graph from the sampler's history, from under the value out to the card's sides (and its bottom when nothing sits under it): an accent line over an accent fade and a dashed midline. Its scale is written under the secondary reading, where the line can't run over it.
  - **Scales:** CPU and GPU fit their history: 10, 25, 50 or 100%, so 4% CPU uses the graph instead of lying on its floor. Network doubles from 64 KiB/s. Temperature goes by 25 °C. Memory and Battery are levels and stay at 100%: a fitted scale would draw 42% as nearly full.
  - One 18 tall strip under the graph. The CPU's bars fill in the accent, at the graph's scale.
- **Cards under it** take the height their rows need. Top apps (3 rows) sit next to the facts (122 tall), their rows level across the two cards. The facts never repeat the top card.

| Page | Right of the value | Strip | Facts |
|---|---|---|---|
| CPU | Clock | One bar per logical processor | Processor name, logical processors, up time |
| GPU | Temperature | Dedicated memory meter | Fan, shared memory |
| Memory | "21.4 of 32.0 GiB" | Committed meter | Available, installed |
| Network | Upload as a second value | Legend (Receive solid, Send dashed) and the adapter | With 2 or more adapters, a list: one row each, the shown one tinted; a click graphs it. With one, no list: the legend names it |
| Drives | n/a | n/a | Space, two drives a row (letter and "412 GiB free of 931 GiB" over a meter, 36 tall), the same drives in the same order as the overview, then Activity per physical disk (active %, read · write) |
| Battery | Time left | n/a (taller graph) | State, rate |
| Temperature | "Hottest of N zones" | n/a (taller graph) | A tile per zone, sharing the width 4 to a row |

- **GPU picker:** with two or more GPUs, a picker in the header shows the measured GPU's name. It replaces the old "GPU ⌄" label.
- **Network adapters:** the list replaces the old adapter popup.
- **Top apps:** apps are grouped by executable (every chrome.exe counts as Chrome) and show their real icons; the mock draws stand-in letter tiles.
  - The page samples processes only while it's open.
  - CPU and Memory read process times and private working sets. GPU reads the GPU Engine counters per process.
- **Overflow** (as built):
  - Drives lists as many volumes as fit above up to 4 disks' activity (6 on a 4-disk desktop), then "+N more in Settings".
  - Adapters lists up to 3, then "+N more in Settings".
  - Temperature lists up to 8 zones, then "+N more in Settings".

## Settings › System

- **Live widgets:** the island's widgets in the island's rows (CPU, GPU and Memory, then Network, Battery and Temperature; Network alone at full width), in Settings card style, with graphs and meters in the Windows accent. A laptop gets 3 and 3, not 4 and 2.
- **Drives:** Windows Storage-style rows, two drives a row in one card: the letter, "412 GiB free of 931 GiB", and a meter. Six drives take 3 rows, not 6.
- **Thermal zones** (added in the build): every zone, when there are 2 or more, since the island's page lists only 8.
- **General:**
  - Update speed.
  - Home status, renamed from Tile status.
  - GPU and Network adapter as ordinary dropdowns, shown only when there's a choice.
- **Readings:** in the page's order. GPU shows only when a GPU exists. Thermal zone is now called Temperature, and Disks is now called Drives.
- **Units:** unchanged.
- **Alerts:** unchanged, except the GPU temperature row says "Set a limit" while no limit is set.

## New data

- Top apps per resource.
- The processor's name.
- Up time.

Everything else on the page is already sampled.

## Not included

- Scrolling detail pages.
- A system power card (Windows has no reading for it).
- CPU temperature (it needs admin rights).
- macOS memory terms.
- A per-page ⋯ menu.
- A graph for drive space or the battery on the overview: they change too slowly for 4 minutes to show anything.
