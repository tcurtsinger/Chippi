# Media tools: brief

Wave 2. Owner: Herb. For Travis to confirm. Decisions: [README](README.md) (task 478 scope, task 484 libraries, task 486 call 4: Convert is a page on the Capture panel, not a tile). Research: [behavior-spec-a.md](behavior-spec-a.md) (Media tools), [architecture.md](architecture.md) section 3 (Encoders), [surfaces.md](surfaces.md) section 4 (Convert). Shared encoder code: [screen-recording.md](screen-recording.md) (`Chippi.Media`). Mockup: Lumen's Convert page in `wave-1.html` for the layout; she adds the Resize and Compress states and Settings before this is built. DESIGN.md wins where it speaks.

## Job

Convert, resize and compress images and short videos on the PC, in a batch, without leaving the originals changed and without uploading anything.

## Doors

| Door | What it does |
|---|---|
| **Capture panel: Convert** | Opens the Convert page. Convert stays usable while a recording runs |
| **Convert page** | Header switches Convert, Resize, Compress. Files, two option dropdowns, Add files, Run |
| **Shortcuts** | Convert media, Resize images, Compress media: each opens the page on that tab with a file picker (below) |
| **Search / Radial menu** | Each verb as Run |
| **Actions** | None |
| **Bar** | "3 of 4 converted" while a batch runs; "Converted 4 · 12.4 MB saved" for 6 seconds after |
| **Settings** | Capture › Media tools |

Dropping files on the page adds them. There is no Explorer menu (README, left for later).

## Behavior

### What it does

| Tab | Images | Video |
|---|---|---|
| **Convert** | To PNG or JPEG | To MP4 (H.264), or to GIF from a clip |
| **Resize** | A width, a height, or a percent, with Fit, Fill (crop) or Stretch | A target height: 1080p, 720p, 480p |
| **Compress** | JPEG quality (a preset: Small, Balanced, High) | A lower bitrate with the same size (the same three presets) |

Inputs are what the PC can decode: images through WIC (PNG, JPEG, BMP, GIF, TIFF, and HEIF, AVIF or WebP where a decoder is installed), video through Media Foundation (MP4 and MOV, HEVC, VP9 and AV1 where the Store extension is present). Chippi lists what the PC can decode by asking Windows (`CodecQuery`, as the spike did) rather than a fixed list. Outputs are the first-build contract: PNG, JPEG, MP4 and GIF. WebP, AVIF and HEIF output, WebM, AVI and extra codecs are left out (README task 478; architecture section 3). No ffmpeg: if a job here needs something Windows can't do, this brief lists it in Not verified and Marge offers it to Travis; none is expected.

### The page

- **Empty:** a drop zone with Add files.
- **With files:** a list with the name, current size and format, the estimated output size where known, and a status per file. More than five files page like Actions lists. A file's row has Remove.
- **Options:** two dropdowns that change with the tab, for example Convert shows Format and Quality, Resize shows Size and Fit. Run is the verb. A summary line states the destination.
- **Run:** processes files in order. A file's row shows progress; after it, the actual bytes and the saving. Cancel stops between files and removes only the partial file of the one running. Retry per failed row.
- **Nothing plays on its own.** A before and after comparison (side by side, one image at a time) is on a row's Compare for images.

### Rules

- **Originals are never overwritten.** Output goes beside the original, or to Settings' Output folder when one is set. A name that exists gets a number (`photo (2).png`).
- **Aspect ratio is kept** unless Stretch is chosen. Never enlarge unless the size dropdown is set above the original and the row says "Enlarge".
- **Transparency to JPEG:** a background color is asked for once in the options (white by default), shown before Run.
- **Metadata:** private metadata (location, camera serial, owner) is removed from new exports by default; Settings has Keep metadata. Orientation is applied to the pixels first, so a rotated phone photo comes out upright.
- **Unsupported input** is refused before Run, on its row, with the format named. A file that fails to decode mid-batch fails that row only; the batch goes on and the summary counts it.
- **Large files:** a video over 4 GB or an image over 100 megapixels is refused with "Too large". Several files run one at a time, so the batch's memory is flat.
- **Encoder:** H.264 through `MediaTranscoder` with a hardware encoder when present, the software one otherwise, the same code as recording's trim. Resize interpolation is Fant, compared with high-quality cubic on a fixture set (architecture section 3); the better one wins and the brief's PR says which.
- **Windows N edition** without the Media Feature Pack: video rows read "Video encoding unavailable"; images still work.
- **Storage:** presets and the last tab are in Chippi's settings JSON. No file list is kept between runs.

### Edge cases

- **A source file on a removed drive or deleted mid-batch:** that row fails with "File not found".
- **Output folder unwritable or disk full:** the row fails, the partial file is removed, and the batch stops after the first disk-full failure with "Disk full".
- **Same file added twice:** shown once.
- **Animated GIF input:** converted to PNG or JPEG gives its first frame, and the row says so; to MP4 it keeps the animation.
- **Cancel during a video:** the partial output is removed.

