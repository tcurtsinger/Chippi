# Admin helper runtime validation

2026-11-16, owner-approved away session. These checks used disposable Windows Sandbox guests, isolated probe files and a native x64 helper. Nothing was installed, updated or elevated on the owner's Windows session. Clipboard, microphone/camera and printer redirection were disabled.

## Findings and corrections

- `Process.GetCurrentProcess().SafeHandle` requested unnecessary full process access. A genuinely restricted child could not open its own process that way. Current-token checks now use the current-process pseudo-handle and `TOKEN_QUERY`. Peer handles request only query-limited information; they remain held throughout the session.
- Inputs are copied from readonly host shares into real guest folders before restricted execution. A LUA token with disabled privileges and medium integrity is checked before downloading anything: both elevation and administrator membership must be false, and the private pipe must open successfully.
- The Sandbox guest cannot elevate this filtered token through its UAC-disabled `runas` path. The helper correctly refused with exit 3. A bounded controller inside the guest starts only the fixed helper path and validated private pipe name; the probe injects this launcher internally while retaining the real native process/account/path/elevation checks. The public production constructor still uses `runas`. This establishes no owner consent/decline result.
- Native connect/Done exchanges passed in separate guests at 125.7 ms and 115.7 ms. The measured interval includes the guest broker's polling and process startup, not a human UAC response. The standard-side process reported `elevated=false` and `administrator=false`.
- The update operation reached its executable guard and refused the guest's volume-root `DELETE` permission. That right cannot rename a drive-volume root, so it was a false positive for ancestor replacement. Only exact drive-root syntax is exempted from object `DELETE`; owner, reparse/final-path, child-deletion and ACL-control checks remain. Ordinary folders and mount-point directories retain the original checks. Immediate executable folders still refuse file/subdirectory creation. [Microsoft's rename rules](https://learn.microsoft.com/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_rename_information) document the volume-root restriction.
- The runner now owns an explicit Sandbox UUID, stops only that UUID in `finally`, and saves native launch errors and helper startup stages. Broker requests and PID responses use atomic file publication; the broker is guest-only, fixed-path and bounded to four launches per probe.

The full independent follow-up review found no material issues. A focused review of the root-specific behavior fix also found none. Fifty-five windowless helper tests passed, including ten real security-descriptor cases covering root `DELETE`, ordinary ancestors, child-deletion, ACL control and immediate-folder creation.

## Completed upgrade check

The owner approved the remaining run on 2026-11-16. The fullscreen gate was clear, and the corrected native x64 helper passed the complete [Sandbox flow](../../tests/Chippi.Elevated.Tests/Sandbox/README.md):

- The caller reported `elevated=false` and `administrator=false`; the native connect/Done exchange passed in 111.3 ms, including broker polling and process startup.
- The missing-package guard check reached winget and returned `Failed` with exit code `-1978335212`, rather than a path-trust refusal.
- A machine-scope 7-Zip fixture advanced from `26.05.00.0` to `26.06.00.0`. The helper returned `Done` with exit code 0, then `Failed` with exit code `-1978335212` for the deliberately nonexistent second package. The two-item batch took 124.4 s, including winget's download and installation work.
- The restricted probe exited with code 0. A separate guest observer found zero remaining `Chippi.Elevated` processes after the batch probe exited. This establishes exit, not a measured subsecond exit duration.
- The runner stopped its own Sandbox UUID; the host's Sandbox list was empty afterwards. No host package installation or update was performed.

The `winget list` output also reported an unavailable `msstore` source; its winget-source 7-Zip rows still established the version change. The helper's update commands explicitly selected `--source winget`.

## Remaining validation

Owner UAC Yes/No interaction remains a separate live check; Sandbox's elevated broker cannot establish it. ARM64 hardware execution remains unverified. The guest timing above does not establish the ten-run, real-consent latency target or a precise helper-exit duration.

Runtime logs and generated probes are temporary session evidence, not archived assets.
