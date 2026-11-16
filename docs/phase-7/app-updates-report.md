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

- No-incremental Release App builds for x64 and ARM64 passed with zero warnings/errors. Final changed-code builds passed for both runtimes. [PR #347](https://github.com/tcurtsinger/Chippi/pull/347) merged with a merge commit after both CI builds, native publishes and the full x64 test job passed.
- CI passed all 70 App updates tests. Locally, the initial windowless suite passed 63 tests; behavior fixes passed all 11 ViewTests and all 27 EngineTests after correcting two inline-text assertions. Helper tests: 55 passed. Core tests: 188 passed.
- Main's first post-merge test job failed the existing Capture Recent `Verbs_wait_while_a_copy_runs` status assertion: its real three-second notification had expired during a delayed dispatcher pump. One rerun of the failed job passed the full suite; both builds remained green. No product or test code was changed for that retry.
- Real read-only discovery completed in 4,003 ms and found 20 updates: six User, eleven Machine and three unknown-scope packages. The source reported a Store warning while preserving winget results. No owner app was installed, updated, repaired or registered, and no UAC prompt was shown.
- Twenty-six native offscreen renders cover dark/light lists, pagination, confirmation, progress, failures, empty/source-error states, Settings, a single selected app among twenty and partial Store failure. They use synthetic data and show no desktop windows. Final fixture timings are recorded in the design notes; they measure layout rather than real compositor frames or COM discovery.
- Independent full code review found three behavior issues, all corrected with deterministic cases and focused review. A later completed-version preference-refresh edge case was also fixed and reviewed. The dedicated design roles were unavailable, so fresh fallback agents performed the scoped UI review and documentation. Their final verdicts cover the stated fixes and functional-regression states.

## Remaining live validation

The owner chose to provide an away time later. The prepared single-file probe and native helper use `build/admin-helper-sandbox.ps1 -AppUpdates`; it has not run for this feature. Its disposable guest must prove a machine-scope 7-Zip upgrade, a User-scope SumatraPDF upgrade through COM, a nonexistent-package failure, one machine helper launch, persisted results and helper exit. It uses its own UUID and disables clipboard, microphone/camera and printer redirection. No owner package changes are authorized by this test.

Normal owner UAC Yes/No and ARM64 hardware execution remain separate checks. Store servicing depends on its account/source availability and was not exercised by the read-only success. Some machine reboot-required outcomes are normalized by winget CLI to exit 0; Chippi honors explicit COM reboot flags and known CLI codes without inventing a flag for that normalized case.

## Installer refresh

`build/pack.ps1 -Version 0.1.9 -Runtime win-x64` completed from merged commit `33eb02bfe56e04806c2c10db77059c3d337e2eb7`. All six release files match `phase-1-release-artifacts.csv`, including the 150,433,489-byte Setup executable. The portable archive's native helper, RID-specific WinGet factory, WinMD, complete WinGet NOTICE and project license/notices match staging byte for byte. The factory also matches the approved package hash. The set was copied to `X:\code\chippi\build\artifacts\releases\0.1.9\win-x64` and its manifest checked again. Nothing was installed or launched.

This refresh contains x64 only. The missing ARM64 C++ toolchain was not installed and no managed helper substitutes for the native one; ARM64 native publishing passed in CI. The remaining Sandbox check above is still deferred.
