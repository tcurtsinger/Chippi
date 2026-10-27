# Phase 4 System monitor: design input for the mockup

For Travis's UI/UX agent. Design inside the approved Surfaces model (PLAN.md › Surfaces, DESIGN.md §5). Research: `behavior-spec.md` (what to show, alert rules) and `architecture.md` (what Windows actually gives us) in this folder. Don't touch `X:\code\chippi` (the main checkout): mockup files go in `X:\code\chippi-scratch\phase-4\design\`.

## Decided
- **Where it lives:** the island (and the docked pages when the island is off) only. No tray readouts, no extra tray icons (Travis, 2026-10-26).
- **Surfaces:** one System monitor tile on the grid and its page(s). Settings › System › System monitor holds configuration only (refresh, units, which readings show, alerts), never the live readings.
- **Refresh:** 2 s default; 1, 2 or 5 s. History: last 120 samples per reading, timestamped. After sleep or a failed read, show a gap, never a 0. A reading that's unavailable shows a short status, not 0.
- **Units:** °C default (°F option), %, GHz, MiB/GiB, KiB/s and MiB/s (network in bits as an option, Mbps), RPM for measured fan speed, % for NVIDIA's intended fan speed. Never label a % as RPM.
- **Disk:** free space per volume and activity. No SMART/health.
- **Deferred, don't draw:** network speed test, per-app rankings, CPU package/core temperature, motherboard fans, fan control, battery temperature.
- **Copy:** DESIGN.md §3. Setting labels are noun phrases in Windows terms; buttons are bare verbs; statuses and errors are short fragments; no explanation lines or tooltips that explain.

## Readings available as a standard user (architecture.md §1)
| Reading | Notes for the design |
|---|---|
| CPU | Total and per logical processor (Travis's PC: 32), effective clock |
| Memory | In use / total and %; system commit |
| GPU | Per adapter (Travis: RTX 4080 + AMD Radeon integrated): load (busiest engine), dedicated and shared memory, temperature and fan RPM where the driver reports them; NVIDIA adds fan % |
| Network | Up and down for the real adapter only; which adapter is shown |
| Disk | Activity per physical disk; free space per volume |
| Battery | Only on PCs with one: charge, charging/discharging, rate, time left |
| Thermal zone | Only where the board exposes one (laptops); label "Thermal zone", never "CPU" |
Design for rows that may be missing (no battery, no thermal zone, one GPU or several).

## Also decided (Travis, 2026-10-26)
- **CPU temperature, motherboard fans, fan control:** a later step. Don't draw them; design so a later CPU temperature row can slot in.
- **Alerts** (all off by default, through the existing Notifications settings, 15-minute repeat): CPU load sustained 12 s (threshold 50–100%, steps of 5), memory used (default 90%, 50–100% in steps of 5), low disk space (5–30%), low battery while discharging (5–50%), GPU temperature with a user-set limit (sustained 12 s), shown only where the card reports temperature. Each needs a switch and its threshold in Settings › System › System monitor, plus its notice (DESIGN.md §5 notice rules).

## Wanted back
- The System monitor tile (status text) on the grid, its page(s), the docked version, and Settings › System › System monitor, in dark (and light for Settings), on the top and a side edge.
- Notes listing every choice DESIGN.md doesn't already make.
