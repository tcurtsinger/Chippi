# Phase 7 behavior spec, part B: Apps and System (Herb, 2026-11-06)

App updates, Uninstaller, Cleaner, Port manager, Displays and brightness.

Research only, no code, and nothing was installed or run. Sources are public pages read on 2026-11-06 (keys like [W1] point to the Sources list at the end). Three marks are used throughout:

- **Fact:** read on a cited page.
- **Inference:** my reasoning from the facts, or from general Windows knowledge that I did not re-read on a page. It needs checking before a brief relies on it.
- **Unconfirmed:** a page I tried failed (several Microsoft winget pages returned 404, and CCleaner's returned 403), or the page doesn't say.

Where a "Best of the field" row says "not stated", the tool may well do it; the public page just doesn't say. Travis's direction (2026-11-06) applies: a shortcut slot for every verb, set only in Settings › Shortcuts, behavior studied but never code or assets, and the island's rules (520 × 360, nothing scrolls, overflow goes to Settings). The doors map is Lumen's (`surfaces.md`); this file only proposes verbs and slots.

## Shared rules (all five)

- **Verbs are shared actions.** Each verb below is one `ChippiAction`, so any door can fire it: an Actions tile, Search, a Radial slice, or a shortcut. Names are bare verbs ("Update all", "Free port"), which is the Copy rule.
- **Shortcut slots are all unassigned by default.** Each feature registers its slots with `ModuleContext.Shortcuts` when it starts, set or not. They show in Settings › Shortcuts, and on the feature's page read-only with Edit. The exception is Brightness up and down, which are the only verbs here you'd press often. They still ship unassigned, because no default key is safe from other apps (an app held Ctrl+Alt+Space on 2026-11-05).
- **Every destructive verb asks first**, as a question with Cancel and the verb ("Uninstall Notepad++?"). A verb fired from a shortcut or the Radial menu opens the same confirm; none runs destructive work silently. Which verbs may skip the confirm is Travis's call (Step 2, decision 7).
- **Prefer the Recycle Bin.** Wherever Chippi deletes files itself, it sends them to the Recycle Bin when the file can go there, and says in the confirm when it can't.
- **Nothing here is installed to study.** No competitor was run.
- **Admin.** Several verbs need elevation. This spec says which; how the admin helper works is Kayla's (`architecture.md`). A verb that needs admin and can't get it says "Needs admin" and does nothing, it doesn't fail halfway.

---

## 1. App updates

### Best of the field

| Tool | What it does best | How it works (steps) | What Chippi takes |
|---|---|---|---|
| UniGetUI [U1] | One window over several package managers: bulk update of the selected packages; skip a version or ignore a package entirely; per-package install options remembered for future updates; update checks on an interval or daily or weekly; a tray icon showing what is available; 1,000 recent operations kept in a history with up to 5,000 log lines each. Fact. | Open, see the list of available updates, tick packages, click update; options are saved per package. List columns, pin, run as admin and release notes: unconfirmed. | The shape: list, select, update, with a per-package "ignore" and a history. Not the several managers: winget only (the catalog says winget). |
| `winget upgrade` [W1] | The ground truth. With no arguments it lists upgrades; `--all` upgrades them and the page says to run it without arguments first to preview. `--include-unknown` upgrades packages whose installed version can't be read; `--include-pinned` upgrades pinned ones (pinned apps are skipped by `--all` without it); `--source`, `--scope user or machine`, `--silent`, `--accept-source-agreements`, `--accept-package-agreements`. Fact. | `winget upgrade` then `winget upgrade --all` or one id. `winget list --upgrade-available` shows only upgradable apps, with the source each update comes from. Fact. | The engine's vocabulary: Chippi's list is `winget upgrade`'s list, with "Unknown version" apps off by default and shown behind a switch, matching `--include-unknown`. |
| Patch My PC Home Updater [P1] | Silent installs by default; "option to automatically close programs before updating"; skip updates for any application; schedule; suppresses restarts; per-app "Disable Auto Updating" under a three-dot menu. Fact for the first four; the menu is from a summary, unconfirmed. | Open, see My Apps with updates marked, run all or pick. | "Close the app first" as an option when the app is running, and "Skip" as a per-app choice. |
| winstall [S1] | Browse the winget catalog and build a pack to install together. Not an updater. Fact. | Browse, add to a pack, run the generated command. | Nothing. Installing new apps is out of Phase 7's scope. |

### What Chippi's feature does

A list of installed apps winget knows an update for: the app's real icon (`AppIcon.View`), name, installed version, available version, and the source. A header line says how many updates and when winget last checked. Verbs act on one row or on all rows:

| Verb | Behavior | Needs admin | Can't be undone | Shortcut slot |
|---|---|---|---|---|
| Check for updates | Runs the equivalent of `winget upgrade` and fills the list. Read-only. | No | n/a | Check app updates |
| Update | Updates one app silently (`--silent`, agreements accepted). Per-row. | Some apps (the installer asks for it) | The old version is replaced. Rolling back is a reinstall of the old version, which winget can do only if the source still has it. Inference. | none: a per-row action |
| Update all | Updates every non-ignored app in the list, one after another, with progress per row. | Some | As Update | Update all apps |
| Skip | Hides one app's available version, or the app for good ("Always skip"). | No | Reversible: shown in a "Skipped" list with Restore | none: a per-row action |
| Close and update | If the app is running, closes it with a normal close request first, then updates. Asks if the app has unsaved work? Chippi can't know. The confirm says "Close Firefox and update?" | Some | The app is closed | none: a per-row action |

- **Scope.** The first build covers the update list, Update, Update all, Skip, and a history of what ran. Pinning, backup and restore (`winget export` and `import` exist [W2]), scheduled checks, and notifications are listed so Travis can trim or add: UniGetUI has the schedule and backup [U1]; Patch My PC has the schedule [P1]. **Recommend:** no schedule and no backup in Wave 3; a "Check" on open plus the verb above.
- **Unknown version.** Apps whose version winget can't read stay out of the list unless the switch "Show apps with an unknown version" is on; Update on one of those says plainly that its version couldn't be checked. Fact for the flag [W1]; the wording is mine.
- **Elevation.** Not stated on the winget upgrade page [W1], and the winget return codes page returned 404, so the exit code for "needs admin" is unconfirmed here. **Open for Kayla:** how Chippi knows an update needs admin (the installer's exit code, or the manifest's `scope` / elevation requirement) before it starts. Chippi never shows UAC during a check, only when the verb is fired.
- **Store apps.** The uninstall page notes that `--source winget` avoids Microsoft Store agreement prompts [W3]. How `msstore` apps behave in `upgrade` is unconfirmed. **Open for Kayla**, with the COM API question: the winget API index page returned 404, so whether winget has a supported COM or PowerShell API I could not confirm from Microsoft's docs. (Inference: the CLI with machine-readable output is the safe fallback.)
- **Running apps and reboots.** Updating a running app can fail or ask to close it, and some installers want a reboot. Show "Close Firefox and try again" or "Restart to finish" on the row; never restart for Travis.
- **Failure.** A failed update says "Update failed" with the app and winget's reason, and the list is unchanged.

### Verb summary

Check for updates, Update, Update all, Skip, Close and update. Shortcut slots: Check app updates, Update all apps.

---

## 2. Uninstaller

### Best of the field

| Tool | What it does best | How it works | What Chippi takes |
|---|---|---|---|
| Revo Uninstaller Pro [R1] | Runs the app's own uninstaller, then scans for leftover files, folders and registry entries. By default files go to the Recycle Bin, registry entries to a Backup Manager, and a restore point is created at the start. A multiple-uninstall queue makes one registry backup and restore point for the whole queue and runs silent uninstallers where possible. A forced uninstall takes the program's name or folder for apps with no working uninstaller. Store apps are a separate list. Fact. | Pick the program, uninstall, then pick a leftover scan mode. Mode names ("advanced 3-mode"): the homepage says three modes but doesn't name them: unconfirmed. | The flow (uninstall first, then scan) and the safety net: Recycle Bin for files, a backup for registry entries. The queue for several apps at once. |
| BCUninstaller [B1] | Finds apps from many places (registered, hidden or protected, damaged or missing uninstallers, portable, Chocolatey, Steam, Store, Windows Features, Windows Updates). Sort by columns, filter by options on the left, regex filter, badges (Protected, Store App, System component, Update). "Quiet uninstallation" needs no clicks where an app supports it; MsiExec is "not guaranteed to work". "Uninstall manually" skips the uninstaller and lists files and registry keys to delete by hand, and the manual calls it "not recommended". **Simulate mode** "will prevent BCU from uninstalling or removing anything". Fact. | Open, sort and filter, tick several, run a queue. The restore-point prompt and the admin requirement come from a summary of the manual, not verbatim: unconfirmed. Leftover scan modes and confidence levels: unconfirmed (that section of the manual wasn't reachable). | Badges (so Windows components and updates aren't offered as normal apps), the filter, and **Simulate**: show everything that would be removed, remove nothing. This is the best idea for a preview before deleting. |
| Geek Uninstaller [G1] | Speed and simplicity. "Force Removal" for stubborn programs; "Clean Removal" does a deep, fast scan and removes leftovers. Recently installed or modified programs are highlighted. Type-to-search. One portable EXE, x64 and ARM64. Store apps are a separate view (Ctrl+Tab). Fact. | Open, type to find, Clean Removal. Columns, restore points and admin: unconfirmed. | Type-to-search on open, the highlight for recent installs, and a single uninstall verb with the leftover scan as a step rather than a separate tool. |
| `winget uninstall` [W3] | The engine for apps winget knows: `--silent`, `--interactive`, `--force`, `--all-versions`, `--scope`, `--product-code`, and for portable apps `--purge` and `--preserve`. Fact. | `winget uninstall <id>`. | Used for apps winget lists; others use their registered uninstall string. |

Where installed programs are registered: Microsoft documents `HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall` with a subkey per product code, and the `UninstallString` set by Windows Installer [M1]. That page covers only MSI-made entries. The other locations (the same key under `HKCU`, the 32-bit view under `WOW6432Node`) and `QuietUninstallString` are my inference and not on the page I read. Store apps are a different mechanism (packages, not Uninstall keys): inference; no page read.

### What Chippi's feature does

A list of installed apps: real icon, name, publisher, size, install date, with search on open and a filter for kind (Apps, Store apps, Windows components shown only if asked). The first screen is a list and a search box; selecting a row shows its verbs.

| Verb | Behavior | Needs admin | Can't be undone | Shortcut slot |
|---|---|---|---|---|
| Uninstall | Runs the app's own uninstaller (quiet when it has one, else its normal wizard, which Chippi does not click through). Afterwards, offers Scan for leftovers. | Often (machine-wide installs) | The app is removed. Reinstall is the recovery [R1]. | none: a per-row action |
| Scan for leftovers | Looks in the app's former install folder, its folders in the user's AppData and ProgramData, and its registry keys, and lists what it found with a confidence mark. Read-only. | No | n/a | none |
| Remove leftovers | Removes what's ticked. Files go to the Recycle Bin where they can; registry entries are exported to a backup file first. A preview of exactly what goes comes first (Simulate). | Some paths | Recycle Bin files: restorable. Registry entries: restorable from the backup file Chippi writes. Files that can't go to the Recycle Bin (over its size limit, network) are permanent; the confirm says so. | none |
| Uninstall several | A queue of ticked apps: one confirm listing them all, one after another with progress. | Often | As Uninstall | Uninstall selected apps |
| Open install folder | Reveals the app's folder in File Explorer. | No | n/a | none |
| Remove broken entry | For an app with no working uninstaller: removes only its entry from the list (Revo's "forced uninstall" and BCU's "manually" take over here, with the "not recommended" warning [B1]). Off in the first build. | Often | The entry is gone; files stay. | none |

- **Leftover confidence.** Only the leftover scan's rules matter here, and no tool's rules were readable: BCU's scan modes and confidence levels, and Revo's mode names, are unconfirmed. **Recommend** Chippi lists only items whose path or key contains the app's exact install folder name or its publisher folder name and is empty or only holds files belonging to the app, marks each "Safe" or "Check first" (Check first means unticked), and never lists anything under `Windows`, `System32`, a shared Microsoft key, or another app's folder. This is Chippi's own rule, inference, to be tested with fixtures.
- **Restore point.** Revo makes one at the start of an uninstall [R1]. Creating one needs admin and System Protection on, and Windows limits how often it creates them (inference, not read). **Open for Travis:** Chippi offers a restore point only if one is cheap, or leaves it out and relies on the Recycle Bin and registry backup. **Recommend** leaving it out of the first build.
- **Protected and system entries** are hidden unless asked, and never offered for bulk removal: Windows components, Chippi itself, and the winget "framework" packages.
- **Testing.** Never against Travis's real apps. Fixtures: a fake Uninstall key set and a stand-in installer in the Sandbox.

### Verb summary

Uninstall, Scan for leftovers, Remove leftovers, Uninstall several, Open install folder, (later) Remove broken entry. Shortcut slot: Uninstall selected apps.

---

## 3. Cleaner

### Best of the field

| Tool | What it does best | How it works | What Chippi takes |
|---|---|---|---|
| BleachBit [C1] | Cleaners are named groups (Cache, Cookies, Logs, Recent file lists, Temporary files). **Preview** "is 'safe' and makes no changes" and lists the files, then Delete. CLI: `--preview` then `--clean`; `--shred` for secure erase; `--all-but-warning` "will delete many files". Its CLI docs show that cleaning browsers can delete saved passwords (`firefox.passwords`). Shredding is permanent and weaker on SSDs. Fact. Whether preview shows a size, what's selected by default, whether deletion uses the Recycle Bin (the docs call it permanent and don't mention it), and what needs admin: unconfirmed. | Close apps you'll clean, tick cleaners, Preview, adjust, Delete. | The flow: **Preview first, always**, as its own step before Delete. Categories with a plain description. No shredding and no free-space wipe. |
| Storage Sense [S2] | Automatic and conservative: off by default; the Recycle Bin and Downloads rules are user settings with "over N days" thresholds; Downloads and cloud content "will not be managed unless you specify". Runs only on the system drive, when signed in and online more than 10 minutes. Previous Windows installations are listed under Temporary files. Fact. The rest of the Temporary files list: unconfirmed. | Settings › System › Storage. | The conservative default: Downloads and the Recycle Bin are never touched unless asked. |
| Disk Cleanup (`cleanmgr`) [C2] | `/sageset:n` saves a chosen set of cleanups, `/sagerun:n` runs it on every drive, `/autoclean` removes files left after a Windows upgrade. Fact. The docs use old wording (Temporary Internet Files, Downloaded Program Files), so they may be out of date. "Clean up system files" and elevation: unconfirmed. | Pick drive, tick categories, OK. | Saved selection: Chippi remembers which categories were last chosen. |
| CCleaner [C3] | Cleans browser and app leftovers, Recycle Bin, memory dumps, logs and caches; has a registry cleaner, free-space wipe, startup editing, and deletes System Restore points. Source is Wikipedia: the official page returned 403. Fact (via Wikipedia). Its history includes a 2017 backdoor and a 2018 data-collection change, and version 7 removes the options to close it or stop it starting at login. | Analyze, review, Run Cleaner. Not read. | A cautionary list: no registry cleaner, no restore-point deletion, no free-space wipe, no startup behavior. The registry cleaner is the feature with the worst risk for the least gain (inference). |
| WizTree [Z1] | Finds space rather than cleaning: reads the NTFS Master File Table directly for speed, and says this needs admin. The page doesn't describe deleting. Fact. | Pick a drive, scan, view a tree map. | Out of scope for Phase 7 as a feature. A "Biggest folders" list is a candidate for later and would need the same admin discussion. |

