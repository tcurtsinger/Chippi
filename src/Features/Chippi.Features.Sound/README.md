<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
# Sound modules

Audio routing lives in Routing/. SoundAudio.Get(logger) supplies the single
process-wide AudioService to all Sound modules. Its events use the app dispatcher
even when ModuleRegistry starts a module from a background continuation.
Each module acquires and disposes its own service lease.

AudioRoutingModule accepts an AudioService for fake-device tests and an optional
transport-endpoint exclusion predicate for S3's verified device identities.
WinningMicrophone resolves the routing list independently of the OS default,
including while Processing owns the capture roles.
S3 must wire its verified transport predicate into app composition when it lands;
S2 deliberately does not identify VB-CABLE by its display name.

Settings save immediately. RoutingList retains seeding and removed-device state,
repairs only unambiguous IDs, and appends new active endpoints.
Quick selection promotes a saved device to first priority. A disabled flow
releases its claims without changing Windows' current selection.

Settings, flyout and island views use S0 contributions. Tests link the unchanged
AudioFakes.cs. Offscreen review screenshots and a parity coverage map are under
X:/code/chippi-scratch/phase-3/s2/.
## Mic mute shortcut

`Mute/MicMuteModule` takes an audio lease and a message-only `RegisterHotKey` registration (Ctrl+Shift+M by default). All microphone writes run on the shared engine through `MicrophoneMute`; there is no keyboard hook or separate audio enumerator. The latch includes VB-CABLE capture endpoints, mutes newly active mics before processing rebuilds, and releases only mute changes it owns. Mixed, unavailable and absent microphones remain visible states. Shortcut collisions keep manual Mute usable. Windows chords and F12 are never registered. Shortcut creation, registration and disposal use the application dispatcher even when the registry starts a module on a worker thread.

The settings page, Sound section and shared Microphone page use `MicMuteView`. `Sound feedback` defaults off and uses Windows system sounds only after a successful explicit toggle when enabled. Tests inject fake registration and tone actions, so no real shortcut, sound or audio setting is touched. A running latch is session-owned; it cannot restore an unplugged mic after the final audio lease or recover an app crash by itself.
