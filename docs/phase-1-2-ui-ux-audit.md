# Chippi Phase 1-2 UI/UX audit

Audit of the shipped Phase 1 shell and Phase 2 Downloads organizer, measured against vorssaint-utils, which is the feature and quality bar. Done 2026-10-23 on the installed build (`%LOCALAPPDATA%\ChippiApp\current`, v0.1.0) with Downloads organizer and Keep awake installed. Nothing was changed in the code.

**Method.** Each surface was opened through UI Automation and screenshotted: the flyout (Tidy and Tools tabs), all 8 Settings pages, the Downloads organizer's Settings, Rules and History pages, the Keep awake page, and the resting island. These were compared with 15 vorssaint screenshots (`C:\Users\Travis\Desktop\VU Screenshots`, kept out of the repo). Type and color were measured from the pixels of both sets.

**Travis's complaint:** "It feels super unpolished. It's hard to navigate, find what I need and perform actions." And: "Look how modern, polished, clean and minimal Vorssaint is. The text is clean, the font is smaller."

## Summary

There are two root causes, one structural and one visual.

1. **Actions live in Settings, not in the tray panel.** In vorssaint every panel tab is a working tool. In Chippi the flyout mostly points you into Settings, and Settings hides features one level down inside the Features hub. Part of this comes from DESIGN.md §1 itself: "four tabs, one screen each, everything else in Settings."
2. **The visual system is heavy.** The font is not actually bigger: letter heights match vorssaint's. Chippi's text is about 45% heavier and about 6% wider. The chassis is a saturated navy and not a neutral gray, the accent is a gradient where vorssaint uses one flat color, and every row is its own card where vorssaint groups rows in one container.

