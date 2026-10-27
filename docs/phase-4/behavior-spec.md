# Phase 4: System monitor behavior

Karl, 2026-10-26. Research draft for Kayla's review, before product decisions and mockups.

## 1. Scope and evidence

PLAN.md makes vorssaint's behavior the reference and requires Windows adaptations. Chippi keeps the approved Surfaces model: island pages for readings, Settings for configuration. This document proposes behavior, not a screen design or an implementation. API availability, privilege requirements, ARM64 support, polling measurements, and helper design belong to Kayla's architecture.

Evidence labels:
- **Documented:** upstream README, wiki, release notes, or a vendor's documentation.
- **Source-verified:** upstream source read to settle a behavior the documentation omits. No source, strings, icons, or layout copied into Chippi.
- **Proposed:** a Chippi recommendation requiring the brief or Travis's decision.
- **Unverified:** not established by the sources read; never a promise of hardware support.

Vorssaint baseline: upstream main at `746239b0220571d6694ffacae79f3eb9dcd09193`. LibreHardwareMonitor (LHM) source baseline: `3f118427b0fd38a72c3135c766807ebb5d605128`. These are research snapshots, not dependencies selected for Chippi. No reference application was installed or executed, no sensors were probed, and no settings or fan controls were changed.

## 2. Vorssaint baseline

### Readings and presentation

**Documented:** the monitor covers CPU/GPU load, memory, temperatures, battery health, and power, with history. It offers selectable menu-bar readings and bars, network traffic/session totals, and optional alerts. Fan control is a beta feature with manual speed and temperature curves. [Upstream README](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/README.md)

**Source-verified:** the detailed choices include CPU logical-core loads, memory breakdown, disk capacity/activity/health, network addresses, battery time, and peripheral batteries. Unavailable snapshot readings are optional, and the model describes hiding their rows. The current upstream includes disk tools/protection and connected USB devices, which exceed the Phase 4 monitoring request. [Panel configuration](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/UI/Settings/MonitorPanelConfig.swift), [snapshot](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/SystemMonitor/SystemMonitor.swift)

| Reading | What the reference establishes | Proposed Chippi behavior |
|---|---|---|
| CPU total | Percentage of total machine capacity, plus history | Total utilization, 0 to 100%, and short history. Name the measurement consistently with the Windows provider Kayla selects. |
| CPU per core | Optional load per logical processor, sampled while the CPU row is visible | Expandable logical-processor breakdown; identify logical processors rather than calling SMT threads physical cores. Avoid dozens of always-visible readings. |
| CPU clock | Not established as a displayed field in the monitor configuration/snapshot reviewed | Current reported clock in GHz when available. Do not imply it is an all-core effective clock or compare it directly with a nominal boost limit. |
| GPU load | Percentage and history | Named GPU selector on multi-GPU PCs. Use a documented utilization definition. If following Task Manager, report the busiest engine, not an average that dilutes a saturated engine. |
| GPU memory | Separate dedicated VRAM is not established by the reviewed Mac snapshot | Dedicated memory used/total and shared memory used, separately, where available. Integrated shared RAM must not be called physical VRAM. |
| Memory | Used, app-used, total, compressed, cached, swap, pressure, and history | Physical memory used/total and available first. Commit used/limit in details if provided. Do not import macOS pressure colors as a Windows metric without an agreed Windows definition. |
| Temperatures | CPU, GPU, and battery; selectable temperature unit | Selected GPU temperature where its driver reports it. ACPI readings, if present, are labelled Thermal zone, never CPU package. CPU package/core and battery temperature need a later supported sensor source; none is established for the basic Phase 4 route. |
| Battery | Charge, battery flow, time remaining, health; separate system/adapter power | Charge %, charging/discharging/plugged-in state, rate W, estimated remaining or charging time when reported. Health/cycles/design capacity optional. Hide battery section on a desktop with no battery. |
| Network | Receive/transmit rates, session totals, local addresses, optional speed test | Selected adapter's download/upload, name, and history. Session totals explicitly reset with monitoring session. Details may show local addresses. No automatic speed tests. |
| Disk | Capacity/free space, read/write activity, history, health/tools | Volume free/total and disk read/write rates; optional active time. Distinguish physical disk from volume. SMART health/temperature conditional; defer disk repair/protection tools. |
| Power/fans | System and adapter draw separate from battery flow; RPM | Show only actual measured power for its named component. CPU package watts are not whole-PC power. RPM reading is separate from permission to control a fan. |

The reference facts in this table come from the pinned panel configuration and snapshot above. Per-core sampling is specifically demand-driven. [CPU core sampler](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/Metrics/CPUCoreSampler.swift)

### Refresh, history, and units

