# Phase 7 design reference

Preserved from [PR #287](https://github.com/tcurtsinger/Chippi/pull/287), commit `39479294c764422eea9976b419fd1d330c0ac5c9`, on November 10, 2026. These are historical alternatives and future feature concepts, not a specification of the current app. The original pages, screenshots, and selection metadata are kept together for reference.

Open any HTML file in a browser. Each collection contains numbered alternatives; `design.json` records the original choices where one was made. A `picked` or `built` entry describes the state when the mockup was created, not current implementation status.

| Collection | Starting page |
|---|---|
| Home | [v1](home/v1.html) |
| Capture panel | [v1](capture-panel/v1.html) |
| Frozen screen | [v1](capture-overlay/v1.html) |
| Capture results | [v1](capture-results/v1.html) |
| Displays | [v1](displays/v1.html) |
| Capture settings | [v1](settings-capture/v1.html) |
| Shortcut settings | [v1](settings-shortcuts/v1.html) |
| Recording setup and controls | [v1](recording/v1.html) |
| Recording results | [v1](recording-page/v1.html) |
| Recording settings | [v1](settings-recording/v1.html) |
| Convert | [v1](convert/v1.html) |
| Media tools settings | [v1](settings-media-tools/v1.html) |
| Update apps | [v1](update-apps/v1.html) |
| App update settings | [v1](settings-app-updates/v1.html) |
| Uninstaller | [v1](uninstaller/v1.html) |
| Clean junk files | [v1](clean-junk/v1.html) |
| Cleaner settings | [v1](settings-cleaner/v1.html) |
| Ports | [v1](ports/v1.html) |

[System samples](system.html) and [tokens.css](tokens.css) reproduce the theme at the time of these mockups. They are local to this reference library; the app's XAML themes and root `DESIGN.md` remain the current sources of truth.

The recording concepts include region selection and separate floating controls. The shipped recording implementation from [PR #335](https://github.com/tcurtsinger/Chippi/pull/335) follows the later Window/Screen and puck-control decisions instead. Concepts for Convert, App updates, Uninstaller, Cleaner, and Ports do not imply those features have shipped or authorize implementation.
