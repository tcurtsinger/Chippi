# App updates: implemented design

Recorded 2026-11-16 from `AppUpdatesView.cs`, `AppUpdatesUi.cs` and `AppUpdatesSettingsView.cs` in `src/Features/Chippi.Features.AppUpdates/Views`, the shared theme resources, and native offscreen fixture renders. The approved surface reference is [app-updates.html](design/app-updates.html). This is an extension of the incumbent Windows 11 design in [DESIGN.md](../../DESIGN.md), with no new visual world or shared token changes.

## Layout and states

Actions > Apps > Update apps uses the compact 520 × 360 DIP island, with a 13 DIP semibold header and five 46 DIP app rows per page. Each row places a registered app icon, or a name-specific monogram, beside the app name and installed-to-available version/source. Update and an ellipsis menu occupy the right column. The header offers Check again and Update all; pagination, Skipped and Settings stay in the footer. There is no additional home tile.

The summary gives the update count primary text and the last-check or active-app context secondary text. During a batch, the header says “Batch in progress”; the summary names the app currently updating. The progress track is capped at 180 DIP and shortens with the available copy width. Completed rows pair a green check with “Updated”; queued, updating, restart and failure states retain readable text. Failed rows expose Retry, which returns through confirmation. Updates change existing row parts rather than rebuilding the list on each progress event.

A single-app batch preserves the full discovered list and its pagination. In the 20-app fixture, the summary changes from “0 of 1 updated” to “1 of 1 updated” while the footer still pages through 20 rows; Update all subsequently targets the remaining 19 available apps. Batch completion and failure counts come from the selected batch, independently of the visible list. Unselected row actions are disabled while the batch runs and become available again afterward.

A partial Microsoft Store check failure keeps the discovered count and checked-time context visible, followed by a caution-colored warning run. The summary's tooltip and accessible name share an explicitly composed full string, preserving the count, context and warning if the visible line is trimmed. A failure with no discovered rows keeps the dedicated check-failed state.

Confirmation presents a short question above the captured app names and target versions. Five apps fit per confirmation page; larger batches paginate. Cancel receives initial keyboard focus, beside the primary Update action. The two explicitly removed explanatory lines, “Windows may ask you to allow this batch.” and “Updates continue when you close this panel.”, are absent from the implemented views. Their removal does not imply a change to batch lifetime.

Settings uses the existing Card, SettingRow, SettingsLayout, SwitchWithLabelStyle, SecondaryButtonStyle and ShortcutSetting controls. Its sections contain the unknown-version option, skipped versions with Restore, up to 50 history results, and the two read-only shortcut summaries with Edit in the central editor. Skipped and History support section links. No page introduction was added.

## Incumbent consistency

The views resolve colors through the existing IslandText, IslandTextSecondary, IslandTextTertiary, IslandAccent, IslandAccentTint, IslandAccentFill, IslandTrack, IslandGalleryFace, Success and Warning resources. IslandButtonStyle, IslandAccentButtonStyle and IslandIconButtonStyle provide button states and focus treatment. Settings uses the general theme's native controls. Dark and light therefore use the established neutral layers, violet island primary treatment, green success and amber warning treatment without feature-specific palette values.

Text inherits the shared Segoe family and tabular figures. The feature uses 13 DIP semibold for the island header, 12 DIP app names, 11 DIP detail/status text, and a 16 DIP semibold confirmation question. Monograms and review containers use the incumbent softly rounded forms. These local dimensions describe this compact surface; they do not introduce an app-wide type or spacing scale. Names, glyphs and status text carry meaning together; color is supplementary.

## Evidence and limits

The session's `shots` folder contains 26 current native WPF PNGs: list-5, list-12, list-next, confirm-5, confirm-12, running, results, empty, check-failed, settings, partial-store-20, single-running-20 and single-result-20, each in dark and light. They use synthetic apps and loaded themes; no Windows were shown. Sampled running, results, Settings and the three added 20-app states agree with the source patterns above. The initial review covered the original 20 renders; the later review focused on the count, preserved list and partial-warning fixes.

The fresh fallback UI reviewer was used because the dedicated role was unavailable. Its initial three material findings were resolved in one fix batch and the original 20 states were recaptured. Later behavior fixes retained unselected apps, separated batch counts from list counts, and preserved partial-check context. All 11 ViewTests then passed. The focused follow-up disposition was `SHIP`: the count/list/partial-warning findings were resolved, the prior three fidelity fixes were preserved, and no regressions were observed within that focus. These were fix-list reviews, not unrestricted full UI approval. This document also uses the fallback documenter instructions because the dedicated role was unavailable.

The latest fixture report records cached construction/layout at 9.67 ms dark and 4.78 ms light, and page switch/layout at 3.32 ms dark and 1.66 ms light. These supersede the older fixture ranges still present in the session direction contract. They measure fixture layout, not compositor frames, COM startup or real package discovery. A separate read-only backend check found 20 updates in approximately four seconds. Actual Sandbox upgrade validation still requires an owner-approved away session and remains pending; functional feature completion is not claimed. No tests, desktop interaction, host updates, elevation, clipboard or audio actions were performed for this documentation pass.

## Preserved documentation drift

The existing root DESIGN.md uses legacy numbered headings and frontmatter groups outside the current Impeccable schema. A root `.impeccable/design.json` is absent; the small reference-specific `design.json` files under `docs/phase-7/design/reference` are separate artifacts. The approved unknown-version setting also retains explanatory caption copy despite the root document's stricter state/data-only caption rule. None of these existing conditions was repaired or promoted into a new system rule. DESIGN.md, the reference sidecars and the absence of PRODUCT.md were preserved within this pass's single-file boundary.
