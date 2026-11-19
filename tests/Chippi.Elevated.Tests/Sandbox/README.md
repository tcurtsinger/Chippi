# Admin helper check

This check uses a networked, disposable Windows Sandbox. Clipboard, audio/video input and printer redirection are disabled. It installs WinGet and two versions of a throwaway 7-Zip installation only in the guest. Nothing is installed, updated, captured or elevated on the owner's PC. The window can take the foreground: run only in an owner-approved away session, with the normal fullscreen/Zoom gate.

Prepare an isolated probe folder (never point the runner at a real Chippi install):

```powershell
$env:PATH='C:\Program Files\dotnet;'+$env:PATH
dotnet publish tools/Chippi.AdminProbe/Chippi.AdminProbe.csproj -c Release -r win-x64 -o "$env:TEMP\chippi-admin-session\app"
dotnet publish src/Chippi.Elevated/Chippi.Elevated.csproj -c Release -r win-x64 -o "$env:TEMP\chippi-admin-session\helper"
Copy-Item -LiteralPath "$env:TEMP\chippi-admin-session\helper\Chippi.Elevated.exe" -Destination "$env:TEMP\chippi-admin-session\app\Chippi.Elevated.exe"
./build/admin-helper-sandbox.ps1 -App "$env:TEMP\chippi-admin-session\app" -Out "$env:TEMP\chippi-admin-session\results"
```

The runner uses the existing Windows Sandbox CLI and a UUID it owns, stopping only that UUID in `finally`. It copies readonly host inputs into real guest folders; the restricted child never executes from a redirected host folder. Results are copied back before shutdown.

The probe is deliberately named `Chippi.exe` to exercise the production peer identity check and refuses outside WDAGUtilityAccount. A LUA token with disabled privileges and medium integrity is created inside the guest. Before downloading anything, preflight requires both `TokenElevation=false` and administrator membership=false, and creates the private pipe. A connect/Done exchange then checks the actual native helper and measures connection time.

Sandbox's UAC-disabled `runas` path does not elevate this filtered token: the real helper correctly exits 3. Therefore the guest's already elevated controller launches only the fixed native helper path and a validated pipe name, with a four-launch bound. An internal launcher injection in the production client is used only by this Sandbox probe; all pipe/PID/path/account/elevation checks remain real. The normal public client constructor still uses `runas`. This test does not establish owner consent or decline.

One batch upgrades the older machine-scope 7-Zip fixture, then requests a nonexistent package. The first must succeed, the second must fail without retry, and the guest confirms the installed version advanced. `token.json`, `preflight.json`, `handshake.json`, `helper.json`, `results.json` and `guest.log` retain the results, connection time and batch duration. Client errors include helper startup stage/exit codes. No private owner data is read.

Sandbox may not show a UAC prompt. Its success cannot establish the owner's real Yes/No consent flow; that live check remains separate. If network/bootstrap or guest elevation fails, report it as unverified rather than substituting host operations.

## Port manager, Cleaner and Uninstaller

Same probe folder, with `-Tools`. The guest has no network and installs nothing:

```powershell
./build/admin-helper-sandbox.ps1 -App "$env:TEMP\chippi-admin-session\app" -Out "$env:TEMP\chippi-admin-session\tools" -Tools
```

`tools-fixture.ps1` builds throwaway things in the guest, elevated: two listeners on `127.0.0.1` (one with a window, one without), a service `ChippiFixtureSvc` on 47013, old and new files in each Cleaner folder (a held file, a junction to a folder that must survive), and five apps (an HKLM app with leftovers and a junction in its install folder, one whose uninstaller relaunches itself from Temp, one whose uninstaller Users can write, Firefox with a Thunderbird sharing the Mozilla folders, and an HKCU app). The medium-integrity probe then drives each feature's own engine with the real helper (`ToolsCheck.cs`): Free and Stop, refusals of RpcSs, a service not owning the port and System; measure and clean; one uninstall queue, then leftovers in a new session, then Dismiss. `tools-verify.ps1` reads the results elevated. `results.json` lists every failure, the timings, the Recycle Bin, the registry exports and the environment each uninstaller saw.
