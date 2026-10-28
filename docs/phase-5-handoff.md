# Chippi Phase 5 brief: Clipboard and text

Marge, 2026-10-28. APPROVED by Travis ("Yes go ahead", 2026-10-28). Screens and copy: [design/notes.md](phase-5/design/notes.md) and DESIGN.md › Clipboard and text (approved 2026-10-28). notes.md wins on screens and copy; this brief wins on behavior. Research: [behavior-spec.md](phase-5/behavior-spec.md) (Karl) and [architecture.md](phase-5/architecture.md) (Kayla), cross-reviewed clean. Scope: [scope.md](phase-5/scope.md), where notes.md doesn't replace it.

## 1. Decisions (Travis, 2026-10-27 and 2026-10-28)

- **Phase split:** Phase 5 is Clipboard history, Paste as plain text, Clean URL, Scratchpad and Shelf. Quick toggles, Cleaning mode, Command bar, Radial menu and AI agent usage are Phase 5.5. Quick panel leaves the catalog.
- **Scope:** scope.md's In list; its Out list stays out.
- **Recording:** off until turned on (page or Settings), then kept across restarts, encrypted.
- **Uninstall:** uninstalling Clipboard history erases its saved history (asking first when there is some); Scratchpad notes and Shelf items stay and come back on reinstall.
- **Plain paste shortcut:** none by default.
- **Screens:** three tiles (Clipboard, Scratchpad, Shelf). Clean URL lives behind the Clipboard header's link button, and is the whole page when Clipboard history isn't installed. Plain paste is a shortcut only, with a beep and the tab glyph when it can't paste. No Settings pages for Scratchpad or Shelf.

## 2. Lead calls (Marge)

