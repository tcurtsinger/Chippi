# UI smoke tests

Opt-in, external-process checks adapted from the Phase 1 UIA scripts. Run on an unlocked Windows desktop. The suite briefly opens Chippi windows, toggles the dev island and a dev-only startup entry, and sends Escape to the test Settings window. Do not use the keyboard/mouse during the run. No installer is invoked. Startup may register dev-profile notifications; Dispose removes that registration. The island tests briefly start a one-hour Keep awake session, then stop it. Killing the test-owned process during cleanup also releases its execution-state request. Enter and Space go only to the test profile's focused tab.

Build the app, then run from the checkout:

```powershell
dotnet build src/Chippi.App/Chippi.App.csproj -c Release -r win-x64
$env:CHIPPI_SMOKE_EXE = (Resolve-Path src/Chippi.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/Chippi.exe).Path
$env:CHIPPI_UI_SMOKE = '1'
dotnet test tests/Chippi.UiSmoke/Chippi.UiSmoke.csproj -c Release --logger 'console;verbosity=normal'
Remove-Item Env:CHIPPI_UI_SMOKE, Env:CHIPPI_SMOKE_EXE
```

Use win-arm64 on ARM hardware. CHIPPI_SMOKE_EXE may also point to a portable build's current/Chippi.exe. It must support CHIPPI_DEV_PROFILE. These checks do not install anything.

Ordinary dotnet test discovers these tests but skips them unless explicitly enabled. The project deliberately does not reference or copy App: the executable is built separately, and the same checks can exercise packaged builds. Each test owns a GUID profile, filters every top-level UIA lookup by its process ID, and cleans only its own processes, startup value and profile folders in Dispose. Tests run serially. Failed test output includes the dev log before cleanup. Polling has a 15-second deadline, rather than fixed startup sleeps.

Coverage: second launch opens flyout; startup/island UIA toggles persist real state; Add features switches, badge text and Coming later names/search; direct module sidebar navigation, Escape closes Settings, and close/reopen starts on the first feature; flyout tabs retain their selection within the process and the Settings footer opens the default page. Handler counts remain covered by the earlier in-process probe, not by these external tests. Tidy coverage: empty state; folder picker and live modes; Rules navigation; preview approval with a held file unchecked; verified Undo; held Review; failed Retry planning with current rules; cleared history stays hidden after a mode change; Retry stays invokable after row refresh. Each fixture owns its watched folders and SQLite history inside its GUID dev profile. The default session seeds an initialized organizer with no folders, so it never watches the real Downloads folder.

Phase 2.5: the Settings and flyout checks have been rewritten. Desktop execution is pending a scheduled idle window. Island checks cover the enable switch; open/close through Invoke, Enter and Space; Keep awake chips and running/off status; and Tidy folder/mode plus Organize now opening a preview without approving it. Dragging, live Windows accent changes and Escape reaching the foreground app remain manual checks in the Phase 2.5 live-check list.
