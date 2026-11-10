# Media tools

Wave 2 implementation, 2026-11-10. Travis selected [Convert v1: options above the list](design/reference/convert/v1.html) and authorized the build: “I like the options above the list. Let's use that layout. Go ahead and build”. The native WPF implementation follows the existing Chippi system in `DESIGN.md`. Verification and remaining hardware checks are recorded in [media-tools-verification.md](media-tools-verification.md).

Related decisions: [README](README.md), [surfaces.md](surfaces.md) section 4, [architecture.md](architecture.md) section 3, and the shared `Chippi.Media` library introduced by [Screen recording](screen-recording.md).

## Job

Convert, resize and compress batches of images and videos locally, preserving the originals. Files are neither uploaded nor played automatically.

## Direction contract

**THESIS:** One compact batch workbench keeps the active options above the files, so the owner can check a whole batch before Run.

**OWN-WORLD:** Inherit Chippi's neutral dark and light surfaces, Segoe text, existing violet accent, rounded native controls, and Lucide icons. Settings uses the existing Card and SettingRow components.

**STORY:** Add or drop files, choose the operation and its settings, check row refusals, then Run. Progress stays with each file; finished images offer Compare and exports offer Open.

**FIRST VIEWPORT:** A 520 × 360 island page places the title and Convert/Resize/Compress controls in the header, options above a five-row list, the destination below, and Add files left with Run or Cancel right. Larger batches page in groups of five.

**FORM:** The first supplied layout, `convert/v1.html`, was explicitly selected. No seed round applies to this precise existing reference; seed: none (unavailable).

**FINISH:** unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance.

## Doors

| Door | Behavior |
|---|---|
| Capture › Convert | Opens the page on its last selected tab while Media tools is installed; remains available during recording |
| Page header | Switches Convert, Resize, Compress |
| Search / Radial menu | Shared Convert media, Resize images, Compress media commands |
| Global shortcuts | The same three commands, initially unassigned; open the selected tab and a file picker |
| Activity puck | Batch count while running; completion count, failures or bytes saved afterward |
| Settings › Capture › Media tools | Output folder, Keep metadata, Default level, JPEG background, and three read-only shortcuts with Edit |

There is no separate home tile, Actions entry, or Explorer context menu. Media tools does not require screen-capture permission merely to process files.

## Operations

| Tab | Images | Video |
|---|---|---|
| Convert | PNG or JPEG; animated GIF can also become MP4 | MP4/H.264, or GIF for clips up to 60 seconds |
| Resize | PNG at a percentage or width/height; Fit, centered Fill crop, or Stretch | MP4 at a percentage or target height, preserving aspect ratio |
| Compress | JPEG with Small, Balanced or High quality | MP4 with the same dimensions and a lower target bitrate |

- Defaults are JPEG, Balanced, white JPEG background, Keep metadata off, and output beside the originals. Resize starts at 50% with Fit.
- Image size presets are 50%, 75%, 100%, 150%, 200%, 1920 × 1080, 1280 × 720 and 640 × 480, plus custom dimensions. The Scale field accepts 1–400%; dimensions accept 1–16384 pixels. Video uses the preset/custom height and retains its own aspect ratio.
- PNG shows Lossless instead of an inactive quality choice. GIF export uses the existing 15 fps exporter. Quality applies to JPEG and MP4.
- Windows' installed WIC decoders determine recognized image extensions. Video is checked by attempting a Media Foundation decode. Store codecs remain dependent on the PC; no codec package, ffmpeg, or other dependency is added.
- Output formats are PNG, JPEG, MP4 and GIF. WebP/AVIF/HEIF output, WebM and extra transcode controls remain out of scope.

## Page behavior

- Add files or drop paths onto the empty page or existing list. Duplicate paths appear once. The list shows source name, size, format and state; full paths and errors have tooltips.
- Five files fit on a page; Previous and Next appear for larger batches. Remove drops a row from the queue without deleting its source or export.
- Run processes files sequentially away from the UI thread. Running rows show percentages; completed rows show output bytes and percentage change. A smaller export is not guaranteed for every input.
- Cancel interrupts the current export, removes its private partial, retains completed exports, and marks remaining waiting rows cancelled. Retry runs an individual failed row. Disk full stops the batch; other row failures allow later files to continue.
- Compare shows an original/export image pair without playback; Back returns to the queue. Open launches an export only after an explicit click.
- Options and file editing are disabled during a run. Changing options prepares retained source rows for another run. Settings persists options and shortcuts, never the file list.

## File and encoder rules

- Originals and existing output names are never overwritten. An export is written to a private `.chippi-<id>` partial beside its destination, then published through a non-overwriting move. Collisions receive `photo (2).png`, `photo (3).png`, and so on.
- Output goes beside its source unless an Output folder is configured. A missing source fails its row. An unwritable destination reports an access/file error.
- An input over 4 GiB or an image over 100 megapixels is refused. Resizes also reject target or intermediate images over 100 megapixels. MP4 requires at least 48 × 48 pixels; dimensions are rounded down to even values for H.264.
- Image EXIF orientation is applied to pixels before resize/crop. JPEG transparency is flattened against the visible six-digit background color, white by default. PNG retains transparency. Fant interpolation is retained after a synthetic fine-detail comparison against cubic.
- New image exports start without private source metadata. MP4 exports explicitly clear known private Windows shell fields because MediaTranscoder otherwise carries them forward. Keep metadata copies supported author/title/copyright/date/camera/location fields where the destination supports them; it is not a byte-for-byte metadata archive.
- Animated GIF to PNG/JPEG uses its first frame and says First frame before Run. GIF to MP4 preserves frame timing, logical canvas, transparency/background composition and disposal behavior.
- MP4 encoding prefers hardware and retries with software when preparation or encoding fails. GIF output reuses recording's bounded 60-second exporter. Unsupported codecs or encoding failures stay on their row.
- Stopping the module cancels and drains active inspection, export and GIF sample requests before disposing its UI and shortcut registrations.

## Shared code

`Chippi.Features.MediaTools` and `Chippi.Features.MediaTools.Tests` are in `Chippi.slnx`. Module id is `media-tools`; its result page is hidden from the home gallery. App registration and `CapturePanelHost` provide the Convert entry. Search and Radial use shared commands; shortcuts use `ModuleContext.Shortcuts` and the central editor. Settings uses the existing components, while media inspection/export/batch code lives in `Chippi.Media`. No new packages or shipping raster assets are introduced.

The finish review accepted the final options-above-list implementation after fixes to fifth-row fit, light-theme Remove icons, custom percentages, inactive quality options, GIF composition and encoder fallback. Root `DESIGN.md` and its global sidecar remain unchanged because this is an extension of the incumbent system.
