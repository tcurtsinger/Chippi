# Text and QR: brief

Wave 1. For Travis to confirm. Decisions: [README](README.md) (ZXing.Net approved, task 484). Research: [behavior-spec-a.md](behavior-spec-a.md) (Text and QR), [architecture.md](architecture.md) section 4, [surfaces.md](surfaces.md) sections 3, 4 and 7. Shared selector and freeze: `capture-layer.md` (Kayla). Mockup: Lumen's `wave-1.html` (Text page, QR code page, bar lines, Settings). DESIGN.md wins where it speaks.

## Job

Get the text, or the link in a QR code, off anything on screen without retyping it, and see exactly what was found before anything happens with it.

## Doors

From [surfaces.md](surfaces.md) section 3, every one:

| Door | What it does |
|---|---|
| **Capture panel: Text** | Starts Extract text |
| **Capture panel: QR code** | Starts Scan QR |
| **Capture bar, on a selection** | Copy text and Scan QR on the bar under a capture's selection, so a region you already drew needs no second drag |
| **Shortcuts** | Copy text from screen, Extract text, Scan QR |
| **Search** | Each verb as Run |
| **Radial menu** | Any verb |
| **Actions** | None |
| **Bar** | "Copied text · 31 words", "No text found" |
| **Settings** | Capture › Text and QR |

A click on a panel tool closes the island first, so the panel is never in the picture, then freezes the screen.

## Behavior

All three verbs use the shared selector overlay: the screen freezes, Travis drags a region (or hovers and clicks a window), and the region's pixels are cut from the frozen image. Everything below happens on this PC; nothing is sent anywhere.

### Copy text from screen

Select, recognize, and on success copy the text and show "Copied text · 31 words" for 6 seconds. With no text found, nothing is copied, the clipboard keeps what it had, and the bar shows "No text found" in Caution. This is PowerToys Text Extractor's flow.

### Extract text

Select, recognize, and open the Text page on the Capture panel. The text is editable. The page shows the language used and the word count, and has Copy all (Ctrl+C copies a selection, as in any text box), Reselect (the selector again, replacing the text only after a new region is read) and Retry (same region, same language, once more; useful after changing the language). Nothing is copied until Travis chooses Copy.

### Scan QR

Select and decode. Dragging a region decodes that region. Enter with no region, or Ctrl+A, decodes the whole display under the pointer; a code Travis can see doesn't have to be selected precisely. Then the QR code page:

- **One code:** its kind (Link, Text, Wi-Fi, Contact, Email, Phone, Message, Location, Other) and the payload in full, selectable. Copy writes the payload. Open appears only for a web link (see below).
- **Several codes:** the header switches between them ("1 of 3"); each is listed separately with the same fields. Never merged, never ordered by guess: by position, top to bottom then left to right.
- **Wi-Fi codes** show name, security and a password hidden behind Show. **Contacts** show the lines as text. Chippi doesn't join networks or add contacts.
- **No code found** and **a code that can't be read** are two states: "No QR code found" when the decoder sees nothing, and "Unreadable QR code" when it found a pattern but couldn't decode it (damaged, too small). The second suggests selecting closer.
- **Open link.** Only for payloads that parse as an absolute `http` or `https` URL. The full address shows above the button with the host emphasized; a host with non-ASCII characters or an `xn--` label gets "Check the address" next to it. Open hands the exact string to the default browser, once, on Travis's click. It never follows redirects itself and never opens on its own. Every other scheme (`file:`, `ms-settings:`, `tel:`, `sms:`, `intent:`, `smb:` and the rest), payments and shell-looking text are shown as text with Copy only. A QR code can carry any URL, so showing it first is the point.

### Capture bar buttons

On a capture's selection, Copy text and Scan QR read that same selection, markup included, without a second drag. Copy text copies the text (with its bar line) and opens the Text page; Scan QR opens the QR code page. Each page opens while the capture screen still holds the foreground, then the capture screen closes (Travis's call from his live check, 2026-11-08: Copy text there copied without a page, and closing first let the browser take the foreground and close the page).

### Recognition

