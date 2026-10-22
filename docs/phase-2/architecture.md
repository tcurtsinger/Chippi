# Phase 2 architecture draft: Downloads organizer

Author: Kayla. Date: 2026-10-22. Status: draft for Marge's brief, nothing built.
Inputs: PLAN.md Phase 2, DESIGN.md, Karl's `behavior-spec.md`, Travis's four decisions.

## Decisions this draft builds on

| Topic | Travis's call | What it means here |
|---|---|---|
| History | SQLite | `Microsoft.Data.Sqlite`, kept behind one store class (§3) |
| Notices | Two places, configurable per event | Windows notifications and the island are separate places. For each event kind (files organized, file held for review, failure, archive imported) the user picks whether it shows as a Windows notification, in the island, both or neither. The Tidy tab always lists everything. (Travis, 2026-10-22) |
| Explorer | Classic menu, label **Organize** | HKCU `shell` verb in the "Show more options" menu. The label is one word. |
| First mode | Manual | A newly added folder starts in Manual |
| Copy rule (standing) | No verbose copy | Labels name the action only: "Organize", not "Organize with Chippi". Should go into DESIGN.md §3 copy rules. |

Not in scope unless Travis adds it: Mouzi's Suggest mode, trash actions, filename normalization, scheduling.

## 1. Projects

| Project | References | Holds |
|---|---|---|
| `src/Chippi.Rules` (new, WPF-free, no Core reference) | none | Rule model, conditions, first-match planner, destination placeholders, rename templates, ignore-file parser, built-in exclusions, misleading-name check, path validation |
| `tests/Chippi.Rules.Tests` (new) | Rules | The bulk of Phase 2's tests: pure functions, no disk |
| `src/Features/Chippi.Features.Downloads` (new) | Core, UI, Rules, `Microsoft.Data.Sqlite` | The module: watcher, readiness, queue, executor, history, undo, archive import, views |
| `tests/Chippi.Features.Downloads.Tests` (new) | Downloads | Pipeline and executor against temp folders; history against a temp SQLite file |

Why Rules is separate: everything that decides *what should happen* to a file is pure. It's then testable without a disk or a clock, and reusable later (Explorer "Organize" previews, archive import, a future CLI). The module owns *doing* it.

New packages, both permissive: `Microsoft.Data.Sqlite` (MIT), which brings `SQLitePCLRaw.bundle_e_sqlite3` with win-x64 and win-arm64 natives. The single-file publish already sets `IncludeNativeLibrariesForSelfExtract`. WS5's pack check must confirm `e_sqlite3.dll` loads on both RIDs. ARM64 can only be built, not run, here.

### Rules model (in `Chippi.Rules`)

```
Rule        { Id, Name, Enabled, Order, Conditions, Action }
Conditions  { Extensions?, NameRegex?, MinBytes?, MaxBytes?, ModifiedAfter?, ModifiedBefore? }   // all present ones must match (AND)
Action      = MoveTo(destinationTemplate) | RenameTo(nameTemplate) | Hold(reason)
FileFacts   { Name, FullPath, Bytes, Created, Modified, Attributes }          // gathered by the module, passed in
Planner.Plan(FileFacts, IReadOnlyList<Rule>, PlanContext) -> PlannedOperation
PlannedOperation { Kind: Move|Rename|Hold|NoMatch, RuleId, Destination, Warnings[] }
```

Settled here so the brief doesn't inherit Mouzi's gaps:
- **Order:** an explicit `Order` integer, unique per rule. The editor reorders by drag, so there are no ties.
- **Date basis:** placeholders use the local date at planning time. Conditions use the file's last-modified time, with inclusive bounds in local time. (Mouzi uses UTC days; for a desktop user, local time is what they'd expect.)
- **Regex:** .NET `Regex` with `IgnoreCase | CultureInvariant` and a 50 ms match timeout, applied to the full file name. A timeout counts as no match and is logged.
- **Extensions:** case-insensitive, given without the dot. A file with no extension matches only an explicit "no extension" entry or the catch-all.
- **Placeholders:** `{year}` `{month}` `{day}` `{extension}` `{filename}` `{stem}`. An unknown token, `..`, a reserved name (CON, NUL, …), invalid characters or a trailing dot or space fails validation in the editor, never at run time.
- **Destinations:** a relative destination resolves under the watched folder; an absolute one may be anywhere the user can write. Both are canonicalized, and the result must not be the source folder itself.
- **Collisions:** the planner proposes `name (2).ext`, `name (3).ext`, … The executor re-checks at apply time (§2).