### What Chippi's feature does

A list of categories with a size per category, each with a checkbox, a one-line plain description and a "Safe", "Check first" or "Needs admin" tag. The page opens by measuring (read-only) and then shows the totals; Clean runs only after a confirm showing the size and count.

Proposed categories for the first build (Chippi's own proposal, inference; Microsoft pages for the Windows ones weren't fully readable):

| Category | Where | Default | Admin | Recycle Bin? |
|---|---|---|---|---|
| User temp files | `%TEMP%` (files not touched for over a day, skipping any in use) | Ticked | No | Permanent: they're temp files by definition, and the Bin would only hold them |
| Thumbnail and icon caches | The user's Explorer cache folder | Ticked | No | Permanent (rebuilt) |
| App caches | Named per app (a short list: Chromium and Firefox browser caches, but never cookies, passwords or history; Discord, Slack, Teams caches) | Unticked, "Check first" | No | Permanent |
| Windows Update download cache | The `SoftwareDistribution` download folder | Unticked | Yes | Permanent |
| Crash dumps and logs | User and system dump locations | Unticked | Some | Permanent |
| Recycle Bin | Empties it | Unticked, off in the first build | No | Permanent: empties the bin itself |
| Downloads | Never listed | n/a | n/a | n/a |

- **Verbs.** Measure (read-only; opens with the page), Clean, Clean selected.

