# Admin helper: brief

Wave 3, built first in the wave. Owner: Kayla (the helper, its client in Chippi, the Sandbox runner). Screens: none of its own; the four wave 3 features show its results on theirs. Reviewer: Karl, full review (Chippi's first elevated code). Task 811. Status: for Travis's confirmation with the wave 3 briefs. Nothing is built yet.

[Decisions](README.md) override the research. References: [architecture §6](architecture.md#6-the-admin-helper) and [§11](architecture.md#11-standard-user-or-admin), [behavior spec B](behavior-spec-b.md) (the "Needs admin" column, Leftover ownership), the wave 3 briefs (`app-updates.md`, `uninstaller.md`, `cleaner.md`, `port-manager.md`, Herb).

Decided (Travis, task 483, 2026-11-06): the helper is built, narrow, with the risk accepted as stated in architecture §6. A separate program, started with one UAC prompt only when Travis presses a button that needs it, a closed list of jobs each checked again inside the helper, and it exits when done. It covers batched updates and uninstalls, leftovers in Program Files and `HKLM`, Windows' own temp and update caches, and stopping a service or an admin process holding a port. Left for later (task 478): closing a single connection, finding large folders, emptying the Recycle Bin.

This brief settles architecture §6 for the build. Where it changes §6, it says so.

## Job and doors

Let one UAC prompt cover a job Travis asked for, and let the helper refuse anything outside that job, even if Chippi asks.

| Door | What happens |
|---|---|
| A feature's button that needs admin | Update, Update all, Uninstall, Uninstall several, Remove leftovers, Clean, Free port, Stop service. Chippi's own confirm comes first ("Update 5 apps?"); the UAC prompt comes after Update, never before the question. |
| Shortcuts, Search, Radial, Actions | Reach the same buttons through the feature's own verb, so they show the same confirm, then the prompt. The helper has no verbs of its own anywhere. |
| UAC prompt | Windows' own. Chippi is unsigned, so it names the file, `Chippi.Elevated.exe`, with an unknown publisher. |
| Bar | Nothing of its own. The feature's line ("Updating 2 of 5") covers the session. |
| Settings | Nothing. There's no switch to turn the helper on, and no "always run as admin". |
| Log | Every verb, its check result and its outcome, through Chippi's log. |

## How it starts

- **Only from a button.** `ShellExecuteEx` with the `runas` verb and `SEE_MASK_NOCLOSEPROCESS`, run right after Travis answers the feature's confirm. Never at startup, from a timer, as a service or as a scheduled task, and never to read: a check for updates, the leftover scan and the Cleaner's measure run as Travis, and rows they can't read say "Needs admin".
- **One session at a time.** While a helper runs, the next admin job goes to it, with no second prompt. A job asked for while none runs starts one.
- **Its manifest asks for admin** (`requireAdministrator`), so it can't run unelevated by accident. It checks `TokenElevation` first anyway, and exits if it isn't elevated.
- **Same account only.** The helper must run as the same user as Chippi. If Windows asks for another account's password (an over-the-shoulder prompt), the pipe's DACL refuses that account and the helper exits; Chippi shows "Needs admin". Travis's account is an administrator, so his prompt is a Yes.
- **Prompt declined:** `ShellExecuteEx` fails with `ERROR_CANCELLED`. The feature shows "Needs admin" on the rows that needed it and does nothing to them; rows that didn't need admin go ahead as Travis.
- **File missing or won't start:** "Admin helper failed", logged with the Windows error.
- **Exit:** when the pipe closes, when Chippi sends `Done`, or after 60 s with no job running. After `Uninstall`, it waits up to 10 minutes for that app's `DeleteLeftovers` or `Done`, since the review happens in between. A Chippi exit or crash closes the pipe, so the helper never outlives it by more than the job in flight.

## How it talks

- **The pipe is Chippi's.** Chippi creates a named pipe with a random name, `FILE_FLAG_FIRST_PIPE_INSTANCE`, one instance, `PIPE_REJECT_REMOTE_CLIENTS`, and a DACL naming only Travis's logon session; the default DACL would let Everyone read [pipe security]. The name is the helper's only argument (`--pipe <name>`); any other command line exits at once.
- **Each end checks the other.** Chippi checks the client with `GetNamedPipeClientProcessId` against the process it launched. The helper checks the server with `GetNamedPipeServerProcessId`: it must be `Chippi.exe` in the helper's own folder. Neither check stops a program running as Travis that has got into Chippi itself; that's the accepted risk below, and why the verbs are narrow.
- **Messages:** length-prefixed JSON, 64 KB at most, typed with `System.Text.Json` source generation (NativeAOT has no reflection). Each request has an id and one verb with typed fields. The answers are `Progress`, `Done`, `Refused(reason)` and `Failed(error)`. A message that doesn't parse, an unknown verb or an extra field ends the session.
- **No generic form.** There's no "run this", no path to execute, and no command line the helper passes on. A verb Chippi doesn't know can't be sent.

## The verbs

Each verb names what to act on; the helper looks up the details itself and refuses anything its own checks don't match. A refusal does nothing for that item and goes on with the next.

| Verb | Used by | Chippi sends | The helper checks again |
|---|---|---|---|
| `UpdatePackage` | App updates | winget package id | Id pattern; winget's own path |
| `Uninstall` | Uninstaller | `HKLM` Uninstall key name and registry view | The key, its uninstaller's file |
| `DeleteLeftovers` | Uninstaller | Items found by the scan, each with the class it was shown in and whether Travis ticked it | Every rule under Leftovers below |
| `MeasureCategory`, `CleanCategory` | Cleaner | A category name | The name is in the helper's own list |
| `CloseProcess`, `EndProcess` | Port manager | PID, process start time, port | The process still holds that port, isn't protected |
| `StopService` | Port manager | Service name, port | The service's process holds that port, isn't protected |

### UpdatePackage(id)

- **Which updates need the helper** (answers spec B's open question). Chippi asks winget's COM API, as Travis, for the update's installer (`GetApplicableInstaller`) and reads its `Scope` and `ElevationRequirement`, both in `PackageInstallerInfo` [winget IDL]:
  - `ElevationRequired`, or `Scope` System: the helper.
  - `ElevationProhibited`, or `Scope` User: Chippi, as Travis. winget refuses to run an installer that prohibits elevation from an elevated process (error 0x8A150056) [winget errors].
  - Neither declared: the installed package's `InstalledScope` metadata; machine goes to the helper, anything else to Chippi. (**Inference:** the exact value it reads for machine is checked in the first Sandbox run.)
- **Pattern:** dot-separated segments of letters, digits, `+`, `_` and `-`, 128 characters at most, not starting with `-`, so an id can never read as an option.
- **Command:** `winget upgrade --id <id> --exact --source winget --silent --accept-package-agreements --accept-source-agreements --disable-interactivity` [winget upgrade], the id one argument. `upgrade` acts only on a package that's installed. Store apps (`msstore`) aren't sent: they update through the Store as Travis.
- **Which winget (changes §6).** Not the `winget.exe` alias in `%LOCALAPPDATA%\Microsoft\WindowsApps`: Travis's own account can write that folder, so another program running as him could swap the alias and the helper would run it elevated. The helper finds App Installer's package with `GetPackagesByPackageFamily` (`Microsoft.DesktopAppInstaller_8wekyb3d8bbwe`) and `GetPackagePathByFullName`, and runs `winget.exe` from that folder under `Program Files\WindowsApps`, which only Windows can write. **Inference:** running winget from its package folder works elevated, as admin scripts do for SYSTEM; the first Sandbox run checks it.
- **Several updates** are one session, one at a time, with `Progress` per package. A failure goes back with winget's exit code and doesn't stop the rest.
- winget's command line, not its COM API, inside the helper: COM activation from an elevated unpackaged process has an open crash report [winget#4377], and NativeAOT has no built-in COM.

### Uninstall(view, keyName)

- **The key:** opened under `HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall` in the named view (`KEY_WOW64_64KEY` or `KEY_WOW64_32KEY`), refused if it's a registry link, a `SystemComponent` entry or an update with a `ParentKeyName`. An `HKCU` entry is never run elevated: any program running as Travis can write one, so Chippi runs it as him.
- **The command:** a Windows Installer entry (`WindowsInstaller` 1 and a product-code key name) runs `%SystemRoot%\System32\msiexec.exe /x <product code> /passive /norestart`, built by the helper. Any other entry runs its `QuietUninstallString` if it has one, else its `UninstallString` with the app's own wizard, which Travis clicks through.
- **The file it runs** must be an absolute path that exists, and must pass the path rules below. Neither it nor any folder above it may be writable by Travis's standard token (the writable check below): an uninstaller in a folder Travis can write could have been swapped. An unquoted path with spaces is resolved by the helper, trying each `.exe` boundary from the left, and passed to `CreateProcess` as the application name, never left for Windows to guess.
- **Before it runs,** the helper keeps the key's values (name, `DisplayName`, `Publisher`, `InstallLocation`, product code, the uninstaller's folder) for `DeleteLeftovers` in this session, since the uninstaller usually deletes the key.
- **Waiting:** the uninstaller runs in a job object, and the helper waits until the job has no processes, so an uninstaller that copies itself to Temp and restarts is still waited for. The result carries the exit code (3010 means "Restart to finish") and whether the key is gone.

### DeleteLeftovers(app, items)

As architecture §6, with the precise checks:

- **The app's identity is the helper's own:** the key values kept by `Uninstall` for that app in this session. Without them, nothing is deleted elevated.
- **Class checked again.** A "Safe" item must sit inside the kept `InstallLocation`, or be a key or folder named by the kept uninstall data. A "Check first" item is accepted only if the request says Travis ticked it after the review. An item whose class the helper's own check doesn't match is refused.
- **Never another app's.** The helper reads every installed app's Uninstall entry (both `HKLM` views and Travis's `HKCU`) and refuses an item that is, contains or sits inside another app's `InstallLocation`, install folder or uninstall key. Removing Firefox with Thunderbird installed refuses `Program Files\Mozilla Thunderbird` and the shared `Mozilla` folder above it.
- **Fixed deny list:** `Windows`, `System32`, the `Program Files`, `Common Files` and `ProgramData` roots, user profile roots, and Chippi's own folders.
- **Path rules** (also used for `Uninstall`'s file): canonical before any check. Refused: relative paths, `.` and `..` segments, `\\?\` and `\\.\` prefixes, UNC and network paths, alternate data streams, 8.3 short names. Containment compares whole segments, case-insensitively: `C:\Program Files\App` doesn't contain `C:\Program Files\AppData`.
- **No reparse points on the way.** The item and each folder above it are opened with `FILE_FLAG_OPEN_REPARSE_POINT`; any reparse point, or a `GetFinalPathNameByHandle` path that isn't the requested one, is refused [reparse points]. The folders above stay open without `FILE_SHARE_DELETE` until the item is gone, so none can be renamed or swapped for a junction in between. The item itself could still be renamed by someone who could already delete that name; what gets recycled is then only a name in a locked folder, never a link's target, because nothing is followed (below).
- **Only what Travis can't delete himself.** The writable check: the helper takes Travis's standard token (`TokenLinkedToken` from its own elevated token) and asks `AccessCheck` whether that token could delete the item (`DELETE` on it, or `FILE_DELETE_CHILD` on its folder). If it could, the item is refused and goes back to Chippi's standard-user delete. An item owned by anyone but Administrators, SYSTEM or TrustedInstaller is refused outright: an admin-only leftover that a standard user owns was planted.
- **Registry keys:** only under `HKLM\Software`, each view named, only keys named by the kept uninstall data, refused if the key or any key under it is a link, or if Travis's standard token could delete it (then it's the standard path's). Exported first with `%SystemRoot%\System32\reg.exe export` to a `.reg` file in Chippi's data folder, then deleted with `RegDeleteTree`.
- **Files and folders** go to Travis's Recycle Bin with `SHFileOperation`, `FOF_ALLOWUNDO` and `FOF_WANTNUKEWARNING` (no COM). Same user, so the same bin. A reparse point inside a deleted folder goes as a link; its target is never touched.

### MeasureCategory(name), CleanCategory(name)

- **Categories are fixed in the helper's code,** each a folder and a rule. Cleaner's brief chooses which ship; the helper knows only those. Candidates from spec B and §11:

| Name | Folder | Travis can list it (measured 2026-11-07) |
|---|---|---|
| `WindowsTemp` | `%SystemRoot%\Temp` | No |
| `WindowsUpdateDownloads` | `%SystemRoot%\SoftwareDistribution\Download` | Yes |
| `DeliveryOptimization` | Delivery Optimization's cache under `ServiceProfiles\NetworkService` | No |
| `SystemCrashDumps` | `%SystemRoot%\Minidump`, `%SystemRoot%\MEMORY.DMP`, `ProgramData\Microsoft\Windows\WER\ReportArchive` and `ReportQueue` | Yes |

- **Rule:** files not written for over a day; a file in use is skipped and counted; no reparse point is followed (a link inside is removed as a link); deleted for good, as decided in task 485.
- **Measuring needs the helper** for the folders Travis can't list. Chippi never prompts only to measure: those rows show "Needs admin" with no size. **Recommend** for the Cleaner's brief: Clean with such a row ticked starts the helper, which measures, then the confirm shows the full size ("Clean 1.4 GB for good?"), then Clean. One prompt, and the question's size is real.
- The helper doesn't stop Windows Update or Delivery Optimization to clean their caches; files they hold are skipped.

### CloseProcess, EndProcess(pid, startTime, port) and StopService(name, port)

- **The row is read again** in the helper (`GetExtendedTcpTable`, `GetExtendedUdpTable`): the PID must hold that port now, and its start time (`GetProcessTimes`) must match, so a reused PID is refused.
- **Never:** PID 0 or 4, `csrss`, `smss`, `wininit`, `winlogon`, `lsass`, `services`, `svchost` (a service is stopped instead), Microsoft Defender's processes, protected processes, Chippi, or the helper.
- **`CloseProcess`** posts `WM_CLOSE` to the process's top-level windows; Chippi can't do this for an elevated app, since Windows blocks it across integrity levels. `EndProcess` is `TerminateProcess`, Port manager's separate Force end.
- **`StopService`:** the service's process (`QueryServiceStatusEx`) must hold that port, it must be a service process (not a driver), accept Stop, and not be on a fixed list of Windows' core services (`RpcSs`, `RpcEptMapper`, `DcomLaunch`, `LSM`, `SamSs`, `BFE`, `mpssvc`, `WinDefend`, `EventLog`, `Dhcp`, `Dnscache`, `nsi`, `PlugPlay`, `Power`, `BrokerInfrastructure`, `Winmgmt`, `CryptSvc`, `Schedule`, `ProfSvc`, `gpsvc`). A service others depend on fails with Windows' own `ERROR_DEPENDENT_SERVICES_RUNNING`; the helper doesn't stop the dependents.

## The risk

As architecture §6, accepted in task 483: Chippi is unsigned and installed in `%LOCALAPPDATA%\ChippiApp`, which Travis's account can write, so a program already running as him could replace `Chippi.Elevated.exe` and the next prompt would elevate that. Microsoft doesn't treat UAC as a security boundary [MSRC criteria]. The helper doesn't make that worse and doesn't make it easy:

- The prompt shows only right after a button and Chippi's own question.
- One NativeAOT executable, no managed DLLs beside it, and DLLs load from System32 only (`SetDefaultDllDirectories` with `LOAD_LIBRARY_SEARCH_SYSTEM32`) [DLL security].
- Nothing it runs comes from a place Travis's account can write: winget from its package folder, uninstallers only from folders his standard token can't write, `msiexec` and `reg.exe` from System32.
- No standing elevation: no service, task or startup entry.

The full fix (signing, installing under Program Files) stays out of scope. PowerToys' way, restarting the whole app as admin [PowerToys admin], isn't taken.

## Edge cases

- **Chippi quits or crashes mid-job:** the job in flight finishes (an uninstaller can't be stopped halfway safely), then the helper sees the closed pipe and exits. Nothing queued after it runs.
- **The helper crashes:** Chippi sees the pipe close, marks the rows still waiting "Admin helper failed", and doesn't retry by itself.
- **Installing a new Chippi while a helper runs:** the installer closes Chippi, so the helper exits after the job in flight. Until it does, its file is in use. **Not verified:** whether Velopack's installer waits for it or fails and needs running again.
- **A job longer than the idle timer** (a big installer): the 60 s idle counts only while no job runs.
- **Two features want admin at once** (an update batch running, then Free port): the second job queues in the same session.
- **The prompt sits unanswered:** Chippi's row shows "Waiting for admin" until Windows answers; there's no timeout of Chippi's own.

## Shortcuts

None. The helper has no verbs, slots or commands. Every door reaches it through a feature's verb and that verb's confirm.

## What we took

| Tool | Taken | Left out |
|---|---|---|
| winget | Its scope and elevation fields decide what needs admin; its command line runs the batch | Its COM API inside the elevated process (crash report, no COM in NativeAOT) |
| Revo Uninstaller (spec B) | Registry entries backed up before they're deleted; files to the Recycle Bin | Restore points (left out in task 478) |
| PowerToys | Nothing | Restarting the whole app as admin: every feature would run elevated |

## Copy

Every string, all in features' screens. Marge runs them through Decide before the installer.

- Needs admin (a row or verb that needed the helper and didn't get it).
- Waiting for admin (a row while the prompt is open).
- Admin helper failed (couldn't start, or ended mid-job).
- Refusals, on the row of the item kept: "Kept: shared with <app>", "Kept: protected folder", "Kept: changed since the scan". Each feature's own errors ("Update failed", "Uninstall failed") are in its brief.

## Shared code

- **New projects,** for Marge's approval: `Chippi.Elevated` (the helper, NativeAOT `win-x64` and `win-arm64`), `Chippi.Elevated.Protocol` (the message types and verbs, no dependencies, used by both sides) and `Chippi.Elevated.Tests`, all in `Chippi.slnx`.
- **Core:** `IAdminHelper` (start a session, send a job, its results) with a fake for feature tests; the elevation check (`TokenElevation`) moves here from TextSnippets and SmoothScrolling, its third use.
- **Chippi.UI:** the real `AdminHelper`: the pipe, the launch, the client checks.
- **App:** `ModuleContext.Admin`; `Capability.Admin` starts meaning the helper. `build/pack.ps1` puts `Chippi.Elevated.exe` beside `Chippi.exe` for each RID.
- **AGENTS.md:** the helper's test rules (below) go in with the PR.

## Tests

- **CI, no elevation, no prompt** (a fake launcher, the fake pipe, fake registry and process views):
  - the protocol: a malformed message, an oversized one, an unknown verb or field, and a command line other than `--pipe <name>` each end the session; a client or server that isn't the expected process is refused; a declined prompt (`ERROR_CANCELLED`) gives "Needs admin" and runs nothing
  - routing: each `Scope` and `ElevationRequirement` pair, and `InstalledScope` when neither is declared, goes the right way; `msstore` is never sent
  - ids: good ids pass; `-x`, `a b`, `a;b`, a 129-character id and an empty one are refused
  - `Uninstall`: links, `SystemComponent`, `ParentKeyName` and `HKCU` keys refused; MSI entries build the `msiexec` line; quoted, unquoted-with-spaces and environment-variable paths resolve; a file in a folder the fake standard token can write is refused
  - `DeleteLeftovers`: every case in architecture §6 (Firefox with Thunderbird, a Safe item outside `InstallLocation`, no kept record, `..`, 8.3, `\\?\`, UNC, a data stream, `App` against `AppData`, a registry link, a key outside `HKLM\Software`), plus a Check first item not ticked, and an item the fake standard token could delete
  - categories: an unknown name refused; the day rule and in-use skips on a temp tree
  - processes: PID 0 and 4, each protected name, a reused PID (start time differs), a PID no longer on the port, a core service and a driver are refused
  - lifetime: idle 60 s, the 10-minute wait after `Uninstall`, a long job not timed out, exit on pipe close, a second job reusing the session (injected clock)
- **CI, real file system, no admin:** junction and path checks on temp trees (junctions need no admin): a junction as the item, above it, and pointing at a fixture "System32"; the held folders block a rename; a junction inside a recycled and a deleted folder is removed as a link with its target intact. The CI runner is an admin without UAC, so nothing there may depend on running unelevated.
- **Windows Sandbox, the real helper,** once, at the end (`run.ps1 -Checks admin-helper`, a self-contained build with the NativeAOT helper):
  - a throwaway standard user made by the run, whose token (`LogonUser`) stands in for Travis's standard token in the writable and owner checks, plus symbolic links and a folder planted by that user
  - `Uninstall` and `DeleteLeftovers` on test apps the run registers under `HKLM` (a stand-in uninstaller exe, its folders and keys, one that relaunches itself from Temp, and a sharing "Thunderbird")
  - categories against folders the run fills; `CloseProcess`, `EndProcess` and `StopService` on a listener and a throwaway service the run creates on `127.0.0.1`
  - `UpdatePackage` in the separate networked `.wsb` with winget installed (architecture §14), on a small package the run installs first
  - first, two questions: whether the Sandbox's account gets a UAC prompt at all, and whether `runas` with a declined prompt can be driven there. Where it can't, the prompt and Decline are Travis's live check.
- **Travis's live check:** one real prompt, Yes and No; an update batch and an uninstall of throwaway apps he picks; Free port on a throwaway listener. Never his real apps.

## Done means

One PR with the helper, its client and the Sandbox check, CI green on x64, ARM64 and the test job, Karl's full review, then the Sandbox run with these timings:

| What | Target | How |
|---|---|---|
| Prompt answered to helper connected | Under 500 ms | Stopwatch from `ShellExecuteEx` returning to the pipe's first message, 10 runs |
| A verb's checks | Under 50 ms each; `DeleteLeftovers` with 200 items and 300 installed apps under 500 ms | Timestamps in the helper's log |
| Helper exit after `Done` | Under 1 s | Process exit time |
| Helper size | Under 5 MB per RID | The packed file |

The timings and a list of every refusal the run hit go to the room.

## Not verified

- winget run elevated from its package folder, and the `InstalledScope` value for a machine install (Sandbox, networked).
- Whether the Sandbox's account sees a UAC prompt, so whether Decline can be tested there.
- `TokenLinkedToken` and `AccessCheck` as the "could Travis delete it" test on a split-token admin like Travis's: the Sandbox uses a separate standard user's token instead, and his live check is the first split-token run.
- The job object catching every self-relaunching uninstaller (one that breaks away from its job isn't waited for).
- `SHFileOperation` from the elevated helper filling Travis's own Recycle Bin (same account, so **inference** yes).
- Building `win-arm64` NativeAOT on this PC: the Visual Studio Build Tools here have the x64 C++ tools but not ARM64's, which NativeAOT needs to link. CI's image has both (**inference**). Installing the ARM64 component needs Travis's yes before the first local pack; until then the ARM64 helper comes from CI.

## Sources

Read 2026-11-07, besides architecture §6's.

- [winget IDL](https://github.com/microsoft/winget-cli/blob/master/src/Microsoft.Management.Deployment/PackageManager.idl) (`PackageInstallerInfo.Scope`, `ElevationRequirement`, `PackageVersionMetadataField.InstalledScope`), [winget errors](https://github.com/microsoft/winget-cli/blob/master/src/AppInstallerSharedLib/Public/AppInstallerErrors.h) (`0x8A150056`, installer prohibits elevation), [winget upgrade](https://learn.microsoft.com/en-us/windows/package-manager/winget/upgrade), [winget#4377](https://github.com/microsoft/winget-cli/issues/4377)
- [pipe security](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights), [reparse points](https://learn.microsoft.com/en-us/windows/win32/fileio/reparse-points), [DLL security](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-security), [MSRC criteria](https://www.microsoft.com/en-us/msrc/windows-security-servicing-criteria), [PowerToys admin](https://learn.microsoft.com/en-us/windows/powertoys/administrator)
