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

- **Working** means a turn is in progress. **Waiting on you** requires an outstanding request for the owner. Completed or aborted turns and quiet sessions are **Idle**. Idle sessions leave the list after 30 minutes and closed sessions leave on the next scan. Codex workers spawned by a chat do not appear as independent sessions.
- Claude Code reports an open prompt with its session status. Codex questions are matched to their answers by call ID. A tool call without evidence of a user prompt stays Working.
- **Clicking a session brings its window forward** (Travis): the terminal or the app it runs in.

## Surfaces

1. **Home button** (Travis): "Agents", shown like any feature's. With 10 already shown, one is turned off for it in Settings › Island & pet. Its status is the closest limit: the agent's short name and its highest window ("Claude 42%"). At the warning level or over, the glyph and status turn Caution.
2. **The Agents page,** top to bottom, nothing scrolling:
   - **A card per agent found:** its name (12 / 600) and plan (11 tertiary), and at the right a state when there is one ("As of 2:10 PM" in tertiary, "Signed out" in Caution). Then a 22-tall row per window: its name in secondary, a 4-tall bar in the accent fill (Caution at the warning level or over), the percentage (Caution then too), and "Resets 6:10 PM" ("Resets Thu 9:00 AM" past today) in 11 tertiary. A row's tooltip says it in full.
   - **Sessions:** a card of 28-tall rows, each a button: the provider’s product icon, the session’s name or folder, and the state and elapsed time at the far right. The destination app remains available in the tooltip. Whole rows only: with per-model rows, fewer sessions fit; all of them are in Settings.
3. **The bar** (Travis): while an agent works, it shows like an activity: the provider’s icon in the lead, "Working · 42%" (its closest limit) over "Claude Code · chippi" ("2 sessions" when several). Waiting on you gives the icon an amber backing and ranks above other activities; Working ranks below them (Travis). A song still comes first. A switch between working and waiting shows for six seconds. Routine progress updates keep the existing timeout. A 350ms hover on the puck recalls the current state. When the bar folds, the lead provider’s icon sits beside Chippi in the inward-facing side tab. Mic mute and Keep awake use the same row. The tab and puck share one continuous outer rim. It uses the island accent for working and amber if any session needs input. It disappears when neither state applies.
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
- Claude Code's session file reports `waiting` for an open prompt. Older versions that report only busy or idle cannot identify that prompt.
- **Chippi's reactions** to limits and resets: not chosen.
