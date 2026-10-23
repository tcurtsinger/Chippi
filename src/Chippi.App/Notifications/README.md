# Classic Windows notifications

`ClassicToastPresenter` renders the module's escaped title/body/actions. It uses the Windows SDK's classic `Windows.UI.Notifications` API, not Windows App SDK. Callbacks carry bounded, versioned argument arrays, never shell commands. The module supplies event copy and actions; pending-only notices can open review without a history run.

`ToastStartup` handles `--toast-server` before WPF's ordinary instance check. Its STA dispatcher waits at most ten seconds for a valid callback. A warm click forwards decoded arguments to the existing named-pipe owner and exits. A cold click retains its new `SingleInstance` owner for App startup, with the decoded initial arguments; App never acquires that owner twice. Additional callbacks queue until the shell's activation router is attached.

On every primary startup (and server startup), registration refreshes the Start Menu shortcut's AppUserModelID and ToastActivatorCLSID plus HKCU LocalServer32. The installed identity is `velopack.ChippiApp`; production preserves Velopack's existing shortcut target and other properties. A missing installed shortcut makes the backend unavailable. Portable builds without that installed shortcut do not create a production shortcut.

Dev profiles derive separate deterministic CLSIDs/AUMIDs and own a separate shortcut. Normal exit revokes the live factory but leaves persistent activation available for cold clicks. Test harnesses must explicitly clean up afterwards:

`Chippi.exe --cleanup-dev-notifications --dev-profile <profile>`

The cleanup command refuses a missing/invalid dev profile, so it cannot fall back to the production identity. Production uninstall clears only Chippi's notification history and COM key, never Velopack's shortcut. Dev cleanup also deletes its owned dev shortcut.

A registration failure returns no presenter. App leaves `ModuleServices.Toasts` null, so the existing Notifications page shows its amber unavailable line. A delivery failure is logged and marks the service unavailable. Payload errors are contained without failing the organizer.

Tests: run `dotnet test tests/Chippi.Notifications.Tests/Chippi.Notifications.Tests.csproj -c Release` until the lead adds it to the solution. Registry tests use unique fake HKCU roots and shortcuts in temporary folders; they do not register a real notification identity or show toasts.

The Windows SDK target adds runtime projections from Microsoft.Windows.SDK.NET.Ref 10.0.19041.57 (`Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll`). Package license metadata: https://aka.ms/WinSDKLicenseURL. Include these in the Phase 2 dependency inventory before release.