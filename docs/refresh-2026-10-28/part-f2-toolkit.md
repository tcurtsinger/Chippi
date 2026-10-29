## Summary

Settings pages get one shared layout API in `Chippi.UI.Controls.SettingsLayout`. About is the proof migration; feature-page conversions follow after this merges.

~~~diff
 Chippi.UI
+ Section, Card, Row, Empty, Expander, Combo, AddButton
+ shared 48 / 60 row heights and 16,8 / 50,8,16,8 padding
- 34-high standard segmented control
+ 32-high control, fitting a 48-high row
 About
- local row/card wrappers
+ shared Row/Card factories, same text and handlers
~~~

[API and usage](settings-toolkit-api.md).

## Evidence

- Six WPF regression cases pass: combo/button/segment rows stay 48 and become 60 with a caption; section/card gaps stay 52 with or without a header button; the Add action runs once; Empty is one caption-color row; expansion indents 50 and preserves switch state; About uses the shared rows and retains its buttons.
- Full Release suite: 1,313 passed, 0 failed, 16 desktop-input tests skipped. Island: 172.
- Release x64 and ARM64: 0 warnings, 0 errors.
- All renders are unshown WPF controls at 768 content width, with invented gallery state. Before uses main `dbc8dab`; after uses this branch. The gallery compares existing controls/layout with the new factories. About is the actual page from each source tree, without invoking View or Donate.
- Gallery [dark before](part-f2-toolkit/before-dark.png) / [after](part-f2-toolkit/after-dark.png), [light before](part-f2-toolkit/before-light.png) / [after](part-f2-toolkit/after-light.png).
- About [dark before](part-f2-toolkit/before-about-dark.png) / [after](part-f2-toolkit/after-about-dark.png), [light before](part-f2-toolkit/before-about-light.png) / [after](part-f2-toolkit/after-about-light.png).
- Raw test/build logs and renderer: `X:/code/chippi-scratch/refresh/toolkit/`.

## Merge Danger

**Door:** two-way

No settings format or module behavior changes. The helpers leave feature state, bindings and handlers with their callers.

**Blast Radius:** UI

Shared row/expander padding and standard segmented-item height affect existing pages too. Explicit island item-height overrides remain. The helper sets in the other feature folders stay until their owners migrate those pages. No actual Settings snapshot or native input ran for this toolkit; the full Settings-window check follows integration with F1 and the page conversions.
