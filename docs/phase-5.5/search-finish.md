# Search finish verification

Implemented the five approved Search critique priorities and real app icons in app and open-window results.

## Covered

- Automatic versus deliberate selection, including delayed file results and empty-query refreshes.
- One pending launch per visit; late completions and queued accessibility actions cannot act on a newer visit or query.
- Loading, no results, file-search failure with Retry, action failure with retained identity, and hiding with Undo.
- Accessible selection and action metadata, full file paths, and more legible selected details and verbs in both themes.
- Settings persistence recovery, shortcut registration rollback and capture exit, and focus-preserving Hidden recovery with explicit category enabling.
- Same-name file disambiguation and the Search engine label.

## Evidence

The Release CommandBar test project passes 83 tests, with no warnings or errors. Providers and commands are fakes; tests do not execute desktop actions.

Fifty offscreen WPF renders cover dark and light themes at the shipped widget size, plus Settings and shortcut rows. They use the production controls and styles. The eight-result boundary fits 272 DIP of rows plus 24 DIP of feedback in the 296-DIP body. Captures cover actual executable-icon image fixtures, fallback initials, long names, duplicate filenames, loading, empty, unavailable, pending, failed, Undo and Settings recovery states.

Local evidence: `C:/Users/Travis/.codex/visualizations/2026/11/02/01a19ea8-fc02-7280-9bac-0b0a550022c0/search-finish/`.

Independent source review identified a stale queued accessibility-action issue with shared result keys; query-version guards and a regression test resolve it. The final review found no remaining material issues in the reviewed paths.

## User testing

No visible windows, desktop input, real launches, live hotkey registrations or live window-icon queries were used. Installed-app integration, OS focus, Windows Search service behavior and live Narrator announcements remain for user testing. The offscreen accessibility tests verify patterns and metadata; they do not claim a connected presentation source or screen-reader session.
