# Chippi Phase 2 brief: Downloads organizer

Phase 2 turns the "Downloads organizer" placeholder into a real module. It watches folders, sorts new files by rules, previews moves before making them, keeps a history with verified undo, and adds an Explorer "Organize" entry. It builds on the Phase 1 shell without changing its fixed decisions.

Read first, in order:
1. This brief.
2. [phase-2/architecture.md](phase-2/architecture.md): projects, pipeline, history, Core changes, workstreams (Kayla).
3. [phase-2/behavior-spec.md](phase-2/behavior-spec.md): how each behavior should work, researched from Mouzi (Karl). Behavior only: never copy Mouzi's code, strings or assets.
4. `DESIGN.md`, including the new Bare Label Rule in §3, and `PLAN.md` Phase 2.

## 1. Decisions (Travis, 2026-10-22)

| Topic | Decision |
|---|---|
| History | SQLite through `Microsoft.Data.Sqlite` (MIT), behind `IHistoryStore`. Only `SqliteHistoryStore` touches SQLite |
| Notices | Two places: Windows notifications and the island. For each event kind (organized, held for review, failed, archive imported) the user picks Windows, island, both or neither. The Tidy tab always lists everything |
| Explorer entry | Classic menu ("Show more options"), per user, no admin. The label is exactly **Organize** |
| First mode | A newly added folder, Downloads included, starts in **Manual** |
| Copy | The Bare Label Rule (DESIGN.md §3): labels name the action only, never the app |

## 2. Lead calls (Marge)

- **Folder modes:** only PLAN.md's three, Silent, Manual and Paused. No Suggest mode, trash actions, filename normalization or scheduling.
- **Timing:** Silent waits a 30 s grace period. Files that aren't ready yet are retried with backoff for up to 24 hours.
- **Zero-byte files** are always skipped until they grow, whatever Mouzi's docs say.
- **Existing files:** files already in a folder when it's added are never moved silently. They show up only in Organize now's preview.
- **Archive import** always goes through preview and never applies on its own. Limits: 10,000 entries, 4 GB expanded, and a 100:1 compression-ratio check.
- **History** is kept 90 days by default, with Clear history in the History view.
- **Undo** is a new checked operation, verified by SHA-256, never a blind reverse move.
- **No self-triggering** (P2-B, found in the P2-A review): the executor remembers every file it just produced (path plus fingerprint), including rename-only results that stay in a watched folder and files restored by undo, and the watcher skips them. Without this, a Silent rename rule renames its own output forever (`2026-2026-2026-invoice.pdf`). Covered by a test.

## 3. Fixed decisions carried from Phase 1

- C# / .NET 10 / WPF, Windows 11, x64 and ARM64, GPL-3.0-or-later.
- Standard user only.
- No telemetry and no network calls.
- No stock WPF chrome.
- DESIGN.md wins over taste.

## 4. Workstreams

Ownership is set out in architecture.md §6.

| # | Workstream | Owner | Reviewer | Starts |
|---|---|---|---|---|
| P2-0 | Core contracts (module data folder, notifications with per-event places, activation with arguments, settings deep links, Shell integration capability) and Chippi.UI additions (themed checkbox, text field, drag handle, with gallery states) | Kayla | Karl | now; lands first |
| P2-A | `Chippi.Rules` and its tests: rules model, first-match planner, placeholders, rename templates, `.chippiignore`, misleading-name check, path validation | Karl | Kayla | now (pure, no dependencies) |
| P2-B | Pipeline, readiness, executor, history, undo, archive import, and their tests | Kayla, after P2-0 | Karl | after P2-0 and P2-A's planner interface |
| P2-C | Views: Tidy tab, preview and approve, rules editor, History, module settings page | assigned later | | after Travis approves the new designs |
| P2-D | Toast backend spike, then the Explorer verb, activation routing and packaging | Karl, after P2-A | Kayla | spike after P2-0 |
| P2-E | UI smoke tests: Phase 1's UI Automation checks plus the Tidy flows, as a test project | assigned later | | after P2-C |
| Lead | DESIGN additions and the review page, composition slots, integration, report | Marge | Kayla | now |

## 5. Design work before P2-C

These aren't designed yet. Marge produces mockups in the Studio Rack style and Travis approves them before any view is built:
- the preview and approve view
- the rules editor
- the History view
- the module settings page, including the per-event notification grid
- the Tidy card with more than one watched folder
- waiting and failed stash rows
- the themed checkbox
- notification copy

## 6. Process

- Branch and worktree per workstream. Each owner touches only the folders they own.
- A second member reviews before merge. Marge merges and pushes `main`.
- Scratch, review notes and probes go in `X:\code\chippi-scratch\phase-2\`, never the repo root.
- Dev runs use `CHIPPI_DEV_PROFILE`. Nobody installs on Travis's PC without asking.
- One lead thread. No side jobs.
- At the end of the phase, run the [end-of-phase checklist](end-of-phase-checklist.md).

## 7. Done when

- Build and test pass with 0 warnings for win-x64 and win-arm64. Rules and pipeline tests cover the behavior spec.
- In Manual mode, a new download appears in preview, approving moves it, and undo restores it with a matching hash.
- Silent mode moves after the grace period, never touches partial, locked, 0 KB or cloud-placeholder files, and never moves files that were there before the folder was added.
- Held files (misleading names, ignore matches) show in amber with their reason and are never moved silently.
- Notices reach exactly the places set for each event, and the Tidy tab lists every event.
- The Explorer "Organize" entry appears only while the module is installed and its switch is on, opens a preview for that folder, and is removed on uninstall.
- Archive import previews contents within the limits and moves nothing until approved.
- The installer still installs, updates in place and uninstalls cleanly, and `e_sqlite3.dll` loads in the packaged x64 build.
- Every new label follows the Bare Label Rule.
