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

Phase 5: ScratchpadKeyboardTests is an opt-in desktop case for Ctrl+N, Ctrl+Tab and reverse cycling, F2 with Cancel/Enter, tab-row arrows, Tab leaving the note and Esc closing the island. It installs only Scratchpad in its GUID dev profile, so no Clipboard or Clean URL feature reads the clipboard. Chords check the owning process, target focus and foreground handle before pressing keys and release modifiers in finally. This case has been compiled only, not run; execute it only in an authorized live session. Windowless layout, page and fake-clipboard cases are in Chippi.Island.Tests.

## Phase 5 Shelf live checks

These require a freshly authorized idle desktop session. They have not been executed by the C6 test author. Use an isolated dev profile, invented files in its own temporary folder, and a separate destination folder. Keep the production clipboard and profile out of the session. Do not use the full UiSmoke suite for this check.

1. Drop two invented files together onto Shelf. One item appears; both source files still exist. Repeat with invented text and a picture from a test-owned source. Drag over a child, then another child: the island stays open and the drop still succeeds.
2. Drag an unpinned file item into the test destination. Explorer offers Copy, including while Shift is held. On an accepted copy, the Shelf item leaves, the source files remain byte-for-byte unchanged and the destination files match them.
3. Cancel a drag with Esc while the test-owned source/target is verified in front. The Shelf item remains and the island hold is released. Repeat an accepted copy with the item pinned: it stays on Shelf.
4. Pass over the closed tab for less than 250 ms: it swells with Inbox but does not open. Stay over it: Shelf opens without taking the keyboard. Leave the tab and panel without dropping: it closes after 500 ms. Return during that interval: it stays open. Drop on the page: normal closing resumes. The tab itself never accepts a drop.
5. At 1280x720, open Shelf docked through the tray, drop on its page and drag a test item out. Confirm three columns remain usable with more than nine items and the scrollbar does not cover the third column.

Automated C6 coverage: real module/page/panel drop routing and hold release, top/side/docked layout, and outgoing text/file/picture payloads run without an HWND. Two `ShelfSmokeTests.Visible_*` cases exercise the injected 250/500 ms timers and drop-ending mode using manual timer ticks, synthetic routed drags and actual windows on CI. They do not invoke native OLE drag-out. The payload test does not claim to exercise DoDragDrop's Copy-only argument or accepted/canceled removal; those are steps 2 and 3 above.
