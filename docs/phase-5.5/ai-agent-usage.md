# AI agent usage: brief

Confirmed by Travis, 2026-11-01, and its mockup approved the same day: [Chippi Phase 5.5: AI agent usage](https://claude.ai/artifact/QiLGvSVj9mfZ16P29hR2U2). DESIGN.md wins where it speaks.

## Job

See at a glance how much of Claude Code's and Codex's limits you've used, when they reset, and which sessions are working or waiting on you, without opening either one.

## What it reads

Claude Code and Codex only (Travis). Chippi never reads either one's credentials, and sends nothing anywhere itself.

| | Limits | Sessions |
|---|---|---|
| **Claude Code** | Chippi starts `claude` for a moment in its headless control mode, with no tools, MCP servers, hooks or settings, and asks for what `/usage` shows. Claude Code signs itself in, and nothing goes to a model. When that fails, Chippi reads the Claude app's own `%APPDATA%\Claude\plan-usage-history.json` file (the 5-hour and weekly percentages, only as fresh as the app's last check). This is how vorssaint reads them (Travis) | Claude Code's own file per running session (`~/.claude/sessions`): the process, the folder, the session's name, and busy or idle |
| **Codex** | Its session logs between checks (each turn records the limits), and Codex's own server, asked for the account's limits (Codex signs itself in) | Its session logs: a turn being written by a process still running |

- **How often:** every minute while the page or its Settings page shows, every 5 minutes otherwise, and not at all while the session is locked or the display is off. A check that fails waits longer before the next one, up to 30 minutes for what only you can fix (not installed, signed out, too old).
- **Windows:** Claude's 5-hour and weekly limits, and a weekly limit per model (Opus, Sonnet) when the plan has one (Travis). Codex's 5-hour and weekly limits, whichever its plan has.
- **Reads as used** (Travis): "42%", as `/usage` and Codex's `/status` say it.
- **A window past its reset time** shows "Reset" until the next reading, not 0%.

## Sessions

- **Working:** a turn is being written. **Waiting on you:** the turn has ended and the agent waits for your next message. A session idle for more than 30 minutes, or whose process has gone, leaves the list.
- A permission prompt in the middle of a turn can't be told apart from a long-running tool, so it shows as Working.
- **Clicking a session brings its window forward** (Travis): the terminal or the app it runs in.

## Surfaces

1. **Home button** (Travis): "Agents", shown like any feature's. With 10 already shown, one is turned off for it in Settings › Island & pet. Its status is the closest limit: the agent's short name and its highest window ("Claude 42%"). At the warning level or over, the glyph and status turn Caution.
2. **The Agents page,** top to bottom, nothing scrolling:
   - **A card per agent found:** its name (12 / 600) and plan (11 tertiary), and at the right a state when there is one ("As of 2:10 PM" in tertiary, "Signed out" in Caution). Then a 22-tall row per window: its name in secondary, a 4-tall bar in the accent fill (Caution at the warning level or over), the percentage (Caution then too), and "Resets 6:10 PM" ("Resets Thu 9:00 AM" past today) in 11 tertiary. A row's tooltip says it in full.
   - **Sessions:** a card of 28-tall rows, each a button: the agent's monogram on the accent tint (CC, Cx: Chippi never redraws another company's logo), the session's name or folder, "Working · 12 min" in tertiary or "Waiting on you" in Caution, and the app it runs in. Whole rows only: with per-model rows, fewer sessions fit; all of them are in Settings.
3. **The bar** (Travis): while an agent works, it shows like an activity: the agent's monogram in the lead, "Working · 42%" (its closest limit) over "Claude Code · chippi" ("2 sessions" when several). Waiting on you turns the glyph Caution and ranks above other activities; Working ranks below them (Travis). A song still comes first.
4. **Alerts on the bar** (Travis), each for 6 seconds, like Plain paste's feedback:
   - **Near a limit:** when a window passes the warning level, 80% by default (Travis): "80% used" over "Claude Code · 5-hour", the glyph in Caution.
   - **A reset:** when a window that had passed the warning level resets: "Reset" over "Claude Code · 5-hour".
   - Chippi doesn't react.
5. **Settings › AI agent usage:** the page's limits first, a section per agent, then **Sessions**, every one with Switch to (Settings can do everything the island can), then **Agents**: a row each for Claude Code and Codex with a switch and its state ("Signed in · Max", "Not found", "Signed out", "Too old to read limits"), then **Alerts**: Warning level (50% to 95%), Near a limit and Resets switches, both on.

## States

- **An agent that isn't installed** has no card; with neither, the page says "No agents found".
- **Signed out:** its card says "Signed out" in Caution, and its rows are gone.
- **A reading that's stale** (checks failing): the rows keep the last one, with "As of 2:10 PM" in tertiary on the card.
- **Errors** read "… failed" (DESIGN.md §3).

## Cost

Each Claude check starts Claude Code for a few seconds; each Codex check starts its server. At most every 5 minutes in the background, it should be measured in the build (CPU and memory) before it ships.

## Left out

- **Cursor and GitHub Copilot:** not chosen.
- **Tokens, cost and Codex's credits:** not chosen.
- **Codex's banked resets:** vorssaint can use one from its page; not offered here.
- **Seeing a permission prompt:** would need a hook in Claude Code's settings, which Chippi doesn't change.
- **Chippi's reactions** to limits and resets: not chosen.
