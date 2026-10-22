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

After the lead applies build/integration-proposal.md and approves root files:

```powershell
./build/pack.ps1 -Version 0.1.0
```

The script restores the pinned local tool, publishes each RID to fresh staging, runs vpk pack, and requires both Setup.exe and Portable.zip. It stops immediately on tool or packaging failure. Output: build/artifacts/releases/<version>/<RID>, including an artifacts.csv file with byte sizes and SHA-256 hashes. It does not install, sign, upload or configure an update feed. Staging stays under ignored build/artifacts; no recursive cleanup is performed.

The workflow builds both RIDs, runs every test project on x64, and publishes using each profile. It takes the SDK from global.json. It requests read-only repository access and has no secrets, release or upload step. It has not run on GitHub locally.

## Validation so far

On 2026-10-21, both self-contained profiles published successfully and Core tests passed (57). The x64 published Chippi.exe started, logged normal startup and exited 0, because this branch precedes WS1 and has no tray. This is a startup smoke check only. ARM64 is publish/build-only on the x64 machine. Setup execution would install the app and requires separate authorization.

Repo-root LICENSE/notices/tool manifest and App startup/project edits require lead approval. Installer artifacts and final sizes are pending until that integration. No claim of successful Velopack packaging yet.

Official references: https://docs.velopack.io/getting-started/csharp and https://docs.velopack.io/reference/cli/content/vpk-windows
