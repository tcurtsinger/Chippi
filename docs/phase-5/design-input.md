# Phase 5 Clipboard and everyday tools: design input

Superseded by [design/notes.md](design/notes.md) where they differ: three tiles (Clipboard, Scratchpad, Shelf), Clean URL inside the Clipboard page, and Paste as plain text as a shortcut only.

For Travis's UI/UX agent, 2026-10-27. Final: Travis answered tasks 27 to 32.

Read [scope.md](scope.md), [behavior-spec.md](behavior-spec.md), and [architecture.md](architecture.md), then `X:\code\chippi\DESIGN.md` sections 3 and 5 and `PLAN.md` > Surfaces. The scope sheet is the approved feature set (task 29); research separates measured behavior, implementation proposals, and remaining limits. It does not approve extra screens or source-app features.

Design inside the approved Surfaces model. Mockup files go in `X:\code\chippi-scratch\phase-5\design\`. Leave `X:\code\chippi` and its main checkout untouched.

## Decided

- **Phase split:** Phase 5 covers Clipboard history, Paste as plain text, Clean URL, Scratchpad, and Shelf. Quick toggles, Cleaning mode, Command bar, Radial menu, and AI agent usage move to Phase 5.5. Quick panel leaves the catalog; Keep awake already shipped. Travis answered Split and Yes on tasks 27 and 28.
- **Where it lives:** island tiles and pages, with the same pages docked by the tray when the island is off or hidden for fullscreen. No separate quick panel, standalone Scratchpad window, cursor Shelf window, extra tray icon, or tray readout.
- **Settings:** configuration only. Clipboard history, Paste as plain text, Clean URL, and Scratchpad belong under Settings > Clipboard & text. Shelf remains in the catalog's Files group; show any Shelf configuration under Settings > Files > Shelf. Do not move it into Clipboard & text just to group the mockup boards.
- **Independent installation:** each feature can be installed alone. Manual Clean URL must have a way in that works without Clipboard history installed or recording. The approved scope gives Clean URL its own page; include that board and the History-uninstalled configuration. A History row action may be additional, never its only door.
- **Overlap:** Clipboard history keeps `Overlaps Win+V`; Paste as plain text keeps `Overlaps PowerToys`; Scratchpad gets `Overlaps Notepad` (Marge's call). No additional badge was established for Clean URL or Shelf.
- **Privacy:** honor explicit private-copy markers and an app list to skip. No password guessing. Do not draw a sensitive-text detection switch. Privacy exclusions should not expose the skipped content through a preview, tile status, search result, notice, or log.
- **Automatic Clean URL:** off by default and never rewrites a copy marked private. No network request is needed to clean a URL.
- **Paste:** best effort. For a destination running as admin, or one whose privilege level cannot be checked safely, offer the copied result with `Copied`; do not imply it was pasted. Chippi does not elevate to paste.
- **Dependencies:** no new libraries (Marge's call). This affects the build, not labels or explanation rows in the mockup.
- **Copy:** DESIGN.md section 3 applies to every screen, state, menu, confirmation, and tooltip. See the copy requirements under Wanted back.

## What Windows gives us

These are design constraints from the reviewed architecture, not new user settings.

| Capability | What the mockup must accommodate |
|---|---|
| Clipboard events | A listener responds to changes. Recording can be off or paused while the installed feature's saved content remains available. No live polling indicator. |
| Privacy markers | The service checks private/history-excluded markers before reading content, while holding the clipboard. A password copied without a marker may still be recorded. Do not promise that every password is recognized. |
| Copied types | Unicode text, copied image data, and file-path lists. Existing files are references, not backups or files Chippi moved. Rich text is saved as plain text for History. |
| Bounds | 1 MiB of actual UTF-8 per text, 16 MiB encoded image, 100 paths per copy, 128 MiB total including pins. Architecture adds a 16.6 MP decoded-image cap. Show a short unavailable/limit state where needed; never turn a fixed cap into a setting just to explain it. |
| Missing files | A referenced file can be removed, renamed, disconnected, or online-only later. Show an unavailable reference. Listing/previewing it must not trigger a cloud download. |
| Pasting into another app | Chippi can remember the previous app and try to return focus. Shortcut registration can fail because another app owns it. Show a conflict state without suggesting the other app's shortcut was taken over. |
| Paste completion | A clipboard data request does not prove the intended app inserted text. The spike covered one WPF target; its restore waited 1.5 seconds. Reader identification and late-reader behavior remain untested. Do not draw a guaranteed-success tick or `Pasted` state based on those measurements. |
| Restoring the prior copy | Restore is best effort and never replaces a newer copy. Some private app formats cannot be preserved, and a slow app can read after restoration. This limitation goes in delivery notes, not a permanent helper line. |
| Shelf drag onto the tab | A drag can open the island on the Shelf page without taking focus; the pointer can continue into the page and drop there. Show the tab's drag-over state, opening transition, and accepted drop state. |
| Shelf while docked | The tray icon cannot accept a drop or open the Shelf by drag. Once opened through the tray, the docked Shelf accepts drops. Draw that already-open state, not a tray drop target. |
| Shelf transfer | Copy-only outbound. A canceled drop keeps the item. Only an accepted drop removes an unpinned item from the Shelf; it never deletes the original source file. |
| Scratchpad | Editing needs keyboard focus inside the island/docked page. Local autosave is possible; failure retains the draft and shows a short status. A failed load must not become an empty editable note that overwrites saved notes. |
| Local storage | Content is encrypted locally, with no cloud sync. Ordinary settings export, diagnostics, and notices exclude history, notes, and Shelf contents. Clearing local data does not clear Win+V or another clipboard manager. Deletion does not promise unrecoverability from backups. |

## Also decided

### Travis's answers (2026-10-27)

| Task | Answer | Design treatment |
|---|---|---|
| 29 Scope | Yes, as in scope.md | Draw only what scope.md lists as In. |
| 30 Recording | Off until turned on, kept across restarts | Installing Clipboard history (alone or through Essentials) records nothing until the person switches recording on. Once on, history is kept across restarts, encrypted. Show off (first use), recording, paused and populated states. |
| 31 Uninstall | Clipboard history only | Uninstalling Clipboard history erases its saved history; Scratchpad notes and Shelf items stay and come back on reinstall. If the uninstall confirmation for Clipboard history needs to differ, it's a question with Cancel and the verb. |
| 32 Plain paste shortcut | None | No default shortcut. Settings shows an unassigned Shortcut control, plus assigned and conflict states. Don't reserve Ctrl+Shift+V, Ctrl+V, Win+V or a PowerToys shortcut. |

### Feature behavior

Details for drawing scope.md's states, without the research's deferred extras.

- **Clipboard history:** text, pictures, and file references. Search, preview, pin/unpin, Copy, Paste, delete, and Pause. 50 recent entries retained for 7 days; pins until unpinned. A `Clear history` action keeps pins; a separate `Erase all` includes them. Those names describe scope, not an exception to bare-verb buttons. Show distinct confirmation questions so the operations remain distinguishable with `Clear` / `Erase` action buttons. Keep saved history separate from the current system clipboard; no timer/lock/sleep clearing feature.
- **History states:** off (first use), paused, empty after clearing, search with no result, populated text/image/file entries with pins, missing file, clipboard busy, and storage/image limit. Use fabricated examples only. Show that clearing recent entries preserves pins and that privacy exclusions do not create readable secret placeholders.
- **Paste as plain text:** retain text, line breaks, and tabs; remove formatting. Draw its requested tile/page as a small daily action surface, with shortcut configuration in Settings and the `Copied` fallback. Keep this independent of History. Do not add transformation options, OCR, rewriting, or AI.
- **Clean URL:** its own manual Clean page plus automatic cleaning off by default; built-in rules and sites to skip. Show input, changed output, unchanged input, invalid input, and copied result. Preserve ordinary query fields, playback time, and URL fragments; no shortening, redirect expansion, or online checking. For a changed example, `https://youtu.be/abc?si=share&t=30` becomes `https://youtu.be/abc?t=30`. Show automatic mode and excluded sites only in Settings; the daily manual action stays on the page. No full custom parameter editor.
- **Scratchpad:** up to 12 named tabs, autosave while typing, local Markdown preview, explicit text export, and notes retained until deleted. Draw editing, tab management, preview, empty note, save failure, and failed-load states. Preview never fetches remote images or links. No retention setting or separate floating window. The File organizer's explicit Save rule is unchanged; Scratchpad autosave is its own approved catalog promise.
- **Shelf:** drag files, text, links, and image data in and out; persist entries across restarts and keep them when its page closes. Pin/unpin, Copy out only, remove after accepted outbound drop unless pinned. Draw empty, populated mixed items, selected item, pin, missing reference, accepted/canceled drag, and limits. No grouped piles, mail-app attachment promises, Share, shake-to-open, or moving files.

