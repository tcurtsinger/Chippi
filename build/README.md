# Build and packaging (WS5)

The SDK is selected by global.json. Use Windows and put dotnet on PATH.

```powershell
dotnet build src\Chippi.App\Chippi.App.csproj -c Release -r win-x64
dotnet build src\Chippi.App\Chippi.App.csproj -c Release -r win-arm64
dotnet test tests\Chippi.Core.Tests -c Release
dotnet publish src\Chippi.App\Chippi.App.csproj -p:PublishProfile=win-x64
dotnet publish src\Chippi.App\Chippi.App.csproj -p:PublishProfile=win-arm64
```

Both profiles are self-contained and single-file, with native libraries extracted by .NET and trimming disabled for WPF. Output: build/artifacts/publish/<RID>. No architecture-specific PlatformTarget is introduced.

The tracker approves the integration in build/integration-proposal.md. The repo-local vpk tool is pinned to 1.2.161. Its manifest enables roll-forward because this host has only .NET 10 while vpk targets .NET 8. It does not install another runtime.

```powershell
./build/pack.ps1 -Version 0.1.0
```

The script restores the pinned local tool, publishes each RID to fresh staging, runs vpk pack, and requires both Setup.exe and Portable.zip. It stops immediately on tool or packaging failure. Output: build/artifacts/releases/<version>/<RID>, including an artifacts.csv file with byte sizes and SHA-256 hashes. It does not install, sign, upload or configure an update feed. Staging stays under ignored build/artifacts; no recursive cleanup is performed.

The workflow builds both RIDs, runs every test project on x64, and publishes using each profile. It takes the SDK from global.json. It requests read-only repository access and has no secrets, release or upload step. It has not run on GitHub locally.

## Validation (2026-10-21)

- App Release builds for win-x64 and win-arm64: zero warnings/errors. Use the App project with `-r`; a solution-level RID build is unsupported.
- Core tests: 57 passed. The workflow discovers all test projects, including Keep awake once its branch merges. GitHub-hosted execution has not run yet.
- `build/pack.ps1 -Version 0.1.0` completes for both RIDs with the pinned local tool. Artifact manifests contain file sizes and SHA-256 hashes, excluding themselves.
- x64 Setup.exe `--help`, the extracted portable launcher, and `current/Chippi.exe` launch and exit 0 under `CHIPPI_DEV_PROFILE=ws5`. No installation was performed. App startup reaches the expected `No tray composed yet; exiting` in this pre-WS1 worktree.
- ARM64 Setup and portable executables cannot launch on this x64 Windows host. They require an ARM64 machine; no ARM64 runtime success is claimed.
- Portable archives include LICENSE and THIRD-PARTY-NOTICES.md beside the application, with byte-identical copies verified after extraction. Font OFLs are also present.
- Velopack warns that the approved bootstrap lives in OnStartup rather than Main, but its application check passes. No skipVeloAppCheck is used. Unsigned-output warnings are expected; code signing is out of scope.
- No feed URL, update manager, telemetry, upload, signing, startup entry, or administrator execution is configured. Package build tooling can restore dependencies; that is separate from the app's offline runtime behavior.

Artifacts (bytes):

| Runtime | Setup.exe | Portable.zip | Full nupkg |
|---|---:|---:|---:|
| win-x64 | 72,265,527 | 64,750,453 | 64,751,415 |
| win-arm64 | 65,685,287 | 59,776,356 | 59,777,319 |

These artifacts package the WS5 worktree before the other workstreams merge. Repack after integration; this is packaging/startup evidence, not a finished Phase 1 application.

Official references: https://docs.velopack.io/getting-started/csharp and https://docs.velopack.io/reference/cli/content/vpk-windows

## Install directory separation (2026-10-22)

The Velopack package ID is ChippiApp, so the default install directory is %LOCALAPPDATA%\ChippiApp. The Windows display title remains Chippi. Logs remain at %LOCALAPPDATA%\Chippi\logs. New artifacts use the ChippiApp- prefix; older Chippi- artifacts do not contain this fix. Keep the install directory separate from logs because Velopack prompts to repair any nonempty target folder. This package-ID change does not migrate older Chippi installs; uninstall the old package before using the new one.
