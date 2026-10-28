# Chippi.Clipboard

The shared clipboard service for the Phase 5 features ([brief](../../docs/phase-5-handoff.md) §2 and §3, [architecture](../../docs/phase-5/architecture.md)). No module opens the clipboard itself.

## For feature modules

- **`ModuleContext.Clipboard`** (`Chippi.Core.Clipboard.IClipboardHub`), set by the shell:
  - `Listen(copied, skip)`: a lease. The listener runs only while a lease exists (History while recording, Clean URL while automatic cleaning is on). Copies arrive on a worker, after the privacy gate, never Chippi's own writes. `ClipPrivacy.NoHistory` copies still arrive, flagged. A copy over a limit arrives with `Content` null and `Limit` set. `skip` gets the owner's executable path (Apps to skip); a skipped copy isn't read for that lease.
  - `ReadAsync()`: the current copy on demand (Clean URL's prefill). Never starts the listener.
  - `WriteAsync(content)`: Copy. File lists are written as a copy, never a move.
  - `CaptureTarget()` before the island takes the keyboard, then `PasteAsync(content, target)`: Sent, or Copied when the target is elevated, unreadable or won't come forward (no keystrokes; the content stays as the copy).
  - `PastePlainTextAsync()`: plain paste into the window in front. Sent, Copied, NoText (pictures or files only, clipboard untouched) or Busy. `Beep()` is the failure sound.
- **`Storage.SealedDatabase`**: `Open(path, DpapiKeyProtector.Instance)` under `ModuleContext.DataDirectory`. Create your own tables on `Connection`; store payloads as `Seal(bytes, table, rowId)` and read them with `Unseal` (bound to that table and row). `Hash` for duplicates, `Compact` after deletes, `Erase` for erase-all (recreate your tables afterwards). One worker per database.
- **Limits** are in `ClipLimits`. Pictures arrive as top-down BGRA (`ClipImage`); the page encodes PNG and thumbnails on its own worker and applies the 16 MiB encoded cap.

## Tests

Use `FakeClipboard`, `FakeForeground` and `ManualTime` from `tests/Chippi.Clipboard.Tests/Fakes` (link the files into your test project). Never construct `WindowsClipboardSystem` or `WindowsForeground` in a test: they act on Travis's real clipboard and keyboard.