### Ignore file

The file is `.chippiignore` in the watched folder: one pattern per line, `*` wildcard only, `#` comments, blank lines skipped, case-insensitive, file names only (not recursive). It's a deliberately small format: no negation, no `?`, no `**`. Built-in exclusions always apply first (§2).

### Misleading names

A name is flagged when its last extension is executable or script (`exe msi bat cmd com scr pif ps1 vbs vbe js jse wsf hta lnk`) and either of these is true:
- the extension before it looks like a document, media or archive (`invoice.pdf.exe`);
- the name contains a bidi override (U+202E or U+202D) or a run of spaces before the extension.

A flagged file is always held, even in Silent mode, and starts unselected in preview. The check never opens or runs the file, and a notification never offers to open it. This is a filename heuristic, not a malware scan, and the UI doesn't claim otherwise.

## 2. Watcher pipeline (module)

```
FileSystemWatcher (per folder, top level only)
  -> Debouncer (coalesce events per path, 1 s)
  -> Readiness gate          -- built-in exclusions, ignore file, 0 KB, cloud placeholder, grace, stability, lock probe
  -> Mode router             -- Silent: plan + apply | Manual: add to Pending | Paused: no watcher at all
  -> Planner (Chippi.Rules)
  -> Executor                -- re-validate, move/rename, record history
```

- **Watcher:** one `FileSystemWatcher` per Silent or Manual folder; Paused folders get none. It listens for Created, Renamed and Changed, top level only, with a 64 KB buffer. On `Error` (buffer overflow) it does a full top-level rescan rather than trusting the event stream.
- **Built-in exclusions:** files that are hidden or system; dot files; `desktop.ini`, `thumbs.db`, `~$*`; partial downloads (`.part .partial .crdownload .download .tmp .opdownload .!ut`). Also cloud placeholders (`FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS` or `OFFLINE`), so a OneDrive folder is never hydrated by accident.
- **Readiness:** a file is ready when all four hold:
  1. it is not 0 KB;
  2. its size and last-write time are unchanged across two samples 2 s apart;
  3. its last write is older than the folder's grace period (default 30 s for Silent, a draft value);
  4. it opens with `FileShare.None`.
  
  A file that isn't ready is retried with backoff (2 s, 5 s, 15 s, 60 s, then every 5 min), for at most 24 h. After that it shows as "Waiting" in Pending rather than retrying forever. A 0 KB file waits the same way, because browsers create the placeholder before writing to it.
