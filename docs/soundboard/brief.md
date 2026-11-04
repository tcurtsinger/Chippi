# Soundboard

Approved 2026-11-03. Mode: **Operate**. This extends Chippi's existing Audio panel and Actions gallery styling.

## Job

While gaming or in a call, find a saved sound and play it through the virtual microphone in one click. Hear the same sound locally without monitoring a delayed copy of your voice. Organize sounds into collections and prepare new sounds without leaving the widget.

## Approved composition

Direction B, **Collection tabs**, selected after three mockups. Audio gains a third remembered tab, Soundboard. Collection names run across the top, followed by collection overflow, Add and a pencil. The first viewport holds twelve tiles in a four-column, three-row gallery. Additional sounds scroll. Use Chippi's existing neutral surfaces, Segoe typography, violet selection and native glyphs in both themes.

Tiles show a cover or an audio glyph, title and duration. The playing tile has a visible progress treatment. A compact footer holds the playing sound, Stop, Send and Hear. Empty collections offer Add. Edit supports tile reordering by dragging and keyboard, then Done; collection editing stays inside the widget.

## Behavior

- One clip at a time. Clicking another replaces it. Stop ends the clip and keeps voice capture running.
- Send feeds VB-CABLE; Hear follows the current real Windows output. Independent levels persist.
- Voice and clips mix at the virtual output after voice processing. Mic processing can be on or off. Mute mic silences physical voice only; clips remain playable. Explicit Windows mute on the virtual endpoint is respected.
- Soundboard does not take over Windows default-device roles. Select CABLE Output as the microphone in the game or call. The existing Mic processing setting may already do this through its own default-device option.
- The selected Audio view and collection persist. Widget and Settings share the same library and single player.

## Add and edit

Choose one or several local files, or paste a direct audio, YouTube or SoundCloud link. Prepare a durable local copy. Review each sound's title, optional cover, collection, favorite flag and trim range before adding it. Waveform selection and explicit time fields support trimming. Preview plays locally only, including before virtual-mic setup. Cancel stops the preparation/preview and discards its temporary draft.

Source files are never modified. Deleting a sound removes only Chippi's owned copy. Imported clips are limited to 250 MB of source media and 30 minutes. Protected, unavailable and sign-in-only links show an actionable error; no account cookies are imported. No sample sounds are installed.

## Finish pass (2026-11-04)

Preserve the approved collection tabs and twelve-tile composition. Large collections create only nearby tile controls; scrolling, keyboard navigation and reordering still reach every sound. Preserve the scroll position across library changes. Use bounded, readable error messages and full-name tooltips where text is trimmed.

Pending saves and previews are single-flight. Navigating away cancels unfinished preparation and releases its draft after any in-flight review operation finishes. Each editor owns only its own preview. Reject stale edits from another view instead of overwriting newer metadata. Keep exact stored trim values when only the name or collection changes; accept local decimal separators in explicit time fields. Names retain complete Unicode text elements.

## Required states

Populated and empty collections, Favorites, collection overflow, long titles, playing/idle, edit/reorder, file/link import, preparation/cancel, trim review, errors, missing/disabled virtual mic and failed voice capture. Missing virtual mic blocks sending, while import and local preview stay available. A voice-capture error allows clip-only playback and is reported visibly.

## Verification boundary

The owner will perform live playback and desktop interaction checks. Local implementation validation uses fake audio devices/players, file-only decoding, isolated temporary storage and unhosted WPF renders. Do not launch or control the installed app, query or change live audio devices, play sound, or send desktop input for this task.