Usability score (Nielsen's 10 heuristics, 0–4 each): **22 / 40, acceptable.** The weakest areas are consistency (1), recognition over recall (1) and efficiency for repeat use (1).

| Heuristic | Score | Why |
|---|---|---|
| Visibility of system status | 3 | Status line, folder mode line, Keep awake state |
| Match with the real world | 3 | Plain, short words |
| User control and freedom | 3 | Undo on every move, per row and per run |
| Consistency and standards | 1 | Keep awake page breaks every pattern; two Settings buttons in the flyout |
| Error prevention | 3 | Preview before moving, held files |
| Recognition rather than recall | 1 | Features are not in the sidebar; no settings search |
| Flexibility and efficiency | 1 | Common actions take 5–7 steps; no quick actions |
| Aesthetic and minimalist design | 2 | 39 "Coming soon" cards; explainer sentences on most pages |
| Error recovery | 3 | History with undo |
| Help and documentation | 2 | Captions explain, but there is no help or feedback entry |

## Structural issues (ranked)

P0 blocks a task · P1 major friction · P2 has a workaround · P3 polish.

### 1. The flyout can't do much (P1)
- **What:** The Tools tab holds one tile, "Keep awake · Off". Clicking it starts the saved mode, and there is no way to choose a duration there. Keeping the PC awake for an hour takes about 7 steps: flyout › Settings (which reopens on the last page visited) › Features › scroll › Keep awake card › "For a duration" › set › Start. In vorssaint it is one click on a "1h" chip.
- **What:** In Tidy, "Organize now" and "Rules" sit below 8 stash rows, so you have to scroll to find the tab's main actions.
- **Fix:** Every installed feature gets a working panel in the flyout. Keep awake gets 15m · 30m · 1h · 2h · ∞ · Until… chips and a "Keep display on" switch. Tidy pins its actions above the footer and shows 3–4 rows plus "View history".
- **Where:** `src/Chippi.App/Flyout/FlyoutPages.cs`, `src/Features/Chippi.Features.KeepAwake/KeepAwakeModule.cs:27` (its single tool tile), `src/Features/Chippi.Features.Downloads/Views/Tidy/TidyView.xaml`.
- **Conflicts with:** DESIGN.md §1 ("everything else in Settings") and §5 Tray flyout ("Tools tab: a 3-column grid of tool tiles").

### 2. Installed features are not in the Settings sidebar (P1)
- **What:** The sidebar lists 8 app-level pages. Downloads organizer and Keep awake exist only as cards inside Features, and opening one keeps "Features" highlighted with a back arrow. Three pages are nearly empty: Appearance only links to two other pages, Shortcuts says "No shortcuts yet", and Permissions lists 5 "Not in use" rows for features that aren't built. There is no settings search.
- **Fix:** Put a search box at the top of the sidebar. List each installed feature as its own sidebar entry, grouped (Files, System…), with app pages (General, Island & pet, Notifications, About) in their own group. Remove Appearance. Hide Shortcuts and Permissions until something uses them.
- **Where:** `src/Chippi.App/Settings/SettingsWindow.xaml`, `SettingsController.cs`, `Pages/SimplePages.cs`.
- **Conflicts with:** DESIGN.md §5 Settings window (the fixed 7-item sidebar).

### 3. The Features hub is mostly "Coming soon" (P1)
- **What:** 41 cards, 39 of them "Coming soon", so the 2 working features are buried. The preset segment (Essentials · Audio · Power user · Everything · Custom) does almost nothing while nearly everything is unavailable. Each card carries two hidden actions: the card opens settings, the switch installs.
- **Fix:** Show only available features. Fold Coming soon into one collapsed "Coming later" list of compact rows. Hide presets until enough features exist to make them matter. Give installed cards a visible "Open" affordance or let the sidebar (issue 2) carry navigation.
- **Where:** `src/Chippi.App/Settings/Pages/FeaturesPage.cs`, `src/Chippi.App/Settings/Features/FeatureCatalog.cs`.

### 4. Pages don't share one pattern (P2)
- **Keep awake page:** the title appears twice (header and body), controls sit in a narrow loose column instead of row cards, a bare mono "Off" has no label, and it has a "Save settings" button although every other page saves on change (`KeepAwakePage.xaml:33-34`).
- **Rules page:** the test field is an empty box with its hint ("Type a file name to see where it goes") outside it, after a chevron, so the box looks broken.
- **Flyout:** two Settings buttons, the header gear and the footer button.
- **Settings:** reopens on the last page visited, even a deep one like Downloads › History (`SettingsController.cs:17,35`).
- **Fix:** One feature-page template: title, a status or primary-action row, then grouped rows, always saving on change. Keep one Settings button in the flyout.

### 5. The island doesn't fit Windows yet (P2)
- **What:** On a Mac the island sits in the dead space around the camera notch. On Windows the top of the screen is where title bars and browser tabs live, and the resting pill sat on top of the browser's tab strip in the capture. With nothing live, hovering it only says "Nothing live right now".
- **Fix:** Hide the pill at rest or slide it away when a maximized window is under it. Later, make the expanded island a quick launcher for installed features, as vorssaint's grid is. This joins the PLAN.md backlog item on island position.

## Visual issues

Measured from the screenshots. Both sets are at 1× scale.

### Type
| Sample | vorssaint | Chippi |
|---|---|---|
| Row label ("Launch at login" / "Run at startup") | 13 px tall, 28% ink | 13 px tall, 41% ink |
| Sidebar item ("General") | 43 × 10 px, 40% ink | 48 × 10 px, 59% ink |
| Page title ("General") | 61 × 13 px | 64 × 13 px |
| Caption | 10 px tall, 26% ink | 12 px tall, 26% ink |

Ink is the share of the text's bounding box covered by letter strokes, a stand-in for weight. Same height, about 45% more ink and about 6% wider: Chippi reads as bigger because it is heavier, not because it is larger.

- vorssaint uses the system font (SF Pro) at Regular for labels, and Semibold only for headings.
- Chippi uses Manrope, a wide geometric face, at Medium (500) for body and SemiBold (600) for every row title, with Bold and ExtraBold headings (`src/Chippi.UI/Themes/SharedStyles.xaml:13-88`). There is almost no weight contrast, so everything shouts at the same level.
- **Fix:** Use Segoe UI Variable, the Windows 11 system font and the counterpart of SF Pro. Regular (400) for labels and body, Semibold (600) only for page titles, section headers and the selected item.

### Color
| Role | vorssaint | Chippi |
|---|---|---|
| Window background | `#1C1C1E` neutral gray | `#0B0F1F` navy |
| Card | `#262628` | `#131930` |
| Sidebar | `#28282C` | `#0F1426` |
| Selected item | `#0A84FF` solid | `#232E55` with a violet glow border |
| Switch on | `#0A84FF` flat | violet→azure gradient |

- vorssaint is neutral gray with one flat accent. Chippi's blue-tinted navy and gradient accent read as heavier and busier.
- **Fix:** Neutral gray surfaces and one flat accent. Keep the gradient for Gumdrop only.

### Layout and components
- **Grouped rows.** vorssaint puts all the rows of a section in one rounded container with hairline dividers. Chippi gives every row its own card with a gap, which doubles the number of shapes on screen. **Fix:** One container per section, divided rows.
- **Density.** Chippi's rows and cards carry more padding (feature cards are at least 128 px tall) and larger controls (40×23 switch, `SharedStyles.xaml:284`). **Fix:** Tighter rows, a 36–40 × 20 switch.
- **Emphasis.** Chippi stacks fills, borders and glows on selected states. vorssaint uses one move: a solid accent fill. **Fix:** Drop the glows and gradient borders.
- **Icons.** vorssaint tints each row's glyph with the accent, which makes lists scannable. Chippi's icons are gray in chips, and several repeat: the grid glyph means Features, Window layouts and Quick panel; the keyboard glyph covers 4 items; the droplet is both Island & pet and Cleaning mode. **Fix:** One distinct glyph per feature, tinted with the accent.
- **Words.** Most pages open with an explainer sentence ("How Chippi looks and starts, and where its settings live."), against DESIGN.md's Bare Label Rule. **Fix:** Cut them.

## Smaller things (P3)
- Stash rows repeat the full path `C:\Users\…\Downloads\Documents` on every row. History already uses "Downloads › Documents".
- "Recently stashed" mixes in "Restored" rows, and the same file can appear twice.
- The folder picker in Tidy is a name with a tiny chevron and doesn't read as a picker.
- The About page shows raw markdown (`# Third-party notices`, `##`).
- Permissions rows say "Not in use. Also needed by…" when nothing uses them.

## What already works
- The Downloads History with grouped runs and per-row undo is better than anything in the vorssaint shots.
- Copy is short and plain, and controls carry good accessible names ("Undo test.txt", "Install Shelf").
- Gumdrop is distinctive and clearly Chippi's own.

## Constraints
- **vorssaint's TRADEMARKS.md** reserves its name, logo, icon, trade dress and official branding. Chippi can look as native to Windows 11 as vorssaint looks native to macOS, using Windows' own font, surfaces and conventions. It keeps its own name, icon, Gumdrop and accent, and does not copy vorssaint's assets or reproduce its screens pixel for pixel.
- **Approved decisions this audit challenges:** Direction A "Studio Rack" (PLAN.md, DESIGN.md §1–6), Manrope as the UI font (§3), the Synth Gradient Signal Rule (§2) and the four-tab flyout (§1, §5). Changing them needs Travis's sign-off and a DESIGN.md update.

## Decisions (Travis, 2026-10-23)

| Topic | Decision | Replaces |
|---|---|---|
| Base look | Neutral dark grays, like Windows 11 Settings | Studio Rack navy chassis (DESIGN.md §2) |
| Accent | Flat, no gradient. A setting in General: Follow Windows (the default), Blue, Violet, Green or Pink. Each choice has a dark-theme and a light-theme shade | Synth Gradient and the Signal Rule (§2) |
| UI font | Segoe UI Variable: Regular for labels and body, Semibold for titles, headers and the selected item | Manrope at 500–800 (§3) |
| Numbers | Segoe UI Variable with tabular figures | JetBrains Mono and the Readout Rule (§3) |
| Light mode | Must look as good as dark. Being mocked up next to dark | Hopper's light mirror (§2) |
| Flyout | Panel-first: an icon tab strip, one working panel per installed feature, Settings only for configuring | Four tabs with everything else in Settings (§1, §5) |

Gumdrop keeps its own gradient as the one brand moment.

Travis approved the dark mockup on 2026-10-23 ("looks great"). Light mode was still to review.

Mockup (2026-10-23): [Chippi Native Look](https://claude.ai/artifact/5M1W8XSCiDPvCpc5Hrd2Bq). It shows the Settings window (7 clickable pages, with live Theme and Accent controls in General), the Keep awake flyout and the Tidy flyout, each in a dark row and a light row. Travis approved light mode the same day. DESIGN.md was rewritten to the new look on 2026-10-23, and the code plan is [ui-refresh-handoff.md](ui-refresh-handoff.md).

Light palette in the mockup, based on Windows 11 light: window `#F3F3F3`, cards `#FBFBFB` with a 6% black hairline, flyout cards `#FFFFFF`, text `#1A1A1A`, captions `#5D5D5D`, caution `#9D5D00`. Light accent shades carry white text: Blue `#005FB8`, Violet `#6B4FBB`, Green `#107C10`, Pink `#B4235A`. Dark shades carry black text: `#4CC2FF`, `#B79CFF`, `#6CCB5F`, `#FF99A4`.

## Suggested order
- **Now:** structural issues 1–3 (flyout panels, sidebar, Features hub), built on the new visual direction once it is decided.
- **Later:** an icon-strip tab bar the user can reorder, so the flyout scales past 10 features; then island behavior on Windows; then the P3 list.