| Verb | Behavior | Admin | Can't be undone | Shortcut slot |
|---|---|---|---|---|
| Measure | Scans the chosen categories and shows sizes. Read-only, never deletes. | Some categories | n/a | Scan for junk files |
| Clean | Cleans the ticked categories after a confirm "Clean 1.4 GB?". Skips files in use and reports them; never forces. | Some | Permanent except where the table says otherwise. This is the one thing in the feature Travis can't get back, so the confirm says so. | Clean junk files |

- **Never listed:** browser cookies, saved passwords, history, autofill, the registry, System Restore points, and anything in Documents, Desktop, Pictures or Downloads. BleachBit's own docs show how easily a cleaner reaches saved passwords [C1], and CCleaner's list shows the other risks [C3].
- **Running apps.** A category whose app is open (a browser cache while the browser runs) says "Close Chrome to clean this" and is skipped; it's not forced.
- **Size accuracy.** The sizes are what Measure found; files that change before Clean are cleaned or skipped, and the result says how much was actually freed.
- **Testing.** Only against fixture folders in the Sandbox or a temp folder set up by the test. A dev run points the categories at the fixture root, not at the real `%TEMP%`.

---

## 4. Port manager

### Best of the field

| Tool | What it does best | How it works | What Chippi takes |
|---|---|---|---|
| TCPView [T1] | Live list: "updates every second" by default, adjustable; changed endpoints show yellow, deleted red, new green. Reports the process that owns each endpoint, with the service name. Closes ESTABLISHED TCP connections from File or the right-click menu. Save writes the list to a file; name resolution can be toggled. Fact. Columns, filters, admin, whether it can end a process: not stated. | Open and watch; right-click a row to close the connection. | The highlighting of new and ended entries, which makes "what just opened this port" visible. A calm refresh (a few seconds, and a pause switch) rather than the CPU churn of every second. |
| CurrPorts [N1] | The richest list: process name, path, version info, creation time, user name and more, chosen through Choose Column; filter strings such as `include:process:firefox.exe`; actions: close selected connections, **kill the owning process**, copy, save HTML or XML or text, whois lookup (a separate download); marks new ports green; refreshes every 2 to 10 seconds; has a "Run As Administrator" command, because some process paths and user names need it; "Ask before any action" can be switched off. Fact. Undo: not stated. | Open, filter, select, close or kill. | The columns that answer "who holds this port": process, path, and user. A filter box. Copy. A confirm before ending a process, not a switch to turn the confirm off. |
| `netstat` [N2] | Read-only. `-a` all, `-n` numeric, `-o` includes the PID, `-b` names the program and "can be time-consuming and will fail unless you have sufficient permissions", with an optional interval. Fact. | `netstat -ano` and read the PID. | The proof that the data is simple and needs no admin for the PID. Chippi shows the process name for you. |
| `Get-NetTCPConnection` [N3] | Filters (`-State`, `-LocalPort`, `-OwningProcess` and more); read-only; the page doesn't say it shows the process name, only the PID. Fact. | One query. | Filter vocabulary: state and port. |
| IP Helper `GetExtendedTcpTable` [N4] | The mapping: with `TCP_TABLE_OWNER_PID_ALL` it returns the PID that owns each connection, for IPv4 and IPv6 (`MIB_TCPTABLE_OWNER_PID` and `MIB_TCP6TABLE_OWNER_PID`). The documented sizing pattern is a call that returns `ERROR_INSUFFICIENT_BUFFER` with the needed size. Fact. UDP uses the sibling `GetExtendedUdpTable` (inference, same family, not on the page read). | n/a, an API. | The engine. Admin isn't mentioned on the page; reading PIDs for other users' processes without admin is inference and Kayla should test it. |

