# Sound shell smoke tests

S7 exercises the real shell and Sound module contributions together, with fake external dependencies.
It is separate from `Chippi.UiSmoke`, whose opt-in desktop tests launch another Chippi process.
This project does not launch Chippi.exe or run App.OnStartup.

Run on Windows after checking that no full-screen application or presentation is in front:

```powershell
dotnet test tests/Chippi.SoundSmoke -c Release
dotnet test tests/Chippi.SoundSmoke -c Debug
```

The tests run serially on a pumped STA dispatcher with DispatcherSynchronizationContext. They use
UI Automation providers on controls owned by the test, without SendKeys, mouse input or foreground
activation. Settings and flyout windows are constructed but never shown. The island page test shows
its non-activating window at x=-20000, outside the real desktop. Check the desktop before running
it, using the team's fullscreen-check.ps1 before each local run.

Each test has a GUID dev profile under `%LOCALAPPDATA%\Chippi-dev\s7-*`, with its own settings,
module data and processing recovery marker. StopAllAsync runs before that profile is deleted.
No startup, toast or Explorer integration is registered.

Safety boundaries:

- A shared AudioService is constructed with FakeDeviceSystem and FakePipelines. Endpoint defaults,
  volume, mute, app output and processing are all in memory. No capture or playback stream opens.
- MicMuteModule gets a fake IMicHotkey. No system shortcut is registered. Its tone callback throws.
- MicProcessingModule gets a setup implementation that throws if invoked. Its descriptor's
  requirement factory is replaced by a fake, so Install Driver and Sound settings only increment
  counters. No vendor package is downloaded, unpacked or launched.
- The island receives fake media sessions. Transport commands only increment counters.

Coverage:

- All four Sound modules contribute exactly one shared flyout tab. Automation mutes the fake mics,
  the header changes, and uninstall/reinstall removes/re-adds exactly one section.
- Settings routes create the real routing (Output and Microphone), processing, App volume and mute
  pages. Automation changes call-role routing, starts a fake pipeline, changes app volume and mutes.
- Add features renders all six requirement states and sends Install Driver, Sound settings and the
  Ready switch to their fake actions or real module registry as appropriate.
- The island grid opens the real shared Microphone page (routing, processing and mute), Output,
  Volume mixer and Now Playing. Removing App volume while its page is open returns to the grid.

These checks do not prove physical keyboard/focus behavior, visible animation, actual media
transport, native shortcut registration, driver installation, real audio routing, packaged RNNoise
or App.OnStartup registration. Those remain separate live/package checks. No new fake mode is
added to the production app; its only test seam is an InternalsVisibleTo declaration.

Smoke logging: the testhost redirects the static log to its own GUID dev profile before tests load. SmokeLogTests fails before writing if that logger points at production and verifies a unique marker appears only in the test log. The fake Install Driver test also checks its message in that log. Profile logs are removed when the testhost exits. No real driver setup runs.
