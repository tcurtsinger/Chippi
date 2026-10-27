# Phase 4: System monitor architecture

Kayla, 2026-10-26. Research draft for Karl's review. Read with Karl's [behavior-spec.md](behavior-spec.md).

Evidence labels: **Measured** (the read-only probe on Travis's PC), **Documented** (primary source, linked), **Inferred** (reasoned, not confirmed).

Probe: `probe\` (C#, .NET 10, query calls only). Readings: `Probe.exe 5` writes `probe\bin\Release\net10.0-windows10.0.19041.0\win-x64\probe-result.md`. Cost: `Probe.exe --cost <n> 300`, raw results in `probe\cost-runs.tsv`. Nothing was installed, elevated or registered; the probe ran non-elevated (`elevated=False`). Travis's PC: Windows 11 26300 x64, 32 logical CPUs, NVIDIA GeForce RTX 4080 plus AMD Radeon integrated graphics, Intel I225-V Ethernet, no battery.

## 1. Every reading

**How cost was measured** (Karl's correction): each reading ran alone in its own process for 300 polls 1 s apart, after one setup poll; the cost is the process's CPU time over those 300 polls (`Process.TotalProcessorTime`) divided by 300. A baseline process that polls nothing used 0 ms over the same 302 s. Process CPU time counts in 15.6 ms steps, so each per-poll figure is ±0.05 ms. Separate processes each pay their own wake-up, so one sampler thread doing several readings costs less than the sum (Inferred).

| Reading | API | Admin or driver | ARM64 | Cost per poll (% of a core at 1 s) | Result on Travis's PC |
|---|---|---|---|---|---|
| CPU total and per logical processor | PDH `\Processor Information(*)\% Processor Utility`, uncapped format. Task Manager's measure since Windows 8 per third-party sources; Microsoft doesn't document it (Inferred) | No | Yes (PDH is in-box) | 0.52 ms (0.05%) | Measured: 32 instances named `group,index`, plus `_Total` |
| CPU clock | PDH `Processor Frequency` x `% Processor Performance` / 100, an effective clock (Inferred: no Microsoft doc of the formula) | No | Inferred yes (counters exist; values not checked) | in the CPU row | Measured: 4300 MHz x 109% = 4691 MHz |
| CPU, cheap fallback | `GetSystemTimes` | No | Yes | 0.31 ms | Time-based busy %, reads lower than utility under turbo. Not the primary |
| Memory | `GlobalMemoryStatusEx` for physical use; `GetPerformanceInfo` for system commit (Karl: MEMORYSTATUSEX's page-file fields can be limited by a process or job limit, [Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/ns-sysinfoapi-memorystatusex)) | No | Yes | 0.31 ms | Measured: 24.1 of 61.7 GB in use (39%), system commit 33.6/65.6 GB |
| GPU load and memory, any vendor | PDH `\GPU Engine(*)\Utilization Percentage`, `\GPU Adapter Memory(*)\Dedicated Usage` and `Shared Usage`. Load: each engine (adapter LUID, `phys`, `eng`) summed over processes, then the busiest single engine is the adapter's load ([Microsoft: busiest engine](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/)). Grouping by engine type would add two 3D engines together (Karl's correction) | No | Yes (WDDM counters) | 1.88 ms (0.19%), the most expensive; 814 instances here, growing with running processes | Measured: RTX 4080 6.5% (15 engines), Radeon 0.0% (11 engines), Basic Render 1.6% (33 software engines); dedicated 1722 MB / 33 MB / 0 MB |
| GPU temperature, fan, power, any vendor | `D3DKMTQueryAdapterInfo(KMTQAITYPE_ADAPTERPERFDATA)`: `Temperature` in deci-Celsius, `FanRPM`, `Power` in tenths of a percent ([Microsoft](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/ns-d3dkmthk-_d3dkmt_adapter_perfdata)). Task Manager's GPU temperature, documented for dedicated GPUs with WDDM 2.4+ ([Microsoft](https://blogs.windows.com/windows-insider/2019/08/16/announcing-windows-10-insider-preview-build-18963/)) | No (Measured) | Inferred yes; depends on the driver filling it | 1.09 ms (0.11%) | Measured: RTX 4080 53.4 °C, AMD Radeon integrated 46.0 °C (so not only dedicated GPUs); fan 0 rpm on both at idle; Basic Render returns STATUS_INVALID_PARAMETER, expected |
| GPU detail, NVIDIA only | NVML (`nvml.dll`, System32 on DCH drivers, else `%ProgramW6432%\NVIDIA Corporation\NVSMI`; [NVIDIA](https://docs.nvidia.com/deploy/nvml-api/index.html)): temperature, utilization, VRAM used/total, fan % (intended speed, not RPM) | No (Measured: worked non-elevated on a normal driver install) | No: NVIDIA has no Windows-on-ARM driver | 0.73 ms (0.07%); 14 ms to load | Measured: RTX 4080 54 °C, 9%, 2041/16376 MB, fan 0% |
| CPU temperature, in-box | PDH `\Thermal Zone Information(*)\Temperature` (ACPI thermal zone; Kelvin) | No | Yes | 0.16 ms | Measured: PDH_NO_DATA, this desktop board exposes no ACPI thermal zone |
| CPU temperature, WMI | `root\WMI MSAcpi_ThermalZoneTemperature` | Admin per community sources (Inferred) | Yes | 1.25 ms (0.13%) | Measured: "Not supported" as a standard user. Don't use: PDH reads the same zones without admin |
| Battery state | `GetSystemPowerStatus`: AC, percent, seconds left (-1 when unknown) ([Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-system_power_status)) | No | Yes | 0.05 ms | Measured: flag 0x80, no battery |
| Battery rate and capacity | `Windows.Devices.Power.Battery.AggregateBattery.GetReport()`: `ChargeRateInMilliwatts` (negative while discharging), remaining, full charge, design; null when not reported ([Microsoft](https://learn.microsoft.com/en-us/uwp/api/windows.devices.power.batteryreport)) | No | Yes | 0.36 ms | Measured: `NotPresent`, all null |
| Network up and down | `GetIfTable2`, `MIB_IF_ROW2.InOctets/OutOctets`, only rows with `HardwareInterface` set and `FilterInterface` clear | No | Yes | 0.57 ms (0.06%) | Measured: 23 rows, 1 counted (Intel I225-V) |
| Network, what not to use | `NetworkInterface.GetIPStatistics()` (.NET) | No | Yes | 3.02 ms (0.30%), 5x the above | Lists filter-driver copies (WFP, QoS) of the same adapter, so summing it counts the traffic 4 times |
| Disk activity | PDH `\PhysicalDisk(*)\% Idle Time` (active = 100 minus idle), `Disk Read/Write Bytes/sec` | No | Yes | 0.42 ms | Measured: 5 physical disks with their volume letters |
| Disk space | `GetDiskFreeSpaceEx` per volume | No | Yes | Not probed | Inferred |

**A full page** (CPU, memory, GPU load and temperature, network, disk) sums to about 4.8 ms of CPU per second polled separately: 0.5% of one core at 1 s, 0.24% at Karl's 2 s default, under 0.02% of this 32-thread machine. NVML adds 0.7 ms on NVIDIA. The one cost that grows is the GPU Engine counter set (one instance per process per engine): poll it only while a GPU reading is shown.

**Setup cost:** opening the PDH CPU query took about 110 ms of thread time on the first poll. Open queries on the sampler thread, never on the UI thread.

## 2. Temperatures and fans

### What a standard user gets (no driver, no admin)
- **GPU:** temperature and fan RPM from D3DKMT for any vendor whose WDDM 2.4+ driver fills them (Measured on NVIDIA and AMD here); NVML on NVIDIA as a refinement.
- **CPU:** only an ACPI thermal zone through PDH, where the OEM exposes one (laptops often do; this desktop doesn't). It's a zone or board reading, not the CPU package or a core, so label it "Thermal zone", never "CPU" (Documented that it's the ACPI zone: [Microsoft](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/ioctl-thermal-read-temperature); its units in PDH are Inferred).
- **Battery temperature:** no general API reports it (Inferred). Karl's battery-temperature alert has no Windows source to read.
- **Motherboard sensors, CPU package and core temperatures, fan RPMs other than the GPU's:** not available. They need MSR, SMU, Super I/O or EC access, which needs a kernel driver.

### LibreHardwareMonitor and its driver (Documented)
- **WinRing0 is gone from LHM.** PR [#1857](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/pull/1857) "Swap WinRing0 to PawnIO" (merged 2025-09-16) first shipped in [v0.9.5](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/v0.9.5) (2026-01-07). The latest release is [v0.9.6](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/v0.9.6) (2026-02-14), with PawnIO modules 2.2.
- **WinRing0 is on Microsoft's vulnerable driver blocklist.** Measured, not just reported: I downloaded [the current blocklist](https://aka.ms/VulnerableDriverBlockList) (enforced policy 10.0.29545.0) and it has 30 WinRing0 entries; PawnIO has none. The blocklist is on by default since Windows 11 22H2 and enforced with memory integrity, Smart App Control or S mode ([Microsoft](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/microsoft-recommended-driver-block-rules)). [CVE-2020-14979](https://cveawg.mitre.org/api/cve/CVE-2020-14979): any local user can read and write arbitrary memory through it. Defender detects it as [HackTool:Win32/Winring0](https://www.microsoft.com/en-us/wdsi/threats/malware-encyclopedia-description?Name=HackTool:Win32/Winring0). Chippi must never ship or load WinRing0.
- **PawnIO** ([site](https://pawnio.eu/), [driver](https://github.com/namazso/PawnIO), [setup](https://github.com/namazso/PawnIO.Setup)): a signed, scriptable kernel driver; clients load small signed Pawn modules (MSR, Ryzen SMU, Super I/O, EC, SMBus, Dell SMM, ARM MSR) through an IOCTL. Installed separately with `PawnIO_setup.exe` (admin; latest 2.2.0, 2026-03-15, may need a restart). The device is admin-only by default; 2.2.0 can expose it to non-admins, which its author calls "not recommended". LHM finds it through `HKLM\...\Uninstall\PawnIO` and opens `\\?\GLOBALROOT\Device\PawnIO` ([source](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LibreHardwareMonitorLib/PawnIo/PawnIo.cs)); it doesn't bundle the driver. The kind of signature (attestation or WHQL) isn't stated (Inferred: attestation).
- **LHM without admin:** its README says some sensors need admin and recommends running elevated. Without the PawnIO handle it reaches roughly what §1 already reads without it (Inferred).
- **ARM64:** LHM added ARM64 builds in PR [#1941](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/pull/1941) (tested only in a VM; a reviewer doubted they work). PawnIO 2.0.1 fixed ARM64 install and has an ARM MSR module. Sensor coverage on Snapdragon: unknown.

### Licenses (Documented)
- **LHM:** MPL-2.0 with no "Incompatible With Secondary Licenses" notice ([LICENSE](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LICENSE)), so MPL §3.3 makes it compatible with GPL-3.0-or-later ([FSF](https://www.gnu.org/licenses/license-list.html)). Using the `LibreHardwareMonitorLib` NuGet package (targets net10.0) is fine; its files stay MPL and their source must be available, including changes. Its own dependencies (DiskInfoToolkit, HidSharp, RAMSPDToolkit-NDD) need checking for THIRD-PARTY-NOTICES.
- **PawnIO:** GPL-2.0-or-later with an exception for programs that talk to it only through the device IOCTL; modules are LGPL-2.1. Shipping `PawnIO_setup.exe` unchanged next to Chippi, as with VB-CABLE, would be aggregation (Inferred; ask the author if we ship it).

### What the "optional elevated helper" in PLAN.md would take
1. **PawnIO installed:** an "Install Driver" row like VB-CABLE's: Chippi ships the unmodified `PawnIO_setup.exe`, pinned by hash and signer, and runs it with one UAC prompt (plus a restart when it asks).
2. **A process that can open PawnIO:** Chippi runs as a standard user and the device is admin-only, so a helper must run elevated. Exposing the device to all users is not acceptable (any process could then reach ring 0).
   - **Service (LocalSystem), recommended if we do this:** starts without a prompt every boot. Its exe must live somewhere only admins can write (Program Files), not in Velopack's per-user `%LocalAppData%\ChippiApp`, or any process of the user could swap the binary and get SYSTEM ([Velopack installs per user without elevation](https://docs.velopack.io/packaging/operating-systems/windows)). Installing and updating it needs a UAC prompt each time the helper changes.
   - **Scheduled task "run with highest privileges":** what HWiNFO uses; it doesn't work for standard-user accounts ([HWiNFO forum](https://www.hwinfo.com/forum/threads/auto-start-in-standard-account-without-uac.1590)) and has the same binary-location problem.
3. **IPC:** a named pipe with an explicit DACL for the logged-on user's logon SID only; the default pipe DACL gives Everyone read access ([Microsoft](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights)). The helper hosts LHM and answers read-only sensor snapshots; no write commands.
4. **Uninstall:** Chippi's uninstall hook removes the service (one UAC prompt), and never removes PawnIO, which other apps (FanControl) may use.

That's a second installer, a SYSTEM service, an elevation story for updates, and a ring-0 driver on every PC that opts in: a project the size of Install Driver plus a security review. **Recommendation:** keep it out of Phase 4; plan it as its own later step, like Volume boost.

**It fits later without reworking Phase 4:** every reading in Phase 4 comes through a source interface (§4), and the sampler and UI work on "reading present or absent". A later `PrivilegedSensorSource` that talks to the helper's pipe adds CPU package, core and motherboard readings as new sources; nothing in Phase 4's sampler, page or alerts changes.

### Fan control: no-go for Phase 4
- Writing fan speeds needs the same PawnIO plus elevated helper, then a write path on top: Super I/O per board, EC per laptop, Dell SMM, or the GPU vendors' own APIs (FanControl uses NvAPI and ADLX for GPU fans; [FanControl](https://github.com/Rem0o/FanControl.Releases)). There is no documented no-admin route (Inferred). FanControl says laptops are "mostly no".
- Each board needs its own testing of safe limits, restoring automatic control, and conflicts with OEM tools. No board other than Travis's is available to test.
- **Verdict:** no-go for Phase 4. GPU fans are shown read-only: measured RPM where D3DKMT's `FanRPM` reports it, and NVML's intended fan speed as a percentage, labelled % and never RPM. The two are different readings and never stand in for each other.

## 3. Without NVIDIA

- **GPU load, memory, temperature, fan:** the PDH GPU counters and D3DKMT perf data are Windows', not a vendor's. Measured on the AMD Radeon integrated GPU here: load, dedicated memory and 46.0 °C, with no AMD software. Intel and Qualcomm GPUs are WDDM drivers too; whether their driver fills `Temperature` is per driver (Inferred). A zero or unsupported value hides the row; it never shows 0 °C.
- **NVML** is only a refinement on NVIDIA (fan % while `FanRPM` is 0, VRAM total). Chippi works the same without it. Load it by full path from System32 or `%ProgramW6432%\NVIDIA Corporation\NVSMI`, never by name search.
- **AMD ADLX / Intel IGCL:** not needed, because D3DKMT covers temperature. ADLX's SDK license is its own agreement, not open source ([ADLX](https://github.com/GPUOpen-LibrariesAndSDKs/ADLX)), so avoid it.
- **ARM64:** every in-box API above exists on ARM64. NVIDIA has no Windows-on-ARM driver. Snapdragon laptops' CPU readings come from ACPI thermal zones if the OEM exposes them (Inferred). No ARM64 PC was available, so ARM64 stays build-only as in Phase 3.

## 4. Fit with Chippi

- **One feature:** `system-monitor` (already in `FeatureCatalog.cs`, group System), project `src/Features/Chippi.Features.SystemMonitor`.
- **Sources:** each reading is a provider behind an interface (`ICpuSource`, `IGpuSource`, `IMemorySource`, `INetworkSource`, `IDiskSource`, `IBatterySource`, `ITemperatureSource`), so tests use fakes, as `FakeDeviceSystem` does for audio, and a privileged source can be added later (§2). A source that fails or returns nothing is marked unavailable and retried at a slow rate; a missing value is never 0.
- **Sampler:** one background thread in the module, like `Chippi.Audio`'s engine thread. It polls only what a consumer needs: the island page while it's open, a tile status, a tray readout, an alert. Nothing shown and no alert on means no timer, as Karl's spec says vorssaint does.
- **Snapshots:** the sampler publishes an immutable snapshot with a timestamp per reading; history is a ring buffer per series (Karl's 120 samples) with gaps after sleep (`PowerModeChanged`).
- **Island:** one "System" tile (`IslandTileSpec`, key `system`), status = one short reading (for example "CPU 9%"), and one page. `Column` can put CPU and GPU side by side, as Sound does.
- **Settings:** a System monitor page under System, with configuration only (refresh, units, readouts, alerts), per the Surfaces rule that Settings never repeats a daily control.
- **Alerts:** through the existing notifier (`FeatureEntry.Notifier`, Windows and Island places), one notice kind per alert.
- **Stable identity** (Karl's correction):
  - **GPU:** its LUID changes every boot. Remember it by PnP device instance ID only; the vendor, device and description repeat across identical cards. Map instance ID to LUID with `CM_Get_Device_Interface_List(GUID_DISPLAY_DEVICE_ARRIVAL)`, `DEVPKEY_Device_InstanceId` and `D3DKMTOpenAdapterFromDeviceName`. Measured: `PCI\VEN_10DE&DEV_2704&SUBSYS_511A1462&REV_A1\4&1babdf5b&0&0009` (RTX 4080), `PCI\VEN_1002&DEV_13C0&...\4&1ebe6a9c&0&0041` (Radeon), `ROOT\BasicRender\0000`.
  - **Network adapter:** `InterfaceGuid`. **Disk volume:** its volume GUID path.
- **Tray readouts:** the tray icon is one `Shell_NotifyIcon`; a numeric readout means drawing the number into extra notification-area icons, one per reading (TrafficMonitor uses a taskbar window instead). A design decision for the mockup; both work without admin (Inferred, not probed: it would add icons to Travis's tray).
