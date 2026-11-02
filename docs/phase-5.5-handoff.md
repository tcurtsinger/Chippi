# Chippi Phase 5.5 brief: building it

Travis, 2026-11-01: "Ok go ahead let's build it." Each feature is built from its brief and approved mockup in [docs/phase-5.5/](phase-5.5/README.md); the brief wins on behavior, DESIGN.md on looks.

## 1. Decisions (Travis, 2026-11-01)

- **Order:** Quick actions, Cleaning mode, AI agent usage, Command bar, Radial menu. AI agent usage stands alone, and its one unknown (whether `claude` still answers the usage request) gets checked early; the Command bar builds the list of Chippi's commands that the Radial menu reuses.
- **In parallel:** AI agent usage is built alongside Quick actions, in its own session, worktree and PR.
- **Agent check:** running `claude` headless once and `codex app-server` once on Travis's PC, read-only, to confirm the live paths answer.

## 2. Pull requests

One per feature, each in its own worktree, reviewed by another agent, merged with Travis's OK once every CI job is green.

| PR | Feature | Shared code it changes |
|---|---|---|
| 1 | Quick actions | The home goes to 5 columns × 2 rows, and every home name becomes one word (Awake, Organizer, System). The `quick-toggles` placeholder becomes the `quick-actions` module. The Shortcuts page lists its shortcuts |
| 2 | Cleaning mode | Adds the Cleaning action to Quick actions; the pet's squish with the grin |
| 3 | AI agent usage | Where an agent ranks on the bar (Waiting on you above other activities, Working below) |
| 4 | Command bar | A list of Chippi's commands every feature fills, with values ("keep awake 2 hr", "volume 40"); the island opening with the keyboard over a fullscreen app |
| 5 | Radial menu | The pet looking toward a point; reuses the commands list |

New dependencies are asked about first. The Command bar is expected to need two: a way to query Windows Search, and a Unicode data file for emoji names.

## 3. The owner's PC

AGENTS.md's rules hold, and for this phase:

- **Nothing that acts on the PC runs in tests or dev runs:** lock, displays off, screen saver, sleep, restart, shut down, sign out, Empty Recycle Bin, Quit apps, End frozen apps, Restart Explorer, Eject, switching screens, the radios, the camera, dark mode, the power plan. Each is behind an interface, tested on fakes; Travis tries the real ones in the live checks.
- **Keyboard and mouse hooks** (Cleaning mode, the Radial menu) are checked only in Windows Sandbox.
- **The clipboard:** the Command bar's clipboard rows and the Radial menu's Selected text go through the clipboard hub and its privacy rules; their checks run in Windows Sandbox.

## 4. Done when

- Both runtimes build with no warnings; the tests pass.
- Each surface matches its mockup, dark and light, in a dev profile, with shots beside the mockup and timings for opening pages (and for typing, in the Command bar).
- Travis's live checks pass.
