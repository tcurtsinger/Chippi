## Summary

Four more island pages fit the fixed body: Now Playing, App volume, Keep awake and File organizer.

~~~diff
 Now Playing
- 112 artwork with controls beside it
+ 304 artwork, 184 controls column; transport aligned to the bottom
 App volume
- horizontally scrolling strips and monograms
+ 56 / 34 / 28 strips, at most 14, program-file icons or two-letter tiles
 Keep awake
- small chips at the top
+ centered 3 × 2 cards; the lit choice says Stop under the pointer
 File organizer
- folder and mode stacked; three large rows
+ one 44 control card, whole 30 rows, bottom actions
- Silent (display label)
+ Auto (stored enum and JSON value unchanged)
~~~

## Evidence

All pairs use main `78184b8` and this branch, the same 520 × 360 host and invented state. The render harness creates real views without showing a window. Audio uses fake sessions and devices; the organizer fixture is not started, so its control card and footer are disabled in these shots. It never watches a real folder.

- [Now Playing before](part-d1-d4/before-playing.png) / [after](part-d1-d4/after-playing.png), and [empty before](part-d1-d4/before-playing-empty.png) / [after](part-d1-d4/after-playing-empty.png).
- [App volume before](part-d1-d4/before-volume-5.png) / [after](part-d1-d4/after-volume-5.png), [12 apps](part-d1-d4/after-volume-12.png), [14 apps](part-d1-d4/after-volume-14.png), [empty](part-d1-d4/after-volume-0.png).
- [Keep awake before](part-d1-d4/before-keep-awake.png) / [after](part-d1-d4/after-keep-awake.png), and [on before](part-d1-d4/before-keep-awake-on.png) / [after](part-d1-d4/after-keep-awake-on.png). The on state is assigned in the fixture; no real awake session starts.
- [Organizer before](part-d1-d4/before-organizer.png) / [after](part-d1-d4/after-organizer.png), and [empty before](part-d1-d4/before-organizer-empty.png) / [after](part-d1-d4/after-organizer-empty.png).
- Ten added cases cover app densities at 7 / 12 / 14 / 18, two-letter fallback, empty Now Playing bounds and disabled transport, centered Keep awake cards, and organizer card/list bounds. Existing session volume/mute tests still drive the same fake module methods.
- Full Release suite: 1,303 passed, 0 failed, 16 desktop-input tests skipped. Sound 81, Keep awake 32, Downloads 118, Island 163. After the final organizer panel adjustment, all three focused layout tests pass again.
- Release x64, Release ARM64 and Debug x64 compile with 0 warnings and 0 errors.
- The native icon loader was also checked against the installed dotnet.exe program file: frozen 20 × 20 image returned. The screenshots deliberately use nonexistent program paths to show the two-letter fallback.
- Actual Debug `--island-pages` completed with 13 captures using a disposable dev profile containing Keep awake only. Clipboard recording was off; Sound and File organizer were not installed in that profile. Its process and dev notification/profile data were removed, and production settings hash was unchanged. No native keyboard/mouse injection, Setup or real audio changes.
- Raw logs, fixture harness, program-icon check and app captures: `X:/code/chippi-scratch/refresh/d1/`.

## Merge Danger

**Door:** two-way

No saved-settings migration. `FolderMode.Silent`, `RunTrigger.Silent` and serialized `silent` remain intact.

**Blast Radius:** UI

The shared island slider track now follows Orientation and stretches vertically for App volume; Output keeps the horizontal slider. Program-file icons are cached, frozen and release their native handles. Overflow apps wait for Part F3's full Settings list. Physical pointer hover, real app sessions and device output picks remain for the final live check.

Review follow-up: the stale Stop label and no-folders alignment both reproduced before the fix. Refresh now sets every choice label from the current session and hover state, and the no-folders view sits in an overlay Grid. Three added regression cases pass, including Until staying Stop across a timer refresh and a stopped duration returning to its label. Island 166/166, Keep awake 32/32 and Sound 81/81 pass; both Release targets remain warning-free. Hover is simulated inside WPF without native input. [No folders](part-d1-d4/after-organizer-no-folders.png), [lit hover](part-d1-d4/after-keep-awake-hover.png).

The selected hover now lightens with white over the accent, and native icons are read large then reduced to 20. Long app names keep their full tooltip; no extra text-rendering control was added solely to mark a third wrapped line with an ellipsis.