- **Privacy:** background capture, automatic Clean URL and clipboard-prefilled previews honor `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory = 0` and `Clipboard Viewer Ignore`, checked while Chippi holds the clipboard and before any content is read (architecture §2 and §3). Malformed markers fail closed. An explicitly requested plain paste still works on a private copy (notes #20), without recording or previewing it; temporary writes and restoration preserve its exclusions. No password guessing. Apps to skip by executable path. No clipboard content, note text, Shelf item or URL in logs, settings export, diagnostics or notices.
- **Clean URL:** the source rule set in behavior-spec §5 (global plus site rules, proper subdomains only). Automatic cleaning off by default, never on a copy marked private, one cleaned History entry, never an original-plus-cleaned pair. No network access.
- **Paste:** best effort. Elevated or unreadable targets get no keystrokes: the plain text becomes the copy (notes #19). Restore never overwrites a newer copy. Chippi never elevates.
- **Storage:** SQLite (already a dependency), each entry sealed with AES-GCM, one key wrapped with DPAPI (architecture §4). History and Shelf keep separate stores and separate 128 MiB caps. Erase checkpoints, vacuums, deletes and makes a new key; no claim of physical erasure.
- **Scratchpad preview:** the Markdown subset in notes #32, rendered by Chippi; no new library, nothing fetched.
- **Dependencies:** none new.
- **Overlap badges:** Overlaps Win+V (Clipboard history), Overlaps PowerToys (Paste as plain text), Overlaps Notepad (Scratchpad).
- **Copy:** DESIGN.md §3. Every new string is checked by the reviewer and by Marge's Decide pass before an installer.

## 3. Architecture (architecture.md §1)

- New WPF-free library `src/Chippi.Clipboard`: listener (`AddClipboardFormatListener`), privacy gate, format reader and writer, paste sender, encrypted store, with `FakeClipboard` and `FakeForeground` for tests. The real clipboard can't be isolated on Travis's PC, so behavior is tested on fakes.
- `IClipboardHub` in Core, owned by the shell and available only while a clipboard feature is installed. Its single background listener runs only for active History recording (not paused) or automatic cleaning. Manual Copy, Clean URL prefill, Shelf Copy and plain paste use on-demand operations without enabling recording or a persistent listener. No module opens the clipboard itself.
- Modules `clipboard-history`, `paste-plain-text`, `clean-url` (group Clipboard & text), `scratchpad` (Clipboard & text), `shelf` (Files). Feature projects under `src/Features/`; project names go to Marge for `Chippi.slnx` before they're added.
- Shell hooks (Marge's files; ask before changing, no implementation approval implied by this list):
  - Shelf drag over the closed tab: 250 ms, opens Shelf without focus, closes after leaving without a drop; Inbox glyph while over the tab (notes #48).
  - The island stays open while a drag is in progress: dragging an item out of the Shelf (the pointer leaves the island, which must not count as a click-away or hover-out) and a drag-in over the open page; normal closing resumes when the drag ends (notes #48, #49).
  - Plain paste's six-second ClipboardType tab glyph with accent/Caution and accessible status, without a tile dot, notice group or extra live button (notes #19).
  - Page-owned header commands for Clipboard's Clean URL, Pause/Resume and More, shared-page switching without duplicate headers, and Scratchpad's Saved/Save failed header status with the correct color (notes #8, #23, #29).
  - Page-aware initial keyboard focus and first Esc handling before both the panel's PreviewKeyDown and the global Esc closer; an empty History search falls through to close, Clean URL returns to History only when History is installed, and other pages retain their normal behavior (notes #58-61).
  - Previous external target capture before the island takes focus, safe foreground handoff and closing the island only after the paste attempt; a missing/refused target keeps the page open with Copied (notes #62).
  - A scoped modal-dialog guard for Scratchpad Export so the Save dialog's clicks/Esc do not close the island underneath it, with normal close behavior restored afterward (notes #33).
  - Add features' conditional pre-uninstall confirmation for saved Clipboard history, before the install switch calls the registry; Cancel preserves the installed module and data (notes #56).

## 4. Workstreams

| # | Work | Owner | Reviewer | Starts |
|---|---|---|---|---|
| C0 | `Chippi.Clipboard` + `IClipboardHub`: listener, privacy gate, reader/writer, paste sender, store, fakes; catalog glyphs (notes #2) and Quick panel removed | Kayla | Karl | after approval |
| C1 | Clean URL rules engine (pure, every rule and the five examples in behavior-spec §5 on fake data) | Karl | Kayla | after approval |
| C2 | Scratchpad: store, autosave and flush, tabs, preview, Export, Load and Save failures (notes #29–36) | Karl | Kayla | after C0 storage contracts (Karl takes C1 first; C2 doesn't use it) |
| C3 | Clipboard tile and page: History and Clean URL views, first use, clearing, limits, Settings › Clipboard history and Clean URL, uninstall question (notes #1–17, #23–28, #51–56) | Kayla | Karl | after C0 contracts and C1 |
| C4 | Paste as plain text: shortcut row, paste handoff (notes #62), failure beep and tab glyph (notes #18–22) | Karl | Kayla | after C0 |
| C5 | Shelf: store, page, drag in and out, drag over the tab, docked (notes #37–50) | Kayla | Karl | after C3 |
| C6 | Smoke tests on fakes: tiles, pages top/side/docked at 1280×720, Settings, keyboard (notes #58–62), confirmations | Karl | Kayla | after C2-C5 are integrated |
| Lead | slnx, integration, shell-hook approvals, Decide string pass, installers, live checks, report | Marge | Kayla | throughout |

C0 and C1 can start in parallel only after Travis approves this brief. C0 publishes the hub, store and paste contracts before dependent work starts; C2 may develop against their fakes while C0 implementation finishes. C3 then precedes C5. All shared-file changes still need Marge's approval.

## 5. Process

Same as Phase 4: branch and worktree per workstream; the other member reviews (behavior and every new string); Marge merges through a GitHub PR after all CI jobs pass, never with checks pending. Review bar: only bugs that break functionality or normal use block a merge. Scratch in `X:\code\chippi-scratch\phase-5\`. Dev runs use `CHIPPI_DEV_PROFILE`. Nothing installed and no admin on Travis's PC. **Never read, write or log Travis's real clipboard** in tests, spikes or automation; clipboard spikes run in Windows Sandbox only. Desktop checks: screenshot every monitor, confirm the foreground window first, stop background scripts by PID.

## 6. Done when

- Both win-x64 and win-arm64 build with 0 warnings; tests pass on x64; behavior-spec §10's checks that remain within the approved scope are covered on fakes; deferred features stay out.
- The tiles, pages, docked versions and Settings match the approved notes and DESIGN.md §3.
- Nothing listens to the clipboard unless History is recording (not paused) or automatic cleaning is on.
- Installer installs, updates in place and uninstalls; Travis's live checks pass, using test text he copies himself.
