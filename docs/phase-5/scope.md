# Phase 5 scope (proposed, for Travis)

Phase 5: Clipboard history, Paste as plain text, Clean URL, Scratchpad, Shelf. Each lives on its island page (docked by the tray when the island is off), configured in Settings. Phase 5.5 (later): Quick toggles, Cleaning mode, Command bar, Radial menu, AI agent usage. Quick panel leaves the catalog; Keep awake already shipped.

## In
- **Clipboard history:** text, pictures and copied files (as references, never copies of the files). Search, preview, pin, Copy, Paste, delete. Pause. "Clear history" (keeps pins) and a separate "Erase all". 50 recent, kept 7 days, pins until unpinned. Size limits: 1 MiB per text, 16 MiB per picture, 100 files per copy, 128 MiB in total.
- **Privacy:** never records a copy a password manager marks as private. An app list to never record from. Nothing from history, notes or Shelf goes into settings export, logs or notices.
- **Paste as plain text:** keeps the text, line breaks and tabs, drops formatting, then puts your original copy back. Best effort: a slow app can get the original. Apps running as admin get "Copied" instead of a paste.
- **Clean URL:** a manual Clean on its page, plus automatic cleaning of copied links, off by default. Built-in tracker list (utm_, fbclid, gclid and others, plus YouTube, X, Reddit, Spotify, TikTok rules) and sites to skip. Never touches a copy a password manager marks as private.
- **Scratchpad:** up to 12 named tabs, saves as you type, Markdown preview (no images or links fetched), export as text. Notes kept until deleted.
- **Shelf:** drag files, text, links and pictures in and out. Kept across restarts. Copy out only (never moves your files). An item leaves the Shelf after it's dropped somewhere, unless pinned. Dragging onto the island tab opens the Shelf; docked, it takes drops once open.

## Out (later or never)
- History: editing saved entries, pasting several at once, text from pictures (OCR), picture editing, manual reordering, unlimited history, guessing which text looks like a password.
- Clean URL: a full custom rule editor (sites to skip covers the common case).
- Scratchpad: auto-delete of old notes, a separate floating window.
- Shelf: grouped piles, attachments dragged from mail apps, Share, shake to open, moving files.
- Clearing the clipboard on a timer, lock or sleep.
