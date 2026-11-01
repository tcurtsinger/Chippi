# Radial menu: brief

Confirmed by Travis, 2026-11-01, and its mockup approved the same day: [Chippi Phase 5.5: Radial menu](https://claude.ai/artifact/QFJSjaBzEbZbKmassLftuQ). DESIGN.md wins where it speaks.

## Job

Hold a button, flick, let go: run what you use most right where the pointer is (the music, the window under it, an app, a shortcut, a piece of text, anything Chippi does) without reaching for the keyboard or the taskbar.

## Opening it

- **Triggers,** picked in Settings › Radial menu, any or all (Travis): holding the middle button, holding Back, holding Forward (the side buttons), or a keyboard shortcut. None is on when the feature is installed: nothing opens it until one is turned on (Travis). A click of a mouse trigger still reaches the app as a click: the wheel opens only once the button is held 250ms without moving. A press that moves first stays the app's drag (middle-drag to pan, autoscroll).
- **Where:** centered on the pointer (Travis), kept whole on its screen: near an edge it shifts in, and directions are read from its middle.
- **Over a fullscreen game or video:** it opens anyway (Travis). In a game that hides the pointer, it opens in the middle of the screen.
- **Focus:** it never takes focus, so the app in front stays in front and shortcuts, text and window actions land in it. While it's open, Chippi keeps the mouse's clicks and Esc for itself.
- It stays closed during Cleaning mode.

## Picking

Both ways work (Travis):

- **Hold, flick, let go:** with the trigger still held, point toward a slice and let go to run it. Direction is what counts, not distance: anywhere past the hub in a slice's direction points at it, on the ring or beyond it.
- **Let go in the hub** (or tap the shortcut) and the wheel stays open: click a slice. Esc, a right click or a click off the wheel closes it.
- **The pointed slice** takes the accent tint with its glyph in the accent, its name shows in the hub, and Chippi looks toward it.
- **Running:** the slice flashes the accent fill, Chippi grins, and the wheel fades out over 150ms. The action runs as the fade starts, so nothing waits on it.
- **Submenus** (Travis: a slice opens a second wheel): a slice with a small chevron at its outer edge opens one when it's clicked, or, with the trigger held, once the pointer is 32 past the ring on it. The new wheel opens just past the first, its middle 156 out in that slice's direction, so a held flick arrives at its hub and flicks chain (right, then down). The first wheel shrinks to a 40 hub showing the submenu's glyph, joined to the new one by a hairline; clicking it goes back. The direction toward it is **Back**, a slice with an arrow, and the submenu's slices follow it clockwise, evenly, so a submenu holds up to 7. Up to 3 wheels deep.

## The wheel

- **2 to 8 slices** (Travis), spread evenly from the top, clockwise: 4 sit at the compass points, 8 at every 45°, so a flick lands without looking. A ninth goes in a submenu. Hairlines divide them.
- **Shape:** a disc 248 across on the island's fill, with the widget's rim (it floats over any window or wallpaper) and the panel's shadow (DESIGN.md §4). The slices are the ring from 54 to 124 out of the middle; the hub, 96 across, is ringed by a hairline inside it. The pointed slice's tint sits 3 inside its hairlines with 3 corners.
- **Slices** carry a glyph only, 20 (an app's or file's own icon at 24), in the middle of the ring, in Text.
- **The hub** (Travis: Chippi and the name): Chippi at 48, idle (wide awake while Keep awake runs), his pupils turned toward the pointed slice; under him the pointed slice's name in 12 / 600, one line, trimmed. With nothing pointed, a submenu's hub shows its own name in 11 tertiary; the first wheel's shows Chippi alone.
- **Colors:** the island's, in the app theme, with the pet's violet as the accent.
- **Motion:** it grows from 90% at the pointer as it fades in, over 140ms ease-out; closed without a pick, it fades over 100ms. Under reduced motion it only fades, and Chippi's pupils move at once.

## What a slice can do

Travis picked the first five kinds in round 1 and the rest from Claude's list.

| Kind | What it does | Glyph |
|---|---|---|
| App | Opens a Start menu app or a program | Its own icon |
| File or folder | Opens it | Its own icon |
| Link | Opens a web address in the default browser | Globe |
| Key shortcut | Presses keys in the app in front ("Ctrl+Shift+T", "Win+Shift+S") | Keyboard |
| Chippi | Anything Chippi does: its Quick actions, Keep awake, mute the mic, switch the output, the Command bar, Cleaning mode, an island page | The feature's |
| Media | Play/pause, Next, Previous, Mute, Volume up, Volume down | Each one's |
| Window | On the window under the pointer when the wheel opened: Snap left, Snap right, Maximize, Minimize, Close, Keep on top, Move to other screen | Each one's |
| Text | Types saved text at the cursor, without touching the clipboard | Text |
| Command | Runs a PowerShell or cmd line, hidden | Terminal |
| Selected text | Searches the web for it (the Command bar's engine), opens it as a link, or copies it as plain text. It copies the selection, then puts the clipboard back; Clipboard history doesn't keep that copy | Search, Link, Paste |
| Settings page | Opens a Windows Settings page (Bluetooth & devices, Display, Sound) | Settings |
| Submenu | Opens a second wheel | One you pick |

Every slice has a name, shown in the hub, filled in from what it does and changeable, and a glyph: its kind's, an app's or file's own icon, or one picked from the island's glyphs.

## Settings › Radial menu

- **Opening:** one card of switch rows, Hold middle button, Hold Back button and Hold Forward button, and Shortcut (Not set until recorded, then removable). The shortcut also shows in Settings › Shortcuts, with clashes flagged as today.
- **Wheel** (Travis: edit the wheel itself): one card, the wheel drawn at its real size on the left and the selected slice's fields on the right.
  - It starts empty (Travis): the empty ring with a + at its top, "No slices" under Chippi, and "No slices" where the fields go.
  - Click a slice to select it; drag it onto another to swap them. A + in the next free place adds one, until there are 8.
  - Fields: Kind, then what that kind needs (an app picker, Browse, the address, the keys recorded, the text, the command line, or a list of Chippi's, the media or the window actions, or the Settings pages), Name, Glyph with Change, and Remove.
  - A submenu slice has Open, which steps the editor into its wheel, with the path above it ("Wheel › Media") and Back.

## States

- **A slice whose target is gone** (an app uninstalled, a file moved): its glyph in tertiary with a small Caution mark. Pointed, the hub says "Not found" in Caution; picked, nothing runs. Settings shows the same mark on it.
- **Errors** read "… failed" on the bar for 4 seconds, as the Output shortcut reports there, since the wheel has gone by then.
- **An empty wheel** opens as the hub alone, with "No slices" under Chippi.
- **No trigger on:** the wheel can't open; the feature's Settings row says "No trigger".

## Left out

- **Profiles and per-app wheels:** Travis chose submenus only.
- **Self-filling lists, scroll to adjust, key sequences and virtual desktops:** offered, not chosen.
- **The island:** no home button (Travis). It lives at the pointer and is set up in Settings, and the home stays at 10 buttons.
- **More than 8 slices on one wheel.**
