# App updates implementation and validation

2026-11-16. Approved mockup: [App updates](design/app-updates.html). Native design evidence and review scope: [design notes](app-updates-design.md).

## Implemented

- Actions > Apps > Update apps, Search/commands and two initially unassigned central shortcuts. No home tile, startup scan or installer launch during discovery.
- Read-only winget and Store discovery through the approved `Microsoft.WindowsPackageManager.ComInterop` 1.29.380 projection. The official native static factory, WinMD and complete NOTICE ship as external, RID-correct content. Direct unpackaged COM activation failed with `0x80040155`; native vector views require Count/index copying on the tested App Installer instead of an optional iterable query.
- Individual and batch confirmations capture the exact reviewed app set. Updates run sequentially, with one shared helper session for confirmed machine updates. User/Store servicing uses strict User scope; no forced scope conversion, hash override or forced app shutdown. Unknown-version entries are hidden by default and explicitly labeled when included.
- Five-row pagination retains unselected apps while a selected batch runs. Latest-batch counts are independent of the list count; successful versions leave subsequent actionable batches. A fresh check can discover a later version. Both explanatory sentences removed during approval remain absent.
- Skip a version or always skip, Restore, and the last 50 per-app results. Terminal machine results persist immediately; returning results are deduplicated and stale progress cannot overwrite completion. Queue cancellation lets an already-started installer finish.
- Soft close uses an exact registered executable match and the shared ActionRunner/PC, with captured-window revalidation. It does not kill a process or dismiss a save dialog. Dev profiles and explicit read-only sources refuse native updates before activation.

## Local evidence

- No-incremental Release App builds for x64 and ARM64 passed with zero warnings/errors. Subsequent changed-code builds also passed on x64; final ARM64 and CI results are recorded with the PR.
- Initial windowless feature suite passed 63 tests after native integration fixes. The code-review behavior batch ran 69 tests: 67 passed and two new inline-text assertions failed; their semantic-text/tooltip corrections passed all 11 ViewTests. The final completed-version preference regression passed with all 27 EngineTests. CI runs the final combined suite. Helper tests: 55 passed. Core tests: 188 passed.
- Real read-only discovery completed in 4,003 ms and found 20 updates: six User, eleven Machine and three unknown-scope packages. The source reported a Store warning while preserving winget results. No owner app was installed, updated, repaired or registered, and no UAC prompt was shown.
- Twenty-six native offscreen renders cover dark/light lists, pagination, confirmation, progress, failures, empty/source-error states, Settings, a single selected app among twenty and partial Store failure. They use synthetic data and show no desktop windows. Final fixture timings are recorded in the design notes; they measure layout rather than real compositor frames or COM discovery.
- Independent full code review found three behavior issues, all corrected with deterministic cases and focused review. A later completed-version preference-refresh edge case was also fixed and reviewed. The dedicated design roles were unavailable, so fresh fallback agents performed the scoped UI review and documentation. Their final verdicts cover the stated fixes and functional-regression states.

## Remaining live validation

The owner chose to provide an away time later. The prepared single-file probe and native helper use `build/admin-helper-sandbox.ps1 -AppUpdates`; it has not run for this feature. Its disposable guest must prove a machine-scope 7-Zip upgrade, a User-scope SumatraPDF upgrade through COM, a nonexistent-package failure, one machine helper launch, persisted results and helper exit. It uses its own UUID and disables clipboard, microphone/camera and printer redirection. No owner package changes are authorized by this test.

Normal owner UAC Yes/No and ARM64 hardware execution remain separate checks. Store servicing depends on its account/source availability and was not exercised by the read-only success. Some machine reboot-required outcomes are normalized by winget CLI to exit 0; Chippi honors explicit COM reboot flags and known CLI codes without inventing a flag for that normalized case.

Installer refresh follows the green-CI merge. Local packaging is x64; the missing ARM64 C++ toolchain is not installed automatically and no managed helper substitutes for the native one.
