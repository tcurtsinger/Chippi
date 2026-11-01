# System page (proposed 2026-10-31)

Replaces the System monitor tables (the 2026-10-31 critique scored them 24/40). Open `index.html` to click through it; `render.ps1` writes `shots/`. Colors and sizes are the island and Settings tokens from `Dark.xaml` and `Light.xaml`.

Owner's choices (2026-10-31): clicking a widget opens a detail page in the island. The CPU, GPU and Memory pages list the top 3 apps. The overview's meters are thin and gray. The feature is called "System" and the Settings group keeps its name "System".

## Overview (496 × 304, never scrolls)

- **Load row (96 tall):** CPU, GPU and Memory at 160 each, or 244 each when there's no GPU. They're the same on every PC.
- **Then the rest:** Network, Battery and Temperature share the next row: one at full width (82 tall), or two or three side by side (96 tall).
- **Drives:** always the bottom row, at full width.
  - Up to 4 drives sit in columns.
  - With 5 or more, the 3 fullest show (an alerting drive comes first), then "+N more drives".
  - A single drive gets one wide row.
- **Widget anatomy:** each widget is a button.
  - **Top line:** a 14 glyph and the name in 12 tertiary, with one fact on the right in 11 tertiary (GPU temperature, the adapter, the drive count).
  - **Value:** 24 / 600 in tabular figures, with the unit at 14 in secondary.
  - **Context line:** 11 tertiary: clock, the GPU's name, "21.4 of 32.0 GiB", the upload rate, time left, "Hottest of 3 zones".
  - **Meter:** 4 px, gray on the track, for CPU, GPU, Memory, Battery and each drive. Network and Temperature have none.
  - **Alerts:** a widget whose alert holds turns its value and meter Caution.
- **Hover:** a wash and a brighter border. A click opens the widget's page.

## Detail pages

- **Navigation:** the header reads "‹ CPU". Back or Esc returns to the overview, sliding in from the left; opening a page slides in from the right, using the island's page slide (12 px, 180 ms). The island reopens on the overview.
- **Top card (166 tall):**
  - The value at 32 / 600 with one secondary reading on the right.
  - A 120-sample graph from the sampler's history: an accent line over an accent fade, a dashed midline, and its scale at the top right.
  - One 18 tall strip under the graph.
- **Bottom row (130 tall):** Top apps (3 rows) next to the facts. The facts never repeat the top card.

| Page | Right of the value | Strip | Facts |
|---|---|---|---|
| CPU | Clock | One bar per logical processor | Processor name, logical processors, up time |
| GPU | Temperature | Dedicated memory meter | Fan, shared memory |
| Memory | "21.4 of 32.0 GiB" | Committed meter | Available, installed |
| Network | Upload as a second value | Legend (Receive solid, Send dashed) and the adapter | Adapters list: one row each, the shown one tinted; a click graphs it |
| Drives | n/a | n/a | Space rows (letter, meter, "412 GiB free of 931 GiB"), then Activity per physical disk (active %, read · write) |
| Battery | Time left | n/a (taller graph) | State, rate, time left |
| Temperature | "Hottest of N zones" | n/a (taller graph) | A tile per zone |

- **GPU picker:** with two or more GPUs, a picker in the header shows the measured GPU's name. It replaces the old "GPU ⌄" label.
- **Network adapters:** the list replaces the old adapter popup.
- **Top apps:** apps are grouped by executable (every chrome.exe counts as Chrome) and show their real icons; the mock draws stand-in letter tiles.
  - The page samples processes only while it's open.
  - CPU and Memory read process times and private working sets. GPU reads the GPU Engine counters per process.
- **Overflow** (as built):
  - Drives lists as many volumes as fit above up to 4 disks' activity (4 on a 4-disk desktop), then "+N more in Settings".
  - Adapters lists up to 3, then "+N more in Settings".
  - Temperature lists up to 8 zones, then "+N more in Settings".

## Settings › System

- **Live widgets:** the same widgets, 4 across, in Settings card style with the Windows accent meters.
- **Drives:** one Windows Storage-style row per drive: the letter, "412 GiB free of 931 GiB", and a meter.
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
- Graphs on the overview, because history only exists while something is watching, so the overview couldn't open filled.