- **Text: `Windows.Media.Ocr`.** Built in and local, standard user, the same engine PowerToys uses. The region's BGRA pixels become a `SoftwareBitmap` (Bgra8, premultiplied alpha). The engine is created for the chosen language, or the first of Travis's user-profile languages that has OCR installed.
- **Languages.** Only languages with the Windows OCR capability installed are offered. The picker lists them by name; the saved choice that is no longer installed falls back to the profile default and the page says which was used. If none is installed, the bar line and the page say "No OCR language installed" with where to add one in Windows Settings. Chippi installs nothing.
- **Small text.** The brief's tests compare reading at 1× and at 2× scale on fixture images (UI text at 9 to 12 pt, a screenshot of a table, dark and light themes, a code editor). The scale with the better score becomes the default; if 2× wins, regions under 40 px tall are enlarged 2× before recognition. Regions larger than `OcrEngine.MaxImageDimension` on a side (read at run time) are refused with "Text recognition failed" and the size limit named; no screen region reaches 10,000 px, so this is a guard.
- **Line breaks.** Setting: Keep lines (as recognized) or Join lines into paragraphs. Joining merges a line into the previous one unless the previous ends a sentence or the next starts with a bullet, number or indent, and never merges across a blank gap. Tables and perfect reading order are not promised, and the Settings page says so.
- **Word count.** Whitespace-separated tokens of the final text.
- **QR: ZXing.Net** (0.16.11, Apache-2.0, managed, x64 and ARM64). `BarcodeReaderGeneric` over an `RGBLuminanceSource` with `BGRA32`, QR formats only (other barcode families are an extra left out), `TryHarder` on, `AutoRotate` on, and the multi-code decode. QR generation is also left out (README).
- **Threads.** Recognition and decoding run off the UI thread on a copy of the region; the bar line or page appears when done. A second request while one runs replaces it, and the older result is dropped. Pixels are released as soon as the work is done.

### Privacy

A screenshot's text can be a password or a code. OCR text, QR payloads and the image regions are never logged, never written to disk by this feature, and never kept after the page closes. The only things saved are the OCR language and line-break settings. Copying goes through the clipboard hub like any text, so Clipboard history's own rules apply to what Travis copies (see Not verified).

## Edge cases

- **Region too small or empty.** A zero-size or sub-8-pixel drag is treated as no selection: for Copy text, Esc-like cancel; for Scan QR, the whole display on Enter, otherwise nothing.
- **Esc** at any step closes the overlay and keeps the clipboard as it was.
- **Text on a protected window** (`SetWindowDisplayAffinity`) reads as black or is missing; the result is "No text found". The secure desktop can't be read at all.
- **Clipboard write refused.** The bar line reads "Copy failed"; on the Text page the text stays on the page.
- **HDR.** The frozen image is Windows' 8-bit version of the screen; recognition runs on that. See Not verified.
- **A huge QR payload.** Shown in a scrollable box; Copy still writes it all.
- **Rotated or mirrored codes.** `AutoRotate` covers 90° turns; a mirrored code may not decode and shows "Unreadable QR code".
- **A display at 150%.** Regions are physical pixels, so a code is decoded at screen resolution, not 1.5× smaller.
- **Elevated windows** may not read from a standard-user Chippi (unverified). The screen read includes them (inference).

## Shortcuts

Slots under the starter rule (README, task 482). All unassigned.

| Verb | Slot | Notes |
|---|---|---|
| Copy text from screen | Global | Select, then copy |
| Extract text | Global | Select, then the Text page |
| Scan QR | Global | Select, then the QR code page |
| Copy extracted text, Copy QR result | In context | Copy all and Copy on the pages; need a result, so no global slot |
| Open QR link | In context | The Open button. Never a chord, so a link can't open without seeing it |
| Reselect, Retry | In context | The page buttons |
| Accept (Enter), Cancel (Esc), Ctrl+A for the whole display | In context, fixed | The overlay's own keys, never registered with `RegisterHotKey` |

Settings › Text and QR shows the three slots read-only with Edit, which opens Settings › Shortcuts in that row (Travis, 2026-11-05). Nothing is recorded on the feature's page.

## What we took

- **Snipping Tool text actions:** local OCR; editable review; Copy all or selected text. Left out: its quick redaction of emails and phone numbers, since a detector that misses something is worse than none (spec A).
- **PowerToys Text Extractor:** one key, select, copied. Language picker restricted to installed OCR languages. Chippi always says what happened with a bar line.
- **ShareX:** decode QR locally into a review. The payload is data, never launched on its own.
- **Lightshot:** the two buttons on the selection's bar (a bar button instead of a new drag).

## Copy

Every on-screen string, for Marge's Decide pass.

| Where | Text |
|---|---|
| Capture panel tools | Text, QR code |
| Bar: success | Copied text · 31 words (one word reads "1 word") |
| Bar: nothing | No text found |
| Bar: no language | No OCR language installed |
| Bar: refused write | Copy failed |
| Bar: recognition error | Text recognition failed |
| Text page title | Text |
| Text page | Language, 31 words; Copy all, Reselect, Retry |
| QR page title | QR code |
| QR page | Link, Text, Wi-Fi, Contact, Email, Phone, Message, Location, Other; 1 of 3; Copy, Open, Show, Hide; Check the address |
| QR page states | No QR code found; Unreadable QR code |
| QR bar line | QR scan failed (a decoder error, not "none found") |
| Settings page | Text and QR |
| Settings rows | OCR language (installed ones only), Line breaks (Keep lines, Join lines into paragraphs), the note "Reads text on this PC. Tables and reading order aren't kept." |
| Settings: no language | No OCR language installed. Add one in Windows Settings. |
| Shortcut rows | Copy text from screen, Extract text, Scan QR |
| Search / command labels | Copy text from screen, Extract text, Scan QR |

