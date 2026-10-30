# Chippi brief: Shell redesign (the blob widget)

The island at the top of the screen is retired. Chippi becomes a small blob you drag anywhere on the desktop; clicking it opens a 520 × 360 panel in the Windows 11 widget look, with the pet's violet as the accent, and a new blob replaces Gumdrop. Why: the black pill at the top center, the three-across tile grid and the round buttons around the panel read as vorssaint's trade dress (DESIGN.md §1, Boundaries). The new shell stays Windows-native and makes the pet the thing people recognize.

Read first, in order:
1. This brief.
2. `AGENTS.md`.
3. The approved mock (Travis, 2026-10-29): [final-light.png](shell-redesign/design/shots/final-light.png) and [final-dark.png](shell-redesign/design/shots/final-dark.png). The source is [final.html](shell-redesign/design/final.html) (`?theme=dark` for dark); render it with [render.ps1](shell-redesign/design/render.ps1). The clipboard text, files, apps, devices and readings in it are sample data.
4. The rounds that led there: [looks.png](shell-redesign/design/shots/looks.png) (A was chosen, with B's violet as its accent), [mascots.png](shell-redesign/design/shots/mascots.png) and [mochi-jelly.png](shell-redesign/design/shots/mochi-jelly.png) (Mochi's body with Jelly's face, no blush, was chosen).
5. `DESIGN.md` §5, Island pages: every page keeps its content and rules; only the look changes. §7 (Gumdrop) is replaced by the new blob.

## 1. Decisions (Travis, 2026-10-29)

| Topic | Decision |
|---|---|
| Shape | The blob is a widget on the desktop, dragged anywhere. Clicking it opens the panel out of it, toward the middle of the screen. No top-center tab and no edge docking |
| Look | "Windows 11 widget": Windows materials that follow light and dark mode, Segoe UI Variable, 8px corners |
| Accent | The pet's violet to blue on whatever is on: light `#8F5CFF` → `#5B86FF` with white text; dark `#B78FFF` → `#82AEFF` with `#170F2E` text |
| Home | Like Windows Quick Settings: a Now Playing card, a 4 × 2 grid of labelled buttons with each tile's status under its name, the volume slider, and a footer with the blob, "Chippi" and Settings |
| Pages | Same 520 × 360 panel, nothing scrolls, today's content and rules. Each page gets a back arrow to home |
| Pet | A new blob called Chippi, like the app: Mochi's soft rice-cake body with Jelly's big shiny eyes and open grin, no blush. Idle, asleep (shut eyes, "o" mouth, z) and listening (happy eyes, headphones). One flat pet color with soft shading, so every pet color in Settings works |
| Gone | The tile grid, the round (orbit) buttons, the tab shapes, edge docking, Gumdrop |

## 2. Defaults chosen while planning (Travis can overrule any)

- **Clicks:** the blob opens the last page, or home. The song chip opens Now Playing; it replaces the live round button. Right-click keeps today's menu (Open settings, Quit).
- **Where it starts:** the main display's bottom right, 24 in from the work area's corner, for new and existing installs. The position saves as the display plus fractions of its work area. A position that is off screen after a display change falls back to the bottom right of the main display.
- **Display setting:** Main display and One monitor stay. Follow active window goes: a widget you place yourself shouldn't jump between screens.
- **Home toggles:** Keep awake's button starts the last-used duration (1 hr the first time) and stops it when on; Microphone's mutes and unmutes. Their arrow opens the page. Buttons follow Settings › Island & pet's order and hiding; fewer features means fewer buttons, and the layout doesn't move.
- **Widget motion:** hover grows the puck 4px and wakes the blob; a drag lifts it (scale 1.12, a slight tilt, a deeper shadow); release drops it where it is, kept inside the work area. Reduced motion snaps. It hides over fullscreen apps and never hides at rest.
- **Panel material:** try real acrylic first (PR 1 measures it). If opening or switching pages goes over the 6 ms frame budget at 165 Hz, ship a solid tinted surface instead: light `#F5F5F6`, dark `#28282C`, both at 96%.
- **Branching:** the PRs below merge into an integration branch, `redesign/shell`, after review and green CI. Travis installs from it for live checks, and it merges to `main` once, when the shell is complete. That keeps every install from `main` coherent.

## 3. The blob's name

Chippi, like the app (Travis, 2026-10-29). Settings › Island & pet and DESIGN.md call it Chippi; its screen-reader name is "Chippi, idle" (or asleep, listening); its control is `ChippiPet`. Nothing is left open.

## 4. What exists today

Mapped on 2026-10-29 at `4ae231c`.
- **Tokens:** `src/Chippi.UI/Themes/Palette.xaml:10-21` holds the `Island*` keys, deliberately never themed (always dark). `src/Chippi.App/Island/IslandStyles.xaml` (406 lines) holds the island's control styles. Feature views read the keys through `SetResourceReference` (Now Playing 18 uses, Clipboard 17, Shelf 16, Scratchpad 14, Volume 13, Tidy 11, System monitor 10, Routing 9, Keep awake 6), so re-theming the keys reaches every page.
- **The tab:** `IslandWindow.xaml(.cs)` (1,109 lines): docking and snapping, swell, the song and equalizer (`TabLevel`), activity and mic glyphs, drag, the Shelf drop target, the tab menu, fullscreen hiding and display following. `IslandGeometry.cs` (255 lines): tab sizes, docking, snapping and the orbit layout.
- **The panel:** `IslandPanelWindow.xaml(.cs)` (882 lines): the morph out of the tab (open, close, page), page hosting, `PageHeader`, orbit buttons, keyboard and Esc. `IslandGrid.cs` (180 lines) is the tile grid; `IslandLayout.cs` the orbit settings.
- **Pet:** `src/Chippi.App/Pet/Gumdrop.cs` (572 lines: moods Idle, Sleepy, WideAwake and Celebrating, blink, squish, sparks, `PetColor`) and `PetSheet.cs`, used in about 12 files, Settings › Island & pet among them.
- **Tests:** `Chippi.Island.Tests` (199 in Release). The geometry, tab, drag and panel tests assume the tab and its edges; expect to rewrite them rather than patch them.

## 5. The work, in order

Each is one PR into `redesign/shell`, with its own worktree, review and proof (AGENTS.md, "Done means used, with proof"). Times are agent hours.

1. **Theme and controls (about 3 h).** The `Island*` keys move into the light and dark theme dictionaries and follow the app theme. Add the violet accent keys and fill gradient. Restyle `IslandStyles.xaml` to the mock: buttons, the split button, segments, the slider with a ringed thumb, cards, radios, the switch. Spike acrylic on the panel and measure it. Proof: every page's shot in both themes beside the mock; open and page-switch frame times.
2. **The new blob (about 2 h).** `ChippiPet`, a new pet control: geometry, the three moods, plus Celebrating and Wide awake mapped onto it, blink and squish, every pet color. It replaces Gumdrop everywhere, Settings' preview and Nothing playing included. Proof: a sheet at 24, 32, 48 and 72 in each pet color and both themes.
3. **Panel chrome (about 2 h).** A shell header with a back arrow on every page; the orbit buttons and their Settings go. Esc closes from any page, as today. Proof: tests for back and Esc; shots.
4. **Home (about 3 h).** `IslandGrid` becomes the Quick Settings home. Toggles need a small module API: `IslandTileSpec` gains an optional toggle (is on, toggle). The volume slider shows only with Sound installed and its arrow opens Sound. Proof: shots with 1, 5 and 8 features; toggle tests.
5. **The widget (about 6 h, the largest).** `IslandWindow` becomes the puck and its chips: free drag with the saved position, hover and lift, the song chip (reusing the song scroll and `TabLevel`'s bars), the activity chip (the highest activity, such as "1 hr left"), the red mic badge, the Shelf drop target (a 250 ms hold opens Shelf), the right-click menu, fullscreen hiding and display changes. The panel anchors to the puck, opens toward the screen's middle, stays inside the work area, and morphs out of the puck. Edge docking, tab shapes and the orbit layout leave `IslandGeometry`. Proof: a live check with music (gated by `build/fullscreen-check.ps1`), a drag across displays if there are two, frame timings for open, close and drag, and new geometry tests for every corner and a taskbar on each edge.
6. **Pages to the mock (about 3 h, split if it runs long).** Now Playing's accent play disc, App volume's strips, Keep awake's cards, File organizer's segment, Clipboard's open row, Scratchpad's tabs and segment, Shelf's tiles and actions, System monitor's cards. Proof: each page's before and after beside the mock.
7. **Docs and release (about 1 h).** Rewrite DESIGN.md §5 (the widget and panel) and §7 (the new pet) from what was built; update PLAN.md and AGENTS.md; refresh the installer; merge `redesign/shell` into `main`.

About 20 agent hours in all, roughly three working days with reviews and live checks.

## 6. Risks

- **Acrylic on a layered, transparent window.** Blur behind can cost compositor time, and the old page-switch stutter was compositor-side. Measure before committing to it; the solid surface is the fallback.
- **Free placement.** Mixed DPI across displays, a taskbar on any edge, a display removed while the panel is open, and a puck in a corner, where the panel must open inward. The geometry tests cover each.
- **Light on light.** Over a light desktop the puck and chips need their edge and shadow to stay visible (the mock has both).
- **Muscle memory.** The tab leaves the top center. The first run after the update could briefly point at the new spot; out of scope unless Travis asks.
- **Test churn.** Rewriting the island tests is part of PRs 3 to 5, not a follow-up.
