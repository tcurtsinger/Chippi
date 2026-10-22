# UI smoke tests

Opt-in, external-process checks adapted from the Phase 1 UIA scripts. Run on an unlocked Windows desktop. The suite briefly opens Chippi windows, toggles the dev island and a dev-only startup entry, and sends Escape to the test Settings window. Do not use the keyboard/mouse during the run. No installer or toast registration is invoked. Keep awake is opened but never activated.

Build the app, then run from the checkout:

```powershell
dotnet build src/Chippi.App/Chippi.App.csproj -c Release -r win-x64
$env:CHIPPI_SMOKE_EXE = (Resolve-Path src/Chippi.App/bin/Release/net10.0-windows/win-x64/Chippi.exe).Path
$env:CHIPPI_UI_SMOKE = '1'
dotnet test tests/Chippi.UiSmoke/Chippi.UiSmoke.csproj -c Release --logger 'console;verbosity=normal'
Remove-Item Env:CHIPPI_UI_SMOKE, Env:CHIPPI_SMOKE_EXE
```

Use win-arm64 on ARM hardware. CHIPPI_SMOKE_EXE may also point to a portable build's current/Chippi.exe. It must support CHIPPI_DEV_PROFILE. These checks do not install anything.

Ordinary dotnet test discovers these tests but skips them unless explicitly enabled. The project deliberately does not reference or copy App: the executable is built separately, and the same checks can exercise packaged builds. Each test owns a GUID profile, filters every top-level UIA lookup by its process ID, and cleans only its own processes, startup value and profile folders in Dispose. Tests run serially. Failed test output includes the dev log before cleanup. Polling has a 15-second deadline, rather than fixed startup sleeps.

Coverage: second launch opens flyout; startup/island UIA toggles persist real state; hub card/description, badge text, Coming soon and Invoke accessibility; Back, real Escape, sidebar and window close/reopen. Handler counts remain covered by the earlier in-process probe, not by these external tests. Tidy flows wait for p2-views integration.