### What Chippi's feature does

A list: protocol, local address and port, remote address and port, state, and the process (real icon, name, PID, path on hover). The page opens filtered to **listening** ports, which is what "who holds this port?" almost always means, with a switch for all connections. A search box takes a port number or a name ("3000", "node").

| Verb | Behavior | Admin | Can't be undone | Shortcut slot |
|---|---|---|---|---|
| Find port | Types a port number, shows who holds it. Opens the page with that filter. | No | n/a | Find port |
| Free port | Ends the process that holds the port, after a confirm that names the process and the port: "End node.exe (PID 4120) to free port 3000?". It asks the process to close first; "Force end" is a second, separate step if it doesn't. | Not for the user's own processes; yes for others' and for services | The process's unsaved work and state are lost. No undo. | none: per-row action |
| Close connection | Closes one TCP connection (as TCPView and CurrPorts do [T1] [N1]). Listed as a candidate for later: closing a connection needs admin (inference) and is rarely what you want. | Yes (inference) | The connection ends; the app may reconnect | none |
| Copy | Copies a row's port, address or PID text. Writes the clipboard, so tests are Sandbox only. | No | n/a | none |
| Refresh | Reads the table again. Auto-refresh every few seconds while the page is open, a pause switch. | No | n/a | none |

- **Protected processes.** System, services and Chippi itself are shown but not endable: the row says why ("Windows service") instead of a verb. Ending a process Chippi doesn't own says "Needs admin" and does nothing without the helper.
- **Why the confirm names the process.** The one thing Travis can't recover is the process's state, so the question has to name it.
- **Findings from the field:** TCPView refreshes every second [T1] and CurrPorts offers 2 to 10 seconds [N1]; Chippi picks 3 seconds while the page is open and zero when it's closed (inference, to be timed).
- **Testing.** A test listener opened on a random high port by the test itself, and a child process the test started: never an arbitrary real process.

