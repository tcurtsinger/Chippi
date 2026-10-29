## Summary

System monitor fits the fixed island body as two tables. Settings lists every drive.

~~~diff
 Island System monitor
- two columns of graph cards and processor cells
+ readings table: CPU, GPU, Memory, Network, optional Battery and Thermal zone
+ disks table: letter, Used, Free, Total
+ 28 DIP rows; tight disk header and rows both 22
 Settings
+ every drive, without the island's row limit
~~~

The sampler, alerts and configurable tile status retain their behavior. The page no longer requests per-processor samples or shows a graph-span note. Settings keeps only its disk watch after capability discovery and releases it on unload.

## Evidence

- **Before:** main 95f25f0, same fake readings in a 520 x 360 host.
  **After:** this branch, same values and host.
- [Desktop before](screens/part-c/before-desktop.png) / [after](screens/part-c/after-desktop.png).
- [Crowded before](screens/part-c/before-crowded.png) / [after](screens/part-c/after-crowded.png).
- [Laptop before](screens/part-c/before-laptop.png) / [after](screens/part-c/after-laptop.png).
- Five new regressions cover exact seven-drive fit in 304 DIP, fewer whole rows with Battery/Thermal zone, sorted volumes and Used = Total - Free, failed/pending values, and the full Settings drive list.
- Full Release suite: 1,274 passed, 0 failed, 16 desktop-input tests skipped. System monitor: 81; Island: 160. Four obsolete graph-scale cases were removed with their page-only implementation.
- Release x64 and ARM64, and Debug x64: 0 warnings, 0 errors.
- Actual Debug --island-pages completed on a disposable dev profile with only System monitor installed: 13 images on top, left and right, success recorded in the log. Readings were read-only; Clipboard recording was off and File organizer was not installed.
- Owned snapshot process stopped and profile/notification registration removed. Production settings hash unchanged. No Setup or keyboard/mouse injection.
- Raw logs, render harness, fixtures and snapshots: X:/code/chippi-scratch/refresh/c/.

## Merge Danger

**Door:** two-way

No saved-settings format changes.

**Blast Radius:** UI

Settings now polls disk space while its page is open. The island leaves overflow drives out, with every drive available in Settings. No real alert was triggered; fake-data regressions cover the warning color.