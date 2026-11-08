# Cleaner: brief

Wave 3. For Travis to confirm. Decisions: [README](README.md) (scope 478, shortcuts 471 and 482, admin helper 483, deleting 485, where things live 486). Research: [behavior-spec-b.md](behavior-spec-b.md) section 3, [architecture.md](architecture.md) sections 6, 7 and 11, [surfaces.md](surfaces.md) sections 6 and 7. The helper: `admin-helper.md` (Kayla). Mockup: later; Lumen draws it before this is built. DESIGN.md wins where it speaks.

## Job

Show how much space temp files and caches take, and clear them in one step, without ever touching anything that matters.

## Doors

| Door | What it does |
|---|---|
| **Home tile** | None |
| **Actions › Apps › Clean junk files** | The categories, measured on open, with sizes and the last ticks remembered. "Check first" and "Needs admin" tags. Clean in the header asks first |
| **Shortcuts** | Scan for junk files, Clean junk files |
| **Search** | Each verb as Run |
| **Radial menu** | Any verb |
| **Bar** | "Cleaned 1.0 GB" for 6 seconds |
| **Settings** | Apps › Cleaner: categories with descriptions and what each never touches |

## Behavior

### Measure first

Opening the list measures the categories (read-only, off the UI thread) and fills in sizes as each finishes. Nothing is deleted by measuring. A category that needs admin is measured only when a helper-free measure is possible; otherwise it shows "Needs admin" and no size until Travis cleans: Clean with one ticked brings up the UAC prompt first, the helper measures it (read-only), then the question shows the full size, and Cancel deletes nothing (admin-helper.md, Clean, in order).

### Categories in the first build

From spec B, trimmed by README task 478 (emptying the Recycle Bin and large-folder finding are left out):

| Category | Where | Ticked by default | Admin |
|---|---|---|---|
| User temp files | `%TEMP%`: files not touched for over a day | Yes | No |
| Thumbnail and icon caches | Explorer's cache folder | Yes | No |
| App caches | A short named list: Chromium and Firefox browser caches (never cookies, passwords, history, autofill), Discord, Slack, Teams | No, "Check first" | No |
| Windows temp | `C:\Windows\Temp` | No | Yes |
| Windows Update download cache | `SoftwareDistribution\Download` | No | Yes |
| Delivery Optimization cache | Its cache folder | No | Yes |
| Crash dumps and logs | User dump folders; system dump folders | No | Some |

Travis's ticks are remembered (Disk Cleanup's saved selection).

### Never listed, never touched

Cookies, saved passwords, history, autofill, the registry, System Restore points, the Recycle Bin, and anything in Documents, Desktop, Pictures or Downloads. A category's folders are fixed in code, not read from settings or a request. A reparse point inside a category is removed as a link and never followed.

### Verbs

| Verb | Behavior | Admin | Ask first |
|---|---|---|---|
| Scan for junk files | Measures the ticked categories | Some | No |
| Clean junk files | Cleans the ticked categories | Some | "Clean 1.0 GB for good?" with Cancel and Clean. The detail says it can't be undone |

- **For good.** Caches and temp files are deleted, not recycled: the Recycle Bin frees no space until it's emptied (README task 485).
- **Files in use** are skipped and counted, never forced: "Cleaned 1.0 GB · 12 files in use".
- **A running app's cache** (a browser) shows "Close Chrome to clean this" and is skipped.
- **Freed size is the real number.** Files change between Measure and Clean; the result reports what was actually freed.
- **Admin categories** go to the helper as `CleanCategory(name)`: a fixed name mapped to fixed folders inside the helper. One prompt for all the admin categories ticked, and none unless one is ticked.

## Edge cases

- **Nothing to clean:** "Nothing to clean" with Scan again.
- **Measure takes long** (a cache of hundreds of thousands of files): sizes appear per category as they finish; the page stays responsive; Clean waits for its categories to be measured.
- **A folder missing or access denied:** that category reads "Not available" and is skipped, never an error for the whole page.
- **UAC declined:** the admin categories are skipped and the result says "Needs admin"; the standard categories still run.
- **Closing Chippi mid-clean:** "Stop cleaning and quit?"; files already deleted stay deleted.
- **Disk almost full:** no special handling; nothing is written except the result line.

