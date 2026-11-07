# App updates: brief

Wave 3. For Travis to confirm. Decisions: [README](README.md) (scope task 478, shortcuts 471 and 482, admin helper 483, libraries 484, where things live 486, defaults 487). Research: [behavior-spec-b.md](behavior-spec-b.md) section 1, [architecture.md](architecture.md) sections 6, 7 and 11, [surfaces.md](surfaces.md) sections 3, 6 and 7. The helper: `admin-helper.md` (Kayla). Mockup: later; Lumen draws it before this is built. DESIGN.md wins where it speaks.

## Job

See which installed apps have an update, and update one or all of them without opening a terminal.

## Doors

From [surfaces.md](surfaces.md) section 3, every one:

| Door | What it does |
|---|---|
| **Home tile** | None |
| **Actions › Apps › Update apps** | A list like Quit apps: each app's real icon, versions and Update, Update all in the header. "Everything is up to date" with Check again when there is nothing |
| **Shortcuts** | Check app updates, Update all apps, plus the per-row slots below |
| **Search** | Check app updates, Update all apps as Run; "update firefox" finds the app and Run asks first |
| **Radial menu** | Any verb |
| **Bar** | "2 of 5 updated" with a progress ring while a batch runs |
| **Settings** | Apps › App updates |

## Behavior

### The list

winget's own upgrade list, read through the COM API: the app's icon (`AppIcon.View`), name, installed version, available version and source. A header line says how many updates there are and when Chippi last checked. The page checks when it opens (read-only; no prompt, no install) and again on Check. Apps whose installed version winget can't read stay out unless "Show apps with an unknown version" is on; Update on one of those says its version couldn't be checked (matches `--include-unknown`).

### Verbs

| Verb | Behavior | Admin | Ask first |
|---|---|---|---|
| Check app updates | Reads the list. Never installs | No | No |
| Update | One app, silent, agreements accepted | Some installers ask for it themselves | "Update Firefox?" with Cancel and Update |
| Update all apps | Every non-skipped app, one after another, progress per row | One helper prompt for the machine-wide ones (below) | "Update 5 apps?" listing them, with Cancel and Update |
| Skip | Hides this version, or the app for good ("Always skip") | No | No. Reversible from Settings › Skipped with Restore |
| Close and update | If the app is running, asks its window to close, then updates | As Update | "Close Firefox and update?" with Cancel and Update |

A running app is never ended by force. If it doesn't close, the row says "Close Firefox and try again". A reboot an installer asks for shows "Restart to finish" on the row; Chippi never restarts the PC.

### Who installs

- **Per-user and Store apps:** Chippi (standard user) calls winget's COM API (`Microsoft.WindowsPackageManager.ComInterop`, README task 484) with the package, scope and silent options the list gave.
- **Machine-wide apps:** a batch of these goes to the admin helper as `UpdatePackage(id)` once each, under one UAC prompt. The helper runs `winget upgrade --id <id> --exact --silent` itself (architecture section 6); it doesn't use COM. A single machine-wide update outside a batch also goes through the helper, so the prompt names Chippi, not each installer.
- How Chippi knows an update is machine-wide before it starts: the package's installed scope from winget. **Not verified**, see below. If the scope can't be told, Chippi treats it as machine-wide and uses the helper.
- **No prompt in a check.** A UAC prompt appears only right after Travis confirms an Update.

### Failure

"Update failed" on the row with the app and winget's reason; the list is unchanged. A failure in a batch doesn't stop the rest; the bar ends with "4 of 5 updated" and the page lists the one that failed.

### History

The last 50 runs (app, versions, date, result) in Chippi's own data folder: no commands, paths or installer output. Shown in Settings.

### Left out (README task 478)

Schedules, backup and restore, version pins, installing new apps, other package managers. Pinned apps are shown only so they're skippable; Chippi doesn't set pins.

## Edge cases

