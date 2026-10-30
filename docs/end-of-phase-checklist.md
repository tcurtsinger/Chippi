# End-of-phase checklist

Run this once a phase's report is written and the last change is merged and pushed (Travis, 2026-10-22). The lead runs it; nothing here touches `main`'s history.

## During the phase (so cleanup stays small)
- Review notes, handoffs, probes and UI Automation scripts go in `X:\code\chippi-scratch\phase-N\`, not in the repo root.
- Dev runs use `CHIPPI_DEV_PROFILE=<name>`, so test settings and logs stay under `Chippi-dev`.

## Cleanup
1. **Check before deleting.** Every worktree has no uncommitted changes, and every branch is merged into `main` (`git branch --no-merged main` prints nothing). Anything that fails either check is reported, not deleted.
2. **Archive.** Zip the untracked and ignored leftovers in the main checkout (review notes, handoffs, probes, scripts, screenshots, scratch files) to `X:\code\chippi-archive\phase-N-leftovers-<date>.zip`. Keep `build\artifacts` (the current installers) and `bin`/`obj`.
3. **Delete** the archived originals, then confirm `git status` shows nothing untracked.
4. **Worktrees and branches.** `git worktree remove` each workstream worktree, `git branch -d` its branch, then `git worktree prune`. Only `main` remains, locally and on GitHub.
5. **Dev profiles and temp files.** Delete `%APPDATA%\Chippi-dev`, `%LOCALAPPDATA%\Chippi-dev` and `%TEMP%\chippi*` (probes, UI Automation runs, generated assets, the Core tests' `chippi-tests` folder). Then confirm none of these dev registrations are left:
   - `Chippi-dev-*` values under `HKCU\...\CurrentVersion\Run`
   - `ChippiOrganize-dev-*` keys under `HKCU\Software\Classes\Directory\shell` and `...\Directory\Background\shell`
   - `Chippi-dev-*.lnk` under `%APPDATA%\Microsoft\Windows\Start Menu\Programs`
   - the dev notification `CLSID` keys under `HKCU\Software\Classes\CLSID`
6. **Processes.** No dev or test Chippi is left running. Travis's installed copy is never touched.
7. **Record** what was archived and removed in the phase tracker.

## Phase 1 cleanup (2026-10-22)
- Archived 30 items (487 files, 11.7 MB) to `X:\code\chippi-archive\phase-1-leftovers-2026-10-22.zip`. That includes the UI Automation scripts (`uia-*.ps1`), worth reviving as a CI smoke test in Phase 2.
- Removed 15 workstream worktrees and 16 merged branches. Only `main` remains.
- Deleted the `Chippi-dev` test profiles (a11y, karl, lead, toggle, ws1 to ws5). No dev Run entries were present.
- Deleted 14 `%TEMP%\chippi*` scratch folders (about 15 MB), caught by Kayla after the first pass.

## Phase 2 cleanup (2026-10-23)
- Run before the Phase 2 report, at Travis's request; the report follows the UI/UX pass.
- Archived `X:\code\chippi-scratch\phase-2` (1,301 files, 306 MB) to `X:\code\chippi-archive\phase-2-leftovers-2026-10-23.zip` (175 MB), then deleted it. The main checkout had no untracked leftovers.
- Deleted the toast spike in `build/toast-spike/` (classic COM was chosen; see `docs/phase-2-handoff.md`).
- Every workstream worktree and branch was already removed at merge. Only `main` remains, locally and on GitHub.
- Deleted the `Chippi-dev` profiles (lead, p2-core-review, p2-core-review-ui), `%TEMP%\chippi-tests`, and the dev profile's Windows notification entry. No dev Run, Explorer, CLSID or Start Menu entries were present. The installed copy's registrations and running process were left alone.

## Phase 2.5 cleanup (2026-10-24)
- Run after the report (`docs/phase-2-report.md`, #27). Karl took a read-only inventory first.
- Archived `X:\code\chippi-scratch\phase-2.5` (2,005 files, 399 MB) to `X:\code\chippi-archive\phase-2.5-leftovers-2026-10-24.zip` (197 MB), checked the entry count, then deleted it. The main checkout had no untracked leftovers.
- Superseded installer sets are kept in `X:\code\chippi-archive\phase-2.5-installers-0.1.0*`. `build\artifacts` holds the current build (#26).
- Every worktree and branch was removed at merge (PRs #2 to #27). Only `main` remains, locally and on GitHub.
- Deleted 17 `Chippi-dev` profiles each in Roaming and Local, 2 `%TEMP%\chippi*` items, 22 dev Explorer keys, 16 dev Start Menu shortcuts and 16 dev notification CLSIDs. No dev Run values or dev processes were present. The installed copy's registrations and running process were left alone.

## Phase 3 cleanup (2026-10-26)
- Run after the Phase 3 report merged (#55, main `176e160`) and Travis reported Pass on the live checks. Karl performed the cleanup; this PR records it.
- Archived `X:\code\chippi-scratch\phase-3`, `phase-3-prep` and `phase-3-surfaces` into `X:\code\chippi-archive\phase-3-leftovers-2026-10-26.zip`: 2,852 files, 2,196,172,702 source bytes, 838,921,822 ZIP bytes. Verified every archived file's size and SHA-256, then rechecked that the sources were unchanged before deleting the three original folders. Archive SHA-256: `A00BD3E15C032E11956A03E1768D404142F336014CD3C1169E10B3C6684DDEBE`.
- Removed the clean `chippi-boost-gate` and `chippi-boost-loopback` worktrees and the merged local `phase-3/boost-gate` branch. The report worktree was already removed at merge; Kayla removed `chippi-review-52`. Deleted the merged remote `feat/surfaces-model` branch after checking its head was unchanged and merged.
- Exception approved by Marge: kept `spike/boost-loopback` locally and on GitHub at `921e9383a47569b491c7ce9c518d32ef32297f5d`, because the durable [experiment findings](phase-3/boost-experiment.md) name its source. The only worktree besides main is this cleanup PR's worktree, to be removed after merge.
- Deleted `audit` and `surfaces` from both Roaming and Local `Chippi-dev` (four profile folders), plus both now-empty `Chippi-dev` roots. Kayla had already deleted `island-click` and `s6`. Removed two `%TEMP%\chippi*` folders (`chippi-s3-tests`, `chippi-tests`), four dev Explorer keys, four dev Start Menu shortcuts and four dev notification CLSIDs. The shortcuts/CLSIDs included remnants from `island-click` and `s6`. No dev Run values or Windows notification settings keys were present.
- Final audit: no dev profiles, temp items, Run values, Explorer keys, shortcuts, notification CLSIDs/settings keys or dev/test Chippi processes remain. Main has no untracked files; its only ignored files are `bin`/`obj` and `build/artifacts`. All 12 current release files still match their manifest. Installed Chippi stayed running as PID 46688; its settings and VB-CABLE were not changed.
- Evidence is retained outside the deleted scratch folders at `X:\code\chippi-archive\phase-3-cleanup-2026-10-26`: per-file archive hashes, archive summary, before/after registration and process inventories, final state and archive/audit scripts. Current installers and earlier installer archives remain in place.

## Phase 4 cleanup (2026-10-27)

- Run after Travis reported Pass on task 26 and the result update merged (#67, main `82ae166`).
- Archived only `X:\code\chippi-scratch\phase-4` to `X:\code\chippi-archive\phase-4-leftovers-2026-10-27.zip`: 377 files, 347,438,991 source bytes, 117,977,613 ZIP bytes. Verified the file count, every file's size and SHA-256, and that the source was unchanged before emptying the folder. The ZIP also contains one empty-directory entry. Archive SHA-256: `7ABD87FB2446333A96F077283958741988565A772631878DD1E6771DA5D265D8`.
- Preserved `chippi-scratch\audit-2026-10-27` and checked its file hash unchanged. The earlier first-pass installer archive remains in `chippi-archive\phase-4-installers-0.1.0-firstpass-763a619`.
- All phase worktrees and remote workstream branches were already removed. Pruned stale remote/worktree references and deleted the verified merged local `fix/audit-quick-wins` branch. Main and this cleanup PR's worktree remain; Marge removes the latter after merge. GitHub has only `main` and the retained `spike/boost-loopback` experiment before this cleanup branch is pushed.
- Kept local `docs/phase-4-report` at `eda0b65`: its result patch is equivalent to the merged live-results patch, but the commit itself is not an ancestor of main. The checklist says to report an unmerged branch rather than delete it. Also retained the local boost experiment reference.
- Deleted three dev profile folders (`m2` in Roaming and Local, `notifications-test` in Local), both `Chippi-dev` roots, 13 `%TEMP%\chippi*` items, one dev notification CLSID and one dev Start Menu shortcut. No dev Run values, Explorer keys or notification-settings keys were present; the final registration inventory is empty.
- Rechecked all 12 current release files by size and SHA-256 without changing `build\artifacts\releases\0.1.0`. Installed Chippi stayed running as PID 39868 and its settings hash was unchanged. No dev/test Chippi process remains. No audio, driver or installed-app changes were made.
- Evidence is outside the emptied scratch folder at `X:\code\chippi-archive\phase-4-cleanup-2026-10-27`: per-file archive hashes and summary, before/after dev inventories, audit-folder hashes, protected release manifest, installed-process/settings checks, and the archive/cleanup/verification scripts. Main has no untracked files; its ignored files remain build artifacts and bin/obj outputs.

## Phase 5 cleanup (2026-10-28)

- Run after Travis reported Pass on the live checks (task 34) and final installer spot-check (task 57), and the report merged (#81, main `7d24c3f`).
- Archived `chippi-scratch/phase-5`, `cable-probe`, `cable-karl-review` and `review-phase-5` to `X:/code/chippi-archive/phase-5-leftovers-2026-10-28.zip`: 1,827 files, 778,131,320 source bytes, 323,068,003 ZIP bytes. Verified every archived file's size and SHA-256, then rechecked the sources were unchanged before deleting the four original folders. Archive SHA-256: `21E412A85D3D598F3D1F1D8DCFA4EA3AE3BD614B695FF9AE4E26630C76A52725`.
- All workstream worktrees and GitHub branches were already removed. Pruned 22 stale remote Phase 5 references and deleted the verified merged local `pr83` branch. The retained `spike/boost-loopback` was also absent from GitHub before cleanup; restored it from the preserved local commit `921e9383a47569b491c7ce9c518d32ef32297f5d`. GitHub now has only `main` and that spike. The local cleanup-record branch is retained for lead review.
- Deleted three dev profile folders (Roaming `review5`, Local `review5` and `notifications-test`), both now-empty `Chippi-dev` roots, nine `%TEMP%/chippi*` items, two dev Explorer keys, one dev notification CLSID and one dev Start Menu shortcut. No dev Run values or Windows notification-settings keys were present. Final dev registration inventory is empty.
- Protected `chippi-scratch/audit-2026-10-27` is unchanged by file hash. All 12 final release files still match the manifest. Installed settings hash is unchanged; no Chippi process was running before or after cleanup, and none was started or stopped. No audio, driver, real clipboard or installed-app changes were made. Unrelated scratch folders, build artifacts and bin/obj outputs remain untouched.
- Evidence is outside the removed scratch folders at `X:/code/chippi-archive/phase-5-cleanup-2026-10-28`: per-file archive hashes and summary, archive and dev-cleanup scripts, before/after registrations, protected audit/release/settings checks and final state. Main has no untracked files. This cleanup record was committed in an isolated worktree, removed after the commit; only the main worktree remains.

## Phase 5.1 cleanup (2026-10-30)

- Run after Travis passed the shell redesign live check, #137 merged the redesign into `main` (16bfd6f), the final installer was installed, and #138 recorded its hashes (main `4132538`).
- Archived everything left in `chippi-scratch` from the refresh and the redesign (21 folders and 6 loose files) to `X:/code/chippi-archive/phase-5.1-leftovers-2026-10-30.zip`: 4,839 files, 1,140,401,529 source bytes, 469,211,516 ZIP bytes. Every file's size and SHA-256 was verified in the ZIP and the sources rechecked before deleting. Archive SHA-256: `7FB478CEE671D7CBD82E6287C6C40659F0B6989345189AF8C75EF3A88BF853B1`. The archive includes one untracked probe (`KaProbe.cs`) copied out of `chippi-shell-pages-fix` before that worktree was removed.
- Kept in place, unchanged by hash: `chippi-scratch/audit-2026-10-27` and both x64 installer copies, `chippi-scratch/shell-redesign/installer-16bfd6f` (the installed build) and `installer-4f52e49` (the live-check build).
- Copied the full 12-file installer sets out of the pack worktrees before removing them, each verified against its manifest commit: `chippi-archive/phase-5.1-installers-0.1.0-16bfd6f` (main) and `phase-5.1-installers-0.1.0-4f52e49` (`c588fbc`).
- Removed 20 worktrees (`chippi-shell-*`, `chippi-pack-refresh-7`, `chippi-pack-shell-1`, `chippi-review-*`), 23 local branches and 24 GitHub branches, each checked to be an ancestor of `origin/main` first. GitHub now has only `main` and `spike/boost-loopback`.
- Kept local `refresh/report` and its worktree `chippi-refresh-report` (`faf8d2c`): it holds `docs/refresh-2026-10-28/report.md`, which never reached `main` and has no PR. Reported to the lead instead of deleted.
- Deleted both `Chippi-dev` roots (10 Roaming and 15 Local profiles), nine `%TEMP%/chippi*` items, 10 dev Explorer keys, 11 dev notification CLSIDs and 11 dev Start Menu shortcuts. No dev Run values or notification-settings keys were present. The final dev registration inventory is empty.
- Installed Chippi stayed running (PID 21184, `ChippiApp/current`), and its settings hash is unchanged. No audio, driver, real clipboard or installed-app changes were made. The main checkout's untracked `.impeccable/` is the owner's and was left alone.
- Evidence is at `X:/code/chippi-archive/phase-5.1-cleanup-2026-10-30`: per-file archive hashes and summary, archived installer hashes, before/after worktrees, branches and registrations, the settings and process checks, and the scripts. `tools/` keeps copies of the pack hash check and the island-test loop, which were in the archived scratch.