## Shortcuts

| Verb | Slot | Notes |
|---|---|---|
| Scan for junk files | Global | Opens Actions › Apps › Clean junk files after measuring |
| Clean junk files | Global | Opens the confirm for the remembered ticks; never cleans silently |

Unassigned. Settings › Cleaner shows them read-only with Edit.

## What we took

- **BleachBit:** measure first as its own step; named categories with a plain description. Left out: shredding, free-space wipe, anything that reaches saved passwords.
- **Storage Sense:** conservative defaults; Downloads and the Recycle Bin untouched.
- **Disk Cleanup:** the remembered selection.
- **CCleaner (a caution):** no registry cleaner, no restore-point deletion, no startup behavior.
- **WizTree:** nothing in this build; a biggest-folders view is left for later.

## Copy

| Where | Text |
|---|---|
| Page and menu | Cleaner; Actions row: Clean junk files |
| Rows | User temp files, Thumbnail and icon caches, App caches, Windows temp, Windows Update cache, Delivery Optimization cache, Crash dumps and logs |
| Tags | Safe, Check first, Needs admin |
| Header | 1.0 GB found; Clean, Scan again |
| Confirm | Clean 1.0 GB for good? (Cancel and Clean) |
| Bar | Cleaned 1.0 GB |
| Result | Cleaned 1.0 GB · 12 files in use |
| States | Close Chrome to clean this; Nothing to clean; Not available |
| Errors | Clean failed; Needs admin |
| Settings | What each category never touches (one line each) |
| Shortcut rows and Search | Scan for junk files, Clean junk files |

## Shared code

- **New:** `Chippi.Features.Cleaner` and `.Tests`, in `Chippi.slnx` (names to Marge first). Module id `cleaner`, already in the catalog **without** `Capability.Admin`; the PR adds it and the "Admin for some categories" badge, as App updates has. Shared Core code, called out in the PR.
- **The helper:** `MeasureCategory(name)` and `CleanCategory(name)` are `admin-helper.md`'s verbs; its category list matches this table (`WindowsTemp`, `WindowsUpdateDownloads`, `DeliveryOptimization`, `SystemCrashDumps`).
- **Shared with the other Apps briefs:** the Apps menu in Actions.
- **Settings pages:** Lumen's. Verbs through `IModuleUi.AddCommands`; slots through `ModuleContext.Shortcuts`.
- **Not changed:** the shortcut service.

## Tests

- **CI, unit,** on a fixture root that stands in for each category's folder: the age rule, in-use skipping (a file held open), junctions removed as links with the target untouched, cookies and passwords folders never matched, sizes summed, remembered ticks, the confirm size, freed-size reporting after a file changes between Measure and Clean, and an unreadable folder skipping only its category. A dev run points categories at the fixture root, never at the real `%TEMP%`.
- **Offscreen shots:** the list measuring, measured, with tags, the confirm, the result, empty; dark and light.
- **Sandbox, once at the end:** the helper cleans a fixture folder standing in for each admin category (the helper's own test hook for a root; never the real Windows folders on Travis's PC). No UAC prompt in tests or dev runs.

## Done means, and timings to set

- **Page open:** under 100 ms with the last sizes shown first (marked "from earlier"), fresh sizes filling in behind. Target, not measured.
- **Measure** of a typical profile: reported, never blocking the UI.
- **Clean of 1 GB** across 10,000 fixture files: timed and reported.
- Screenshots of the list, the confirm and the result next to the mockup. The Sandbox run passes.

## Not verified

- The exact folders for Delivery Optimization's cache and the system dump locations on Windows 11 aren't confirmed from Microsoft pages (Storage Sense's and Disk Cleanup's lists were unreadable). Kayla or the build confirms them before the category ships; a category whose folder can't be confirmed is left out.
- App cache folder names for Discord, Slack and Teams are from memory, not read.
- Elevation behavior of Disk Cleanup, for comparison only.
- Timings are guesses until measured.
