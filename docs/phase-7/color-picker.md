# Color picker: brief

Wave 1. For Travis to confirm. Decisions: [README](README.md). Research: [behavior-spec-a.md](behavior-spec-a.md) (Color picker), [architecture.md](architecture.md) section 5, [surfaces.md](surfaces.md) sections 3, 4 and 7. Shared selector and freeze: `capture-layer.md` (Kayla). Mockup: Lumen's `wave-1.html` (Color page, bar line, Settings). DESIGN.md wins where it speaks.

## Job

Point at any pixel on screen and have its color on the clipboard in the format you use, with one key and one click.

## Doors

From [surfaces.md](surfaces.md) section 3, every one:

| Door | What it does |
|---|---|
| **Capture panel: Color** | Starts Pick color. A click closes the island first, so the panel is never in the picture |
| **Capture panel: the Color page** | Opens after Pick and review color: the sampled color, its formats and the recent palette |
| **Shortcuts** | Pick color, Pick and review color (section below) |
| **Search** | Each verb as Run. No value row |
| **Radial menu** | Any verb |
| **Actions** | None |
| **Bar** | "#7C5CFF · Copied as HEX" with a swatch, 6 seconds |
| **Settings** | Capture › Color picker |

## Behavior

Both verbs use the shared selector overlay in a pick mode: the screen freezes on every display, not dimmed (a dim would change the color you read), and the pointer gets the magnifier. The sample is read from the frozen image, never the live screen, so a video frame or animation gives one stable answer. The picker's own windows are never in the frozen image, since the freeze happens before any overlay shows.

- **Magnifier.** A grid of about 11 × 11 physical pixels, each enlarged, the center pixel outlined, with the center pixel's value in the Settings copy format under it. It follows the pointer and flips away from a display edge. The shared magnifier from `capture-layer.md` is used, with the value line added; this brief doesn't build a second one.
- **Pick color.** Click or Enter copies the center pixel in the chosen copy format, closes the overlay and shows the bar line. Esc or a right click cancels and keeps the clipboard as it was. Arrow keys move the sample one pixel; Shift+arrow moves ten.
- **Pick and review color.** Same selector. A click or Enter copies nothing and opens the Color page with the sampled color. Its Copy buttons are the only things that write the clipboard.
- **Color page.** A large swatch; one row each for HEX, RGB, HSL, HSV and CMYK with a Copy button; the recent palette (up to 10 swatches, newest first, a click re-opens that color on the page and nothing else); Pick again and Clear. The palette also fills from Pick color, so one history serves both verbs.
- **Formats.** All from one sRGB 8-bit sample, written as:

  | Format | Example for #7C5CFF |
  |---|---|
  | HEX | `#7C5CFF` (upper case, always six digits) |
  | RGB | `rgb(124, 92, 255)` |
  | HSL | `hsl(252, 100%, 68%)` |
  | HSV | `hsv(252, 64%, 100%)` |
  | CMYK | `cmyk(51%, 64%, 0%, 0%)` |

  Hue is rounded to a whole degree, saturation and the rest to whole percents. HEX is the default. The spec's "and the rest" (OKLCH and others) are left out: five formats cover what the field's pickers ship by default.
- **Clipboard.** Written as text through the clipboard hub (`ClipContent.FromText`); the feature never opens the clipboard itself. Whether Clipboard history records it is the hub's normal rule for any text; a color is not a secret, so nothing special is done.
- **What the value means.** The sampled screen color, not the source asset's. Chippi says so on the Settings page ("Reads the color Windows shows on screen").
- **Storage.** The palette (up to 10 colors as HEX) and the copy format are in Chippi's settings JSON. Nothing else is kept, and no pixels leave memory.

### Edge cases

- **HDR or a color-managed display.** The frozen image is Windows' 8-bit composed version of the screen, so a value can differ from the app's own color. Said in Settings, not worked around. Whether Travis's display runs HDR is unknown (see Not verified).
- **A window that hides from capture** (`SetWindowDisplayAffinity`) reads as black or is missing. The picker shows what it got.
- **A pick on a display with a different scale.** Each overlay is at its display's DPI and coordinates are physical pixels, so the sample is the exact pixel under the pointer. Moving between 3440 × 1440 and 1920 × 1080 keeps the magnifier on the display it's on.
- **No pixel under the pointer** (a gap between displays): the magnifier hides, Enter does nothing.
- **Elevated windows.** A full-screen read includes them (inference, wave 1 spike); a standard-user Chippi may not read an elevated window's own capture. The frozen screen is a screen read, so this isn't expected to matter here.
- **Clipboard write refused.** The bar line reads "Copy failed" and the Color page, if open, stays.
- **Esc during Pick and review** returns to where Travis was; the palette is unchanged.

## Shortcuts

Slots under the starter rule (README, task 482). All unassigned.