Scope excludes History editing, batch paste, OCR, image editing, manual reordering, unlimited history, and password guessing. None should appear as disabled or coming-soon controls in these screens. The same applies to Phase 5.5 features.

## Wanted back

Save an interactive review page, its screenshots, and notes under `X:\code\chippi-scratch\phase-5\design\`. Use invented clipboard contents, note text, paths, and images. No live clipboard capture or screenshots of Travis's private data.

- **Tiles and pages:** all five features in the existing grid and each feature's requested page, in dark island colors. Include status choices/states and no duplicate daily controls in Settings. Paste as plain text may have a minimal page; record any proposed alternative to the requested tile/page in the notes for approval.
- **Docked versions:** the same pages docked by the tray, using the existing footer instead of orbit buttons. Include Clean URL with History uninstalled, plain paste without recording enabled, and the already-open docked Shelf receiving a drop.
- **Settings:** Settings > Clipboard & text for the four clipboard/text features, in dark and light, with configuration only. Show Shelf configuration in its existing Files group if it has settings. Include the app-exclusion list, the History recording switch (50 recent and 7 days are fixed in scope.md, so no count or retention controls; propose any in the notes), automatic Clean URL and sites to skip, and shortcut unset/assigned/conflict states. Draw only settings scope.md supports; omit rows that merely explain fixed behavior.
- **Edges and fit:** show top and one side edge with the approved orbit-button arrangement, plus docked. Demonstrate a 1280x720 work area with the entire interactive footprint inside it, including buttons/footer. Show how long histories, 12 note tabs, and a full Shelf handle overflow; record the choice in notes. Do not copy Phase 4's System monitor-specific height limit as an unexplained new rule for every feature.
- **Shelf drag:** a short interaction sequence showing external drag over the closed tab, Shelf opening without activation, pointer crossing into the page, drop, and drag-out success/cancel. The tray icon is not a drop zone.
- **Keyboard:** search and editor focus, keyboard entry into pages, tab navigation, selection/pinning, shortcut conflict, and the handoff to the previous app for Paste. Preserve the shell's escape/click-away behavior; notes must explain any editor-specific behavior needing a new choice.
- **Destructive actions:** distinct clear-unpinned and erase-all confirmations, deleting a note, removing a Shelf item without deleting its source, and the Clipboard history uninstall (task 31). Copy follows the rules below.
- **Notes:** list every choice DESIGN.md does not already make: page widths/overflow, tile glyphs/statuses/order, control grouping, exact strings, keyboard details, manual Clean URL entry, and any new interaction.

Copy checklist for every board: setting labels are noun phrases in Windows terms; buttons are bare verbs; statuses/errors are short fragments; confirmations are a short question with Cancel and the verb. No page intros, explanation-only rows, consequence paragraphs, or tooltips that explain. Show state/data where it adds information. Do not put API names, encryption algorithms, Sandbox results, or implementation constraints into product copy.
