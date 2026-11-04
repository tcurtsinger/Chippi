# Search finish verification

Implemented the five approved Search critique priorities and real app icons in app and open-window results.

## Covered

- Automatic versus deliberate selection, including delayed file results and empty-query refreshes.
- One pending launch per visit; late completions and queued accessibility actions cannot act on a newer visit or query.
- Loading, no results, file-search failure with Retry, action failure with retained identity, and hiding with Undo.
- Accessible selection and action metadata, full file paths, and more legible selected details and verbs in both themes.
- Settings persistence recovery, shortcut registration rollback and capture exit, and focus-preserving Hidden recovery with explicit category enabling.
- Same-name file disambiguation and the Search engine label.
- Stable row and Undo controls while changing selection, with no repeat icon requests.
- Quiet cancellation on close or query changes; unexpected cancellation and the five-second file deadline offer Retry. Native queries remain serialized until their work actually ends, and native cancellation stays off the UI thread.
- Shared pending icon reads, stopped-provider cleanup, retryable missing icons, and in-memory recovery from malformed saved settings.
- Higher-contrast fallback initials, using primary ink on the accent tint.

## Evidence

The Release CommandBar test project passes 104 tests, with no warnings or errors. Providers and commands are fakes; tests do not execute desktop actions.

Fifty-six fresh offscreen WPF renders cover dark and light themes at the shipped widget size, plus Settings and shortcut rows. They use the production controls and styles. The eight-result boundary fits 272 DIP of rows plus 24 DIP of feedback in the 296-DIP body. Captures cover actual executable-icon image fixtures, fallback initials, long names, duplicate filenames, loading, empty, unavailable, pending, failed, Undo and Settings recovery states. The fresh Release harness build has no warnings or errors, and its captured production DLL hash matches the worktree build.

Local evidence: `C:/Users/Travis/.codex/visualizations/2026/11/02/01a19ea8-fc02-7280-9bac-0b0a550022c0/search-final-pass/`.

Computed from the rendered brushes and opacity, composited over both black and white desktops: selected fallback initials have at least 8.05:1 contrast in dark and 11.23:1 in light; selected details have at least 7.44:1 and 7.90:1 respectively.

Independent source review identified a stale queued accessibility-action issue with shared result keys; query-version guards and a regression test resolve it. The final review found no remaining material issues in the reviewed paths.

## Final-pass measurements

Five warmed offscreen WPF samples each alternate selection 1,000 times in an eight-result page, using fake providers. Median elapsed time changed from 716.60 ms to 15.40 ms; median current-thread allocation changed from 207,733,376 bytes to 3,336,040 bytes. Repeated icon requests changed from 7,000 to zero, and the result and Undo controls retain their identity. These are control-update measurements, not desktop frame-rate or live-input measurements.

A separate held-reader fixture requests the same pending icon 1,000 times. Queued work and distinct tasks changed from 1,000 each to one each; caller-thread allocation changed from 465,200 to 152,688 bytes. Native extraction was already cached after completion; the improvement removes duplicate queued requests while extraction is pending.

The independent final provider review found no material issues in timeout/gate ownership, cancellation/disposal ordering, shared icon requests or stopped-provider cleanup. Live native integration remains in the user testing scope below.

## User testing

No visible windows, desktop input, real launches, live hotkey registrations or live window-icon queries were used. Installed-app integration, OS focus, Windows Search service behavior and live Narrator announcements remain for user testing. The offscreen accessibility tests verify patterns and metadata; they do not claim a connected presentation source or screen-reader session.
