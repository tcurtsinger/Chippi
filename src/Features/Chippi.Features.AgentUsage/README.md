# AI agent usage (Phase 5.5)

How much of Claude Code's and Codex's limits you've used, when they reset, and which sessions are working or waiting on you. Brief: [docs/phase-5.5/ai-agent-usage.md](../../../docs/phase-5.5/ai-agent-usage.md). Module id `agent-usage`; the home's button is "Agents".

## What it reads

Chippi never opens either agent's credentials (`~/.claude/.credentials.json`, Codex's `auth.json`, the `*.key` files beside Claude's sessions) and sends nothing anywhere itself.

- **Claude Code's limits:** `claude` runs for a moment with no tools, MCP servers, hooks or settings (`ClaudeLimits.Arguments`), answers `initialize` and `get_usage` (`skip_behaviors: true`) as JSON lines, and ends. It signs itself in; nothing goes to a model. A Chippi started inside a Claude Code session drops that session's variables (`CLAUDECODE`, `CLAUDE_CODE_*`) for the child, which otherwise expects its host's sign-in.
  - `initialize`'s `account.tokenSource` is `"none"` when the CLI isn't signed in, and `get_usage` then answers `rate_limits_available: false, rate_limits: null`: Signed out.
  - Signed in (recorded 2026-11-01, plan Team): `subscription_type: "team"`, `rate_limits_available: true`, and `rate_limits` with a list, `limits: [{kind: "session" | "weekly_all" | "weekly_scoped", percent, resets_at, scope: {model: {display_name}}}]`, read first, and the same windows keyed (`five_hour`, `seven_day`, `seven_day_opus`, `seven_day_sonnet`, each `{utilization, resets_at}` or null), read when there's no list. They show as 5-hour, Weekly and "Fable weekly" (the per-model one names its model). A 5-hour window not running has `resets_at: null`. Spend, `extra_usage`, credits and about two dozen code-named keys (all null) are left out.
  - A request it doesn't know is Too old.
- **The fallback:** the Claude app's `%APPDATA%\Claude\plan-usage-history.json` (version 2, samples of `{t, org, u:{fh, sd, so, sn}}`), the newest sample, read when the CLI can't say. It has no reset times, and it's only as fresh as the app's last check, so it shows "As of …".
- **Codex's limits:** `codex app-server` (JSON-RPC lines; standard input must have no byte-order mark): `initialize`, `initialized`, `account/read` (`refreshToken: false`) and `account/rateLimits/read`. Between checks, the newest `token_count` event in the last three days' `~/.codex/sessions/**/rollout-*.jsonl` and the open threads' logs. On first sight Chippi replays complete records in order from the newest 16 MiB. On later scans it reads new bytes in cancellable 256 KiB blocks. A partial record is applied only after its newline. The newest reading wins. Signed out, Codex shows no limits: its logs are from before.
- **Found:** an agent has a card when its program is here, a session of it is open, or (Claude Code) the Claude app's own file is. Logs an uninstalled agent left behind show nothing.
- **Sessions:** Claude Code's `~/.claude/sessions/<pid>.json` (busy, idle or explicitly waiting, with the original process still alive), and Codex's threads whose writer lock (`~/.codex/thread-writer-locks/<thread>.lock`) a running Codex holds. User-facing threads are shown separately, even with the same title. Spawned workers are identified by their own session metadata. Codex task completion and interruption are Idle. A blocking question or accepted desktop question marks a thread “Waiting on you” until the matching response or cancellation. A lock is tested by reading a byte under it. Idle sessions age out after 30 minutes. "Switch to" walks the session's process up to the first parent with a window.

## How often

`UsageSchedule`: every minute while the page or the Settings page shows, every 5 minutes otherwise, the first 10 seconds after start, and nothing while the session is locked or the display is off (`Motion.IsUnseen`). A failed check doubles the wait, up to 30 minutes for not found, signed out or too old, and up to 10 for the rest. Sessions are read every 5 seconds; that turn costs about 1.3 ms with 20 sessions and 48 logs (`CHIPPI_AGENTS_SCAN_TIMINGS`).

## Cost (measured 2026-11-01, x64, Debug dev profile)

| Check | Wall | CPU | Peak working set |
|---|---|---|---|
| Claude Code (`claude` 2.1.314) | 1.6 to 1.7 s | 563 to 719 ms | 224 to 237 MB |
| Codex (`codex app-server` 0.166.0) | 1.0 to 1.6 s | 94 to 219 ms | 59 to 64 MB |

Each run is logged ("Agent usage: Claude check Ok in 1.7 s, CPU 719 ms, peak 237 MB"). In the background that's at most one of each every 5 minutes. The signed-in probe from PowerShell took 2.1 s, 1.2 s CPU and 237 MB.

## Tests and shots

`tests/Chippi.Features.AgentUsage.Tests` runs on fixtures and scripted answers, never the owner's files. The page's states (`agents-*.png`) and the bar's (`agent-bar-*.png`) render from `Chippi.Island.Tests` when `CHIPPI_ISLAND_SHOTS` names a folder; `CHIPPI_AGENTS_TIMINGS` times the page's open.