| Verb | Slot | Notes |
|---|---|---|
| Pick color | Global | Starts the picker |
| Pick and review color | Global | Starts the picker, ends on the Color page |
| Copy HEX, Copy RGB, Copy HSL | In context | Buttons on the Color page, shown with no key. They need a color to act on, so no global slot |
| Clear color history | In context | The Clear button, with its confirm |
| Pick (click), Accept (Enter), Cancel (Esc), nudge (arrows) | In context, fixed | The overlay's own keys, never registered with `RegisterHotKey` |

Settings › Color picker shows the two slots read-only with Edit, which opens Settings › Shortcuts in that row (Travis, 2026-11-05). Shortcuts are never recorded on the feature's page.

## What we took

- **PowerToys Color Picker:** pick with one key and copy in a configured format; a small local history. Left out: its separate editor window with every format, and the live (non-frozen) picker.
- **Flameshot:** one shared picker, so the annotation color choice (Karl's `screen-capture.md`) uses this same sampler rather than a second one.
- **Snipaste and ShareX** (spec A, Lightshot comparison): the magnifier with a coordinate and value. Chippi shows the value, not the coordinate, since the coordinate isn't useful for copying a color.

## Copy

Every on-screen string, for Marge's Decide pass.

| Where | Text |
|---|---|
| Capture panel tool | Color |
| Bar line (HEX) | #7C5CFF · Copied as HEX |
| Bar line (other formats) | rgb(124, 92, 255) · Copied as RGB, and likewise HSL, HSV, CMYK |
| Bar line, write refused | Copy failed |
| Color page title | Color |
| Color page rows | HEX, RGB, HSL, HSV, CMYK, each with Copy |
| Color page buttons | Pick again, Clear |
| Palette empty | No colors yet |
| Clear confirm | Clear recent colors? with Cancel and Clear |
| Settings page title | Color picker |
| Settings: format | Copy format (HEX, RGB, HSL, HSV, CMYK) |
| Settings: palette | Recent colors, Clear |
| Settings: note | Reads the color Windows shows on screen. |
| Shortcut rows | Pick color, Pick and review color |
| Search / command labels | Pick color, Pick and review color |

## Shared code

- **New:** `Chippi.Features.ColorPicker` and `Chippi.Features.ColorPicker.Tests`, both in `Chippi.slnx` (names to Marge first). Module id `color-picker`, already in the catalog with `Capability.ScreenCapture`.
- **Core:** a pure color module (conversions and formatting, no UI), unit tested. Core is shared code, so this goes in the PR description.
- **Chippi.UI / `Chippi.Capture`:** only what `capture-layer.md` already owns: the selector's pick mode, the frozen image read and the magnifier. This brief asks the layer for: the pixel at a physical point, and a callback with the accepted point (or cancel). If the layer's API differs, this brief follows it.
- **Settings:** one page (Lumen builds it). The feature adds its two verbs through `IModuleUi.AddCommands` and its two slots through `ModuleContext.Shortcuts`, with `ShortcutEdits`.
- **Not changed:** the shortcut service, the island, App.

## Tests

- **CI, Core and feature unit tests.** Conversions with known values, including #7C5CFF as above, black, white, pure red, a gray (saturation 0, hue 0) and the 0 and 255 edges; round trips HEX ↔ RGB for all 256 values of one channel; formatting (upper case HEX, rounding of 50.5%); palette add (newest first, a repeat moves to the front, capped at 10), clear, and a corrupt saved list read as empty. The sampler against a fixture BGRA buffer (BGRA order, not RGB). Pick and Pick-and-review against a fake capture layer: Esc and right click copy nothing, Enter copies once, the clipboard hub fake gets one text write; a refused write shows "Copy failed".
- **Offscreen shots** (`CHIPPI_ISLAND_SHOTS`): the Color page filled, empty, and in light theme; the Settings page; the bar line with a swatch. Compared with Lumen's boards.
- **Sandbox, once at the end of the wave** (Windows Sandbox, clipboard redirection on only for this check, networking off): a window of known solid colors; fire Pick color; move to each; click; read the Sandbox clipboard and compare with the known HEX. Also a second display size if the Sandbox gives one, otherwise stated as not run. Never the real clipboard.

## Done means, and timings to set

Target numbers, measured later in the build; none is measured yet.

- **Key to frozen screen with the magnifier showing:** the capture layer's number (Kayla's spike), not repeated here.
- **Pointer to magnifier update:** keeps up with the pointer on the 3440-wide 165 Hz display, no skipped frame inside the movement (counted by DWM present, not `CompositionTarget.Rendering`).
- **Click to bar line:** under 100 ms warm.
- **Color page open:** under 100 ms, already filled, like Phase 6's pages.
- Screenshots of the magnifier on both displays, the bar line, the Color page dark and light, next to the mockup. The Sandbox compare above passes.

## Not verified

- Colors with HDR on, and whether Travis's OMEN runs HDR.
- Whether an elevated window reads correctly from a standard-user Chippi.
- That 11 × 11 is a good magnifier size on a 165 Hz 3440-wide display: Lumen's board and Travis's live check decide.
- Whether the magnifier's value line is legible over a busy background: the board shows it over a light and a dark swatch.
