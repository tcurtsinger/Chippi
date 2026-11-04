# Command bar: brief

Confirmed by Travis, 2026-11-01, and its mockup approved the same day: [Chippi Phase 5.5: Command bar](https://claude.ai/artifact/BnX8BKEJ3XpuQ3oRT2JNsz). DESIGN.md wins where it speaks.

## Job

One shortcut, a few letters, Enter: open an app, a window, a file or a Windows Settings page, run anything Chippi does (with a value), paste an old copy, get a sum or a conversion, copy an emoji, or search the web.

## Opening it

- **Ctrl+Alt+Space** (Travis's pick; changeable in Settings › Shortcuts), or the home's **Search** button, the tenth in the 5 × 2 grid ([quick-actions.md](quick-actions.md), Surfaces). Search has no status line.
- The panel grows out of the widget onto the Command bar page as it does for any page, and takes the keyboard at once (the island otherwise never does until it's clicked).
- With a fullscreen app or game in front it opens anyway, over it, on the widget's screen (Travis). It takes the keyboard, so an app that leaves fullscreen when it loses the keyboard leaves it then, as it would for Alt+Tab: a fullscreen YouTube video in Chrome does (Travis's live check, 2026-11-02; he accepted it over a bar that never takes the keyboard).

## The page

- **The field is the header:** Back, then a 32-tall search field across the rest of the top row, its placeholder "Search", a search glyph at its start, the accent underline of a focused field. No title row.
- **Results** under it: whole rows only, never scrolling, 8 at most. Each row is 34 tall: a 20 lead (the real app icon for apps and open windows; a file, folder, Settings, Chippi, clipboard or math glyph; the emoji itself), the name in 12 over a second line in 11 secondary saying what it is or where it lives ("App", "Chrome", "Downloads", "Windows Settings", "Chippi", "Copied today, 4:12 PM", "12 × 7"). App letters are the fallback while an icon loads or is unavailable. The selected row sits on the accent tint with its verb at the right in primary semibold ink: Open, Switch, Run, Paste, Copy, Search. Its detail uses primary ink at 82% opacity so it remains legible in either theme.
- **Keys:** Up and Down move the selection; the pointer moves it too. Enter runs it and the panel closes. Esc clears the field, then closes the panel. Shift+Delete hides the selected result.
- **Selection:** arriving results update the automatic first choice; once the user chooses a row, refreshes keep that result selected, including on the empty field. Selection updates reuse the row controls and loaded icons. A pending command runs once and retains its Working feedback until completion. Its completion cannot close a newer Search visit or query. Keyboard focus stays in the field while accessibility exposes the result selection, action, position and failure.
- **Empty field:** your most used, the same rows, no label.
- **Feedback:** a temporary 24-tall line below results says "Searching files…", "No results", or "Files unavailable" with Retry. Other usable results remain available. A failed action keeps the result's identity and detail and gives its reason below when known, otherwise "Open failed · Try again" (or its corresponding verb). Nothing adds a permanent toolbar.
- **Files:** same-name files with matching folder names show enough of their parent paths to distinguish them. Tooltips and accessibility names carry the full path.
- **Background work:** file lookups have a five-second deadline and a single native query at a time. Retry remains available after an unexpected cancellation or timeout; changing text or closing Search cancels quietly. Icon requests for the same app share pending work, and a later app refresh retries missing icons. Closed views detach their subscriptions; changed Settings apply to an open Search view.

## What it finds

Every kind is on by default and has its own switch in Settings › Command bar:

| Kind | Enter | Notes |
|---|---|---|
| Apps | Open | The Start menu's, by name or initials ("vsc") |
| Open windows | Switch | By title or app |
| Files and folders | Open | From Windows' own search index, so indexed places only |
| Windows Settings | Open | Its pages ("Bluetooth & devices", "Display", "Default apps") |
| Chippi | Run | Everything Chippi does, the Quick actions included, and its pages. **Values typed in:** "keep awake 2 hr" → "Keep awake for 2 hr", "volume 40" → "Volume to 40%" (with a small level bar), "output buds" → "Output to INZONE Buds" |
| Clipboard history | Paste | Into the app that was in front. Private copies never show |
| Math and conversions | Copy | "12*7" → 84, "5 km in mi" → 3.107 mi, "72 f in c" |
| Emoji | Copy | By name |
| Web search | Search | "Search the web for "…"", always the last row, in the default browser |

Values and answers come first, then matches: what you pick most for the same letters ranks higher (stored only on your PC), then names that start with what you typed, then Chippi, apps, windows and Settings. Files, clipboard copies and emoji come after all of those, even when their names start with what you typed (Travis's live check, 2026-11-02: seven `chrome…` files from a browser tool's caches hid Chrome and its windows).

## Hiding

The selected row has an eye-off button at its end (Travis: the button and Shift+Delete). Hiding shows "Hidden" with Undo below the results until the query changes or the page closes. Undo restores and selects the result. Settings › Command bar › Hidden remains the later recovery path. Values, answers and the web row can't be hidden. Failed saves leave the result unchanged and show inline feedback.

## Settings › Command bar

- **Search engine:** Google (default), Bing or DuckDuckGo. Windows can't tell an app which engine the browser uses, so Chippi asks. The separate Web search switch controls whether web results appear.
- **Results:** a switch per kind, all on.
- **Hidden:** each hidden result, with what it is under it, and Show. "None" when there are none. Show moves focus predictably to the next row. If its category is off, an explicit Enable action is available before and after restoring; Show never silently enables a category.
- **Saving:** a failure restores the saved controls with inline feedback. Shortcut capture exits on Tab, Escape, Cancel, lost focus or unload. The saved keys stay visible alongside any error. Changing a shortcut restores its saved registration if persistence fails; if restoration also fails, it reports that the shortcut is inactive.
- **Recovery on load:** incomplete collections, duplicate or invalid hidden entries, and invalid saved enum values are normalized in memory while valid choices are retained. Loading does not rewrite the saved file.

## Left out

The front app's menu commands (Windows apps don't share a menu bar), pinning, your own names for results, and text snippets (Phase 6, which can join later).
