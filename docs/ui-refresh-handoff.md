# Chippi Phase 2.5 brief: Native look refresh

Phase 2.5 moves everything built so far (the shell, Keep awake and the Downloads organizer) to the native Windows 11 look approved on 2026-10-23, and restructures the flyout and Settings so actions live in the flyout. It comes before Phase 3 so Sound isn't built on the old look. No new features.

Read first, in order:
1. This brief.
2. `DESIGN.md` (rewritten 2026-10-23). It wins over this brief and over taste.
3. The mockup: [Chippi Native Look](https://claude.ai/artifact/5M1W8XSCiDPvCpc5Hrd2Bq). Press Play to click through. Its file names and rule examples are sample data.
4. [phase-1-2-ui-ux-audit.md](phase-1-2-ui-ux-audit.md): why each change is being made.

## 1. Decisions (Travis, 2026-10-23)

| Topic | Decision |
|---|---|
| Look | Native Windows 11: neutral gray layers, grouped cards with dividers, no gradients except Gumdrop, no glows |
| Accent | A General setting: Follow Windows (the default), Blue, Violet, Green, Pink. Each has a dark and a light shade (DESIGN.md §2) |
| Font | Segoe UI Variable, Regular for reading and Semibold for structure. Numbers use tabular figures. Manrope and JetBrains Mono go |
| Flyout | Panel-first: an icon tab per installed feature, each a working panel. Keep awake gets duration chips; Tidy keeps its actions visible |
| Settings | Sidebar with search and the installed features listed directly. Add features replaces the Features hub. Appearance goes; Shortcuts and Permissions hide until something uses them |
| Light mode | Equal to dark, approved in the mockup |
| Gumdrop | Unchanged, and still the one gradient |

## 2. Calls made while writing DESIGN.md (approved by Travis, 2026-10-23)

All six are in DESIGN.md:
- **Default theme is Follow Windows** (it was Dark). Only new installs see it; a saved choice stays.
- **The island is movable** (Travis approved the mockup, 2026-10-23). This replaces the earlier proposal to hide it when nothing is live: Travis uses hover-to-open, so it must stay reachable. It is a small half-oval tab the user drags to the top (the default), left or right edge, where it snaps. Hovering opens it, and at rest it never hides. DESIGN.md §5 Island has the full spec; the mockup's "Movable island" row is the reference.
- **Settings opens on the page its caller asks for**, or the first sidebar item, instead of the last page visited.
- **Tool tiles and the Tools grid go.** Keep awake becomes a flyout panel, and later quick actions become panels too.
- **The violet Source badge ("From Hopper") goes.** Only Overlap (caution) and Neutral badges remain, and Coming-soon features show as plain names under Coming later.
- **Gallery-only controls go:** AppVolumeRow, StatTile, StashRow and IconToggleButton. Phases 3 and 4 design their own rows on the new look.

## 3. What exists today

Mapped from the code on 2026-10-23. Line numbers are as of `b3b672e`.
- **Tokens:** `Dark.xaml` and `Light.xaml` hold 67 matching keys; `Palette.xaml` holds 32 unswapped keys (island, pet, deep gradient, badge tints). Code reads only `*Brush` keys, all through DynamicResource or `SetResourceReference`, so token values can change without touching call sites.
- **Gradients:** `AccentGradientBrush` has 12 references (switch, slider, meter, checkbox, tool tile, island equalizer and progress, Tidy chip, rule and folder chips), `AccentGradientDeepBrush` 4, `SegmentActiveBgBrush` and `SegmentActiveBorderBrush` 3 each. `FocusRingBrush` is violet, with 10 references.
- **Fonts:** Manrope (4 TTFs) and JetBrains Mono (2) are embedded by `Chippi.UI.csproj:18`. `UiFont` has 15 uses and `MonoFont` 10. There are 30 explicit `FontWeight`s outside SharedStyles, and the non-standard `MonoMedium`/`MonoSemiBold` weights (436/472) are tied to the JetBrains files. Nothing sets tabular figures.
- **Accent:** nothing reads the Windows accent. `Chippi.App` targets `net10.0-windows10.0.19041.0`, so `UISettings` is available there; `Chippi.UI` targets plain `net10.0-windows` and can't call it.
- **Flyout:** tabs are a fixed enum, `FlyoutTab { Sound, System, Tidy, Tools }` (`ModuleUi.cs:18`), shown as an equal-width 4-column segment with no overflow. Modules can't name their tab or pick its icon. Keep awake contributes only a tool tile.
- **Settings:** the sidebar is a static 8-item array (`SettingsWindow.xaml.cs:29-39`). Module pages open only under Features through `ModulePageHost` with "Back to Features". `SettingsController` remembers the last page in memory.
- **Couplings to undo:** `DownloadsPage.cs:32` uses a -56 top margin to sit beside the page title. `KeepAwakePage.xaml` draws its own title and wraps itself in a ScrollViewer with `MaxWidth` 640. There are three eyebrow implementations: `c:Eyebrow`, Downloads `Ui.Eyebrow` and a TidyView TextBlock.
- **Island:** `IslandWindow.xaml:23` merges `Dark.xaml` locally, so any app-level accent brush defined in Dark/Light is shadowed inside the island. `IslandStyles.xaml:22` uses `AccentGradientDeepBrush` as a StaticResource, so it won't update.
- **Downloads settings views:** 2,190 lines built in code through the static `Ui` builder (`Views/Settings/Ui.cs`). Restyling the builder carries most of them.
- **Dead code:** `SegmentButtonStyle`, `DeviceListBoxItemStyle`, `MeterProgressBarStyle`, `AccentVerticalBrush`, `CardGradientBrush`, `CardShadow`, `SegmentBrush`, `PillBrush`.
- **Tests tied to structure:** UiSmoke finds "Open settings", "Flyout sections" › "Tidy", the "Features" nav item, "Search features", "Back to Features", "Keep awake settings", the Audio routing card's badges and disabled switch, and real Esc returning to the hub. `ModuleRegistryTests`, `ModuleServicesTests` and `KeepAwakeRecoveryTests` use `AddToolTile` and `SetSettingsPage`. `SettingsViewTests` pins the `module:downloads-organizer/...` link format.

## 4. Steps

Sizes are rough, for one builder: **S** is up to about 2 hours, **M** about half a day, **L** about a day.

### R0 · Spike (S), lands first
1. Render "Segoe UI Variable Text" at FontWeight 400 and 600 in a WPF test window next to static "Segoe UI". If 600 isn't a true semibold, use static Segoe UI everywhere (DESIGN.md §9). Record the result here.
2. Check `Typography.NumeralAlignment="Tabular"` changes the digits in the chosen family.
3. Read `UISettings.GetColorValue(AccentLight2 / AccentDark1)` from `Chippi.App`, change the accent in Windows Settings, and confirm `ColorValuesChanged` fires.

### R1 · Foundation (L), after R0
- **Tokens:** rewrite `Dark.xaml`, `Light.xaml` and `Palette.xaml` to DESIGN.md's front matter.
  - Keep the existing key names where the meaning holds, so call sites don't move: WindowBackground, Surface → card, Panel → flyout card, Input, ButtonBackground/Hover → control, Border*, Text*, Success, Warning → caution, Danger → critical.
  - Retire the gradient keys and point every reference at `AccentBrush` / `OnAccentBrush`. `FocusRingBrush` becomes the text color.
  - Delete the dead keys and styles listed in §3.
- **Accent:**
  - `AppSettings` gains `Accent` (FollowWindows, Blue, Violet, Green, Pink; default FollowWindows) and `IslandEdge` (Top, Left, Right; default Top) with `IslandPlace` (0–1 along the edge; default 0.5). New installs default `Theme` to System.
  - `ThemeManager.SetAccent(dark, light)` (Chippi.UI) writes `AccentBrush`, `OnAccentBrush` and an island-only `IslandAccentBrush` (always the dark shade).
  - A small `AccentSource` in `Chippi.App` resolves the setting, reads `UISettings` for Follow Windows, and marshals `ColorValuesChanged` to the dispatcher.
- **Type:**
  - Point `UiFont` and `DisplayFont` at the R0 family.
  - Re-map the text styles: Display → page title 20/600, Headline → section 14/600, Title → body 14/400, Body → body, Caption → 12/400.
  - Rename `MonoTextStyle` and `MonoReadoutTextStyle` to `NumberTextStyle` (tabular), and remove `MonoFont`, `MonoMedium` and `MonoSemiBold`.
  - Remove the 30 stray `FontWeight`s, keeping only the Semibold cases the Weight Rule allows.
  - Add a `CodeFont` (Cascadia Mono, Consolas) for regex and templates.
  - Delete the Manrope and JetBrains files, their csproj embed and their OFL copies.
- **Controls** (DESIGN.md §6):
  - Restyle Secondary → Button, Primary → Accent button, Switch (with an optional On/Off label), Segmented, Combo, Checkbox, Text field, SearchBox, IconButton, Badge (Overlap and Neutral), StatusDot (no glow), Slider, Tooltip and Scrollbar (fix horizontal orientation).
  - Add Card (a panel that draws dividers between its rows), SettingRow (label, caption, control), ExpanderRow, SelectorBar, a nav item style, a flyout tab style, DurationChip and Swatch.
  - Replace the three eyebrows with the section text style and delete `Eyebrow`.
  - Delete `ToolTile`, `FeatureCard`, `AppVolumeRow`, `StatTile`, `StashRow` and `IconToggleButton` once R2 and R3 stop using them.
- **Gallery:** update `ControlGalleryWindow` to show every control in dark and light with two accents. Reviewers use it as the check.

### R2 · Settings window (L), after R1
- **Frame:** 1120×720, a 40px titlebar, the 272px sidebar and content padding per DESIGN.md §5.
- **Data-driven sidebar:** "Your features" lists the registry's installed modules that have a settings page, then Add features. "Chippi" lists General, Island & pet, Notifications and About.
  - Module pages become first-class nav items: drop `ModulePageHost`'s back button and the Esc-to-hub behavior.
  - `Navigate("module:<id>/<section>")` keeps its format.
- **Opening:** the caller's target, else the first sidebar item. Remove `_lastPage`.
- **Pages:**
  - **Add features** replaces `FeaturesPage`: Available rows plus the Coming later expander. The preset UI is hidden; first-run Essentials still applies in the registry.
  - **General:** add the Accent row; Theme becomes Dark · Light · Follow Windows.
  - **Island & pet:** add Position (Top · Left edge · Right edge, plus Reset); the preview drops to 48px.
  - **Notifications:** the destination tiles become switch rows.
  - **About:** notices open as formatted text, with headings parsed from the markdown.
  - Remove the Appearance page, and hide Shortcuts and Permissions while no feature uses them.
- **Search v1:** build each page once off-screen, index the labels the row builders create, and show the matches grouped by page. Clicking a match opens its page.

### R3 · Flyout and Keep awake (L), after R1
- **Contract:** replace the `FlyoutTab` enum, `AddToolTile` and `ToolTileModel` with `IModuleUi.AddFlyoutPanel(string name, string icon, Func<object> createView, Func<object>? createIslandView = null)`. The island view is the compact version shown in the island; a feature without one doesn't appear there.
  - Update the registry, `FeatureEntry`, and the call sites in both modules. Downloads registers "Tidy" with the folder glyph.
  - Update the Core tests that use the old names.
- **Shell:**
  - Header: Gumdrop 24, name and status, with no settings button.
  - Tab strip: 40×36 tabs in sidebar order, with a More tab after 8. Keep the automation name "Flyout sections"; tab names are the panel names.
  - Footer: subtle Settings and Quit buttons.
  - Height up to 640, with 8px corners and the flyout shadow. Reopen on the last tab used.
  - Empty state: "No features on yet" with Add features.
  - Restyle the tray menu.
- **Keep awake panel:** a status row with a switch, the 6 duration chips (one click starts or stops), "Until…" opening a time popup, Keep screen on, and More options.
- **Keep awake page:** the restyled rows (Default length, Keep screen on, After a restart). Remove its own title, ScrollViewer, MaxWidth and the Save button, so settings save on change.

### R4 · Downloads views (L), after R1; the Tidy panel also needs R3's contract
- **Tidy panel:**
  - Folder card with picker and mode segment.
  - "Organize now" and "Rules" always visible.
  - "Recent" with a History link and up to 4 rows, held rows first.
  - Destinations show as "Documents · 23m".
  - Undone rows read "Restored to Downloads" with no button.
- **Settings views:**
  - Restyle the `Ui` builder onto R1's Card, SettingRow and SelectorBar, then fix what the builder doesn't carry.
  - `DownloadsPage`: a SelectorBar (automation name stays "Section") under the shell's title; delete the -56 margin.
  - Folders become expander rows.
  - The test field becomes a card.
  - History becomes one card per run; Clear uses the Critical button.
  - Preview gets the new checkboxes.
  - Regex and templates use `CodeFont`.
  - Delete `Ui.Eyebrow`.

### R5 · Movable island (L, about 1–2 days), after R1 and R3's contract
- **Tab:** the docked half-oval per DESIGN.md §5, at its sizes and with its live glyph. The window is sized to the tab while closed.
- **Drag and snap:** a 4px threshold, the follow bubble, the target-edge line and a 180ms slide into place.
  - It uses mouse capture and `SetWindowPos`; the window never activates.
  - Exclude the taskbar edge and the corners. Save `IslandEdge` and `IslandPlace`.
  - Dropping on another display sets One monitor for it.
- **Open and close:** 150ms hover open, 250ms close after leaving both, Esc, and Enter/Space on the focused tab.
- **The island:** placement beside the tab toward the screen center; the header, the live activity card, then each running feature's island view from R3's contract. The old resting, live and expanded pill layouts go.
- **Colors and type:** `IslandAccentBrush` with on-accent glyphs, `IslandStyles.xaml:22` made dynamic, and fonts per R1.
- **Keep:** the display setting, fullscreen hiding and the activity priority order.

### R6 · Tests and cleanup (M), last
- **UiSmoke:**
  - The entry point becomes the footer "Settings". Tidy stays "Flyout sections" › "Tidy".
  - Features → "Add features", and "Search features" stays.
  - Module pages are found as nav items, not through "Keep awake settings" or "Back to Features".
  - Replace the Audio routing card checks with Coming later.
  - Drop the Esc-to-hub check.
  - Keep "No element name contains `{`" checks.
- **Notices and docs:** update `THIRD-PARTY-NOTICES.md` and the About fallback text (`SimplePages.cs:90`) for the removed fonts. Mark `docs/phase-1/screens` as the old look.
- **Review screenshots:** run `--settings-snapshot` and a flyout snapshot in dark and light with Follow Windows and Violet, and compare them with the mockup side by side.

### Order

```
R0 → R1 ─┬─ R2 ─────────────────┐
         ├─ R3 ─┬─ R4 (Tidy)     ├─ R6
         │      └─ R5 (island) ──┤
         └─ R4 (settings views) ─┘
```

**File ownership,** so parallel workstreams don't collide:
- **R1:** `src/Chippi.UI/**`, `AppSettings.cs`, and the accent wiring in `App.xaml.cs`.
- **R2:** `src/Chippi.App/Settings/**`.
- **R3:** `src/Chippi.App/Flyout/**`, `src/Chippi.App/Tray/**`, `src/Chippi.Core/Modules/**`, `src/Features/Chippi.Features.KeepAwake/**`, and the one-line Tidy registration in `DownloadsModule.cs`.
- **R4:** `src/Features/Chippi.Features.Downloads/Views/**`.
- **R5:** `src/Chippi.App/Island/**` and `src/Chippi.App/Pet/**`.
- **R6:** `tests/Chippi.UiSmoke/**` and the docs.

## 5. Risks

- **Variable font weights.** R0 settles it before anything is built on Segoe UI Variable.
- **Accent shadowing in the island.** The island's local `Dark.xaml` merge hides app-level accent keys. R1's separate `IslandAccentBrush` avoids that.
- **Off-thread accent events.** `UISettings.ColorValuesChanged` fires on a background thread. Always dispatch.
- **Downloads views are big.** Restyle through the builder first and hand-fix second, rather than rewriting 2,190 lines.
- **Test churn.** R2 and R3 each update the unit tests they break; R6 owns the UiSmoke rewrite.
- **Search scope.** v1 matches labels only: no synonyms, no deep links to a row.
- **Light-theme contrast.** Every screen gets checked in light with a non-blue accent before merge.

## 6. Process

Carried from Phase 2:
- Branch and worktree per workstream. Each owner touches only the files they own (§4).
- A second member reviews before merge. Marge merges and pushes `main`.
- **Review bar (Travis, 2026-10-22):** only bugs that break functionality or affect normal use block a merge. Theoretical edge cases go in one "not fixing" line.
- Scratch and probes go in `X:\code\chippi-scratch\phase-2.5\`. Dev runs use `CHIPPI_DEV_PROFILE`. Nobody installs on Travis's PC without asking.
- At the end, run the [end-of-phase checklist](end-of-phase-checklist.md).

## 7. Done when

- Build and test pass with 0 warnings for win-x64 and win-arm64.
- Every mockup screen matches in dark and light, with Follow Windows and with Violet. Changing the accent in Windows Settings recolors Chippi live.
- Keeping the PC awake for an hour is one click from the flyout. "Organize now" is visible without scrolling.
- The island tab can be dragged to the top, left or right edge, snaps there, reopens there after a restart, and opens on hover. It never covers a window's caption buttons.
- Installed features appear in the Settings sidebar. Add features lists only working features, with the rest under Coming later.
- No Manrope or JetBrains Mono files, keys or credits remain. No gradient remains outside Gumdrop: `LinearGradientBrush` appears only under `Pet`.
- No label has a description under a page title, and every new label follows the Bare Label Rule.
- The installer still installs, updates in place and uninstalls cleanly.