**Source-verified:** Settings offers 1, 2, or 5 seconds, default 2. This is the base sampling interval, not a guarantee that every sensor updates on each tick. [Monitor settings](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/UI/Settings/MonitorSettings.swift)

For an active monitor surface, most metric families target 1 second and therefore use the chosen base interval. Peripheral batteries target 15 seconds. In the background, enabled CPU/memory/network readings and pinned GPU load, chip temperature, fan speed, and power draw retain the base cadence. Non-pinned GPU load targets 10 seconds, fan readings 5, battery/power state and temperature 15, disk 10, peripheral batteries 60. Targets round upward to whole base ticks: at a 2-second base, a 15-second target becomes 16 seconds. Only needed families are sampled; there is no monitor timer when no surface, readout, or alert needs it. [Sampling policy](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/Metrics/MonitorSamplingPolicy.swift), [monitor lifecycle](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/SystemMonitor/SystemMonitor.swift)

History buffers hold up to 120 entries per series. That is a sample count, not a fixed two-minute history across interval choices or background sampling. [History capacity](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/SystemMonitor/SystemMonitor.swift)

Temperature supports Celsius/Fahrenheit. Rates support bytes or bits: byte formatting scales by 1024 despite MB-style labels; bit rates scale by 1000. Watts and percentages have separate formatters. This is reference behavior, not a reason to reproduce ambiguous unit labels. [Formatting](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/Metrics/MetricFormat.swift)

**Proposed:** start at a 2-second refresh with 1/2/5-second choices, subject to Kayla's measured budget. Keep 120 timestamped samples per enabled series in memory, label actual elapsed time, and leave gaps after sleep or missing samples. Stop polling when no consumer needs it. Slow battery capacity/health and expensive sensor families independently. Use %, GHz, °C by default (°F optional), RPM, W, and h/min. Use honest binary units (MiB/GiB and KiB/s/MiB/s) for binary scaling; optional network bit rates use decimal Mbps. Never convert a missing reading into zero.

### Menu-bar readings

**Documented:** opt-in live CPU/GPU/RAM/network/power readings, memory pressure or percentage, and stable-width text were introduced together; later releases added pinned temperatures and persisted ordering. [Changelog](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/CHANGELOG.md)

**Source-verified:** settings allow ordering, values or bars, combined temperatures, separate metric items, and hiding the primary icon when readouts exist. [Monitor settings](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/UI/Settings/MonitorSettings.swift)

**Proposed:** Windows notification-area readouts are opt-in, with a small selectable set and accessible names. The island page remains the complete view. Do not treat macOS menu-bar text as proof Windows supports the same inline arrangement. Decide between compact numeric tray icons and island-only readouts after Kayla's feasibility check. Keep a reliable Settings/Quit entry under the existing hidden-tray rules.

### Alerts: exact reference rules

All six switches default off. Thresholds below are reference defaults, not hardware-safe limits promised by Chippi. [Alert settings](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/UI/Settings/MonitorAlertsControls.swift)

| Kind | Reference trigger/default | Adjustable range | First alert |
|---|---|---|---|
| CPU load | At least 90% | 50 to 100%, steps of 5 | At least 12 seconds across fresh high readings |
| CPU temperature | At least 90°C | 70 to 105°C, steps of 5 | Same sustained rule |
| Battery temperature | At least 40°C | 30 to 50°C, steps of 5 | Same sustained rule |
| Memory | Critical macOS memory pressure | No percentage threshold | On a critical snapshot |
| Disk space | Free space below 10%; only devices of at least 10 billion bytes | 5 to 30%, steps of 5 | On a qualifying snapshot; first qualifying device is selected |
| Battery charge | At most 15%, battery present and not charging | 5 to 50%, steps of 5 | On a qualifying snapshot |

The duration check uses actual read timestamps, ignores repeated cached readings, resets below threshold/unavailable, and uses uptime that does not advance during sleep. Separate cooldowns are per alert kind, not per disk or sensor. Default repeat cooldown is 15 minutes; options are 2/5/15/30/60 minutes. A continuing condition may notify again after cooldown; this is not a once-until-recovery latch. Last-sent timestamps live in process memory, so restart does not retain the cooldown. The cooldown delays repeats, not the first alert. GPU temperature has no separate alert kind in this service. [Alert evaluation](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/SystemMonitor/MonitorAlertService.swift), [sustained rule](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/Metrics/SustainedAlertGate.swift)