---

## 5. Displays and brightness

### Best of the field

| Tool | What it does best | How it works | What Chippi takes |
|---|---|---|---|
| Twinkle Tray [K1] | A tray flyout: click the icon to bring up "Adjust Brightness", click away to hide. Auto changes by time of day or when idle; hotkeys for specific or all displays; **normalize backlight across different monitors**; contrast and other DDC/CI features; a CLI (`--MonitorNum`, `--All`, `--Set`, `--Offset`, `--VCP`). Uses DDC/CI and WMI. Known problems listed: some AMD custom colors, VGA and DVI, USB or Thunderbolt or Surface docks with HDMI or DisplayPort, and DDC/CI that makes some monitors "behave poorly". Fact. Linking, volume, input switching, power: not in its README. ARM64: not mentioned. | Click the tray icon, drag a slider per monitor; press a hotkey. | The flyout (here, a Chippi panel), the hotkeys, "Offset" (nudge by a step), and normalizing different monitors. The known-problem list is the failure copy for monitors that don't answer. |
| Monitorian [M2] | Sliders per monitor "either individually or in unison"; per-monitor brightness and contrast ranges; command line `/get` and `/set` for brightness, contrast, color temperature and input source (an add-on); hotkeys and time commands are paid add-ons in the Store version. Requires an external monitor to be DDC/CI enabled; failure reasons are listed: model lacks DDC/CI, DDC/CI off in the monitor's menu, bad capabilities reply, converters not compatible. Fact. Laptop panels: not addressed. ARM64: not mentioned. | Open the window, drag a slider per monitor or all together. | Move-together mode ("All displays") and the plain-language failure reasons ("Turn on DDC/CI in the monitor's menu"). |
| ClickMonitorDDC [D1] | Not researched: I couldn't find its home page from a confirmed URL. Unconfirmed. | | Nothing from it. |

