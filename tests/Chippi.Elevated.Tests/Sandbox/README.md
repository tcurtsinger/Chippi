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

The probe is deliberately named `Chippi.exe` to exercise the production peer identity check, and refuses to run outside WDAGUtilityAccount. The existing input Sandbox launcher creates its medium-integrity token; it displays no Chippi UI. The production client starts the actual native helper through `runas`. One batch upgrades the older machine-scope 7-Zip fixture, then requests a nonexistent package. The first must succeed, the second must fail without retry, and the guest confirms the installed version advanced. `helper.json`, `results.json` and `guest.log` retain the result and batch duration. No installer text or private owner data is read.

Sandbox may not show a UAC prompt. Its success cannot establish the owner's real Yes/No consent flow; that live check remains separate. If network/bootstrap or guest elevation fails, report it as unverified rather than substituting host operations.