## Shortcuts

Slots under the starter rule (README, task 482). All unassigned. A global slot never acts on a stale selection: each opens the page and a file picker.

| Verb | Slot | Notes |
|---|---|---|
| Convert media, Resize images, Compress media | Global | Opens the page on that tab with the file picker |
| Add media files, Run media jobs, Cancel media job, Retry media job, Open media output | In context | Buttons on the page |

Settings › Media tools shows the three slots read-only with Edit, which opens Settings › Shortcuts in that row.

## What we took

- **PowerToys Image Resizer:** batch inputs and reusable size presets, originals kept. Left out: its Explorer right-click entry.
- **HandBrake queue:** named presets, per-file progress and per-file failure. Left out: its full transcode settings.
- **Squoosh:** before and after size and an image comparison, all local. Left out: formats Windows can't encode.

## Copy

For Marge's Decide pass.

| Where | Text |
|---|---|
| Panel tool | Convert |
| Page tabs | Convert, Resize, Compress |
| Empty | Drop files here, with Add files |
| Buttons | Add files, Run, Cancel, Retry, Remove, Compare |
| Convert options | Format (PNG, JPEG, MP4, GIF), Quality |
| Resize options | Size, Fit (Fit, Fill, Stretch) |
| Compress options | Level (Small, Balanced, High) |
| Row states | Waiting, Converting, Done · 2.1 MB → 640 KB, Failed, Cancelled |
| Refusals | Unsupported format; Too large; File not found; Disk full; Video encoding unavailable |
| Bar | 3 of 4 converted; Converted 4 · 12.4 MB saved |
| Settings title | Media tools |
| Settings rows | Output folder, Keep metadata, Default level, JPEG background, Shortcuts |

## Shared code

- **New:** `Chippi.Features.MediaTools` and `Chippi.Features.MediaTools.Tests`, both in `Chippi.slnx`, using the `Chippi.Media` library (encoder pipeline, GIF path, codec query) that screen-recording.md introduces. Module id `media-tools`, `Capability.ScreenCapture` for the panel (the catalog entry is added with the module). Names to Marge first.
- **Build order:** recording's `Chippi.Media` lands first; Media tools builds on it, so the two PRs are not stacked: Media tools starts after the recording PR merges.
- **Shell:** the Convert tool appears on the Capture panel only while this module is installed (screen-capture.md already provides the slot). Shared code touched is the panel's list and the bar's ambient state, called out in the PR.
- **Settings:** one page (Lumen). Slots through `ModuleContext.Shortcuts` with `ShortcutEdits`.
- **Package:** none new.

## Tests

- **CI, unit.** The job queue against fake codecs: order, cancel between files, a failed file not stopping the batch, retry, unique names (`(2)`, `(3)`), originals untouched (hash before and after), refusal of unsupported, too-large and missing files. Resize math: Fit, Fill and Stretch for landscape and portrait, never enlarging by default, 1 px edge cases. Orientation applied before the resize. Transparency to JPEG on the chosen background. Metadata removed from output and kept when the setting says so. Codec query against a fake source.
- **Real encoders, CI where the runner has them:** a fixture PNG, JPEG, GIF and a 2-second MP4 converted both ways and decoded again, checking dimensions and a known pixel. Tests accept a missing hardware encoder and use the software one.
- **Offscreen shots** (`CHIPPI_ISLAND_SHOTS`): the Convert page empty, with five and with twelve files, running, with a failed row, Resize and Compress tabs, Settings; dark and light.
- **Sandbox, once at the end of the wave.** A batch of 100 disposable images of mixed formats and sizes and three videos, never the owner's files: run each tab, check every output by decoding it (dimensions, format, pixels, orientation, no metadata), originals' hashes unchanged, cancel mid-batch, a locked output folder. Sandbox only.

## Done means, and timings to set

Targets, none measured yet.

- **Page open:** under 100 ms, already filled.
- **Batch of 100 images** (about 3 MP each) to JPEG: total time and per-file median reported; the page stays responsive (no UI-thread decode), counted by DWM present while the batch runs.
- **Video:** a 1-minute 1080p clip to MP4 at 720p and compressed: time, output size, and the ratio to real time.
- **Proof:** the Sandbox results above, shots dark and light next to Lumen's mockup, and the timings.

## Not verified

- Which Store decoders Travis's PC has beyond what the spike read (HEVC, VP9, AV1, HEIF were present).
- Fant against cubic on real photographs: decided on fixtures in the PR.
- ARM64 encoders and the hardware paths on his adapters.
- Whether any job needs ffmpeg. None is expected; this is for Marge to raise with Travis only if the build finds one.
