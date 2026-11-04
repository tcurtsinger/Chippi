# Audio recovery verification — 2026-11-03

The report combined two failures: Audio stayed on App volume when Devices was clicked during a game, and selecting earbuds in Windows reverted to speakers until Chippi closed.

## Evidence and corrections

- Installed logs show Render Console and Multimedia routing repairs immediately after Windows Sound was opened. Output routing previously interpreted every external default change as drift. It now honors a new active output per role until render devices connect or disconnect, an explicit output routing action in Chippi, or restart. Saved priority order is unchanged. Microphone changes and label refreshes preserve the choice. Capture routing and Mic processing still enforce their intended roles.
- The island recorded its first activation attempt as a permanent keyboard handoff for that opening. WPF ListBoxItem consumes a mouse press without selecting when Focus() fails. An unhosted control reproduces this consumed click; an actual game-specific foreground denial was not captured. Audio tabs now select on a deliberate pointer press independently of keyboard focus. The panel retries activation on a later press when it is no longer foreground, preserves the original paste target, and does not run delayed focus changes after another app takes foreground.

## Verification boundaries

Routing tests use fake endpoints, memory settings, fake hotkeys and a fake notifier. Keyboard handoff tests use fake foreground/activation callbacks; tab tests use unhosted controls. No test in this local pass opens a window, sends desktop input, queries or changes live audio devices, or installs or launches Chippi. Existing tests that show windows are reserved for CI.

Local results: 123 Audio tests, all 153 Sound feature tests, five keyboard handoff tests, three Audio pointer tests, and nine Audio layout tests passed. Independent code reviews of routing and tab handling found no blocking defects.

The remaining installed check belongs to the user: during a game, open Audio on App volume, switch to Devices, choose earbuds, then try a different output in Windows Sound. Each choice should hold while the connected output set remains unchanged. Reconnecting an output should resume priority routing. The actual fullscreen-game interaction is not claimed as locally verified.
