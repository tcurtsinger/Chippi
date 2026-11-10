# Media tools verification

Implementation and accepted layout: [media-tools.md](media-tools.md), [Convert v1](design/reference/convert/v1.html). Checked 2026-11-10 using generated fixtures, isolated temporary files and offscreen WPF renders. These checks did not show windows, read the screen or clipboard, play/capture audio, change audio devices, or install Chippi.

## Automated evidence

`Chippi.Media.Tests` passed 42 tests in 9 seconds, including the following Media tools checks:

- Sequential batch order, duplicate paths, per-file failure and retry, unique names, source hashes and partial cleanup.
- Cancellation between jobs, cancellation/draining during inspection, and stop-on-disk-full behavior using injected codecs.
- Resize Fit/Fill/Stretch, tiny dimensions, percentage bounds, oversized results, incompatible formats, 60-second GIF refusal and 48-pixel MP4 minimum.
- Native PNG resize and JPEG transparency against the selected background; source hashes remain unchanged.
- Native JPEG EXIF orientation, private author removal and Keep metadata retention.
- Animated GIF → MP4 → resized/compressed MP4 → animated GIF, with decoded dimensions and duration checks. MP4 title removal and Keep metadata retention are verified through Windows properties.
- A generated partial-frame GIF checks its logical 96 × 96 canvas, background restoration, restore-previous disposal, orientation and frame colors after MP4 encoding.
- A fine checkerboard downsample compares mean error against the source's average for Fant and cubic. Fant's error is no greater in this fixture, so Fant remains the default. This is not a general photographic quality measurement.
- A generated batch of 100 mixed PNG/JPEG inputs at 320 × 240: all outputs decoded, source SHA-256 hashes stayed unchanged, and no private partials remained. Native mid-video cancellation also removed its partial and preserved the source hash.

`Chippi.Features.MediaTools.Tests` passed 13 tests: page/command/shortcut registration, settings write failure, unchanged-options preservation, five-file pagination from twelve files, row/control fit, custom percentage selection, six-digit background validation, applicable format options, and offscreen story rendering. The test project loads the real Chippi themes; it does not open a window.

## Visual evidence and verdict

The offscreen render test uses `CHIPPI_MEDIA_TOOLS_SHOTS=<temporary folder>` and writes dark/light pairs for eight states: empty, Convert, Resize, Compress, running, results with failure/retry, Compare, and Settings. Island pages render at 520 × 360 DIPs. Settings renders at 800 × 600. The file list includes twelve-file pagination.

The finish reviewer inspected the source and the sixteen native screenshots. The final verdict found no remaining material UX blockers: the fifth row fits, Remove is readable in light theme, and progress/cancel, results/retry, comparison and Settings remain usable. The approved options-above-list structure is retained.

The screenshots are temporary review evidence, not shipping assets. No new raster provenance is required. The native panel reuses the existing island brushes, accent button, ComboBox, TextField, Card, SettingRow and Lucide icon system; root `DESIGN.md` and the global sidecar are preserved.

## Not measured or run

- Owner interaction with the installed app and a full Windows Sandbox scenario.
- Sustained 1080p conversion speed, game/frame-time impact, a 100 × 3 MP image stress workload, DWM responsiveness, or a one-minute video benchmark. The 100-small-image fixture is a correctness check, not a performance claim.
- Native ARM64 execution and hardware encoder coverage across adapters.
- Store decoder combinations, Windows N without Media Feature Pack, and every video container/audio stream variant.
- Fant versus cubic on real photographs.

Build/CI and installer verification are reported by the feature PR and release artifact manifest; this document does not claim an unobserved hardware or live-desktop result.

Tab consistency follow-up (2026-11-10): Convert and Audio now share `IslandTabs` and `IslandAudioTabsStyle`, including the focus-independent pointer selection behavior. Convert uses the same 32-DIP header; its 296-DIP body still fits five rows. Thirteen Media tools tests and three windowless shared-tab tests passed; dark/light renders were checked again. The conversion backend is unchanged.
