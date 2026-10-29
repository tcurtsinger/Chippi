## Summary

Settings now has the island controls for System monitor, Keep awake and File organizer, using the shared Settings layout.

~~~diff
 System monitor
+ live readings without the island limit, every drive, General section
- separate always-visible alert threshold rows
+ alert expanders with current thresholds and switches
 Keep awake
- session switch, Default length, After a restart
+ state and Stop, six live length choices, Until time picker, Keep screen on
 File organizer
+ Organize now on each watched folder; options folded under the chevron
+ unmatched preview files folded into one count row
+ Nothing to organize with Cancel and Add rule opening a new unsaved draft
~~~

## Evidence

- All 16 non-desktop test projects pass in Release: 1,335 tests, then both compact organizer regressions pass after adding the folder-action case. Final source has one additional passing case (1,336 total). UiSmoke was not run.
- Monitor 84, Keep awake 32, Downloads 119 in the full run; the additional folder-action regression also passes. The new monitor case retains all 12 thermal zones past the island limit. Existing Keep awake automation test now uses the live Length choice and still starts/stops the fake power service.
- Release app x64 and ARM64: zero warnings and errors.
- Before is main 0bd2080. After is this branch. Real page controls, unshown WPF renders, 768 content width, fake sensor readings and an empty scratch folder. No files organized, real audio/clipboard read, installed settings changed or native input sent.
- Dark: monitor [before](part-f-system/before-monitor-dark.png) / [after](part-f-system/after-monitor-dark.png), Keep awake [before](part-f-system/before-keep-dark.png) / [after](part-f-system/after-keep-dark.png), folders [before](part-f-system/before-folders-dark.png) / [after](part-f-system/after-folders-dark.png), Preview [before](part-f-system/before-preview-dark.png) / [after](part-f-system/after-preview-dark.png).
- Light: monitor [before](part-f-system/before-monitor-light.png) / [after](part-f-system/after-monitor-light.png), Keep awake [before](part-f-system/before-keep-light.png) / [after](part-f-system/after-keep-light.png), folders [before](part-f-system/before-folders-light.png) / [after](part-f-system/after-folders-light.png), Preview [before](part-f-system/before-preview-light.png) / [after](part-f-system/after-preview-light.png).
- Compared the folder header action with c9: the shared header button adds no height and ends just above the card. Raw logs and renderer: X:/code/chippi-scratch/refresh/f-system/.

## Merge Danger

**Door:** two-way

No saved settings format changes. The controls call the existing module methods. Unmatched files remain outside the approval selection; Add rule first discards the preview, including any staged import, then opens the existing rule editor draft.

**Blast Radius:** UI

The organizer wrappers delegate section/card spacing to the toolkit across its Settings tabs. The monitor reuses its reading formatter and picker logic in a Settings mode while retaining island sizing in the default mode. Full live Settings snapshots follow all page integrations; these renders do not certify native popup interaction.