## Shared code

- **New:** `Chippi.Features.ScreenText` and `Chippi.Features.ScreenText.Tests`, both in `Chippi.slnx` (names to Marge first). The module id is `screen-text`, already in the catalog with `Capability.ScreenCapture`, so the project name follows it.
- **New package:** `ZXing.Net` 0.16.11 in the feature project only (task 484). Not added to Core.
- **Core:** nothing new for text; the OCR and QR engines sit behind small interfaces in the feature (`IOcr`, `IQrDecoder`) so tests use fakes.
- **Chippi.UI / `Chippi.Capture`:** only what `capture-layer.md` owns: the selector, the frozen image, cutting a region. This brief asks the layer for: the region's BGRA pixels with width, height and stride; the whole display under the pointer; and the capture screen's bar hooks for Copy text and Scan QR. If the layer's API differs, this brief follows it.
- **Capture screen (Karl's `screen-capture.md`):** adds the two bar buttons and calls this feature's two verbs with the selection. That is the only coupling.
- **Settings and pages:** Lumen builds them. The feature adds its three verbs through `IModuleUi.AddCommands` and its three slots through `ModuleContext.Shortcuts`, with `ShortcutEdits`.
- **Not changed:** the shortcut service, the island, App.

## Tests

- **CI, unit.** Line-join rules (sentence ends, bullets, indents, blank gaps, one line, empty); word count (none, one, many, punctuation, non-Latin); QR payload kinds (URL, Wi-Fi with each security type and an escaped `;`, vCard, mailto, tel, SMS, geo, plain text, empty); Open allowed only for `http` and `https`, refused for `file:`, `javascript:`, `ms-settings:`, `tel:`, a URL with no host and a malformed one; host check flags `xn--` and non-ASCII; the result order of several codes. State machine against fake `IOcr`, `IQrDecoder`, capture layer and clipboard hub: found, none found, no language, engine error, write refused, Esc, a second request replacing the first, and that no text reaches the log (a test logger asserts the fixture string never appears).
- **CI, fixtures with the real engines where the runner allows.** ZXing decodes fixture BGRA images (QR at several sizes, 90° and 180° rotation, two codes in one image, a damaged code, a QR on a busy background, no code). Fixtures are generated in the test, not downloaded. The Windows OCR engine tests accept "no OCR language installed" on the CI runner and skip the recognition assertions there; they run locally and in the Sandbox.
- **Offscreen shots** (`CHIPPI_ISLAND_SHOTS`): the Text page with a paragraph, the QR page with one and with three codes, a Wi-Fi code, an unsafe-link warning, empty and error states, the Settings page; dark and light. Compared with Lumen's boards.
- **Sandbox, once at the end of the wave** (clipboard redirection off, networking off; the clipboard is read only inside the Sandbox): a window drawing known text and a known QR code (generated by the test); fire each verb; read the Sandbox's own clipboard from inside it for the expected text and payload; confirm Open is never invoked without a click. Whether the Sandbox image has an OCR language is checked first; if it hasn't, the recognition check is said not run there. Never the real clipboard.

## Done means, and timings to set

Target numbers, measured later in the build; none is measured yet.

- **Release to bar line, Copy text:** under 600 ms for a 1000 × 300 region, warm. To be measured on fixtures; the first run (engine creation) is timed separately.
- **Scan QR on a whole 3440 × 1440 display:** under 1 s, off the UI thread, with the UI never blocked (an interaction during it responds in a frame).
- **QR and Text page open:** under 100 ms, already filled.
- Key to frozen screen: the capture layer's number.
- Screenshots of both pages in both themes, a multi-code result and an unsafe link, next to the mockup. The Sandbox compare above passes.

## Not verified

- OCR quality for small UI text; Chippi matches PowerToys, not necessarily Snipping Tool, whose engine isn't named. Travis judges it in the live check.
- Whether the Windows OCR engine, `RGBLuminanceSource` over a `BGRA32` buffer, and ZXing's multi-code decode behave as described here: from the docs and package, not run. The first spike in the build confirms the exact ZXing calls (`BarcodeReaderGeneric` with the multi-code decode, `AutoRotate`, `TryHarder`).
- Whether the Windows OCR capability for Travis's languages is installed beyond en-US, and whether the Sandbox and CI images have one.
- Whether copying OCR text should be kept out of Clipboard history. Default here: no special handling, same as any copy. Marge or Kayla may want it excluded; a one-line decision.
- Elevated windows and HDR (see above).
- The timing targets are guesses until measured.
