# Uninstaller: brief

Wave 3. For Travis to confirm. Decisions: [README](README.md) (scope 478, shortcuts 471 and 482, admin helper 483, deleting 485, where things live 486). Research: [behavior-spec-b.md](behavior-spec-b.md) section 2, [architecture.md](architecture.md) sections 6, 7 and 11, [surfaces.md](surfaces.md) sections 6 and 7. The helper: `admin-helper.md` (Kayla). Mockup: later; Lumen draws it before this is built. DESIGN.md wins where it speaks.

## Job

Remove an app the way its own uninstaller does, then find what it left behind and remove only what is provably its own.

## Doors

| Door | What it does |
|---|---|
| **Home tile** | None |
| **Actions › Apps › Uninstall** | Opens Settings › Apps › Uninstaller with its search focused (the open glyph, like Arrangements' Manage) |
| **Shortcuts** | Uninstall app, Scan for leftovers, plus the in-context slots below |
| **Search** | "uninstall notepad" finds the app; Run asks first. Uninstall app opens Settings with search focused |
| **Radial menu** | Any verb |
| **Bar** | "Uninstalling Notepad++" while it runs |
| **Settings** | Apps › Uninstaller: the list and the leftover review live here (the island can't scroll hundreds of apps) |

## Behavior

### The list

Installed apps from three places, read as a standard user (architecture section 7): the `Uninstall` keys under `HKLM` (both views) and `HKCU`, skipping `SystemComponent` entries and updates (`ParentKeyName`); Store and MSIX apps through `PackageManager.FindPackagesForUser`; and winget's list, which maps many entries to package ids. Each row: real icon (`DisplayIcon`, through `AppIcon.View`), name, publisher, size, install date. Search is focused on open and filters as you type; sort by any column; a filter for Apps, Store apps, and Windows components (shown only if asked). Apps installed in the last 7 days are marked, as Geek Uninstaller does. Windows components, Chippi itself and winget framework packages never get a verb.

### Verbs

| Verb | Behavior | Admin | Ask first |
|---|---|---|---|
| Uninstall | Runs the app's own uninstaller: its quiet string when it has one, else its normal wizard, which Chippi never clicks through. Then offers Scan for leftovers | Often | "Uninstall Notepad++?" with Cancel and Uninstall |
| Scan for leftovers | Read-only list, below | No | No |
| Remove leftovers | Removes what's ticked, after a preview that lists every item | Some paths | "Remove 14 items?" with Cancel and Remove |
| Uninstall selected apps | A queue: one confirm listing the apps, then one after another with progress | Often | "Uninstall 3 apps?" listing them |
| Open app install folder | Reveals the folder in File Explorer | No | No |

### Who runs the uninstaller

- **An `HKLM` app:** through the admin helper as `Uninstall(registryKey)`: the helper reads the command from that key itself, because only an admin can write `HKLM`. A queue is one prompt.
- **An `HKCU` app:** always run by Chippi as a standard user, never elevated, since any program running as Travis can write an `HKCU` entry (architecture section 6).
- **A Store app:** `RemovePackageAsync` for the current user, no admin.
- The wizard of a non-quiet uninstaller shows on screen and Travis finishes it. Chippi notices the entry disappear or the process end, and goes to the leftover offer.

### Leftovers

Looked for after the app's uninstaller finishes: the former install folder, the app's folders in `%APPDATA%`, `%LOCALAPPDATA%` and ProgramData, its Start menu shortcuts, and its keys under `HKCU\Software` and `HKLM\Software`. Chippi's own rule (README task 485, spec B "Leftover ownership"), tested on fixtures:

- **Safe (ticked):** only with verified ownership: inside the app's recorded `InstallLocation`, or named by the app's own uninstall data (its product code or its own key), or listed by its package manifest.
- **Check first (unticked, with the reason):** matches by name only: a publisher folder, an AppData folder named like the app.
- **Shared (never offered):** another installed app's `InstallLocation`, install folder or uninstall key is, contains or sits inside it ("Shared with Thunderbird"). Nothing under `Windows` or `System32`, no shared Microsoft key, and no other app's folder is ever listed.

The Uninstall entry is read **before** the uninstaller runs and kept for the scan, since uninstallers usually delete it. With no such record, the scan lists only Check first items and the helper deletes nothing elevated.

### Removing

- **Files and folders** go to the Recycle Bin (`IFileOperation`, recycle on delete). A file that can't be recycled (too big, a drive without a bin) is named in the confirm as permanent.
- **Registry keys** are exported with `reg.exe export` to a `.reg` file in Chippi's data folder first; the confirm says where it is. Travis can merge it back.
- **Under Program Files, ProgramData and `HKLM`,** the items go to the helper as `DeleteLeftovers(app, items)`. The helper works the ownership rule out again itself and refuses anything that fails it (architecture section 6); Chippi's labels aren't trusted. Items the helper refuses because Travis could delete them himself return to Chippi's standard-user delete.
- **Simulate:** the preview is the whole story. Every item with its path, kind and reason is listed before Remove; nothing is removed until Travis presses Remove.

### Left out (README task 478)

System restore points; "Remove broken entry" for an app with no working uninstaller (the entry stays and the row says "Uninstaller not found"); forced uninstall; portable-app discovery.

## Edge cases

- **Uninstaller exits non-zero or is cancelled by Travis:** the app may still be installed. Chippi re-reads the entry; if it's there, the row says "Uninstall failed" and no leftover scan is offered.
- **The app is running:** the row says "Close Notepad++ first". Chippi doesn't end it.
- **A pending reboot** an uninstaller asks for: "Restart to finish"; the leftover scan waits, since files may still be in use.
- **A leftover file in use:** skipped and listed as "In use". Never forced.
- **A junction or symlink in a leftover folder:** removed as a link, its target untouched (the helper's rules apply the same on the standard path).
- **UAC declined:** "Needs admin"; nothing starts.
- **Closing Chippi mid-queue:** "Stop uninstalling and quit?".

## Shortcuts

| Verb | Slot | Notes |
|---|---|---|
| Uninstall app | Global | Opens the target picker (a search list), then the same confirm as the button. It never acts on its own |
| Scan for leftovers | Global | A picker of recently uninstalled apps whose record Chippi still holds |
| Uninstall selected apps, Remove leftovers, Open app install folder | In context | Act on the ticked or selected rows |

All unassigned. Settings › Uninstaller shows the global slots read-only with Edit.

## What we took

- **Revo:** uninstall first, then scan; Recycle Bin for files and a backup for registry keys; a queue with one prompt. Left out: restore points, forced uninstall.
- **BCUninstaller:** badges so components and updates aren't offered as apps; the filter; Simulate (preview that removes nothing).
- **Geek Uninstaller:** type to search on open; recent installs marked; the leftover scan as a step of Uninstall, not a separate tool.
- **winget uninstall:** the engine for packages winget lists.

## Copy

| Where | Text |
|---|---|
| Page and menu | Uninstaller; Actions row: Uninstall |
| Columns and filter | Name, Publisher, Size, Installed; Apps, Store apps, Windows components |
| Row | Uninstall, Open folder; Uninstaller not found; Close Notepad++ first |
| Confirms | Uninstall Notepad++? / Uninstall 3 apps? / Remove 14 items? (Cancel and the verb) |
| Leftover tags | Safe, Check first, Shared with Thunderbird, In use |
| Remove confirm detail | "7 files go to the Recycle Bin. 2 keys are saved to <path> first. 1 file is removed for good." |
| Bar | Uninstalling Notepad++ |
| Errors | Uninstall failed; Needs admin; Remove failed |
| Shortcut rows and Search | Uninstall app, Scan for leftovers |

## Shared code

- **New:** `Chippi.Features.Uninstaller` and `.Tests`, in `Chippi.slnx` (names to Marge first). Module id `uninstaller`, in the catalog with `Capability.Admin`.
- **The helper:** `Uninstall` and `DeleteLeftovers` are `admin-helper.md`'s verbs. This feature supplies the app record and the item classes; the helper's checks are its own.
- **Shared with Cleaner and Port manager:** the Apps menu in Actions; a Recycle Bin delete wrapper (`IFileOperation` with recycle) if one doesn't exist in Core yet. Called out in the PR.
- **Settings pages:** Lumen's. Verbs through `IModuleUi.AddCommands`; slots through `ModuleContext.Shortcuts`.
- **Not changed:** the shortcut service.

## Tests

- **CI, unit:** the app list from a fake registry and package source (HKLM both views, HKCU, SystemComponent, updates, Store, duplicates); the three leftover classes against fixture trees, including the Firefox and Thunderbird case (shared `Mozilla` folder is Shared, never Safe); registry export written before any key goes; the confirm text with and without non-recyclable files; the queue order; the helper client sends only the closed verbs; the log never holds a path or key name beyond the app's name.
- **Offscreen shots:** the list, search, the leftover review with all three classes, the confirm with its detail line, empty and error states; dark and light.
- **Sandbox, once at the end,** never Travis's real apps: a stand-in installer registers a fake `HKLM` app and an `HKCU` app; Chippi uninstalls each through the helper and as standard user; the leftover scan and removal run against fixture folders including a shared folder, a junction and an in-use file. No UAC prompt in tests or dev runs on Travis's PC.
- **Travis's live check,** if any: a throwaway install only.

## Done means, and timings to set

- **Page open:** under 150 ms, already filled from a cached list; a fresh read behind it. Target, not measured.
- **Search keystroke to filtered list** over 300 apps: under one frame of work.
- **Leftover scan** of a typical app: under 3 s, off the UI thread.
- Screenshots of the list, the review and a removal, next to the mockup. The Sandbox run passes.

## Not verified

- Which uninstallers accept a quiet switch is by convention (`QuietUninstallString`, MSI), not documented by Microsoft.
- Revo's and BCU's scan rules weren't readable; the ownership rule is Chippi's own and rests on the fixtures.
- The Sandbox account approving its own UAC prompt (the helper's first run checks it).
- Timings are guesses until measured.
