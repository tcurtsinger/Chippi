# Command bar: brief

Confirmed by Travis, 2026-11-01, and its mockup approved the same day: [Chippi Phase 5.5: Command bar](https://claude.ai/artifact/BnX8BKEJ3XpuQ3oRT2JNsz). DESIGN.md wins where it speaks.

## Job

One shortcut, a few letters, Enter: open an app, a window, a file or a Windows Settings page, run anything Chippi does (with a value), paste an old copy, get a sum or a conversion, copy an emoji, or search the web.

## Opening it

- **Ctrl+Alt+Space** (Travis's pick; changeable in Settings › Shortcuts), or the home's **Search** button, the tenth in the 5 × 2 grid ([quick-actions.md](quick-actions.md), Surfaces). Search has no status line.
- The panel grows out of the widget onto the Command bar page as it does for any page, and takes the keyboard at once (the island otherwise never does until it's clicked).
- With a fullscreen app or game in front it opens anyway, over it, on the widget's screen (Travis).

## The page

- **The field is the header:** Back, then a 32-tall search field across the rest of the top row, its placeholder "Search", a search glyph at its start, the accent underline of a focused field. No title row.
- **Results** under it: whole rows only, never scrolling, 8 at most. Each row is 34 tall: a 20 lead (an app's icon; a window, file, folder, Settings, Chippi, clipboard or math glyph; the emoji itself), the name in 12 over a second line in 11 tertiary saying what it is or where it lives ("App", "Chrome", "Downloads", "Windows Settings", "Chippi", "Copied today, 4:12 PM", "12 × 7"). The selected row sits on the accent tint with its verb at the right in the accent: Open, Switch, Run, Paste, Copy, Search.
- **Keys:** Up and Down move the selection; the pointer moves it too. Enter runs it and the panel closes. Esc clears the field, then closes the panel. Shift+Delete hides the selected result.
- **Empty field:** your most used, the same rows, no label.

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

Values and answers come first, then matches: what you pick most for the same letters ranks higher (stored only on your PC), then names that start with what you typed, then Chippi, apps, windows, Settings, files, clipboard and emoji.

## Hiding

The selected row has an eye-off button at its end (Travis: the button and Shift+Delete). A hidden result never shows again until Settings › Command bar › Hidden brings it back. Values, answers and the web row can't be hidden.

## Settings › Command bar

- **Web search:** Google (default), Bing or DuckDuckGo. Windows can't tell an app which engine the browser uses, so Chippi asks.
- **Results:** a switch per kind, all on.
- **Hidden:** each hidden result, with what it is under it, and Show. "None" when there are none.

## Left out

The front app's menu commands (Windows apps don't share a menu bar), pinning, your own names for results, and text snippets (Phase 6, which can join later).
