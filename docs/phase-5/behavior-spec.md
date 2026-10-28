# Phase 5 behavior research

Draft for Marge and Kayla, 2026-10-27. Research only; no build or screen approval.

## 1. Sources and scope

This covers Clipboard history, Paste as plain text, Clean URL, Scratchpad, and Shelf. Tasks 27 and 28 still decide the phase split. Source behavior below describes Vorssaint, not a decision already made for Chippi. Recommendations and questions are marked separately.

Chippi baseline: main `015d939`; [PLAN.md](../../chippi/PLAN.md), [DESIGN.md](../../chippi/DESIGN.md), and [FeatureCatalog.cs](../../chippi/src/Chippi.App/Settings/Features/FeatureCatalog.cs). Surfaces already decides that daily controls live in island pages and the same pages dock by the tray. Settings holds configuration. A separate floating Scratchpad or cursor Shelf would need an explicit exception; the source app's windows do not authorize one.

Vorssaint was inspected at commit [`746239b0220571d6694ffacae79f3eb9dcd09193`](https://github.com/vorssaint/vorssaint-utils/tree/746239b0220571d6694ffacae79f3eb9dcd09193). Relevant files are saved verbatim under `research/`; `research/vorssaint-tree.json` records the tree SHA and paths. These are reference evidence, not code to port. Source was read, not run. No clipboard API, hotkey, mouse hook, installer, or app launch was used for this research.

Primary external sources checked on 2026-10-27:

- [Vorssaint README](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/README.md), feature summary.
- [Vorssaint defaults](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Core/Defaults.swift), notably lines 1189 to 1192, 1339 to 1356, 1411 to 1414, 1769 to 1786, and 1828 to 1831.
- [Windows clipboard behavior](https://support.microsoft.com/en-gb/windows/apps/using-the-clipboard).
- [Windows registered clipboard privacy formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats).
- [PowerToys Advanced Paste](https://learn.microsoft.com/en-us/windows/powertoys/advanced-paste).
- [KeePass clipboard FAQ](https://keepass.info/help/preview_kb/faq.html) and [KeePassXC clipboard source](https://github.com/keepassxreboot/keepassxc/blob/develop/src/gui/Clipboard.cpp). The KeePassXC link is moving; its Windows marker block was inspected, not executed.
- [Microsoft Notepad session restoration announcement](https://blogs.windows.com/windows-insider/2023/08/31/new-updates-for-snipping-tool-and-notepad-for-windows-insiders/).

## 2. Catalog and overlap

| Feature | Existing Chippi contract | Group / preset | Overlap finding |
|---|---|---|---|
| Clipboard history | Searchable saved copies with pins | Clipboard & text; Essentials and Power user | Keep `Overlaps Win+V`. Windows already provides history and pins. PowerToys Advanced Paste can also select history. |
| Paste as plain text | Paste without source formatting | Clipboard & text; Essentials and Power user | Keep `Overlaps PowerToys`. Advanced Paste has a direct plain-text action. Some apps also provide their own formatting-free paste; that is app-specific. |
| Clean URL | Remove tracking parameters from copied links | Clipboard & text; Essentials and Power user | No equivalent dedicated automatic cleaner was established from the official Windows/PowerToys material checked. Keep the badge unset; this is a scoped finding, not proof that no related tool exists. |
| Scratchpad | Notes saved while typing | Clipboard & text; Power user | Partial Windows overlap: Notepad has tabs and restores unsaved session text. Recommend `Overlaps Notepad`, pending Marge's badge decision. |
| Shelf | Hold dragged files and text between apps | Files; Power user | Windows clipboard can transfer content, but a persistent drag staging shelf was not established in the checked inbox/PowerToys docs. Keep the badge unset. |

Windows history has 25 entries and a 4 MB per-item limit. It accepts text, HTML, and bitmap formats, and preserves pins when ordinary history is cleared or the PC restarts. Chippi should coexist with Win+V, not toggle its setting or import its database. [Windows behavior](https://support.microsoft.com/en-gb/windows/apps/using-the-clipboard)

PowerToys documents `Win+Shift+V` for its window and `Ctrl+Win+Alt+V` for direct plain text. Plain text conversion is local and needs no AI provider. Chippi should not take those shortcuts, Win+V, or Ctrl+V by default. [Advanced Paste](https://learn.microsoft.com/en-us/windows/powertoys/advanced-paste)

## 3. Clipboard history

### Observed source behavior

The reference captures text, copied images, and lists of file paths. Files take precedence over their fallback text, followed by images, then text. Recopying matching text, image hash, or file-path list promotes an existing entry rather than making another copy. Pins survive promotion. Users can search, preview, copy, paste, pin/unpin, delete, edit saved text, and select multiple entries. Image search uses metadata; OCR is a separate capability, not an automatic consequence of recording an image.

Recent entries default to 50. Choices are 20, 50, 100, 250, 500, 1,000, 10,000, or unlimited. Retention choices are 1, 7, 30, 90, 365 days, or no expiry; no expiry is the default. Pins are outside the recent-count and age limits, but still subject to storage bounds. Unpinning gives an entry a fresh recent timestamp. `Clear recent` removes only unpinned entries included in the confirmation snapshot, preserving a new copy that arrives while confirmation is open.

Images and files are included by default. Capture bounds are 100 file paths per copy, 16 MiB per stored PNG, and 64 MiB for raw TIFF input before conversion. Text is capped at 1,000,000 characters, aggregate saved UTF-8 text at 64 MiB, and encoded history metadata at 96 MiB. The metadata limit does not include separately stored image payloads; it is not a total image-store budget.

Saved history reloads across app/PC restarts. It uses a local JSON history file and separate image files. File history stores paths, not backup copies of the user's files. A remembered file can subsequently disappear. Source history capture is initially off; images/files are on once capture is enabled. Sensitive-looking text detection is off, ignored-app list empty, and quick preview off. Source privacy docs explicitly acknowledge that unmarked passwords can be saved.

Evidence: [history service](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/Clipboard/ClipboardHistoryService.swift) (`readPasteboard`, capture bounds, promotion, `clearRecent`, `load`); [support](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/Clipboard/ClipboardHistorySupport.swift) (`ClipboardHistoryEditing`, `retainedEntries`, search); [privacy](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/docs/PRIVACY.md).

### Proposed Chippi behavior, pending decisions

- Start recording only after the person enables it. Essentials installing the module should not silently opt into durable recording.
- Show recent entries and pins on its island/docked page. Search text, file names, and image metadata locally. Use Copy as the reliable action; Paste targets the previous app only when the target can be safely recovered. Kayla owns the platform reliability findings.
- Use 50 recent entries and 7-day expiry as the proposed first-run defaults. Keep pins until explicitly removed. Persist encrypted local history across restarts if Travis approves it. Both retention and persistence differ from Windows history.
- Include text, images, and file references, matching the source types. File references must never copy, move, delete, or hydrate files merely to record history. A missing file remains an unavailable reference until removed, rather than silently changing the list of files being pasted.
- Provisional bounds for Kayla to validate: 1 MiB UTF-8 per text entry, 16 MiB per encoded image with a separate decoded-pixel cap, 100 paths per file entry, and 128 MiB aggregate stored payload. These are Chippi proposals, not source defaults. Pins count toward the byte cap; reject a new pin/capture when pins fill the budget instead of silently evicting a pin. Actual SQLite overhead and decoded-image limits belong in architecture.md.
- Expose a pause for future capture that preserves existing history. Uninstalling/stopping the module stops capture and hotkeys. Whether uninstall also erases saved content needs a decision because Chippi normally restores settings on reinstall.
- Editing saved entries, batch paste, OCR, image editing, arbitrary reordering, and unlimited history are source extras requiring explicit Phase 5 scope calls. They are not implied by the catalog's search-and-pin promise.

## 4. Paste as plain text

The source is a direct shortcut action. It prefers a destination's native matching-style paste where available, otherwise strips to text and temporarily replaces the clipboard for a synthetic paste, then restores the original rich content. It rejects file/media copies that only have incidental text, and can derive text from RTF/HTML when no plain string exists. The README promises preservation of the original clipboard.

Evidence: [PastePlainService.swift](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/QuickTools/PastePlainService.swift). The macOS Accessibility route is not a Windows implementation plan.

Proposed Chippi contract: retain characters, spaces, tabs, and line breaks while dropping formatting. No trimming, URL cleaning, translation, spelling fixes, or AI. Unsupported image/file-only copies leave the clipboard unchanged. A busy clipboard or unsupported/elevated destination produces a short failure status and a Copy fallback where applicable, not an attempt to elevate Chippi. Clipboard restoration must not overwrite a newer copy. An arbitrary destination may read the clipboard late, so restoration cannot be promised for every Windows app until Kayla's spike supports it.

This action needs no history recording. Its temporary writes must be excluded from Chippi history and Windows history/cloud where possible, preserving the original privacy markers when restoring content. No automatic copy of a text selection is proposed. Shortcut choice remains open; registration conflicts must leave the existing shortcut owner working.

## 5. Clean URL

### Observed source rules

The reference accepts one complete HTTP or HTTPS URL, trimming surrounding whitespace and rejecting internal whitespace or another scheme. It removes matching query pairs, matches decoded parameter names case-insensitively, and preserves surviving query spelling/order and the fragment. It does not fetch a URL or follow a short link.

Global names: every `utm_` prefix, plus `fbclid`, `gclid`, `dclid`, `gbraid`, `wbraid`, `msclkid`, `yclid`, `mc_cid`, `mc_eid`, `igshid`, `twclid`, `ttclid`, `li_fat_id`, `mkt_tok`, `_hsenc`, `_hsmi`, `__twitter_impression`, `fb_action_ids`, `fb_action_types`, `fb_source`, and `mibextid`.

Site rules also match proper subdomains, not lookalike suffixes:

| Host | Additional removed names |
|---|---|
| youtube.com, youtu.be | si, pp, feature, kw |
| twitter.com, x.com | s, t, cn, src, refsrc, ref_src, ref_url |
| instagram.com | igsh |
| spotify.com | si |
| reddit.com | correlation_id, ref_campaign, ref_source, rdt, share_id, _branch_match_id, $deep_link, $3p, $original_url |
| tiktok.com | u_code, preview_pb, _d, _t, _r, timestamp, user_id, share_app_name, share_iid |
| bilibili.com | spm_id_from, from_spmid, from_source, share_source, share_from, share_medium, share_plat, share_tag, share_session_id, msource, refer_from, seid, unique_k, vd_source, plat_id, buvid, bbid, up_id, is_story_h5, timestamp, ts, visit_id, session_id, broadcast_type, is_room_feed |
| xiaohongshu.com | xhsshare, author_share, xsec_source, share_from_user_hidden, shareredid, share_id, exsource, app_version, app_platform, apptime, appuid |

Rules support adding names globally/by host and disabling built-ins. The automatic mode defaults off. It only rewrites a single suitable link copy when something was removed; it leaves files, pictures, confidential/transient data, and richer unrelated HTML content alone. The source has more detailed rich-HTML checks than a text-only clipboard test.

Evidence: [URLCleaning.swift](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Core/URLCleaning.swift), [URLCleanerService.swift](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/URLCleanerService.swift).

### Proposed Chippi contract

Manual Clean and Copy on a page, plus optional automatic cleaning, off by default. Use the pinned source rule set above as behavior reference. Preserve unrelated names, path, fragment, parameter order, duplicate surviving parameters, and byte spelling. Keep a YouTube `t=30` playback time, although the `t` on x.com is removable. `si` remains on an unrelated host. Do not strip all queries, all `ref` names, authentication tokens, or URL fragments by guesswork.

Examples for fake-data tests:

- `https://example.org/p?utm_source=a&item=7#part` becomes `https://example.org/p?item=7#part`.
- `https://youtu.be/abc?si=share&t=30` becomes `https://youtu.be/abc?t=30`.
- `https://example.org/?si=keep&ref=keep` is unchanged.
- `https://notyoutube.com/?si=keep` is unchanged.
- `https://example.org/?%75tm_source=a&x=a%2Fb` becomes `https://example.org/?x=a%2Fb`.

Any query rewrite can invalidate a signed URL, even when its removed field looks like a tracker. This is a reason to keep automatic mode opt-in and provide host exclusions; no rule set can promise every cleaned URL still works. No network validation, redirect expansion, telemetry, or URL logging is proposed. Mixed file/image/unknown rich payloads should be left alone automatically. Manual cleaning works on text intentionally supplied to the page.

The history/cleaner interaction needs one rule before implementation. Recommendation: one cleaned entry for an automatically rewritten copy, never an original-plus-cleaned pair. History being paused/off must not enable it just to service Clean URL. Do not record an excluded item before cleaning it or remove its exclusion markers.

## 6. Scratchpad

The source has named tabs (maximum 12, names up to 40 characters), typing with an approximately 800 ms save debounce, tab management, Markdown preview, and text export. It restores saved notes after restart. Default retention is never; options clear text idle for 1, 7, or 30 days when the pad opens. That age is since the last edit, not a clipboard-copy date. A failed load does not authorize saving an empty document over the old file; failed saves retain edits in memory and expose failure.

Evidence: [ScratchpadService.swift](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/QuickTools/ScratchpadService.swift), [ScratchpadSupport.swift](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/QuickTools/ScratchpadSupport.swift), [ScratchpadStore.swift](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/QuickTools/ScratchpadStore.swift).

Recommendation: preserve named tabs, local Markdown preview, and explicit text export in the initial scope so the mockup reflects the reference. Default to retaining notes indefinitely and reopening the selected tab. Autosave without a Save button is specifically the catalog's Scratchpad behavior; it does not change the File organizer rule editor's explicit Save requirement. Never fetch remote images or links just to preview Markdown. On save failure, keep the visible draft and show a short status. Page close, feature stop, and app exit need a flush attempt. Crash recovery is limited to the last completed save, not every keystroke.

A standalone floating pad is not part of the approved Surfaces model. Design island/docked editing unless Travis explicitly changes that. Retention controls and how many tabs ship are open decisions. Export is deliberate user content, so it is separate from ordinary settings export.

## 7. Shelf

The reference is a staging area for files, images, text, links, and grouped piles. It supports drag in/out, selection, previews, pinning, and sharing. A shortcut or mid-drag shake can summon it. Shelf entries survive relaunch/update. Existing files are references; generated image/GIF payloads live in app-owned storage. Removing a shelf entry deletes only app-owned payloads, never the original dragged file.

Its default removes an unpinned item after an accepted outbound drop, keeps the Shelf open, and keeps entries when the Shelf closes. Pinned items are protected from automatic removal. The source can offer a move operation outside the app when removal-after-drop is enabled; retained/pinned entries offer copy-only to avoid stale saved references. A canceled drag is not a successful drop. This behavior must not be confused with merely removing a tile.

Evidence: [ShelfService.swift](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/Shelf/ShelfService.swift), [ShelfSupport.swift](https://github.com/vorssaint/vorssaint-utils/blob/746239b0220571d6694ffacae79f3eb9dcd09193/Sources/Vorssaint/Services/Shelf/ShelfSupport.swift), and the defaults in section 1.

Chippi proposal: island/docked Shelf with drag in/out for file references, text, links, and bounded copied image payloads. Keep entries across restarts, keep on close, remove unpinned entries after accepted outbound copy, protect pins, and offer copy-only outbound in Phase 5. This intentionally narrows the source's move behavior and needs approval. Missing references should remain visibly unavailable; dropping an item must not download an online-only cloud file just to preview it.

Shake-to-open, cursor windows, shell sharing, virtual/promised files from mail apps, and nested piles need explicit scope calls. None follows automatically from a basic WPF file-drop handler. Kayla owns supported Windows drag formats and island opening during a drag. Clearing Shelf and clearing history are separate actions.

## 8. Privacy and clearing

The following are proposed baseline requirements for Kayla/Marge to confirm, independent of a text-guessing option:

1. Check clipboard format names and exclusion markers before reading any payload, thumbnail, preview, search text, or log content. Any `ExcludeClipboardContentFromMonitorProcessing` format excludes the whole copy. A serialized DWORD 0 for `CanIncludeInClipboardHistory` excludes it from Chippi history. A DWORD 1 does not override an exclusion marker. Malformed privacy metadata should fail closed for automatic recording/rewriting. [Microsoft format contract](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats)
2. `CanUploadToCloudClipboard=0` concerns device synchronization, not local history consent. Chippi proposes no synchronization. Preserve it when writing/restoring data rather than mislabeling it as a local-history denial.
3. Honor the legacy registered `Clipboard Viewer Ignore` marker too. KeePass documents this convention; KeePassXC's Windows implementation uses Microsoft's exclusion/history/cloud formats. Password managers do not all mark every copy path. No promise to recognize every password is supported. [KeePass FAQ](https://keepass.info/help/preview_kb/faq.html), [KeePassXC source](https://github.com/keepassxreboot/keepassxc/blob/develop/src/gui/Clipboard.cpp)
4. The source app uses macOS ConcealedType, TransientType, and AutoGeneratedType conventions. They explain its behavior, not the names a Windows port should blindly substitute. A Chippi-owned marker should suppress its own temporary conversion/restore writes. Sensitive-text heuristics are optional; they can both miss secrets and hide ordinary code. App exclusions can supplement markers but cannot reliably identify every browser/password-manager copy.
5. Chippi content remains local. Exclude history payloads, notes, and Shelf contents from ordinary settings export, diagnostics, and notices. Include content in a user export only after a separate deliberate action. Kayla's architecture decides encryption/storage; encrypted-at-rest does not protect data from every process running as the same user.
6. Separate commands: clear unpinned history, erase all Chippi history including pins, clear the current system clipboard, clear Shelf, and delete Scratchpad notes. Clearing one does not silently clear another. A current-clipboard clear or delayed restore must check that the clipboard has not changed since the relevant action.
7. A history-clear operation must remove its app-owned image payloads and prevent an in-flight capture from repopulating erased entries. A still-current clipboard item must not be immediately rerecorded just because history was cleared. Do not claim secure physical erasure of SSD/database/backups. Deleting Chippi data does not delete Windows Win+V history or another clipboard manager's copy.
8. Automatic current-clipboard clearing on a timer, lock, or sleep is a separate source utility. It defaults off there and keeps saved history. Recommend keeping it out of Phase 5 unless requested; honoring a password manager's own clear must never restore its expired secret.

## 9. Decisions needed before screen design

Marge owns putting decisions on Travis's Tasks list. These are research recommendations, not additional questions sent directly to Travis.

| ID | Decision | Recommendation |
|---|---|---|
| B1 | Recording consent, restart persistence, retention | Recording off until enabled; encrypted local persistence once enabled; 50 recent / 7 days; pins explicit and persistent. Confirm Essentials behavior. |
| B2 | History data and capacity | Text, images, file references; bounded bytes/pixels/paths; no unlimited mode initially. Kayla validates the provisional bounds in section 3. |
| B3 | Sensitive-looking text and app exclusions | Always honor explicit markers. Optional heuristic off by default, plus an app-exclusion list, with no guarantee that all passwords are caught. |
| B4 | Clear and uninstall semantics | Clear unpinned plus a separate erase-all action; current clipboard separate. Ask whether feature uninstall keeps or erases saved history/notes/Shelf. Settings alone normally survive reinstall. |
| B5 | History extras | Search, preview, pin, Copy/Paste, and delete initially. Explicit call on editing entries, batch paste, OCR, image editing, and manual reordering. |
| B6 | Plain paste shortcut and reliability contract | Customizable, initially unassigned to avoid stealing Windows/PowerToys shortcuts. Preserve source clipboard when safe; show failure/Copy fallback for unsupported targets. Await Kayla's spike before a universal-paste promise. |
| B7 | URL modes/rules | Manual plus auto off by default; source global/site rules; host exclusions. Decide whether the full custom rule editor ships now. One cleaned history entry for auto mode. |
| B8 | Scratchpad breadth | Named tabs up to 12, local Markdown preview, text export, autosave, no expiry. Island/docked only. Confirm whether retention choices ship now. |
| B9 | Shelf breadth and file semantics | Persist entries; keep on close; pins; remove after successful drop; copy-only outbound. Explicit call on piles, virtual files, sharing, shake activation, and any Surfaces exception. |
| B10 | Overlap badges | Keep Win+V and PowerToys badges; add Notepad for Scratchpad. No new badge established for Clean URL or Shelf. |

Tasks 27 and 28 determine the surrounding roadmap. They should not be treated as approvals of the defaults above. Hotkeys, tile/page grouping, dimensions, interaction, and final copy should be settled in the design input with the UI/UX agent. Use DESIGN.md section 3: noun-phrase settings, bare verbs, short statuses, question/Cancel/verb confirmations, and no helper lines.

## 10. Checks the eventual brief should require

Use fake/test clipboard data only on Travis's PC. Never enumerate or save his actual clipboard during research or automation.

- Every exclusion marker on its own, conflicting flags, malformed values, ignored source, and a source that fails to mark a secret. Explicit marker skips happen before payload access.
- Text/image/file duplicates, count/age/byte limits, pins at capacity, expiry across restart, and source file deletion without user-file deletion by Chippi.
- History clear while capture is pending, delete while preview is open, failure during save, missing/corrupt persisted data, and no plaintext payload in logs/settings export.
- Plain paste preserves Unicode and line breaks; unsupported formats, busy source, new clipboard copy during restore, delayed target reads, and elevated/changed targets. No unwanted input to another window.
- Every global and site URL rule, encoded names, repeated query parameters, signed URL caveat, no-op preserving rich data, image/file fallback text, sensitive markers, and no history loop or duplicate after rewrite.
- Scratchpad tabs, debounce/flush, failed load/save, restart, preview without network, and text export.
- Shelf canceled and accepted drag, copy-only source files, pin retention, missing/cloud files, restart, owned-payload cleanup, island top/side/docked surfaces, and no accidental source-file deletion.

Review state: behavior draft ready for Kayla; architecture cross-review pending. No code, app settings, clipboard data, or production checkout files changed.