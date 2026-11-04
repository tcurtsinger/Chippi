# Soundboard verification

## Harden, optimize and polish — 2026-11-04

The approved B collection-tabs design is preserved. The finish pass adds safeguards for simultaneous views, pending saves, import shutdown, preview ownership, deleted sounds during reorder, interrupted trim gestures and retryable setup failures. Library recovery handles a missing primary file, and oversized writes preserve the previous readable library and backup. Names use complete Unicode text elements; time fields accept locale decimals and keep untouched trim precision.

- **272 Sound feature tests and 151 Audio tests passed in Release.** All local audio uses fakes; UI tests construct unhosted controls. Gated tasks exercise cancellation and concurrent saves/deletes without timing sleeps or real input.
- **26 offscreen captures** cover both themes at 520 × 360 DIP, rendered at 200% scale. In addition to gallery/edit/import/empty/overflow/setup states, these include voice warnings, Unicode titles, long collection names, invalid trims and link-import failure with Retry. The harness explicitly flushes the unhosted visual tree so the full Audio header renders consistently.
- The native composition remains unchanged; clearer bounded errors, collection ellipsis/tooltips, keyboard focus targets and precise time fields are the visual refinements. No global tokens changed.

The opt-in `SoundboardGalleryTimings` probe measures creation, layout and one offscreen render on the UI thread. These are medians of three bounded samples after one small warmup, using fixture clips and no native window or audio. Allocations are cumulative UI-thread allocations for that operation, not retained process memory.

| Library / operation | Before | After |
| --- | ---: | ---: |
| Open/render 12 sounds | 17.182 ms | 15.488 ms |
| Open/render 1,000 sounds | 1,142.611 ms | 23.897 ms |
| UI allocation, 1,000 sounds | 121.45 MB | 3.94 MB |
| Realized tiles, 1,000 sounds | 1,000 | 24 |
| Playback update, 1,000 sounds | 0.0309 ms | 0.0016 ms |

The improvement comes from creating nearby tile controls, retaining scroll position and caching the playing sound lookup. Every clip remains reachable through scrolling and keyboard navigation. Tests also cover reordering beyond the first viewport. These measurements do not prove native presentation frame rate; live dragging, focus, screen-reader navigation and playback remain owner checks.

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

The 0.1.0 x64 and ARM64 packages were built from source `33e6f14c03cd397f7d09b8c356b289cafed8b390`. All twelve release artifact sizes and SHA-256 hashes were verified, along with both app/setup PE architectures, embedded source versions, all four archive CRCs, exact license contents, pinned VB-CABLE ZIP/setup, both RNNoise architectures, and each runtime's native yt-dlp/Node binaries. Verified copies are under `X:/code/chippi/build/artifacts/releases/0.1.0`. x64 Setup is 147,058,124 bytes; ARM64 Setup is 135,382,268 bytes. The publish hook contributes helpers before single-file bundling and copy classification so each helper and license keeps its own relative path.

## Owner checks

Live audio, game/call integration, native dragging/focus, real website imports and ARM64 execution remain untested locally at the owner's request.

1. In Settings → Add features, install Soundboard. Open Audio → Soundboard. An existing custom installation does not automatically add new features.
2. Add a short WAV or MP3 you already have. Change its name, choose a collection, adjust Start/End and Preview. Only the local output should play during Preview. Add it, then reopen Edit to verify the saved trim.
3. Add a second sound. Click one, then the other; the second replaces the first. Stop ends only the clip. Set Send and Hear separately and check that they persist after restarting Chippi.
4. In the game or call app, select **CABLE Output** as the microphone. With VB-CABLE installed, the other end should hear both voice and clips. Mute mic should remove your voice while clips continue. Hear at zero should silence only local monitoring; Send at zero should silence only the clip sent to the virtual mic.
5. Use the pencil, drag tiles, and try Alt+arrow before Done. Switch collections, including overflow/Favorites, then reopen Audio to check its remembered tab and collection. Delete a tile and confirm the original file remains intact.
6. Paste a direct audio link, a YouTube link and a SoundCloud link you can access. Verify preparation, local review and playback after adding. Cancel an in-progress import. Unavailable or sign-in-only links should show a retryable error without adding a broken tile.
7. During playback, change the real output in Windows and switch between Devices, App volume and Soundboard. Chippi should follow the real output without taking it back. Quit from the tray during playback or import; capture, clips and helper work should stop cleanly.
