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
5. **Dev profiles.** Delete `%APPDATA%\Chippi-dev` and `%LOCALAPPDATA%\Chippi-dev`, and confirm no `Chippi-dev-*` value is left under `HKCU\...\CurrentVersion\Run`.
6. **Processes.** No dev or test Chippi is left running. Travis's installed copy is never touched.
7. **Record** what was archived and removed in the phase tracker.

## Phase 1 cleanup (2026-10-22)
- Archived 30 items (487 files, 11.7 MB) to `X:\code\chippi-archive\phase-1-leftovers-2026-10-22.zip`. That includes the UI Automation scripts (`uia-*.ps1`), worth reviving as a CI smoke test in Phase 2.
- Removed 15 workstream worktrees and 16 merged branches. Only `main` remains.
- Deleted the `Chippi-dev` test profiles (a11y, karl, lead, toggle, ws1 to ws5). No dev Run entries were present.
