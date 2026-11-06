# Saved app arrangements: live check

Use the refreshed installer Marge supplies for this check. Do not use an older installer at the same path.
Allow about 20 minutes. Use disposable windows and leave unsaved work open.

## Prepare six windows

1. In PowerShell, create three disposable files:

   ```powershell
   $chippiCheck = Join-Path $env:TEMP ('Chippi-arrangements-' + [guid]::NewGuid().ToString('N'))
   New-Item -ItemType Directory $chippiCheck | Out-Null
   'Arrangement check A' | Set-Content (Join-Path $chippiCheck 'A.txt')
   'Arrangement check B' | Set-Content (Join-Path $chippiCheck 'C.txt')
   'Arrangement check C' | Set-Content (Join-Path $chippiCheck 'D.txt')
   $chippiCheck
   ```

2. Open three separate Notepad windows. Use Notepad's New window command, then Ctrl+O to open A.txt, C.txt and D.txt from the folder printed above. Keep them in separate windows, rather than tabs in one window.
3. Press Win+E twice for two File Explorer windows. Navigate one to the disposable folder and the other to Documents. Press Win+I to open Windows Settings.
4. Place the six windows across both monitors. Leave A.txt normal, maximize C.txt, then maximize D.txt and minimize it. Leave an Explorer window normal on the other monitor.
5. In Chippi, open Settings > General. Note Always on and Pause in these apps so you can restore them. In Settings > Shortcuts, note any binding you change during this check.

## Capture and reuse

1. Open Settings > Snap and scroll to Arrangements. Choose New arrangement. Capture should begin immediately, with no second Capture button.
2. Name it `Live check`. Check the display preview and rows: readable monitor names, app icons, titles and positions. A skipped window has a reason. Chippi's own windows should not be included.
3. Remove any unrelated windows from the draft. This must not close those windows. Save.
4. Choose Edit from the arrangement's options menu. Open launch details on a row. Confirm Arguments and Working folder are offered only for executable targets. Cancel the edit and check the saved arrangement remains.
5. Move the six fixture windows to different positions. Choose Launch on `Live check`. Existing windows should be reused without duplicates. A.txt returns to its saved spot, C.txt is maximized, and D.txt remains minimized. Restore D.txt from the taskbar: it should return maximized.
6. Launch again from Actions > Snap > Arrangements. Then find `Live check` in Search and choose Launch. Both doors should launch the same arrangement.
7. Open Edit again and follow the shortcut's Edit link. It should open that row in Settings > Shortcuts. Set an unused chord that the editor accepts. Return to your fixture windows and try it. The feature page must not record a shortcut itself.

## Relaunch and cancellation

1. Close only the disposable Notepad and Explorer windows. Launch `Live check` again. Missing apps should open; other windows must stay open.
2. This first build saves launch instructions and placement, not documents, tabs or app sessions. A missing Notepad window may reopen blank unless you deliberately supplied its file path in Arguments. An app that redirects a new launch into a window already used by another row should say `Window already used`, rather than moving that window twice.
3. If the run remains in progress, choose Cancel. No further apps or placements should start. Apps already opened must remain. If it finishes before you can press Cancel, record that; the Sandbox check covers cancellation during a pending launch.
4. A failed row offers Retry. Retrying must leave successful rows in place and avoid launching them again.

## Matching, monitors and safety

1. Open two Explorer windows to the same folder. Make a separate arrangement from them, with distinct saved positions. Move both, then Launch. Chippi should pick the nearest candidate without asking. A guessed match stays on the bar until Dismiss, and the run page identifies it as guessed.
2. Save a fixture window on the second monitor. Press Win+P and choose PC screen only. On the remaining display, launch the arrangement. The missing-display window should arrive on the display you invoked Launch from, fitted above the taskbar. The bar should keep the display substitution until Dismiss. Press Win+P and choose Extend to restore both displays.
3. With different scales on your two monitors, check that every window remains reachable and above its taskbar. Fitting can shrink a window; this build does not promise DPI-based enlargement.
4. With Always on off, make your browser fullscreen with F11 and try the arrangement's shortcut. It must not launch or move anything. Exit fullscreen, turn Always on on, return to fullscreen and try again. Other eligible windows may be arranged, but the fullscreen browser must stay on its display and be skipped. While the browser is still fullscreen, minimize it and launch once more. It must stay minimized on its own display; restoring it must still show it fullscreen. Exit fullscreen and restore Always on to its original value.
5. For capture's admin refusal, manually open a blank Windows Terminal using Run as administrator. Windows may ask you for consent for that manual launch. New arrangement should exclude the admin window with `Needs admin`; Chippi must not ask for elevation. Cancel the draft, then close only that blank admin terminal.
6. For a clean run, the bar folds away. A run with a guessed match, a display substitution, a skipped row or a failure stays until Dismiss. Dismiss hides the summary and must not close apps.

## Finish

Delete the temporary arrangements through their options menus. Each confirmation should ask a question with Cancel and Delete. Their actions and shortcut rows should disappear. Restore the shortcut, Always on, display mode and other settings you changed. Close only the disposable fixture windows, then delete the disposable folder in Explorer.

Report Pass, or the step and what happened. Physical mixed-DPI placement, real monitor removal, packaged-app behavior and app-specific minimum/fixed sizes are owner checks; a single-display Sandbox cannot prove them.