- **Lock probe honesty:** a failed open can also mean access denied. That's logged as "unavailable" and treated the same as locked: retry, and never fail the run.
- **Startup and backlog:** each folder stores a `baselineUtc`, set when the folder is added or switched to Silent.
  - Files present before the baseline are never moved silently. They appear under Pending, and "Organize" offers them through preview.
  - Files that arrived after the baseline while Chippi was off are handled normally once they're ready.
  - This replaces Mouzi's name-list baseline, which goes stale when a name is reused.
  - A newly added folder starts in Manual (Travis's call), so its first contact is always a preview.
- **Executor:** it re-validates immediately before acting: the source still exists with the same size and modified time, and the destination is still free. Then:
  1. It creates the destination folders.
  2. On the same volume it does `File.Move` without overwrite. Across volumes it copies to `dest.chippi-partial`, verifies size and SHA-256, renames into place, then deletes the source.
  3. A destination conflict at apply time takes the next free numbered name in Silent mode, and fails that item in a preview run so the user re-plans. Approval never silently authorizes a different destination.
- **Concurrency:** one executor per module, serialized. Watching and planning stay off the UI thread; UI updates go through `BeginInvoke`.
- **Restored files:** a file put back by Undo is remembered by path and fingerprint, and isn't organized again until its content changes.

## 3. History and undo

`IHistoryStore` is the only seam, and `SqliteHistoryStore` is the only class that references `Microsoft.Data.Sqlite`. A JSON store would be one more class behind the same interface. Nothing above it changes.

```
interface IHistoryStore
  RunId          BeginRun(RunTrigger trigger, string folder)          // Silent, Organize (preview), Explorer, ArchiveImport, Undo
  OperationId    Plan(RunId, PlannedRecord)                            // state = Planned
  void           MarkStarted(OperationId) / MarkCompleted(OperationId, Fingerprint) / MarkFailed(OperationId, string error)
  void           EndRun(RunId)
  IReadOnlyList<OperationRecord> Query(HistoryQuery q)                 // folder, text, date range, state, paging
  IReadOnlyList<OperationRecord> Incomplete()                          // Started but not finished: for startup reconciliation
  void           ClearBefore(DateTimeOffset)                           // never touches files
```

Schema (SQLite, WAL mode, `PRAGMA user_version` for migrations):
- `runs(id, trigger, folder, started_at, ended_at)`
- `operations(id, run_id, kind, source_path, dest_path, rule_id, rule_name, bytes, sha256, state, error, planned_at, completed_at, undo_of)`. `state` is one of planned, started, completed, failed or undone.

**Crash consistency:** the history row moves to `started` before the file system changes and to `completed` after. At startup, every `started` row is reconciled by checking where the file actually is: at the destination means completed, at the source means failed, in neither place means failed with a note. A `.chippi-partial` left from a cross-volume copy is deleted only if its fingerprint doesn't match a completed row.

**Verified undo** is a new checked operation, not a reverse move. It requires:
- the organized file exists with its recorded SHA-256;
- the original path is free;
- the original folder still exists (it's re-created if the watched folder still exists).

It moves without overwrite, records an `Undo` run whose operation links via `undo_of`, and marks the original row `undone`. Undo can be done per row or per run, after review.

**Location:** `ModuleContext.DataDirectory/history.db`, normally `%LOCALAPPDATA%\Chippi\data\downloads\history.db`. Since the `ChippiApp` change, this is outside Velopack's install folder, so updates don't touch it. A dev profile gets its own copy.

## 4. Core changes (Kayla owns; land first)

1. **`ModuleContext.DataDirectory`:** a per-module folder for databases and caches, created on demand and kept after uninstall, like settings. The registry gets the data root from `ShellContext`, so dev profiles stay isolated.
2. **Notifications contract:** WPF-free, in Core.
   - `INotifier.Show(Notice)`, where `Notice { Kind, Title, Body, Actions[] }` and an action is a label plus an activation string.
   - The module gets it as `ModuleContext.Notifier`.
   - Each module declares its notice kinds. Settings shows one switch per kind (Travis's "configurable for all three").
   - A disabled or suppressed notice is a no-op and never fails the caller.
   - The backend lives in the App (§6, workstream D).
3. **Activation with arguments:** today `SingleInstance` signals with no payload. The Explorer verb (`--organize "<folder>"`) and toast clicks both need to deliver arguments.
   - Change it to a named pipe `\\.\pipe\Chippi-<name>` that carries the second launch's argument list. Keep the existing foreground handoff.
   - Add `ModuleContext.Activation.Register(verb, handler)`. The shell routes `--<verb>` arguments to the module that registered the verb. No arguments still means show the flyout.
   - Unknown verbs and stale IDs are logged and ignored.
4. **Settings deep link:** `SettingsPageContribution.CreateView(string? section)`, plus `ShellCommands.OpenSettings("module:<id>/<section>")`. A toast click or the flyout's "Rules" button can then open the Downloads page on History or Rules.
5. **Capability:** add `Capability.ShellIntegration` ("Explorer menu entry"), so the Permissions page lists it and flags it unused after uninstall.
6. **No change needed:** `IslandActivityKind.Stash` already fits. Title is the file name, Source is the destination folder's name, and a held file uses Source "Held · <reason>". `FlyoutTab.Tidy` and module settings already exist.

All six are additive, so the Phase 1 modules keep compiling.

## 5. UI pieces

| Piece | DESIGN.md today | Missing (needs design and Travis's approval before building) |
|---|---|---|
| Tidy tab | §5: folder card with mode segment and mode line, RECENTLY STASHED rows with undo, Organize now (primary), Rules (secondary) | More than one watched folder (a folder picker in the card, or one card per folder?); the Pending count; empty state when no folder is added |
| Stash row | §6: name over destination, mono age, undo; held = amber 45% border plus reason | "Waiting" rows (not ready yet); a failed row |
| Island | §5: Stash activity, JUST STASHED tile | Nothing new |
| Preview and approve | Not designed | List of planned operations with checkboxes, rule used, destination, warnings; held items unselected; Approve (primary) and Cancel; per-item failure line after applying |
| Rules editor | Not designed | Ordered list with drag reorder; per-rule editor for conditions, action and template; live validation; a "test a file name" box showing the planned result |
| History | Not designed | Searchable list grouped by run; Undo per row and per run, with a confirm step; Clear history |
| Settings page (module) | Not designed | Watched folders (add, remove, mode, grace), ignore file shortcut, the three notice switches, the Explorer "Organize" switch, Import archive… |
| Toasts | Not designed | Copy only: short, state first ("4 files organized", "1 file held") |

Controls likely needed in Chippi.UI: a checkbox, which DESIGN.md bans in stock form, so it needs a themed one; a drag handle (Hopper's `GripBrush` exists); a text field style for templates, based on `SearchBox` without the icon. Those three would be WS0-style additions with gallery states.

## 6. Proposed workstreams

| # | Workstream | Owns | Depends on |
|---|---|---|---|
| P2-0 | Core contracts (§4), Chippi.UI additions (checkbox, text field, drag handle) | `src/Chippi.Core/**`, `src/Chippi.UI/**` | none. **Lands first.** |
| P2-A | Rules engine | `src/Chippi.Rules/**`, `tests/Chippi.Rules.Tests/**` | none (pure; can start with P2-0) |
| P2-B | Pipeline, executor, history, undo, archive import | `src/Features/Chippi.Features.Downloads/{Engine,Storage,Import}/**`, `tests/Chippi.Features.Downloads.Tests/**` | P2-0, P2-A's planner interface |
| P2-C | Tidy tab, preview, rules editor, history, settings page | `src/Features/Chippi.Features.Downloads/Views/**` | P2-0, P2-B's view models, **DESIGN additions approved** |
| P2-D | Shell integration: toast backend, Explorer "Organize" verb, activation routing, packaging | `src/Chippi.App/Notifications/**`, `src/Chippi.App/Shell/Activation*`, `build/**` | P2-0 contract; starts with a spike (below) |
| P2-E | Automated UI smoke checks: Phase 1's UIA scripts turned into a test project, plus Tidy flows | `tests/Chippi.UiSmoke/**` | runs after P2-C |
| Lead | DESIGN additions and review page, composition slots, integration | `App.xaml*`, `DESIGN.md`, docs | all |

**P2-D spike first, before committing to a notification route.** Build both candidate routes on win-x64 and win-arm64, install with Velopack on a clean VM, show a toast, click it cold (Chippi not running) and warm, then uninstall and confirm no registration is left. Report package size per route.
- **Route 1:** Windows App SDK `AppNotificationManager`. Microsoft recommends it, but it needs the Windows App Runtime deployed or bundled.
- **Route 2:** the classic COM activator using the AUMID Velopack already sets (`velopack.ChippiApp`). That's a smaller dependency, but more hand-written registration.

**Explorer verb:** `HKCU\Software\Classes\Directory\shell\ChippiOrganize` and `…\Directory\Background\shell\ChippiOrganize`.
- `MUIVerb` is `Organize`, the icon is `Chippi.exe,0`, and the command is `"<exe>" --organize "%V"`.
- It's registered while the Downloads module is installed and the switch is on.
- It's removed on module uninstall and in Velopack's uninstall hook, next to the Run-entry cleanup.
- The command points at the stable `ChippiApp\current\Chippi.exe` path.
- Classic menu only, as Travis decided.

## 7. Risks

- **FileSystemWatcher drops events** under load or on network shares. The overflow rescan covers it, and network folders get an "events may be missed" note plus a rescan every 5 min.
- **Cross-volume moves are not atomic.** Copy, verify, then delete, with reconciliation at startup (§3).
- **Long paths:** the app manifest already has `longPathAware`. Tests should cover paths over 260 characters.
- **Antivirus or indexer locks** right after a download completes. That's the reason for backoff rather than failing.
- **Native SQLite in a single-file publish on ARM64:** it builds, but can't be run here (as in Phase 1).
- **Toast deployment** might add tens of MB (Route 1). The spike decides.
- **Classic-menu-only Explorer entry** sits behind "Show more options" on Windows 11. Travis accepted this.

## 8. Questions for the brief

1. Settled: two places (Windows notifications, island), configurable per event; the Tidy tab always lists everything.
2. The Silent grace-period default (30 s draft) and the 24 h retry ceiling.
3. Archive import: always go through preview? (Draft: yes, and imports never auto-apply.) Also its limits: 10,000 members, 4 GB expanded, ratio check 100:1.
4. Multiple watched folders in the Tidy card: picker or stacked cards (design).
5. History retention: keep forever, or default to 90 days with "Clear history"?
