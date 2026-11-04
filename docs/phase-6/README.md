# Phase 6: Windows and input

Window layouts and snapping, app switcher, close protection, focus follows mouse, smooth and linear scrolling, mouse button shortcuts, click and key debounce, super key, text snippets and a shortcut manager (PLAN.md). Shaped with Travis on 2026-11-04 from four pieces of research: behavior specs A and B, the architecture, and the surfaces (all in this folder once their PRs merge).

## Decided (Travis, 2026-11-04)

- **Scope (task 197):** all ten, each in full. Travis relies on none of PowerToys, Snap layouts, AutoHotkey, a mouse app or a text expander, so nothing is skipped or reduced because another tool covers it. Other tools are the bar to beat.
- **Games (tasks 198 and 211, #1):** one **Always on** switch covers every Phase 6 input feature, Debounce included. It's off by default: while a fullscreen game or app is in front on that display, they pause. On means they act everywhere. There's no per-feature switch.
- **Radial menu (task 211, #2):** stays outside Always on. Settings › Radial menu gets its own switch for opening over fullscreen apps, on by default, which keeps the 5.5 behavior.
- **One action model (task 208):** every verb (snap left, next monitor, open the App switcher, a snippet) is one shared action that any door can fire: an Actions tile, Search, a Radial slice, a key shortcut, a Caps Lock combo or a mouse button. A background feature's on/off can also be an Actions tile.
- **Actions are grouped (task 208):** new tiles go into menus inside Actions by topic, like Plan, Power and Quit, not as loose tiles.
- **Snapping lives in Actions (task 209):** no new home tile; the home stays at 11 tools.
- **Shortcuts (tasks 210 and 211, #3):** Mouse buttons, Super key and Shortcut manager become one feature, **Shortcuts**, with one Settings page. It binds any action to a key, a Caps Lock combo or a mouse button and flags clashes.
- **Feature calls (task 211, #4 to #11, all as recommended):**
  - Snap comes first. Saved app arrangements with relaunch are a separate step later in the phase.
  - Chippi's zones show on Shift+drag, and Windows' own Snap bar keeps the plain drag to the top.
  - The App switcher gets its own shortcut first. Taking over Alt+Tab becomes an option once it has proven itself.
  - Close protection: press Alt+F4 twice. The first press shows "Press Alt+F4 again to close" on the bar, and Chippi sends no keys. It's on in every app, and Ctrl+W protection is turned on per app. It covers keys only.
  - Smooth scrolling has per-app speeds and an excluded-apps list in the first build.
  - No mouse-button drag gestures for now.
  - Caps Lock combos: Right Alt isn't offered, and Win isn't in the default set.
  - Text snippets are typed out, never pasted, so the clipboard is untouched. No paste mode and no import or export at first.

## Lead calls (Marge)

- **Build order, three waves:**
  1. The shared input layer (`Chippi.Input`: one keyboard hook and one mouse hook with a watchdog), shared actions, the shortcut service and the pause with Always on. Then Debounce, Focus follows mouse and Close protection.
  2. Shortcuts and Smooth scrolling.
  3. Text snippets, Snap and the App switcher.
- **Focus follows mouse** uses Windows' built-in setting (250 ms, the window raised) and puts the original setting back when it's off or paused.
- **Key debounce** tops out at 200 ms.
- **Settings:**
  - Under Windows: Window layouts, App switcher, Mouse (Focus follows mouse, scrolling, click debounce) and Keyboard (Close protection, key debounce).
  - Shortcuts grows the existing Chippi › Shortcuts page.
  - Text snippets sits under Clipboard & text.
- **Home:** the five- and six-across layout ships when Phase 7 takes the home past 16 tools.

## Hard limits

- **Admin windows:** hook features can't act on a window running as admin. Over one, Caps Lock is plain Caps Lock and Snap can't move it.
- **Anti-cheat:** untested on Travis's games. With Always on off, Chippi sends no input into a fullscreen game.

## Briefs

| Brief | Owner | Mockup |
|---|---|---|
| Input layer, shared actions, pause (`input-layer.md`) | Kayla | None, no new surface |
| Debounce (`debounce.md`) | Herb | Settings rows only |
| Focus follows mouse (`focus-follows-mouse.md`) | Karl | Settings rows only |
| Close protection (`close-protection.md`) | Karl | The bar's line |
| Shortcuts (`shortcuts.md`) | Herb | Lumen, task 212 |
| Smooth scrolling (`smooth-scrolling.md`) | Karl | Settings rows only |
| Text snippets (`text-snippets.md`) | Herb | Settings page |
| Snap (`snap.md`) | Karl | Lumen, task 212 (Actions menu); zones to follow |
| App switcher (`app-switcher.md`) | Karl | To follow |

Each brief is confirmed by Travis, and each new surface's mockup is approved, before it's built.
