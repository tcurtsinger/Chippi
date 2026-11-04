# Text snippets: brief

For Travis to confirm. Research: [behavior-spec-b.md](behavior-spec-b.md) section 4. Shared hub and pause: `input-layer.md` (Kayla). Mockup: the Settings page. DESIGN.md wins where it speaks.

## Job

Type a short trigger anywhere and it becomes its text, with nothing leaving this PC.

## Decided (Travis, task 211 #11)

Snippets are typed out, never pasted, so the clipboard is untouched. No paste mode, and no import or export at first. Only `{{clipboard}}` reads the clipboard.

## How it works

- **Watching.** The hub's last filter reads typed characters into a buffer of 64, using `ToUnicodeEx` with the no-state-change flag so dead keys and accents keep working. It never swallows a key. The buffer lives in memory, is never logged, and clears on expansion.
- **Matching.** The longest trigger wins (`;email2` beats `;email`). "Right away" snippets fire as the last trigger character is typed. "After space, Tab or Enter" ones fire when that key follows, and the delimiter is typed after the text.
- **The buffer resets on** Enter or Tab with no match, arrows, Home, End, Esc, any Ctrl, Alt or Win combination, a mouse click and a change of foreground window. Backspace removes the last character.
- **Inserting.** Backspaces delete the typed trigger, then the text goes in as Unicode key events (`KEYEVENTF_UNICODE`), tagged as Chippi's. New lines go in as Shift+Enter, because Enter sends a chat message. Long text goes in chunks, since one batch drops characters in Remote Desktop and some terminals.
- **Not buffered:** password fields (UI Automation `IsPassword`, only where the app exposes it), while an IME is composing, and while an app on the apps list is in front. Elevated windows aren't seen (status says so).
- **Pause.** Follows Always on like the other features (off: nothing expands while a fullscreen app is in front).

## A snippet

| Field | Rule |
|---|---|
| Name | Text |
| Trigger | 2 to 40 characters, no space or tab, unique. Errors: "The trigger needs at least 2 characters." and "Another snippet already uses this trigger." |
| Text | Plain text with variables |
| Expand | Right away, or After space, Tab or Enter |
| Enabled | Yes |
| Ignore capitalization | Off |
| Folder | Plain text; folders exist only because snippets name them |

Storage: one JSON list in Chippi's settings, stable ids, unknown fields read as defaults.

## Variables

Unknown tags stay as typed, so a typo shows. `{{date}}`, `{{time}}`, `{{datetime}}`, `{{clipboard}}`; a format after a colon (`{{date:yyyy-MM-dd}}`); a time zone (`{{date-tz(America/New_York):yyyy-MM-dd}}`, IANA ids).

- Formats are .NET patterns (`MM` is the month, `mm` minutes), so an **Insert date or time** helper writes the pattern: Type (Date, Time, Date and time), Style (Short, Medium, Long, Full, ISO 8601, Custom), Time zone (searchable), Pattern, a live Preview, Insert or Update.
- `{{clipboard}}` reads the real clipboard at expansion with a time limit, so an app that never answers can't hang typing. A snippet without it never touches the clipboard. Tests use a fake clipboard.

## Settings

Under Clipboard & text: **Text snippets.**

1. Switch "Expand snippets while typing". Off until Travis turns it on, even once Text snippets is installed (decided, task 226).
2. The list, grouped by folder, with a search; each row: name, trigger, Expand, enabled switch.
3. **Add snippet** opens the editor (New snippet, Edit snippet) with the fields above, Save, Delete (a confirm: "Delete this snippet?" with Cancel and Delete) and Cancel.
4. Apps to leave alone (Add an app, Remove).
5. Sound on expansion: off by default, a picker of Windows system sounds, "Sound unavailable" if one is missing.

Empty: "No snippets yet. Add the first one."

Every snippet is also a shared action, **Insert snippet**, so any Shortcuts door (a key, a Caps Lock combo, a mouse button) or a Radial slice can type one. This reuses the Radial menu's Text action.

## No quick menu; Search inserts

Decided (Marge, after review): vorssaint's quick menu overlay is not in the first build. Snippets are reached two ways instead:

- **Insert snippet**, a shared action that any Shortcuts door can fire.
- **Search.** Snippets appear in the island's Search by name and trigger. Choosing one types it into the window Search was opened over, not into Search. Chippi records that window when Search opens. When Search closes, it hands focus back to that window, then types the text as Unicode key events. Search never pauses (Marge's call): a snippet picked there is typed even over a fullscreen app, because it was chosen on purpose, with Always on off too. If that window is gone or has changed owner by then, nothing is typed and Search says "Snippet not inserted". Over an admin window Search shows the snippet but nothing is typed, and says "Not working in admin windows".

A quick menu can come later with its own mockup.

## Edge cases

- A trigger inside a longer word (`;ad` in `;add`): the longest match decides, and right-away snippets can fire early. The editor warns when one trigger starts another: "This trigger starts another one."
- Layout changes mid-typing are followed.
- Editing the snippet that is expanding: the expansion uses the version when the trigger completed.
- Over an admin window nothing expands, and the page says "Not working in admin windows".

## Tests

- Pure matcher on an injected clock: longest wins, delimiters, resets, Backspace, case rule, buffer limit, trigger rules, duplicates.
- Variable renderer: each tag, formats, time zones, unknown tags, a fake clipboard that never answers.
- Hub with a fake source and a recording sender: backspace count, chunks, tagged output, no keys swallowed.
- Windows Sandbox probes (clipboard redirection and networking off): Notepad, a browser field, Word, a terminal and a chat app each get the text with the clipboard unchanged; a password field expands nothing.
- Shots of the page, dark and light, empty and with fifty snippets in folders.

## Done means

- Used in a dev profile with fifty snippets in several folders: every app above, text with accents and emoji, and the clipboard read back afterwards in the sandbox.
- Time from last trigger key to the finished text, for a 200 character snippet.
- Shots next to the mockup.
- Travis's live check, in the apps he types in.

## Not verified

- Every app accepting Unicode key events: documented for `SendInput`, untested per app.
- Whether his apps expose password fields to UI Automation.
- Dropped characters in his terminal and remote sessions.
