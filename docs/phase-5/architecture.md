# Phase 5 architecture research: Clipboard and text

Kayla, 2026-10-27. Research draft for Karl's review, read with his [behavior-spec.md](behavior-spec.md). Covers Clipboard history, Paste as plain text, Clean URL, Scratchpad and Shelf. Nothing here is a decision; section 9 lists the calls it feeds.

Baseline: main `015d939`. Spikes: `spike\` (C#, .NET 10, standard user), raw output in `spike\*.log` and `sandbox-out\`. Nothing was installed and nothing ran elevated on Travis's PC. Travis's clipboard was never read or written: the clipboard and input spikes ran only inside Windows Sandbox with clipboard redirection and networking off (`sandbox\spike.wsb`), and the spike refuses those modes outside the sandbox. Travis's real clipboard sequence counter (a number, not content) was the same before and after the host runs.

## 1. Shape

One shared service in Core, five thin modules on top.

- **`Chippi.Clipboard`** (new library, WPF-free like `Chippi.Rules`): the clipboard listener, privacy gate, format reader and writer, the paste sender, and the encrypted store. Fakes for all of it (`FakeClipboard`, `FakeForeground`), because tests can't own a private clipboard: a standard user can't create a window station on Windows 11 (`CreateWindowStation` returns Access denied, measured), and the session clipboard is Travis's.
- **`IClipboardHub`** in Core, owned by the shell, started only while at least one clipboard module is installed. One listener for the whole app, so Clipboard history and Clean URL never race each other for `OpenClipboard`.
- Modules: `clipboard-history`, `paste-plain-text`, `clean-url` (Clipboard & text group), `scratchpad`, `shelf` (Files group for Shelf, as the catalog has it). Each uses the hub and the store; none opens the clipboard itself.

Pages follow the Surfaces model: each feature that has a daily surface gets an island tile and page (History, Scratchpad, Shelf), docked by the tray while the island is off. Paste as plain text has no page; its control is a shortcut and Settings rows. Clean URL needs a manual Clean that works without Clipboard history installed or recording (auto-clean is off by default, so otherwise it would do nothing). Options for the design input: its own shortcut that cleans the current copy in place; a Clean action on the island grid's tile menu; or a small Clean URL page. When History is installed, its rows can offer Clean too. This is a surface call for Travis and his UI/UX agent (9.8), not a Settings row.

## 2. Clipboard events

**Listener, not polling.** `AddClipboardFormatListener` on a message-only window on the UI thread (the same pattern as `GlobalMicHotkey`). Windows posts `WM_CLIPBOARDUPDATE` after every change; with nothing copied it costs nothing (0 updates and no wake-ups in 10 s idle, `sandbox-out\listener.log`). Polling `GetClipboardSequenceNumber` would add a timer for no gain and still need the same read path.

Measured in Sandbox (420 writes from another thread, 50 ms apart): every write produced exactly one update; the listener saw it 0.10 ms after the writer closed the clipboard (p99 0.22 ms); the whole handler, including reading and hashing, cost 0.37 ms of CPU per update. Reading a 1 KB text held the clipboard open 0.05 ms (p99 16 ms, when it had to wait for the writer); a 1,000,000-character text, 1.9 ms (max 3.2 ms).

Per update:
1. Read `GetClipboardSequenceNumber`. Skip if it's the number our own last write produced (Clean URL's rewrite, plain paste, restores, History's Copy), so Chippi never records itself.
2. Early exit without opening the clipboard: `IsClipboardFormatAvailable` for the markers in section 3. This only saves work; it isn't the gate, because the copy can change before step 4 opens the clipboard.
3. Debounce 150 ms: apps often write a copy in several steps (empty, text, then HTML); read once the sequence number stops moving.
4. `OpenClipboard` with up to 10 retries 10 ms apart. **The gate runs here, while holding the clipboard**: read the sequence number again and check every marker in section 3 (including the `CanIncludeInClipboardHistory` DWORD) before asking for any payload format. Then read the chosen formats and check the sequence number and owner once more before `CloseClipboard` (in a `finally`), since a delayed-render read can run code in the source app. If anything moved, drop the read and wait for the next update. Hold the clipboard as briefly as possible: copy bytes out, decode later.
5. Hand the snapshot to the consumers (History, Clean URL) on a worker only after that check passed; never encode PNG or touch SQLite while the clipboard is open.

Fakes cover the interleavings: a marked copy replacing an unmarked one between the update and `OpenClipboard`, and a change during a delayed-render read.

Formats read, in this order (files win over their fallback text, then images, then text, as Karl's spec has it): `CF_HDROP` (paths only, at most 100), `CF_DIBV5`/`CF_DIB`/`PNG` (size checked before reading, section 4), `CF_UNICODETEXT` under two limits: a raw read limit of 4 MiB on `GlobalSize` (the UTF-16 allocation, which may include padding), then, after decoding up to the first null, Karl's 1 MiB cap on the actual UTF-8 byte count. 600,000 ASCII characters (1.2 MB of UTF-16, 600 KB of UTF-8) pass; 400,000 CJK characters (800 KB of UTF-16, 1.2 MB of UTF-8) don't. HTML and RTF are not stored in Phase 5; a rich copy is stored as its plain text, and History says so by having no rich flag. Never request formats we won't store: with delayed rendering, asking for a format makes the source app produce it (all 60 delayed-render copies in the spike were rendered because the listener asked for their text).

## 3. Excluded content

Checked in this order, all before any payload read:

| Marker (registered format) | Meaning | Chippi |
|---|---|---|
| `ExcludeClipboardContentFromMonitorProcessing` | Don't monitor this copy at all | Skip everything: no history, no Clean URL, no preview. Presence alone counts, whatever its data |
| `Clipboard Viewer Ignore` | Legacy viewer opt-out (KeePass and others) | Same as above |
| `CanIncludeInClipboardHistory` = DWORD 0 | Keep out of clipboard history | No history entry. Clean URL may still run, since it's not history; recommended off for these too (fail closed), see 9.3 |
| `CanIncludeInClipboardHistory` = DWORD 1 | Allowed | Doesn't override either marker above |
| `CanUploadToCloudClipboard` | Cloud sync only | Ignored for local history; preserved when Chippi restores a copy |

Reading the DWORD needs the clipboard open; it's 4 bytes and no payload. A malformed value (not 4 bytes) counts as 0.

Spike result (`sandbox-out\listener.log`), 60 copies of each kind:

| Copy | Recorded | Skipped, reason |
|---|---|---|
| Plain text | 60 | 0 |
| With `ExcludeClipboardContentFromMonitorProcessing` | 0 | 59, exclude (1 not matched to its write by sequence number) |
| `CanIncludeInClipboardHistory` = 0 | 0 | 60, history-0 |
| `CanIncludeInClipboardHistory` = 1 | 60 | 0 |
| With `Clipboard Viewer Ignore` | 0 | 58, viewer-ignore (2 not matched) |
| Delayed rendering | 60 | 0 |
| 1,000,000 characters | 60 | 0 |

Chippi's own writes carry `ExcludeClipboardContentFromMonitorProcessing` (plain paste's temporary text) or are recognised by sequence number (Clean URL's rewrite, which is meant to stay as the copy). Restores put back the original's formats and markers.

What markers can't do: a password manager that writes plain text without a marker is recorded. Section 9.3 offers an app list (by the clipboard owner's process, `GetClipboardOwner` → `GetWindowThreadProcessId` → image name) as a supplement. It's best effort: many apps copy through a helper window or a browser process.

## 4. Storage

**One SQLite file per feature** under `ModuleContext.DataDirectory` (`%LOCALAPPDATA%\Chippi\data\<id>`), Microsoft.Data.Sqlite (already in the repo for File organizer), WAL, `synchronous = NORMAL`, one connection on the module's worker, like `SqliteHistoryStore`.

**Encryption at rest: DPAPI wraps one key, AES-GCM seals each entry.** Measured on Travis's PC (`spike\store.log`):

| Payload | DPAPI protect / unprotect | AES-256-GCM seal / open |
|---|---|---|
| 1 KB | 0.22 / 0.18 ms | 0.001 / 0.001 ms |
| 1 MiB | 41.8 / 41.1 ms | 0.13 / 0.11 ms |
| 16 MiB | 663 / 651 ms | 2.7 / 2.3 ms |

DPAPI per entry would cost two thirds of a second per large image and 0.2 ms per text copy, and adds 230 bytes each. So: at first start a module makes a random 256-bit key, protects it with `CryptProtectData` (current user, no UI) and keeps the blob in the database's `meta` table. Each payload is sealed with AES-GCM (`System.Security.Cryptography.AesGcm`, in the framework; 12-byte nonce, 16-byte tag, +28 bytes) with the entry id as associated data, so rows can't be swapped. DPAPI via P/Invoke or CsWin32, not the `ProtectedData` package.

What this protects: the files copied off the PC, a backup, another user. Not another process running as Travis, which can call DPAPI too (Karl's §8.5 says the same). Search text, previews and file names are sealed too; only the timestamp, kind, size, pin flag and a keyed hash (HMAC-SHA256 with a second key, for duplicate detection) are plain.

**Search** decrypts into memory at page open: 1,000 text entries of 1 KB load and decrypt in 6.2 ms, and a substring search over them takes 0.94 ms. No full-text index (an FTS table would store the text in plain). At the 50-entry default this is nothing; at 1,000 it still fits a keypress.

**Write cost:** 0.032 ms per 1 KB entry with one commit each; 1,000 such entries take 1.3 MB on disk.

**Images** (`spike\image.log`, synthetic screenshot-like pixels):

| Copy | Raw BGRA | PNG | Encode | 320 px thumbnail |
|---|---|---|---|---|
| 1920×1080 | 7.9 MiB | 2.4 MiB | 52 ms | 21 ms |
| 3840×2160 | 31.6 MiB | 9.4 MiB | 174 ms | 28 ms |
| 7680×4320 | 126.6 MiB | 37.1 MiB | 710 ms | 104 ms |

Proposed caps (Karl's B2): decoded size at most 3840×2160×2 pixels (16.6 MP), checked from the DIB header before the pixels are read; PNG at most 16 MiB after encoding, else skipped with a status; images stored as PNG sealed in a separate `blob` table so the entry list stays small; a 320 px thumbnail stored with the entry for the page. Total store cap 128 MiB as Karl proposed, pins included; at the cap a new image is refused rather than evicting a pin.

**Clearing** deletes rows in one transaction, checkpoints the WAL (`PRAGMA wal_checkpoint(TRUNCATE)`), then runs `VACUUM` on the worker so the active file no longer holds the freed pages. Erase-all also deletes the database files and starts over with a new key. That protects the live store only: a copy made earlier (a backup, File History, a crash-left WAL, a file restored from the Recycle Bin) still holds the old wrapped key next to the old ciphertext, and Travis's account can still unwrap it. So no claim that erased history is unrecoverable, and no secure-erase claim on SSDs.

## 5. Pasting

Spike in Sandbox (`sandbox-out\paste.log`, `target-*.log`): the driver plays Chippi at medium integrity with a `RegisterHotKey` shortcut; target A is a normal app (medium), target B is elevated (high).

| Case | Result |
|---|---|
| Island never took focus (opened by click on the tab: `MA_NOACTIVATE` keeps the app in front) | Ctrl+V sent with `SendInput`; the first request for the text came 61 ms later and the target showed it at 69 ms. Unicode and line breaks arrived intact (`é中😀`, `\r\n`) |
| Island took the keyboard, then handed the foreground back | `SetForegroundWindow(A)` allowed (Chippi was foreground); A was in front 5.2 ms later; text arrived 24 ms after Ctrl+V |
| Elevated target | `SendInput` returned 4 of 4 events, yet nothing arrived and no request for the text came. UIPI drops it silently |
| A newer copy arrives before the restore | Sequence number moved (48 → 53); restore skipped, the newer copy kept. The spike compared before opening the clipboard to restore; the design below compares while holding it |

Limits of these results: one WPF target, one reader at a time, and the restore ran after a fixed 1.5 s, not the policy below. No other clipboard reader was running, and no target read late.
| Shortcut gives foreground rights | `WM_HOTKEY` let the medium driver call `SetForegroundWindow` every time (3 of 3) |

The design that follows:

1. **Shortcuts** through `RegisterHotKey` on a message-only window (as `GlobalMicHotkey` does; no keyboard hook). A shortcut someone else owns fails to register; Settings shows it as "Shortcut in use", and the other owner keeps working.
2. **Target**: the foreground window when the shortcut fired, or when the island page opened. Remember its handle, process id and integrity level. If the island took the keyboard, give the foreground back first and wait (up to 500 ms) until `GetForegroundWindow` is that window.
3. **Elevated or unknown target**: compare the target's integrity with Chippi's before sending anything. A medium process can read a high process's level (`OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `TokenIntegrityLevel`, measured: "target B high (read as the driver)"). If the target is higher or can't be read, don't send keys: put the text on the clipboard and show "Copied". Never elevate Chippi to paste.
4. **Modifiers**: wait until the shortcut's keys are released (`GetAsyncKeyState`, up to 1 s) before sending Ctrl+V, or Ctrl+Shift+V reaches the app. Then send Ctrl down, V down, V up, Ctrl up in one `SendInput` call.
5. **Plain text on the clipboard** with delayed rendering: Chippi offers `CF_UNICODETEXT` empty, plus `ExcludeClipboardContentFromMonitorProcessing` so no clipboard manager (Chippi included) records it. Windows asks Chippi for the text when some app reads it (`WM_RENDERFORMAT`). That marks the first data request, not the target's paste: any reader (a clipboard viewer, another manager) can ask first, and a request doesn't prove the text was inserted. Rendering doesn't change the sequence number (12 → 12 measured), so the ownership check still works.
6. **Restore** the original copy on a heuristic: after a render request whose reader is the target's process, plus a short margin, or after a fixed wait (1.5 s, what the spike used) when no such request comes. Identifying the reader by `GetOpenClipboardWindow` during `WM_RENDERFORMAT` is untested; the next spike should check it, and cover another reader asking first and a target that reads late. A target that reads after the restore gets the original copy, so this is best effort, not a guarantee.
   The ownership check runs while holding the clipboard: `OpenClipboard`, compare the sequence number and owner with ours, then `EmptyClipboard` and restore in the same open block; if anything changed, close and leave the newer copy. Restoring needs Chippi to have read the original's formats first: text, HTML, RTF, files, image and the privacy markers. A format Chippi can't copy (private app formats, delayed-render formats from a closed app) is lost on restore, so the brief should say "keeps your copy for text, links, files and pictures", not "always".
