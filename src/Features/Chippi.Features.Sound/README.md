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