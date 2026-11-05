# Input checks in Windows Sandbox

Real keyboard and mouse hooks are checked here, never on the owner's PC (docs/phase-6/architecture.md section 8). `run.ps1`
starts Windows Sandbox (networking and clipboard redirection off) with a published Chippi, a target window and a probe. The
probe sends input with `SendInput` and no Chippi marker, so Chippi takes it for a hand's, aims it at the target, and writes
what arrived.

## Run it

1. Build a self-contained Chippi into a folder of its own (the Sandbox has no .NET). `dotnet publish` needs `pwsh` for the
   soundboard tools; a self-contained build doesn't:

   ```powershell
   cmd /c "dotnet build src/Chippi.App/Chippi.App.csproj -c Release -r win-x64 --self-contained -p:OutDir=%TEMP%\chippi-sandbox\app\"
   ```

   Use `-c Debug` for the `watchdog` and `pause` checks: only Debug builds can drop their own hooks or add the probe's
   filter.
2. Run the checks. The Sandbox window comes to the front, so `run.ps1` refuses while a fullscreen app or Zoom is in front,
   and while another Sandbox runs (only one can):

   ```powershell
   powershell -ExecutionPolicy Bypass -File tests\Chippi.Input.Tests\Sandbox\run.ps1 -App $env:TEMP\chippi-sandbox\app -Out $env:TEMP\chippi-sandbox\out -Checks radial,cost
   ```

   It waits for the probe (about 2 minutes, 5 with `cost`), closes the Sandbox and prints `results.json`.

## Checks

| Check | What the probe does | What passes |
|---|---|---|
| `radial` | A dev profile with only the Radial menu (Middle and Back open it; four Text slices type `up`, `right`, `down`, `left`). A quick middle click; a middle hold, flick up and let go; a Back hold, flick right and let go; a middle drag; a middle hold with Esc | The click reaches the target down then up; the flicks type `up` and `right` and nothing of the wheel's reaches the target; the drag reaches it down then up; Esc types nothing and reaches nothing |
| `cost` | Moves the pointer in circles, up to 1,000 moves a second, for 60 s, three times, with the Radial menu's hooks in | `cost` lists Chippi's CPU milliseconds per run; compare with a build of `main` |
| `watchdog` | A Debug build drops both hooks 45 s after start (`CHIPPI_INPUT_DROP_HOOKS`); the probe moves the pointer and presses Shift for 10 s, then flicks up | The flick types `up` again, and `hubLog` has the hub putting both hooks back |
| `pause` | A Debug build adds a filter that pauses and keeps F24 (`CHIPPI_INPUT_PROBE_FILTER`). `target.ps1 -Fullscreen` is the game: a borderless window over the screen that logs to `fullscreen.log`. F24 windowed and over the game; a middle flick over the game and windowed, with Open over fullscreen apps off; F24 held while the game comes to the front. Then again with Always on, with `powershell.exe` (the target) on Pause in these apps, and with no Radial menu | Windowed, F24 reaches nothing; over the game it does, down and up. The flick over the game reaches the game and opens nothing; windowed it types `up`. The held F24's up reaches nobody. With Always on, F24 over the game reaches nothing; with the target listed, F24 reaches it. With no Radial menu, `hubLog` has the keyboard hook coming off for the game and going back in after it |

`hook-timings.csv` (from `CHIPPI_INPUT_TIMINGS`) has the hooks' time per event: count, median, p99, p99.9 and the longest,
in microseconds. A build without the hub writes none.

## Adding a check

Add it to `probe.ps1` under its own name in `checks.txt`, aim its input at the target (100,100, 800 by 600, a text box
that logs every button and key reaching it), and put what it found in `$found`. Seed the dev profile with only the
features the check needs.
