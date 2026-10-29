## Summary

Refresh Part E fixes the long-link field, task-checkbox alignment, skipped-sites empty state, Clipboard settings order and settings-snapshot search.

```diff
 Clean URL
- single-line scrolling input; unbounded result
+ padded one to three lines; middle ellipsis beyond three; focus selects all
+ Copy keeps the whole cleaned URL
 Scratchpad preview
- 20 DIP task box aligned to the baseline
+ 14 DIP task box centered on the line
 Settings
+ No sites row, including after the last removal
+ Clipboard history, Paste as plain text, Clean URL
 Snapshot self-check
- searches shipped Clipboard history in Coming soon
+ searches upcoming Quick toggles
```

The clean result keeps no Removed caption. The two Clipboard modules still share the same feature object and tile. No stored settings format changes.

## Evidence

- **Before:** main 0978d9d, rendered from a separate scratch build.
  **After:** this branch, same 520-wide offscreen host and test values.
- [Short link before](screens/part-e/before-short.png) / [after](screens/part-e/after-short.png).
- [Long link before](screens/part-e/before-very-long.png) / [after](screens/part-e/after-very-long.png).
- [Checkbox before](screens/part-e/before-checkbox.png) / [after](screens/part-e/after-checkbox.png).
- [Sites before](screens/part-e/before-no-sites.png) / [after](screens/part-e/after-no-sites.png).
- Full Release suite: 1,253 passed, 0 failed, 16 desktop-input tests skipped. A further last-site-removal regression was then added; final Clipboard feature suite: 57 passed. Scratchpad: 25 passed. Windowless Settings regressions: 2 passed.
- Final Release x64 and ARM64 builds: 0 warnings, 0 errors.
- Debug settings-snapshot completed: 36 images, dark/light and Windows/Violet accents. Log records "R2 snapshot checks passed". This used a new dev profile with no installed features, island off and recording off. No real Downloads folder was watched.
- The snapshot profile, its shortcut and registration keys were removed. Production settings hash stayed unchanged; the installed Chippi process was retained.
- Long-link regression checks source preservation, three-line limit, both visible ends, whole-link focus selection and full-value Copy on a fake clipboard. Padding regression checks 28/60 DIP heights and disabled scrolling.
- Raw evidence: X:/code/chippi-scratch/refresh/e/ (test-results, all-tests.log, build logs, snapshot images, render harness and cleanup record).

## Merge Danger

**Door:** two-way

Reverting restores the previous layouts and registration order.

**Blast Radius:** UI

The specialized field template affects only manual Clean URL. Snapshots and offscreen renders cover layout; physical mouse/keyboard behavior was not exercised. Part A's shell remains a separate PR.