Windows' own interfaces: the high-level Monitor Configuration functions `GetMonitorBrightness`, `SetMonitorBrightness`, `GetMonitorContrast` and `SetMonitorContrast` (min, max and current), the low-level `GetCapabilitiesStringLength`, `GetVCPFeatureAndVCPFeatureReply` and `SetVCPFeature`, and `GetPhysicalMonitorsFromHMONITOR` to find the monitors [D2]. For internal panels, WMI's `WmiMonitorBrightnessMethods` in `Root\wmi` has `WmiSetBrightness` and `WmiSetALSBrightness` [D3]. That page doesn't mention laptops and lists no read method, so reading the current level (the class `WmiMonitorBrightness`) is my inference. Both are Kayla's to design and verify.

### What Chippi's feature does

A panel with a slider per display: the display's name, its brightness, and (for external monitors) its contrast, with an "All displays" slider that moves them together. Displays that don't answer show their name with "Brightness isn't available on this display" and a short reason, never a dead slider. Changing Windows' own display settings (resolution, arrangement, night light) is out of scope: the catalog says brightness for every monitor, external ones included.

| Verb | Behavior | Admin | Can't be undone | Shortcut slot |
|---|---|---|---|---|
| Brightness up | Raises the display under the pointer (or all, per a setting) by a step (default 10 %). | No | Reversible by Brightness down | Brightness up |
| Brightness down | The same, lowering. A floor: never below 5 % in one step from a key, so a held key can't leave a black screen (inference; Twinkle Tray's Offset is the same idea [K1]). | No | Reversible | Brightness down |
| Set brightness | Sets one display or all to a value, from the slider. | No | Reversible | none |
| Set contrast | Same, for external monitors only. | No | Reversible | none |
| Match displays | Aligns the displays to the same perceived level (Twinkle Tray's normalize [K1]). Candidate for later. | No | Reversible | none |

- **Which display does the shortcut act on.** The display under the pointer, because that is the one Travis is looking at; a setting offers "All displays" instead. (Proposed; Travis may differ.)
- **What can't be undone.** Nothing here is permanent in the sense of Travis's data. The risk is a monitor stuck at a level he can't see to change. So a key can't push below the floor, and Settings shows a Reset that sets each display to 50 % (proposed).
- **Limits.** The plan limits DDC/CI writes to brightness and contrast, so no power, input source, volume or color temperature, though Twinkle Tray and Monitorian offer some of them [K1] [M2].
- **Failure.** Common reasons are in the field's own lists: DDC/CI off in the monitor's menu, a dock that doesn't pass it, VGA or DVI, and an unsupported model [K1] [M2]. Chippi says which it can tell, and otherwise "This display doesn't answer".
- **Hot plug.** Displays added or removed while the panel is open refresh its list. Inference: Chippi already tracks display changes (Snap, Phase 6).
- **Testing.** Dev runs that change Travis's monitors' brightness say so first and put it back (plan, safety rules). Anything else uses a fake monitor interface. Tests never write DDC/CI to a real panel on CI.

---

## What can't be undone, in one place

| Feature | Verb | What's lost | Recovery |
|---|---|---|---|
| App updates | Update, Update all | The older version | Reinstall the old version if the source still has it (inference) |
| Uninstaller | Uninstall | The app and its settings | Reinstall the app |
| Uninstaller | Remove leftovers | Files, registry entries | Recycle Bin for files; a backup file Chippi writes for registry entries; some files can't go to the Bin |
| Cleaner | Clean | Temp files and caches | None; they're temp and cache files, and the confirm says so |
| Port manager | Free port | The process's state | None |
| Displays | Any | Nothing | Brightness up or down, Reset |

## Questions for Travis's decision list

1. **Elevation per feature.** Updates, Uninstall and parts of Clean need admin. Is the helper approved (Step 2, decision 5)? Without it, those verbs say "Needs admin" and do nothing.
2. **App updates extras.** Schedule, backup and restore, pinning: leave out of Wave 3? (Recommended: leave out.)
3. **Uninstall safety.** Restore point and "Remove broken entry": leave out of the first build? (Recommended.)
4. **Cleaner scope.** The category table above: add or remove categories? Empty the Recycle Bin: off in the first build? (Recommended.)
5. **Port manager.** End a process only, or also close a connection? Does Free port skip the "ask the app to close first" step? (Recommended: process only, ask first.)
6. **Brightness.** Shortcuts act on the display under the pointer or on all? Step 10 %?

## Gaps and things I couldn't confirm

- **winget:** the pin, return codes and API pages returned 404, so pin types, the "needs admin" exit code, and whether a COM or PowerShell API is supported are unconfirmed. `winget upgrade` and `list` options were read [W1] [W2]. Msstore behavior in upgrade is unconfirmed. Kayla's architecture should settle these from the winget-cli repository.
- **Uninstaller:** Revo's and BCU's leftover scan modes and confidence levels, Geek's columns, and whether any of them needs admin to start were not on the pages read.
- **Cleaner:** CCleaner's official page (403) and one Microsoft Storage Sense page (404) were unreadable; Storage Sense's Temporary files list and Disk Cleanup's elevation behavior are unconfirmed. The proposed category list is mine.
- **Ports and brightness:** TCPView's columns and filters (screenshot only), whether closing a connection needs admin, ARM64 support for any of the brightness tools, and ClickMonitorDDC are unconfirmed.
- **Nothing was run.** No tool was installed, and the proposed rules (leftover confidence, the brightness floor, refresh intervals) are inference to be tested in the briefs.

## Sources

Read 2026-11-06.

- [W1] winget upgrade: https://learn.microsoft.com/en-us/windows/package-manager/winget/upgrade
- [W2] winget list, export, import: https://learn.microsoft.com/en-us/windows/package-manager/winget/list , https://learn.microsoft.com/en-us/windows/package-manager/winget/export , https://learn.microsoft.com/en-us/windows/package-manager/winget/import
- [W3] winget uninstall: https://learn.microsoft.com/en-us/windows/package-manager/winget/uninstall
- [U1] UniGetUI: https://github.com/Devolutions/UniGetUI
- [P1] Patch My PC Home Updater: https://patchmypc.com/home-updater
- [S1] winstall: https://winstall.app/
- [R1] Revo Uninstaller Pro: https://www.revouninstaller.com/products/revo-uninstaller-pro/
- [B1] BCUninstaller: https://github.com/Klocman/Bulk-Crap-Uninstaller , https://www.bcuninstaller.com/
- [G1] Geek Uninstaller: https://geekuninstaller.com/
- [M1] Uninstall registry key (MSI): https://learn.microsoft.com/en-us/windows/win32/msi/uninstall-registry-key
- [C1] BleachBit: https://docs.bleachbit.org/ , https://docs.bleachbit.org/doc/general-usage/ , https://docs.bleachbit.org/doc/command-line-interface/ , https://docs.bleachbit.org/doc/shred-files-and-wipe-disks/
- [S2] Storage Sense: https://support.microsoft.com/en-us/windows/manage-drive-space-with-storage-sense-654f6ada-7bfc-45e5-966b-e24aded96ad5
- [C2] cleanmgr: https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/cleanmgr
- [C3] CCleaner (via Wikipedia): https://en.wikipedia.org/wiki/CCleaner
- [Z1] WizTree: https://www.diskanalyzer.com/ , https://www.diskanalyzer.com/faq
- [T1] TCPView: https://learn.microsoft.com/en-us/sysinternals/downloads/tcpview
- [N1] CurrPorts: https://www.nirsoft.net/utils/cports.html
- [N2] netstat: https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netstat
- [N3] Get-NetTCPConnection: https://learn.microsoft.com/en-us/powershell/module/nettcpip/get-nettcpconnection
- [N4] GetExtendedTcpTable: https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable
- [K1] Twinkle Tray: https://github.com/xanderfrangos/twinkle-tray
- [M2] Monitorian: https://github.com/emoacht/Monitorian
- [D2] Monitor Configuration functions: https://learn.microsoft.com/en-us/windows/win32/monitor/monitor-configuration-functions
- [D3] WmiMonitorBrightnessMethods: https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmimonitorbrightnessmethods
- [D1] ClickMonitorDDC: not found.
