# Toast spike

Two isolated WPF-capable .NET 10 console hosts implement the integrated `IToastPresenter`. They are not referenced by Chippi or its solution. Modern pins Microsoft.WindowsAppSDK 2.5.1 locally so this experiment does not change the application's central dependencies. Classic uses Windows SDK projections and handwritten COM/shortcut interop.

`pack-spikes.ps1` publishes both routes self-contained for win-x64 and win-arm64 and uses the repository's pinned Velopack. Outputs are under `build/artifacts/toast-spike`; `sizes.csv` records bytes and SHA-256. These are internal spike packages with separate IDs, never replacements for ChippiApp.

Safe check: run a published x64 executable with `--check`. This checks XML escaping and preservation of body/button activation arguments, then exits. Running with no arguments prints help and exits. Neither path calls the registration code.

## Commands for an approved disposable test environment only

Install that route's Setup, then run its installed executable with `--register-and-show`. This writes Windows registration, shows a sample notice and waits 60 seconds for clicks. Body and buttons carry separate activation strings. Clicks are logged under `%LOCALAPPDATA%\ChippiToastSpikes\<assembly>\activation.log`, including the process ID. Close/allow the sender to exit and click a retained notification to test cold COM activation. A cold server waits 60 seconds then exits too. Launch another `--register-and-show` session for each further test.

Classic uses a separate spike shortcut, AUMID `velopack.ChippiToastClassicSpike`, and CLSID `{8F276553-88D0-4A4A-A88D-4357F687C87E}`. Modern uses AppNotificationManager's automatic identity/registration. Both must register in the real installed executable, not a temporary helper, to test cold activation properly.

The spike logs activation; it does not execute organizer actions. Production integration must decode the opaque string into bounded, validated verb arguments, dispatch to the UI thread, and call `ShellContext.HandleActivation`, which already queues while modules start. Keep COM/SDK server registration alive in the cold-launch process; forward its activation to the existing instance when there is one. Do not exit a COM-server launch before its activation callback arrives.

`--unregister` and Velopack's before-uninstall callback invoke cleanup. Normal exit only releases the live registration, leaving persisted registration for cold clicks. Uninstall must clear notification history and persisted registration. Classic removes its own CLSID and clears history; Velopack owns and removes the shortcut. Modern uses `UnregisterAll`. Do not remove shared Windows App Runtime packages. Before production integration, decide how disabling Windows notices unregisters the app without breaking another module that still uses them.

## Remaining acceptance checks

No Setup, registration, toast display, warm/cold click, or uninstall has been run on Travis's PC. Build success does not prove runtime registration. A clean approved VM must check both routes, confirm activation logs for body and buttons while warm/cold, uninstall, and compare registry/shortcut/history snapshots. Check a failed/partial registration cleanup too. ARM64 binaries have not run.

The whole WindowsAppSDK package is bundled without trimming, including components beyond notifications. Its measured size is not the minimum possible modern backend. A Foundation-only dependency can be measured later if modern registration works on the clean VM. Re-run sizes against the full integrated app after choosing a route.

## Review follow-ups

Classic registration must run on every production startup, including after an update: Velopack rewrites its shortcut and erases custom ToastActivatorCLSID properties. The spike already reapplies both shortcut properties whenever Register is called. The Sandbox matrix must include installation of 0.0.1 followed by an in-place 0.0.2 update, startup registration, then body/button clicks. `pack-spikes.ps1 -Version 0.0.2 -Runtime win-x64 -OutputRoot <separate-folder>` builds that update without replacing the 0.0.1 baseline.

Production toast-server entry handling belongs in Program.Main before App.OnStartup's single-instance exit. Register the callback, wait for its payload with a bounded timeout, decode/validate it, then use SingleInstance.TryAcquire(name, arguments). If it returns null, forwarding is complete and the helper can exit; if it returns an owner, retain that owner and start the main app without acquiring a second time. Test both an absent primary instance and an already-running primary whose own callback registration is unavailable/stale, so Windows launches a helper. Ordinary primary-window startup must not swallow or prematurely exit a toast-server launch. The standalone harness verifies callback receipt only, not this full-app handoff.

Standalone package sizes must not be subtracted from the existing Chippi installer to claim incremental backend cost: the hosts and dependencies differ. Measure integrated builds before making that comparison.
