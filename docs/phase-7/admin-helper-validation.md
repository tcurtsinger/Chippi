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

## Remaining validation

The fixture installation succeeded inside the guest, but the helper did not upgrade it: the earlier guard correctly refused before starting winget. The final real-upgrade rerun with the corrected root check was blocked by the owner's fullscreen game. It remains pending; do not report the update as passed.

Run the prepared [Sandbox flow](../../tests/Chippi.Elevated.Tests/Sandbox/README.md) when the owner is away and the fullscreen/Zoom gate is clear. It must prove the version advances, a nonexistent package fails without a retry, and the helper exits. Owner UAC Yes/No interaction remains a separate live check; Sandbox's elevated broker cannot establish it. ARM64 hardware execution remains unverified.

Runtime logs and generated probes are temporary session evidence, not archived assets.
