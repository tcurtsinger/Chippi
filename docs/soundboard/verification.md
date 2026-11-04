# Soundboard verification

## Local evidence

- **151 Audio tests passed.** Fake devices, pipelines, players and clocks cover voice-only mute, shared processing/bypass ownership, rapid replacement/Stop ordering, local-only preview, route changes, virtual-endpoint mute, stream teardown failures, surround-output channel mapping and bounded WAV reads.
- **210 Sound feature tests passed.** These include isolated library/import tests, HTTP/process fakes, managed file decoding, module lifecycle tests and unhosted WPF tests. Original files survive edit/delete, canceled imports discard drafts, malformed libraries recover from backup, stale reorder preserves new arrivals, and late import results do not resurrect a closed editor.
- **16 Audio layout tests passed.** Third-tab availability, one Audio gallery entry, legacy boolean migration, remembered view and fallback after removing a feature.
- Eighteen offscreen captures cover dark/light gallery, reorder, clip editor, file/link import, preparation, missing virtual mic, empty collections and overflow. Synthetic fixture content only. No Window, HWND, desktop input or audio service is created by the capture harness.
- Scoped Soundboard gradient ink passes at least **4.642:1 in light** and **7.573:1 in dark**, sampled across 101 gradient positions. Alt+arrow tests exercise the same `Key.System` normalization used by the routed key handler.
- Independent audio and integration review findings were fixed: multichannel local output, playback state after disposal failure, module cleanup after failure and ambiguous-driver setup copy. The independent Impeccable checklist review scored its two material fixes, contrast and keyboard reordering, resolved (`ship` verdict at that scope).

The specialized Impeccable reviewer/documenter roles were unavailable. Separate agents used the fallback checklists. This is an ordinary native extension with approved mockups and an incumbent DESIGN.md; no HTML/CSS detector ran. PRODUCT.md, web build-state/spec/comp-diff artifacts and a separate quality-bar card were not available and are not claimed as verified.

## Packaging

Both native runtimes include pinned yt-dlp and Node helpers with complete upstream license texts. Helper preparation verifies official archive sizes/hashes and extracted executable sizes/hashes. The x64 helpers passed offline version/flag checks. No installer, driver or app was launched for packaging verification. See the root release manifest for the final installer hashes.

## Owner checks

Live audio, game/call integration, native dragging/focus, real website imports and ARM64 execution remain untested locally at the owner's request.

1. In Settings → Add features, install Soundboard. Open Audio → Soundboard. An existing custom installation does not automatically add new features.
2. Add a short WAV or MP3 you already have. Change its name, choose a collection, adjust Start/End and Preview. Only the local output should play during Preview. Add it, then reopen Edit to verify the saved trim.
3. Add a second sound. Click one, then the other; the second replaces the first. Stop ends only the clip. Set Send and Hear separately and check that they persist after restarting Chippi.
4. In the game or call app, select **CABLE Output** as the microphone. With VB-CABLE installed, the other end should hear both voice and clips. Mute mic should remove your voice while clips continue. Hear at zero should silence only local monitoring; Send at zero should silence only the clip sent to the virtual mic.
5. Use the pencil, drag tiles, and try Alt+arrow before Done. Switch collections, including overflow/Favorites, then reopen Audio to check its remembered tab and collection. Delete a tile and confirm the original file remains intact.
6. Paste a direct audio link, a YouTube link and a SoundCloud link you can access. Verify preparation, local review and playback after adding. Cancel an in-progress import. Unavailable or sign-in-only links should show a retryable error without adding a broken tile.
7. During playback, change the real output in Windows and switch between Devices, App volume and Soundboard. Chippi should follow the real output without taking it back. Quit from the tray during playback or import; capture, clips and helper work should stop cleanly.
