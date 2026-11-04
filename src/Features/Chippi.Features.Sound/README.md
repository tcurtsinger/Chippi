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

`Mute/MicMuteModule` takes an audio lease and a message-only `RegisterHotKey` registration (Ctrl+Shift+M by default). All microphone writes run on the shared engine through `MicrophoneMute`; there is no keyboard hook or separate audio enumerator. The latch excludes VB-CABLE endpoints so muting voice leaves soundboard clips playable, mutes newly active physical mics before processing rebuilds, and releases only mute changes it owns. Mixed, unavailable and absent microphones remain visible states. Shortcut collisions keep manual Mute usable. Windows chords and F12 are never registered. Shortcut creation, registration and disposal use the application dispatcher even when the registry starts a module on a worker thread.

The settings page, Sound section and shared Microphone page use `MicMuteView`. `Sound feedback` defaults off and uses Windows system sounds only after a successful explicit toggle when enabled. Tests inject fake registration and tone actions, so no real shortcut, sound or audio setting is touched. A running latch is session-owned; it cannot restore an unplugged mic after the final audio lease or recover an app crash by itself.

## Soundboard

Install **Soundboard** from Add features (included in Audio and Power user presets), then open **Audio → Soundboard**. Existing custom installations keep their chosen features. Select **CABLE Output** as the microphone in the game or call. Soundboard's setup page can install the bundled VB-CABLE driver only after an explicit Install click; importing and local preview work before setup.

Collection tabs contain a four-column gallery. Add accepts files or direct audio, YouTube and SoundCloud links; each sound gets a local copy, optional cover, favorite flag and trim range. Pencil editing supports drag reorder and keyboard move controls. Collections must be empty before deletion. The footer's Send and Hear levels affect clips independently. Stop stops the clip only. Mute mic mutes voice while sounds continue.

`SoundboardModule` owns one player and serializes library edits across widget and Settings. Playback intent bypasses the metadata semaphore so Stop is not delayed by copying a large import. `SoundboardStore` writes versioned JSON atomically with a backup and only deletes validated, app-owned media paths. `SoundboardImporter` prepares canonical 48 kHz stereo PCM16 WAVs in cancellable temporary drafts, bounded to 250 MB of source media and 30 minutes. Original media is never changed. Cover thumbnails are bounded to 256 × 256 with source pixel limits.

Site imports use pinned native `yt-dlp` and Node helpers under `vendor/soundboard`; no global tool installation, shell, user config, plugins, account cookies, playlists, remote runtime components or FFmpeg. Both helpers are size/SHA-256 verified before use. Unsupported or unavailable links remain errors, with file import offered as a fallback. Tests fake HTTP/processes and audio playback; live website imports and game/call playback require owner testing.