- **winget missing or its source can't be reached:** the page says "App updates need winget" or "Check failed", with Check again. Nothing else is touched.
- **Two updates for one app, or a list that changes while Update all runs:** the run works from the list that Travis confirmed. A row that no longer needs an update is skipped, not failed.
- **Chippi's own update** isn't in the list: Chippi has its own installer path.
- **UAC declined:** "Needs admin"; nothing starts, the list is unchanged.
- **Closing Chippi mid-batch:** the quit confirm names it ("Stop updating and quit?"). Installers already started finish on their own; Chippi doesn't kill them.

## Shortcuts

Slots under the starter and per-row rule (README, task 482). All unassigned.

| Verb | Slot | Notes |
|---|---|---|
| Check app updates | Global | Opens Actions › Apps › Update apps after the read |
| Update all apps | Global | Opens the same confirm as the button |
| Update app, Skip app update, Close and update app | In context | Act on the selected row in the list; no global slot (acts on one row) |

Settings › App updates shows the global slots read-only with Edit, which opens Settings › Shortcuts in that row.

## What we took

- **UniGetUI:** list, select, update, a per-app ignore and a history. Left out: several package managers, schedules.
- **winget upgrade:** the list is its list; unknown versions behind a switch.
- **Patch My PC:** close the app first, skip per app.

## Copy

| Where | Text |
|---|---|
| Actions menu | Update apps |
| Header | 5 updates · Checked 2 min ago; Update all, Check again |
| Row | Update, Skip; Close Firefox and try again; Restart to finish |
| Empty | Everything is up to date |
| Confirms | Update Firefox? / Update 5 apps? / Close Firefox and update? (Cancel and Update) |
| Bar | 2 of 5 updated |
| Errors | Update failed; Check failed; Needs admin; App updates need winget |
| Settings | Skipped (Restore), History, Show apps with an unknown version |
| Shortcut rows and Search | Check app updates, Update all apps |

## Shared code

- **New:** `Chippi.Features.AppUpdates` and `.Tests`, both in `Chippi.slnx` (names to Marge first). Module id `app-updates`, already in the catalog with `Capability.Admin`.
- **New package:** `Microsoft.WindowsPackageManager.ComInterop` 1.29.380 (MIT), in the feature project only.
- **Core and the helper:** `UpdatePackage(id)` is `admin-helper.md`'s verb; this brief only calls it through the helper client. If the helper's API differs, this brief follows it.
- **Island:** the Apps menu is shared with Cleaner and Uninstaller (one change, whoever lands first). The Settings pages are Lumen's; this feature adds its verbs through `IModuleUi.AddCommands` and its slots through `ModuleContext.Shortcuts` with `ShortcutEdits`.

## Tests

- **CI, unit.** The list model from a fake `IPackageSource` (updates, none, unknown versions on and off, skipped, source error); skip and restore; the confirm text for one, several and a running app; batch order and "keep going after one fails"; history trimmed to 50; no command line or installer output in the log.
- **Offscreen shots** (`CHIPPI_ISLAND_SHOTS`): the Actions list, the empty state, a running batch, the Settings page; dark and light.
- **Real winget, read-only,** on Travis's PC: Check runs the real list. Nothing installs.
- **Installing runs only** in the Sandbox against a throwaway package, or on throwaway installs in Travis's live check. The helper's batch runs once with the wave's Sandbox check; no UAC prompt in tests or dev runs.

## Done means, and timings to set

- **Page open with a cached list:** under 100 ms, already filled; a fresh check fills in behind it and says so. Target, not measured.
- **Check:** the time winget takes, shown; the UI never waits on it.
- Screenshots of the list, a running batch and a failure, next to the mockup. The Sandbox batch passes.

## Not verified

- Whether the COM API reports an installed package's scope reliably enough to pick the helper. If it doesn't, every machine-wide-looking update uses the helper.
- How `msstore` packages behave in the list and in updates.
- Whether COM activation from Chippi works on ARM64 with no extra setup (the server is App Installer's own).
- Timings are guesses until measured.
