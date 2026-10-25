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