**Windows availability:** the reference battery-temperature alert has no established Windows source in this plan, so it is deferred. CPU package/core temperature alerts likewise wait for the later helper. GPU temperature remains conditional on the driver; an ACPI zone must not substitute for a CPU package reading. See [architecture §2](architecture.md#2-temperatures-and-fans).

**Proposed:** alerts remain opt-in and use Chippi's existing Windows/Island notification preferences. Preserve the sustained-reading rule for CPU load and temperature, and start with a 15-minute repeat cooldown. Scope cooldowns by kind and target so one full volume cannot suppress another. Opening an alert shows the affected monitor reading. Never alert on stale data or treat resume as sustained load. Decide the Windows memory trigger explicitly; a proposed 90% used threshold is an adaptation, not macOS pressure parity. Temperature limits need explicit user configuration or a verified device limit, not one universal safe number. Low battery should require discharge rather than merely 'not charging', to avoid warning on a deliberately limited, plugged-in battery. No custom beep by default.

## 3. Windows behavior references

| Reference | Documented or source-verified behavior | What to take into Chippi |
|---|---|---|
| Task Manager | CPU total and logical-processor graphs; resource views depend on configuration. GPU views identify adapters, show engine usage, and separate dedicated/shared memory. Overall GPU load follows the busiest engine. | Familiar resource names and honest multi-GPU/memory distinctions. Link to Task Manager for process investigation instead of building process termination into Phase 4. |
| Game Bar Performance | Microsoft documents selectable performance metrics including GPU utilization and VRAM. Resources is a separate widget for identifying resource-heavy apps. | A compact selection can be useful while gaming. It does not establish an alert engine or universal temperature/fan support. Keep Chippi's approved fullscreen policy. |
| TrafficMonitor | Main/taskbar readouts, selectable byte/bit rates, memory used/available/%, optional CPU frequency/temperature, GPU load/temperature, drive temperature/activity. Monitoring defaults to 1000 ms; hardware monitoring defaults off. Notifications support daily traffic, memory usage, and temperature thresholds. | Network rates deserve persistent opt-in readouts; optional sensors should not block basic monitoring. Its taskbar window is a separate surface, not equivalent to an ordinary tray icon. |
| HWiNFO | Broad sensor tables, graphs, tray/OSD, logging, and alerts on monitored parameters with configurable actions. Its author explains that slow sensors can make actual cycles exceed the requested interval. | Show units, target, and availability; measure actual sample time. Do not promise every sensor updates at 1 second. Avoid importing a full diagnostics dashboard. |
| LibreHardwareMonitor | Sensor hierarchy for supported motherboards, CPUs, GPUs, drives, and networks. UI source has plots, tray support, °C/°F, update choices of 250/500 ms and 1/2/5/10 seconds, default 1 second. Fan controls appear only on writable controls, with Default and supported manual percentages. | Capability-driven display and a clear distinction between sensor readings and writable controls. A detected RPM does not establish safe fan control. |

Sources: [Microsoft Task Manager](https://learn.microsoft.com/en-us/troubleshoot/windows-server/support-tools/support-tools-task-manager), [Microsoft GPU semantics](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager//), [Xbox Performance/Resources announcement](https://news.xbox.com/en-us/2020/10/29/xbox-game-bar-update-introducing-resources-widget/amp/), [TrafficMonitor options](https://github.com/zhongyang219/TrafficMonitor/wiki/Option-Settings), [HWiNFO features](https://www.hwinfo.com/about-software/), [HWiNFO author on polling](https://www.hwinfo.com/forum/threads/polling-period-global-vs-logging-cycle.6610/), [LHM README](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/3f118427b0fd38a72c3135c766807ebb5d605128/README.md), [LHM UI behavior](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/3f118427b0fd38a72c3135c766807ebb5d605128/LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs).

**Unverified:** exact current Task Manager/Game Bar timer defaults, HWiNFO alert repeat/cooldown defaults, and TrafficMonitor alert repeat/cooldown are not established by the documentation read. HWiNFO forum discussions mention a 2000 ms default historically; do not present that as a verified current release default. No general alert system was established for the base LHM application. These gaps do not prevent specifying Chippi's own behavior, but must not become claims of reference parity.

## 4. Fan control and hardware limits

**Documented:** Vorssaint advertises manual speeds, curves, and RPM as beta. Its Mac platform is not evidence of Windows hardware coverage. **Source-verified:** fan access checks for exposed controls and valid limits; missing fans and unsupported hardware are separate outcomes. Curves select CPU/GPU/SoC temperature sources. Recovery logic includes helper heartbeat loss, repeated verification/sensor failures, and serious/critical thermal pressure. This shows why a successful RPM read alone is insufficient. [Fan hardware checks](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/FanControl/FanControlHardware.swift), [fan policy](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/FanControl/FanControlSupport.swift)

HWiNFO's author says fan control is not an official general feature: specific Alienware, older Dell (often three levels), and a few HP systems are supported. The post is a hardware limitation example, not a current blanket promise about every model. [Author's reply, 2025](https://www.hwinfo.com/forum/threads/hwinfo-can-control-dekstop-fan-speed.10239/)

LHM exposes manual controls only where the detected sensor has a control object, with hardware-specific minimum/maximum bounds. Supported monitoring chips and wiring vary by board. This does not mean every fan on a supported board is writable. [LHM UI](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/3f118427b0fd38a72c3135c766807ebb5d605128/LibreHardwareMonitor.Windows.Forms/UI/MainForm.cs), [motherboard implementation](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/3f118427b0fd38a72c3135c766807ebb5d605128/LibreHardwareMonitorLib/Hardware/Motherboard/SuperIOHardware.cs)

Game Bar's documented ASUS fan-mode example requires Armoury Crate and its vendor widget. It is not generic fan control in the Performance widget. Task Manager and TrafficMonitor documentation reviewed did not establish fan control. [Xbox vendor integration](https://news.xbox.com/en-us/2020/10/29/xbox-game-bar-update-introducing-resources-widget/amp/)

**Recommendation:** Phase 4 reads RPM where available; defer writable fan controls to a separate hardware-qualified scope. If Travis includes them, require an explicit supported-device list, an original/automatic restoration path tested on each device, independent failure recovery, and conflict handling for OEM tools. Kayla owns the feasibility verdict and driver/helper implications. Nothing should silently disable an OEM thermal policy.

## 5. Proposed Phase 4 acceptance behavior

These are proposed requirements for Marge's brief, pending decisions and the architecture review.

- Installing System monitor exposes one island entry and its readings. Docked pages follow existing tray behavior. Settings changes visibility, units, refresh, readouts, and alert limits; it does not duplicate the daily monitor page.
- Start with CPU, memory, GPU where present, and network. Battery appears only where present. Disk capacity/activity and optional temperatures expand without turning the page into a sensor inventory.
- Remember selected GPU, adapter, and volume by stable identity; use a visible fallback after removal. Adapter totals must not sum a physical interface and its VPN/tunnel or filter-driver copies (WFP, QoS) of the same traffic. Record exactly which adapter is measured.
- Hotplug, resume, counter resets, and failed reads show missing data/gaps until a valid interval exists. Preserve settings without keeping a disconnected device's number looking live. Hide unsupported optional rows; a previously selected unavailable reading shows a short status rather than 0.
- Poll only enabled consumers, keep UI updates responsive, and bound in-memory history. No full-machine stress runs are required to prove basic monitoring behavior. Kayla supplies a measured polling budget before the brief promises 1-second operation.
- Verify total/per-logical CPU distinction, multi-GPU VRAM/shared memory, battery absence/charging/discharge, adapter removal and counter reset, disk/volume identity, stale samples, sleep, and alert repeat/recovery with controlled inputs. Live checks compare definitions and timestamps, not identical instantaneous numbers across tools.
- Every new UI string passes DESIGN.md §3: short noun phrases for settings, bare verbs for actions, short status reasons, no explanatory fixed-behavior rows. Reference wording and screens are not copied.

## 6. Decisions for Travis, for Marge to put on Tasks

| Decision | Recommendation | Reason / dependency |
|---|---|---|
| Temperatures and fan readings | Basic monitor works without extra installation; optional sensor support only after Kayla establishes the supported route | Unsupported sensors must not hold up CPU/memory/network. Any driver/helper choice needs its own clear approval. |
| Fan control | Defer writes; include RPM when readable | Reference support varies by hardware; recovery and OEM conflicts need dedicated validation. |
| Persistent readouts | Opt-in CPU, memory, and network first; choose tray icons or island-only after feasibility | Avoid replacing the approved Surfaces model or assuming a macOS-style text strip. |
| Alerts | Off by default; configurable CPU, low memory, low disk, low battery; temperature alerts only with available sensors and explicit limits | Keeps routine gaming/load quiet and avoids inventing safe thermal thresholds. Proposed 12-second sustain for CPU/temperature, 15-minute repeat cooldown. |
| Disk depth | Capacity/free space and activity now; SMART diagnostics/repair later | Useful low-space visibility without adding a disk-management feature. |
| Refresh and history | 2 seconds default, 1/2/5 choices, short timestamped in-memory history | Matches reference choices while allowing measured cost and sleep gaps. |
| Network speed test and per-app rankings | Defer both from the baseline | Reference includes them, but they add active traffic or a separate process-attribution surface beyond the requested readings. Mark as parity deferrals if adopted. |

No decision in this table is treated as approved. The mockup should follow the architecture's actual availability results. Kayla's peer review is pending.
