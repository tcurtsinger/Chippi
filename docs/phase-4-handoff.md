# Chippi Phase 4 brief: System monitor

Marge, 2026-10-26. APPROVED by Travis with the mockup ("the design is approved, build it", 2026-10-26). Screens and copy: [design/notes.md](phase-4/design/notes.md), boards in [index.html](phase-4/design/index.html), shots in [design/shots/](phase-4/design/shots/). notes.md wins on screens and copy; this brief wins on behavior. Research: [behavior-spec.md](phase-4/behavior-spec.md) (Karl) and [architecture.md](phase-4/architecture.md) (Kayla), both cross-reviewed clean. Design input: [design-input.md](phase-4/design-input.md).

## 1. Decisions (Travis, 2026-10-26)

- **Surfaces:** System monitor lives only in the island and the docked pages. No tray readouts, no extra tray icons.
- **CPU temperature, motherboard fans, fan control:** a later step of their own (PawnIO driver plus an admin helper). Phase 4 installs nothing and needs no admin.
- **Alerts:** CPU load, memory, low disk space, low battery, GPU temperature, as in §2.
- **Screens:** mocked up by Travis's UI/UX agent inside the Surfaces model.

## 2. Lead calls (Marge)

- **Readings:** CPU (total, per logical processor, effective clock), memory (in use, commit), GPU per adapter (busiest-engine load, dedicated and shared memory, temperature and fan where the driver reports them; NVML % on NVIDIA), network (real adapter only), disk (activity per physical disk, free space per volume), battery (only where present), thermal zone (only where exposed, labelled "Thermal zone", never "CPU").
- **Refresh:** 2 s default; 1, 2 or 5 s. Poll only for a live consumer: the open page, a visible tile status, an enabled alert. Nothing live means no timer.
- **History:** 120 timestamped samples per series. Gaps after sleep, resume, hotplug, counter reset or a failed read; a missing value is never 0. An unavailable reading shows a short status.
- **Units:** °C (°F option), %, GHz, MiB/GiB, KiB/s and MiB/s (bits as an option, Mbps), RPM only for measured fan speed, % for NVIDIA's intended fan speed.
- **Identity:** GPU by PnP device instance ID, network adapter by InterfaceGuid, volume by volume GUID path. A removed device shows its status, not stale numbers.
- **Alerts:** all off by default; through the existing notifier (Windows and Island, per Settings › Notifications); one notice kind each.
  - CPU load ≥ threshold (default 90%, 50–100% in steps of 5) sustained 12 s of fresh readings.
  - Memory used ≥ threshold (default 90%, 50–100% in steps of 5).
  - Low disk space: free below 10% (5–30%), volumes of at least 10,000,000,000 bytes, per volume.
  - Low battery ≤ 15% (5–50%) while discharging.
  - GPU temperature ≥ a user-set limit, sustained 12 s of fresh readings like CPU load, offered only where the card reports temperature; no default "safe" number.
  - Repeat cooldown 15 min (2/5/15/30/60), per kind and target. Never on stale data; resume doesn't count as sustained.
- **Deferred:** network speed test, per-app rankings, SMART/drive health, CPU package/core and battery temperature, motherboard fans, fan control.
- **Copy:** DESIGN.md §3 including Setting labels. Every new string is checked by the reviewer and by Marge's Decide pass before an installer.
- **Docs:** remove "in the tray" from the catalog description and "tray readouts" from PLAN.md; PLAN.md backlog gets "Hardware sensors" (PawnIO + admin helper: CPU/board temperatures, fans, later fan control).

## 3. Architecture (from architecture.md §4)

- Project `src/Features/Chippi.Features.SystemMonitor`, feature id `system-monitor` (group System).
- Sources behind interfaces (`ICpuSource`, `IGpuSource`, `IMemorySource`, `INetworkSource`, `IDiskSource`, `IBatterySource`, `ITemperatureSource`) with fakes for tests; a later `PrivilegedSensorSource` plugs in without changes to the sampler, pages or alerts.
- One sampler thread; PDH queries opened on it, never on the UI thread. GPU Engine counters polled only while a GPU reading is shown. Immutable timestamped snapshots; ring-buffer history.
- APIs: PDH (CPU, GPU engine/memory, disk, thermal zone), GlobalMemoryStatusEx + GetPerformanceInfo, D3DKMT adapter perf data, NVML (NVIDIA, optional, loaded dynamically by full path: System32, else %ProgramW6432%\NVIDIA Corporation\NVSMI; never by name search), GetIfTable2 (hardware, non-filter rows only), GetDiskFreeSpaceEx, GetSystemPowerStatus + Windows.Devices.Power. Never WinRing0. Never .NET's NetworkInterface statistics for totals.

## 4. Workstreams

| # | Work | Owner | Reviewer | Starts |
|---|---|---|---|---|
| M0 | Project, sources, sampler, snapshots, history, identity, fakes, settings model | Kayla | Karl | now |
| D | Docs: brief + research + design notes into docs/phase-4/, DESIGN.md §5 from notes.md, PLAN.md (no tray readouts; "Hardware sensors" backlog), catalog text without "in the tray" | Karl | Kayla | now |
| M1 | Alerts: sustain gate, cooldowns, five notice kinds, tile Caution state (notes #4, #18) | Karl | Kayla | after M0's contracts and D |
| M2 | Island tile (notes #1–4), the 640-wide page under 550px (notes #5–22), docked version | Kayla | Karl | after M0 |
| M3 | Settings › System › System monitor (notes #23–32) | Karl | Kayla | after M1 |
| M4 | Smoke tests on fakes (tile, page incl. short screen, settings, alerts) | Karl | Kayla | after M2 and M3 |
| Lead | Solution file, integration, Decide string pass, installers, report | Marge | Kayla | throughout |

## 5. Process

Same as Phase 3: branch and worktree per workstream; the other member reviews (behavior and every new string); Marge merges through a GitHub PR after CI passes, never with checks pending. Review bar: only bugs that break functionality or normal use block a merge. Scratch in `X:\code\chippi-scratch\phase-4\`. Dev runs use `CHIPPI_DEV_PROFILE`. Nothing installed and no admin on Travis's PC. Desktop checks: screenshot every monitor, confirm the foreground window first, stop background scripts by PID. fullscreen-check before Island tests. No CPU stress loops on Travis's PC.

## 6. Done when

- Both win-x64 and win-arm64 build with 0 warnings; tests pass on x64 (no ARM64 hardware); behavior covered on fakes (gaps, hotplug, identity, multi-GPU, filter adapters, sleep, alert sustain and cooldown).
- The tile, page(s), docked version and Settings page match the approved mockup and DESIGN.md §3.
- Readings on Travis's PC agree in definition with Task Manager (not identical instants).
- Alerts fire, repeat after cooldown and respect Notifications settings, checked with controlled inputs.
- Measured polling cost stays near architecture.md §1 (about 0.24% of one core at 2 s for a full page); nothing polls when nothing is shown and no alert is on.
- Installer installs, updates in place and uninstalls; Travis's live checks pass.