7. **Failure statuses** (DESIGN.md §3 fragments): "Clipboard busy" (couldn't open after retries), "Copied" (elevated or unknown target). Chippi can't detect a paste that didn't land, so there's no status for it.

History's Paste uses the same path with the entry instead of plain text. History's Copy needs none of it.

## 6. Shelf: drag and drop into the island

Spike in Sandbox (`sandbox-out\drag-*.log`): a transparent, topmost, `WS_EX_NOACTIVATE` tab (like the island's) opens a bigger no-activate panel on `DragEnter`; injected mouse input drags a file plus text from another process.

| Case | Result |
|---|---|
| Medium source → medium island | Tab saw `FileDrop, FileNameW, FileName, UnicodeText`; panel shown 9 ms after `DragEnter`; the drag moved on to the panel without letting go; `Drop` received the path and the text; `DoDragDrop` returned Copy; the file stayed where it was |
| Elevated source → medium island | Same. Dropping from an elevated app into Chippi works (the other direction, Chippi into an elevated app, is blocked by UIPI and isn't a Shelf need) |
| Foreground | The tab never took focus while shown or during the drag |

Design:
- **Open on hover**: the island tab gets `AllowDrop`; `DragEnter` opens the island on the Shelf page; the page's drop zone accepts. If the drag leaves without a drop, close after the normal hover delay. Docked (island off): the tray icon can't be a drop target, so a drag can't open the page. Once the docked Shelf page is open (tray click), it accepts drops like the island page; a "Paste" action that takes the clipboard's files or text covers the rest.
- **Accept**: `CF_HDROP` (paths, at most 100 per drop), text, links (text that parses as one URL), and images (`PNG`, `CF_DIBV5`) under History's caps. Always `DragDropEffects.Copy`; never Move, so Chippi can't delete the source.
- **Files are references**: store the path and its size and time, never a copy. A cloud placeholder (`FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS` / `RECALL_ON_OPEN`) is listed by name without reading it, so nothing downloads. Missing files show as unavailable.
- **Virtual files** (Outlook attachments, browser images as `FileGroupDescriptorW` + `FileContents`): out of scope for Phase 5 (Karl's B9); they'd need Chippi to write copies.
- **Drag out**: `DoDragDrop` from the page with `FileDrop` (and text for text items), Copy only. Remove an unpinned item after the drop returns Copy; a cancelled drag (None) keeps it.
- The island's existing `MA_NOACTIVATE` stays: the drop never steals focus from the app the person is dragging between.

## 7. Scratchpad: autosave

- One SQLite file with a `note` table (id, name, order, body sealed like history entries, edited time). Tabs up to Karl's 12.
- Typing goes to memory; a save runs 800 ms after the last keystroke on the module's worker (Karl's source value), plus on tab switch, page close, module stop and app exit (`Shutdown` flushes synchronously with a 2 s limit).
- A failed save keeps the text in memory and shows a short status ("Save failed"); the next keystroke retries. A failed load never saves over the old file: the note opens read-only with the status.
- Keyboard: the island takes the keyboard on the first click inside it (`IslandPanelWindow.TakeKeyboard`), so the text box gets focus the normal way. A shortcut that opens Scratchpad activates the island from `WM_HOTKEY` (allowed, measured in 5) and focuses the box.
- Cost: an 800 ms debounce on a 20 KB note is one sealed row write per pause in typing (estimated under 0.1 ms from the storage numbers in 4).

## 8. Cost

All measured; host numbers on Travis's PC, clipboard and input numbers in Sandbox on the same PC.

| Work | Cost |
|---|---|
| Listener, nothing copied | 0 (event-driven, no timer) |
| Per copy, read and gate | 0.37 ms CPU; clipboard open 0.05 ms for 1 KB text, 1.9 ms for 1,000,000 characters |
| Per text entry, seal and store | 0.03 ms |
| Per 4K screenshot copy | 174 ms PNG encode + 28 ms thumbnail, on a worker after the clipboard is closed |
| History page open, 1,000 entries | 6.2 ms load and decrypt; 0.94 ms per search |
| Plain paste | 24 to 69 ms from Ctrl+V to the text showing in the one WPF target tested |
| Scratchpad save | under 0.1 ms per pause in typing (estimated from the 1 KB and 1 MiB seal and insert numbers, not measured on a note) |
| Memory | decrypted text in memory only while a page is open; at most one decoded image (16.6 MP, 66 MB) at a time while encoding |

Nothing runs when no clipboard module is installed: the hub isn't created.

## 9. Calls this feeds

Numbered to line up with Karl's section 9 where they overlap.

1. **(B1, B4) Persistence**: encrypted history survives restarts by default once recording is on; module uninstall erases the database and its key (otherwise reinstalling silently brings back old copies). Recommended.
2. **(B2) Caps**: 16.6 MP decoded and 16 MiB PNG per image, 1 MiB UTF-8 per text (reading 1,000,000 characters took 1.9 ms, so the cap is about storage, not speed), 100 paths, 128 MiB total. The measurements above support them.
3. **(B3) Clean URL on `CanIncludeInClipboardHistory = 0` copies**: skip (fail closed). A password manager that marks its copies shouldn't have them rewritten.
4. **(B6) Plain paste promise**: "keeps your copy" for text, links, files and pictures only, as best effort (a target that reads late can get the original); elevated targets get "Copied" instead of a paste.
5. **(B9) Shelf while docked**: a drag can't open the docked page (the tray icon isn't a drop target); the page takes drops once it's open, plus Paste.
6. **New dependency**: none. Microsoft.Data.Sqlite is already in; AES-GCM is in the framework; DPAPI by P/Invoke.
8. **Clean URL's manual surface**: a shortcut, a tile action or a small page, so it works without Clipboard history (section 1). For the design input.
9. **Tests**: on fakes; the clipboard can't be isolated on Travis's PC as a standard user. Live checks in the eventual brief use test text Travis copies himself.
