# Debounce: brief

For Travis to confirm. Research: [behavior-spec-b.md](behavior-spec-b.md) section 2. Shared rules, the hub and the pause: `input-layer.md` (Kayla). DESIGN.md wins where it speaks.

## Job

Ignore the extra click or keypress a worn switch sends, with no delay on a healthy one.

## Two filters

Both are filters in the shared input hub, first in the order, and both send nothing.

- **Click filter (mouse).** Left, right and middle only. A press that follows the same button's release inside the window is dropped with its own release and the move packets between. The release of an accepted press always passes, so a stop can't leave a button held. Default 25 ms, range 5 to 100.
- **Key filter (keyboard).** A second key-down of the same key is dropped when the key is still down and the press came inside the window, or when it was the last accepted key and was released less than a window ago. Key-ups always pass. Default 5 ms, range 0 to 200, step 1 (0 is off). One optional window per key, for the one that chatters.
- **Auto-repeat passes.** Windows doesn't flag it, so a key-down on a key that's already down and past the window counts as a repeat. The 200 ms cap stays under the typing repeat delay (about 250 ms at the fastest setting), so a held key never looks like chatter.
- **Never applied to** Chippi's own tagged input or input other programs inject.
- **A key or button's state resets** after 5 s of silence, and when time goes backwards.

## Timing

Time each event with `Stopwatch.GetTimestamp()` on entry to the hook, not the event's own timestamp. The hook's field is millisecond-coarse and may tick at the system timer's 10 to 16 ms, as large as the windows here. The build measures this before relying on either (sandbox probe, below).

## Fullscreen

Debounce follows the one **Always on** switch like every Phase 6 input feature (Travis, task 211 #1): off, it pauses whenever the window in front is a fullscreen game or app, on every display; on, it acts everywhere. A swallowed down still has its up swallowed after a pause begins, so nothing sticks. Kayla suggested it ignore the pause, since it only drops input; Travis said no.

## Settings

No page of its own, no home tile, no action tile. Rows. Decided (Travis, task 226): each switch is off until he turns it on, including right after Debounce is installed.

- **Settings › Windows › Mouse:** "Ignore accidental double clicks", a switch, and its window (the number with a slider, in ms).
- **Settings › Windows › Keyboard:** "Ignore key chatter", a switch, its window, and "Keys with their own window" (Add key, then the key and its window, Remove). Key capture is the existing recorder.
- Each switch shows the count it ignored since it was turned on ("12 ignored"), so Travis can see whether the setting is doing anything. Memory only, not saved.
- Over an admin window, the rows' status reads "Not working in admin windows" (hooks can't see them).
- Copy is proposed here and settled with the mockup: bare-verb buttons, errors read "… failed", "Chippi cannot watch the mouse right now." / "keyboard" when the hook is down.

## Edge cases

- Real fast double click: the 100 ms cap stays below a deliberate double click's second press (Windows' default double-click time is 500 ms, but a fast one is about 120 to 150 ms apart).
- Gaming mouse with a button at 1000 Hz polling and genuinely fast taps: the window is short by default so it passes.
- The hub's hook goes away (Windows drops a slow hook): input passes, the watchdog reinstalls and logs.
- Turning the switch off mid-press: the state clears, the current up passes.

## Tests

- Pure filters on an injected clock, in `Chippi.Input.Tests`: accepted press, chatter inside the window, outside it, the up of an accepted press, an orphan up, repeats, per-key windows, the 5 s reset, time going backwards, pause begin with a key held, tagged and injected input.
- Hub with a fake source and a recording sender: down dropped means up dropped; the order against other filters.
- Windows Sandbox probe (`SendInput`, hardware-like, no Chippi marker): a pair of presses 2 ms apart yields one at the target; a pair 60 ms apart yields two; the same with the key filter. The probe also logs the hook's own timestamp beside the `Stopwatch` reading to settle the timing question.

## Done means

- Both filters run in a dev profile, with the hub's p99 time per event from the hub PR unchanged by more than a few microseconds.
- Sandbox probe results attached, with the timestamp comparison.
- Screenshots of the two Settings rows, dark and light, next to the mockup.
- Travis's live check on his own mouse and keyboard, at a time he sets: a chattering button, if he has one, and a game with Always on off and on.

## Not verified

- Whether the hook's timestamp really ticks at 15 ms: the docs don't say; nothing was measured.
- Which of Travis's switches chatter, if any: untested.
- Over an elevated window the filters do nothing (Windows doesn't call the hook), from Microsoft's docs, not tried.
