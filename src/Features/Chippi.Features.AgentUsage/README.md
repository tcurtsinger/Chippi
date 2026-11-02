# AI agent usage (Phase 5.5)

How much of Claude Code's and Codex's limits you've used, when they reset, and which sessions are working or waiting on you. Brief: [docs/phase-5.5/ai-agent-usage.md](../../../docs/phase-5.5/ai-agent-usage.md). Module id `agent-usage`; the home's button is "Agents".

## What it reads

Chippi never opens either agent's credentials (`~/.claude/.credentials.json`, Codex's `auth.json`, the `*.key` files beside Claude's sessions) and sends nothing anywhere itself.

- **Claude Code's limits:** `claude` runs for a moment with no tools, MCP servers, hooks or settings (`ClaudeLimits.Arguments`), answers `initialize` and `get_usage` (`skip_behaviors: true`) as JSON lines, and ends. It signs itself in; nothing goes to a model. A Chippi started inside a Claude Code session drops that session's variables (`CLAUDECODE`, `CLAUDE_CODE_*`) for the child, which otherwise expects its host's sign-in.
  - `initialize`'s `account.tokenSource` is `"none"` when the CLI isn't signed in, and `get_usage` then answers `rate_limits_available: false, rate_limits: null`: Signed out.
  - Signed in, `rate_limits` holds a window per limit with its share used (`utilization`) and reset (`resets_at`). The 5-hour, weekly and per-model weekly ones show; anything else (spend) is left out.
  - A request it doesn't know is Too old.
- **The fallback:** the Claude app's `%APPDATA%\Claude\plan-usage-history.json` (version 2, samples of `{t, org, u:{fh, sd, so, sn}}`), the newest sample, read when the CLI can't say. It has no reset times, and it's only as fresh as the app's last check, so it shows "As of …".
- **Codex's limits:** `codex app-server` (JSON-RPC lines; standard input must have no byte-order mark): `initialize`, `initialized`, `account/read` (`refreshToken: false`) and `account/rateLimits/read`. Between checks, the newest `token_count` event in the last three days' `~/.codex/sessions/**/rollout-*.jsonl` (only each log's last 256 KB, read again once it grows). The newest reading wins.
- **Sessions:** Claude Code's `~/.claude/sessions/<pid>.json` (busy or idle, its process still the one that wrote it), and Codex's threads whose writer lock (`~/.codex/thread-writer-locks/<thread>.lock`) a running Codex holds, Working or Waiting by the log's last `task_started` or `task_complete`. A lock is tested by reading a byte under it, never by taking it. Waiting over 30 minutes leaves the list. "Switch to" walks the session's process up to the first parent with a window.

## How often

`UsageSchedule`: every minute while the page or the Settings page shows, every 5 minutes otherwise, the first 10 seconds after start, and nothing while the session is locked or the display is off (`Motion.IsUnseen`). A failed check doubles the wait, up to 30 minutes for not found, signed out or too old, and up to 10 for the rest. Sessions are read every 5 seconds; that turn costs about 1.3 ms with 20 sessions and 48 logs (`CHIPPI_AGENTS_SCAN_TIMINGS`).

## Cost (measured 2026-11-01, x64, Debug dev profile)

| Check | Wall | CPU | Peak working set |
|---|---|---|---|
| Claude Code (`claude` 2.1.314) | 1.6 to 1.7 s | 563 to 719 ms | 224 to 237 MB |
| Codex (`codex app-server` 0.166.0) | 1.0 to 1.6 s | 94 to 219 ms | 59 to 64 MB |

Each run is logged ("Agent usage: Claude check Ok in 1.7 s, CPU 719 ms, peak 237 MB"). In the background that's at most one of each every 5 minutes.

## Tests and shots

`tests/Chippi.Features.AgentUsage.Tests` runs on fixtures and scripted answers, never the owner's files. The page's states (`agents-*.png`) and the bar's (`agent-bar-*.png`) render from `Chippi.Island.Tests` when `CHIPPI_ISLAND_SHOTS` names a folder; `CHIPPI_AGENTS_TIMINGS` times the page's open.
