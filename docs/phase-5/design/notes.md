# Clipboard and text mockup: notes (2026-10-28)

Phase 5 inside the Surfaces model: Clipboard history, Paste as plain text, Clean URL, Scratchpad and Shelf. Every copy, note, file, picture and app state in the mockup is invented. Nothing read or wrote the real clipboard.

**Revised 2026-10-28 after Vorssaint** (commit `746239b`, read, not run). Its island has one tile each for Clipboard, Files (its Shelf) and Scratchpad, out of 16. Paste as plain text is a shortcut with no tile, page or panel; it beeps when it can't paste. Clean URL is a row in its tray panel's Utilities list, which opens a link field with the automatic switch. Vorssaint also groups features on one tile (Captures shows while any capture tool is on). So this mockup now has three tiles, and the five-tile version is gone.

**Files**
- `index.html`: all 15 boards. Add `?board=<id>` to see one board alone.
- `shots\*.png`: 2x renders of each board.
- `render.ps1`: renders the PNGs again with headless Edge (`-Only <board>,<board>` for some).

| Board | What it shows |
|---|---|
| `grid-top` | All features with the three new tiles, top edge |
| `history-top` | The Clipboard page, top edge, a text copy selected |
| `history-states` | First use, paused, a picture and a missing file selected, no match, clipboard busy, a picture over the limit, history full |
| `plain` | Paste as plain text, shortcut only: pasted, an admin app, no text, clipboard busy, as the closed tab shows them |
| `clean` | Clean URL in the Clipboard page: opening it, a tracker, several removed, nothing to remove, not a web link, copied, History not installed |
| `scratch-right` | Scratchpad on the right edge, editing |
| `scratch-states` | Preview, renaming, the note's menu, a new note, 12 notes, save failed, notes that can't be opened |
| `shelf-states` | Empty, a drag over it, mixed items, pinned, missing file, full, the two limits |
| `shelf-drag` | Seven frames: over the closed tab, opening, into the page, dropped, dragging out, accepted, canceled |
| `docked` | Docked by the tray: Clipboard; the grid and Clean URL with History uninstalled; the Shelf taking a drop (since 2026-10-28 the docked Shelf only drags out; see #50) |
| `fit` | A 1280×720 screen: History on the right edge, Scratchpad on the top edge, footprints outlined |
| `keyboard` | Focus on opening each page, the keys each page adds, the paste handoff before and after |
| `destructive` | History's menu, Clear and Erase confirmations and results, deleting a note, removing a Shelf item |
| `settings-dark` | Settings › Clipboard history, Clean URL, Paste as plain text (shortcut in use), Add features uninstalling Clipboard history |
| `settings-light` | First use: recording off, automatic cleaning off while adding a site, shortcut not set, shortcut set |

## Approved by Travis (2026-10-28)

All eight, as drawn. The alternatives in the numbered notes were turned down.

1. **Three tiles, as in Vorssaint** (#1): Clipboard, Scratchpad, Shelf. History and Clean URL share Clipboard, the way routing, mute and processing share Sound. This drops the Clean URL page and the Plain paste tile and page that design-input.md asked for.
2. **Clean URL behind a link button in the Clipboard header** (#23). Vorssaint keeps it in a tray panel, which Chippi doesn't have. The alternative is a History · Clean URL SelectorBar under the header, which is easier to find but costs a row every time the page opens.
3. **Plain paste reports a failure with a beep and the tab's glyph** (#19), and no Windows notification. Vorssaint only beeps. With the island off, the beep is all there is.
4. **Clean URL shows its result as you type** (#25), rather than a Clean button.
5. **No Settings page for Scratchpad or Shelf** (#36, #47): nothing in scope to configure.
6. **A Shelf storage cap of 128 MiB** for the text and pictures it keeps (#45).
7. **A 250 ms pause before a drag over the tab opens the Shelf** (#48), so drags that only pass the tab don't open it.
8. **Turn on on History's first-use page** (#10), besides the Settings switch. Vorssaint's island page has the same button.

**Also settled:** Plain paste has no default shortcut (Travis, 2026-10-28, again after the tiles went). It does nothing until one is set in Settings.

## Choices DESIGN.md doesn't make yet

### Grid and tiles
1. **Three tiles** (approved): Clipboard, Scratchpad and Shelf, after System monitor. With all nine the grid is three full rows. Island & pet › Tiles hides or reorders them like any tile, and any of them can be a round button.
   - **Clipboard** appears while Clipboard history or Clean URL is installed. Paste as plain text adds nothing to it, so with only plain paste installed there's no tile.
   - Each feature still installs alone and keeps its own Settings page.
2. **Glyphs** (Lucide):

   | Tile or feature | Glyph | Replaces in the catalog |
   |---|---|---|
   | Clipboard tile, Clipboard history | ClipboardList | Clipboard |
   | Paste as plain text (tab glyph, Settings) | ClipboardType | Clipboard (two features shared one glyph) |
   | Clean URL (header button, Settings) | Link | Check |
   | Scratchpad | NotebookPen | Keyboard |
   | Shelf | Inbox | Download |

   Item kinds inside the pages use different glyphs: AlignLeft for text, Globe for a link, Image, File, Files, FileX for a missing file and Cloud for an online-only file.
3. **Tile names:** "Clipboard", "Scratchpad", "Shelf". All fit a 96 tile. Settings and Add features keep the feature names. The Shelf stays "Shelf", not Vorssaint's "Files", which would read like File organizer.
4. **Statuses**:

   | Tile | Status |
   |---|---|
   | Clipboard | History's: "Off" (recording off), "Paused", "No items", "1 item", "23 items". Without History, Clean URL's: "Manual", or "Automatic" while automatic cleaning is on |
   | Scratchpad | "1 note", "4 notes" |
   | Shelf | "No items", "5 items" |

   - The Clipboard status is never the latest copy's text, because the grid is visible to anyone looking at the screen.
   - None of these tiles has a dot, because none is an activity.
5. **Closed tab:** two glyphs. Shelf's while a drag is over the tab (#48), and plain paste's when it couldn't paste (#19).

### Page widths and overflow
6. **Widths:** Clipboard and Scratchpad are 440, like Now Playing, and Clean URL keeps 440 inside Clipboard so long links wrap less. Shelf is 328.
7. **Overflow:** a page never scrolls. A long list or text scrolls inside its own region, which has a fixed height, under a 3px overlay scrollbar:

   | Page | Scrolling region | Page height |
   |---|---|---|
   | History | List, at most 336 (about 8 rows) | 436 |
   | Scratchpad | The editor's text, 236 | 376 |
   | Shelf | 3 rows of items, 268 | 368 |

   - Scratchpad's tab row shows the tabs that fit, then "+8 ⌄", which lists every note.
   - This isn't Phase 4's height limit: each page's height comes from its content.
   - Every page and its buttons fit a 1280×720 screen. The tallest, Clipboard on the right edge, takes 493 × 542 of a 1280 × 672 work area (board `fit`).
   - Below that, History's list shrinks first, down to 4 rows.

### Clipboard history
8. **Page:**
   - The header reads "Clipboard" and holds Clean URL (link), Pause (Resume while paused) and More (⋯), as icon buttons. With recording off, only Clean URL shows.
   - Then a Search field, then the list.
   - The list has "Pinned" and "Recent" groups under 12 / 600 labels; while searching, the Recent label goes.
   - Rows are 40 high: the kind's glyph (or a 32×20 thumbnail), one line of the copy, and its time on the right ("Now", "2 min", "1 hr", "Yesterday", "Mon", "Oct 21").
9. **Selection opens in place:**
   - The kind and its details ("Text · 4 lines", "Picture · 1920 × 1080", "3 files"), then a preview: up to 4 lines of text, the picture 208 wide, or the files with their sizes.
   - Then the actions: Paste (accent) and Copy, and icon buttons for Pin (Unpin) and Delete.
   - A click selects and a double-click pastes. There's no separate preview pane.
10. **First use** (Travis's answer to task 30):
    - The page shows "Recording off" and Turn on (accent). Turn on and Settings' Recording switch are the same setting, which is the feature on/off exception in DESIGN §5 (approved).
    - Pause is separate: it's temporary and set from the page. It lasts until Resume, across restarts, and shows as "Paused" on the page header and the tile.
11. **Clearing:**
    - The ⋯ menu holds Clear history and Erase all.
    - Each opens a popover under ⋯: "Clear unpinned items?" with Cancel and Clear, or "Erase all items and pins?" with Cancel and Erase. Both verbs are Critical buttons.
    - Clear history is disabled when nothing is unpinned.
    - After Clear the pins stay, with "No recent copies" under Recent. After Erase all the page shows "No items" and recording carries on.
12. **A missing file:**
    - Its row shows FileX, a muted name and "Unavailable".
    - Paste and Copy are disabled, with "1 file unavailable", so a shorter list of files is never pasted without a word. Delete still works.
    - The check reads file attributes only. An online-only file counts as there and is never downloaded to check it.
13. **Limits:**
    - A copy over a limit shows one Caution line above the list until the next copy: "Picture not saved · over 16 MiB", "Text not saved · over 1 MiB", "Not saved · over 100 files".
    - When pins fill the store: "History full · 128 MiB", until a pin is removed.
14. **Clipboard busy:** after a failed Paste or Copy, "Clipboard busy" shows in Caution in the selected row's actions.
15. **Privacy:** a skipped copy (private marker or an app to skip) leaves nothing: no row, no placeholder, no change in the count, no status. The `history-top` board has one between two rows.
16. **Rich copies** are kept as their plain text, with nothing marking them as rich.
17. **Not drawn:** a Clean action on link rows (an optional second door to Clean URL). Leave it out of Phase 5 unless asked; the header button covers it.

### Paste as plain text
18. **Shortcut only** (approved, call 1), as in Vorssaint: no tile, no page and no island or docked surface. The shortcut set in Settings pastes the copy as plain text into the app in front, keeping line breaks and tabs. With no shortcut set it does nothing, and Settings shows "Not set". There's no default (settled).
19. **When it can't paste** (approved):
    - An admin app, or one whose level can't be read: the plain text becomes the copy, Windows plays its default beep, and the closed tab shows the ClipboardType glyph in accent for 6 seconds, named "Plain text copied" for screen readers. Ctrl+V then pastes it. Chippi never elevates.
    - Pictures or files only: nothing changes, a beep, and the glyph in Caution ("No text copied").
    - Clipboard busy: a beep, and the glyph in Caution ("Clipboard busy").
    - The glyph is an activity, not a notice, so plain paste gets no group on the Notifications page. With the island off there's no tab, so only the beep.
20. **A private copy** pastes as usual. Nothing shows either way.
21. **After a paste** nothing shows. There's no "Pasted" state, because Chippi can't know the text landed.
22. **Dropped:** the plain-paste page and the "Plain paste" tile name. The previous version's page showed the copy and the target app before pasting; Vorssaint has nothing like it, and the shortcut covers the daily use.

### Clean URL
23. **In the Clipboard page** (approved): the header's link button swaps the list for one link field (it wraps to 3 lines), a result card with the clean link over "Removed · si", and Copy (accent). The button shows pressed, and the header drops Pause and More. The button again, or Esc, goes back to the list. Without Clipboard history the page is Clean URL alone, and the header shows "Manual" or "Automatic" (the tile's status).
24. **Filled from the copy:** when Clean URL opens and the copy is one web link with no private marker, the field holds it. Otherwise the field is empty and has the keyboard.
25. **Result as you type** (approved). The page is the manual Clean, and Copy puts the result on the clipboard. The alternative is a Clean button that replaces the copy in place; it hides the result until the click, and if nothing changes it's a click for nothing.
26. **States:**
    - "Nothing to remove", with Copy disabled.
    - "Not a web link", in Caution under the field: only `http` and `https` count, one link, no spaces inside.
    - "Copied", with a Success check.
27. **Sites to skip** apply to automatic cleaning only; the manual Clean cleans what you give it. While automatic cleaning is off, the list dims to 60% and stays editable, as on the Notifications page.
28. **Automatic cleaning** rewrites a copy without a status, notice or sound, and History keeps only the cleaned copy.

### Scratchpad
29. **Page:**
    - The header shows "Saved", or "Save failed" in Caution.
    - The tab row is the SelectorBar at 13px (the selected note in Text 600 with an accent bar), then New note (+).
    - The editor is 236 tall, UI font 13 / 20, with no code font.
    - The footer has an Edit · Preview segment and ⋯ (Rename, Export…, Delete). Right-clicking a tab opens the same menu.
30. **A new note** is named in order ("Note 5"), opens in Edit, and has the keyboard. At 12 notes New note is disabled, with no message.
31. **Rename** happens in place on the tab (F2, or Rename in the menu): Enter keeps, Esc cancels, at most 40 characters, and an empty name puts the old one back.
32. **Preview** is local Markdown:
    - Headings, lists, bold and inline code (Cascadia) render.
    - Checkboxes show but can't be ticked.
    - Links are underlined text in the Text color (the Accent Rule) and don't open.
    - Pictures become a muted chip with their alt text. Nothing is fetched.
33. **Export…** opens the Windows Save dialog for the current note (.txt, or .md). The island stays open under the dialog.
34. **Delete:** "Delete “Standup”?" with Cancel and Delete. Deleting the only note leaves an empty "Note 1".
35. **Failures:**
    - Save failed: the text stays as typed. The next keystroke, a note switch and closing all try again.
    - Load failed: "Load failed" in Caution with Retry. There are no tabs and no editor, so nothing can be saved over the notes.
36. **No Settings page** (approved): nothing in scope to configure. A shortcut that opens Scratchpad would be the first row if one is ever wanted.

### Shelf
37. **Page:**
    - A three-column grid of 96 × 84 items, using the tile metrics, newest first.
    - Each item has a glyph or a 48 × 28 picture, its name, and a Caption line: "PDF · 2.1 MB", "Text · 2 lines", "Link", "Files · 4.8 MB", "1280 × 720".
    - A pin glyph (12px) marks pinned items.
38. **Selection:** a 1px accent stroke and a `.10` fill. Under the grid are Copy, Pin (Unpin) and Remove, disabled with nothing selected and hidden when the Shelf is empty.
39. **Copy** is the way out without a drag. It puts the item on the clipboard and doesn't remove it; only an accepted drop does that.
40. **One drop, one item:** three files dropped together are one "3 files" item and drag out together. Text, a link or a picture is one item each.
41. **Dropping in:**
    - The whole page takes a drop.
    - The grid gets a dashed accent outline 4px outside it, and a dashed slot shows where the item will land.
    - Empty, the page is a dashed zone with the Inbox glyph and "No items"; during a drag, its border and glyph turn accent. The text stays muted.
42. **A missing file:** FileX, a muted name and "Unavailable". Copy and dragging out are off; Pin and Remove work.
43. **An online-only file:** the Cloud glyph and "Online only". It's never opened for a preview.
44. **Limits:** after a refused drop, a Caution line beside the actions: "Over 100 files" or "Picture over 16 MiB".
45. **Storage cap** (approved): 128 MiB for the text and pictures the Shelf keeps, in its own store apart from History's. When full: "Shelf full · 128 MiB". Files are references, so they don't count. There's no item-count cap; a full Shelf scrolls (#7).
46. **Remove has no confirmation:** a file item is only a reference, and nothing outside Chippi changes. The verb is Remove, never Delete.
47. **No Settings page** (approved): what happens after a drop and Copy-only are fixed, so there's nothing to set. The Files group keeps File organizer only.

### Shelf drag
48. **Over the closed tab:**
    - The tab swells to its live size (136 × 36 on top) and shows the Inbox glyph in accent beside Gumdrop.
    - After 250 ms over the tab (approved), the island opens on the Shelf page, whatever page was last, without taking focus.
    - If the drag leaves without a drop, the island closes after the normal hover delay.
49. **Dragging out:**
    - The item dims to 40% during the drag, and Explorer offers only Copy.
    - Accepted: an unpinned item leaves the Shelf and a pinned one stays.
    - Canceled (Esc, or a drop nowhere): the item stays.
    - The island stays open either way.
50. **Docked:** the tray icon never takes a drop. ~~Once the docked Shelf is open from the tray, it takes drops the same way.~~ Changed 2026-10-28: the docked Shelf only drags items out. Pressing in another app closes the docked pages before its drag can start, and Travis chose to leave that as a known limit rather than close the pages on release. Drops go through the island tab.

### Settings
51. **Sidebar:** Clipboard & text holds Clipboard history, Paste as plain text and Clean URL. Scratchpad and Shelf have no page (#36, #47).
52. **Clipboard history:**
    - Recording (switch, Off by default).
    - An "Apps to skip" section with Add app on the header. Add app opens a menu of running apps plus Browse… for an .exe.
    - Rows show the app's icon (a monogram in the mockup), its name, its path as caption, and Remove. With none: "No apps".
53. **Paste as plain text:** a Shortcut row, following Mic mute's pattern:
    - Not set: "Not set" and Set.
    - Set: the keys ("Ctrl+Alt+V"), Change and Remove.
    - Capturing: a "New shortcut" row with Cancel; Esc cancels too.
    - In use: "Shortcut in use", a Caution line with a dot under the card. The keys stay shown, so Change picks others. Chippi reserves nothing.
54. **Clean URL:**
    - Automatic cleaning (switch, Off by default).
    - A "Sites to skip" section with Add site on the header. Add site opens an inline row at the top of the card: a field, Add (accent) and Cancel.
    - An entry is a host and covers its subdomains. Rows show the host and Remove.
55. **No rows for fixed behavior:** no count or retention (50 and 7 days are fixed), no picture or file switches, no list of built-in rules, no sensitive-text switch. No extra settings are proposed for Phase 5.
56. **Uninstalling Clipboard history** (Add features switch; Travis's answer to task 31): a popover, "Uninstall and erase history?", with Cancel and Uninstall (Critical). It appears only when history is saved; with none, the switch uninstalls at once like any feature. Scratchpad and Shelf uninstall without a question; their content comes back on reinstall.
57. **Overlap badges:** "Overlaps Win+V", "Overlaps PowerToys" and "Overlaps Notepad". None for Clean URL or Shelf.

### Keyboard
58. **Where the keyboard lands when a page opens:**

    | Page | Focus |
    |---|---|
    | Clipboard | Search field; the link field in Clean URL, or without History |
    | Scratchpad | The note, at its end |
    | Shelf | First item (or the empty zone) |

59. **Clipboard:**
    - Typing searches. Up and Down move the selection while the keyboard stays in the field.
    - Enter pastes, Ctrl+C copies and Ctrl+P pins or unpins.
    - Delete deletes the selected copy only while the search is empty; otherwise it edits the search.
    - The first Esc clears a search or leaves Clean URL, and the next closes the island. This changes the shell's Esc on this page only.
    - Tab reaches the header's buttons; Enter or Space on the link button opens Clean URL.
60. **Scratchpad:**
    - Tab leaves the note and never types a tab character (pasted tabs are kept), so the keyboard can't get stuck.
    - Esc closes the island even from the note, which is already saved.
    - Ctrl+Tab and Ctrl+Shift+Tab switch notes; Ctrl+N makes a new one; F2 renames.
    - In the tab row, Left and Right switch notes right away.
61. **Shelf:** arrows move the focus and the selection follows. Ctrl+C copies, Ctrl+P pins or unpins, Delete removes, and Shift+F10 or the Menu key opens the item's menu (Copy, Pin, Remove).
62. **The paste handoff:**
    - Chippi gives the keyboard back to the target, waits up to 500 ms for it to come to the front, waits for the keys to be released, sends Ctrl+V and closes the island.
    - If the target is gone or never comes to the front, the result goes on the clipboard instead, the page shows "Copied" and stays open.

### Strings

| Where | Strings |
|---|---|
| Clipboard | Clipboard · Clean URL · Search · Pinned · Recent · Recording off · Turn on · Paused · Pause · Resume · More · Paste · Copy · Pin · Unpin · Delete · Unavailable · 1 file unavailable · No matches · No recent copies · No items · Clipboard busy · Picture not saved · over 16 MiB · Text not saved · over 1 MiB · Not saved · over 100 files · History full · 128 MiB · Clear history · Erase all · Clear unpinned items? · Clear · Erase all items and pins? · Erase · Cancel |
| Plain paste (screen-reader names only) | Plain text copied · No text copied · Clipboard busy |
| Clean URL | Link · Removed · si · Nothing to remove · Not a web link · Copy · Copied · Manual · Automatic |
| Scratchpad | Saved · Save failed · New note · All notes · Note 5 · Edit · Preview · Rename · Export… · Delete · Delete “Standup”? · Load failed · Retry |
| Shelf | No items · Copy · Pin · Unpin · Remove · Unavailable · Online only · Over 100 files · Picture over 16 MiB · Shelf full · 128 MiB |
| Settings | Recording · Apps to skip · Add app · Browse… · No apps · Remove · Shortcut · Not set · Set · Change · New shortcut · Shortcut in use · Automatic cleaning · Sites to skip · Add site · Add · Uninstall and erase history? · Uninstall |

## Not drawn, on purpose
- Out of scope: editing saved copies, pasting several at once, OCR, picture editing, manual reordering, unlimited history, guessing passwords, a sensitive-text switch, a custom rule editor, deleting old notes, a floating Scratchpad, grouped piles, mail attachments, Share, shake to open, moving files, clearing the clipboard on a timer.
- Phase 5.5 features, and anything disabled or "coming soon" for them.
- A tray readout, an extra tray icon, a separate quick panel or window.
- Delivery note, not product copy: restoring the original copy after a plain paste is best effort. A slow app can read the original, and some private app formats can't be put back.
