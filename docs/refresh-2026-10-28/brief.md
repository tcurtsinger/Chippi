> Amendment: F3 #5: no 'Removed · …' caption (Travis, task 65, 2026-10-28).

Build Travis's approved island refresh. Every page and the All features grid share one compact fixed size, 520 × 360, so the island never changes size between pages. Sound is made uniform, System monitor is cut down to two tables, and six issues from the Phase 5 review get fixed. The Settings window gets its approved redesign, and Settings can now do everything the island can (Part F). Repo: X:\code\chippi, branch from main.

Travis's priorities: a smaller island with smaller elements over a bigger one. Accessibility is not a goal. Don't add accessibility work, and leave existing accessible names as they are.

Mockups (source of truth):
- **X:\code\chippi-scratch\fixed-size\**: every page at 520 × 360 (shots\a.png to d.png), the page states (shots\ps1.png to ps3.png: Keep awake on, the empty pages, a long link), and the shell on real screens (shots\shell.png). The caption in index.html lists the rules. All sizes come from here.
- **X:\code\chippi-scratch\refine-sound-monitor\**: Sound's muted and Mic processing on states (shots\sound.png). Its sizes and two-line device rows are older; use fixed-size's.
- **X:\code\chippi-scratch\settings-shell\**: the Settings window. shots\s1.png to s5.png show the shell, and c1.png to c9.png show the pages. The today\ folder holds the current app for comparison. Part F builds from here.

Order: Part A changes the shell, and every page depends on it, so it merges first. After that, Parts B to E can run in parallel, one builder each, on separate branches with separate PRs. Part F edits the same feature folders, so it starts once B to E have merged. Its shell half (F1) may start right after Part A.

## Compact sizes (every part uses these)
| Element | Size |
|---|---|
| Text | 12, captions and secondary lines 11, page titles 13 / 600 |
| Island | 12 inset, 20 outer corners, 8 inner card corners (20 − 12) |
| Gap | 8 between every card, tile and column, and below the header. The body is six 76 DIP columns with 8 DIP gaps |
| Header row | 24, with 8 below it. Header text and buttons end at the 12 inset; the last icon button gets a −7 right margin, so its glyph lines up with the cards' right edge. The same −7 applies to the last icon button in Scratchpad's tabs and action rows and in Clipboard's expanded entry |
| Buttons | 28 tall, 10 padding, text 12, MinWidth 64 on every text button, Shelf's and Clipboard's included |
| Icon buttons | 28 × 24, with a 14 glyph |
| Glyphs | 12, 14, 16 or 20 only, all drawn with the same ~1.25 px line. LucideIcon currently uses a fixed 1.6 in a 24 box, which draws 0.8 px at 12 and 1.33 px at 20. Set its StrokeThickness to 30 / Size instead (Chippi.UI/Controls/LucideIcon.cs) |
| Corners | Filled surfaces (cards, selected rows, expanded entries, tiles) 8. Controls (buttons, fields, search) 4 |
| Content inset | Content inside every row and card starts 10 in, including Clipboard rows, its search box, its section labels and Scratchpad's tabs. Fields and the search box count their 1 px border (padding 9) |
| Control cards | 44 tall, 10 padding |
| List rows | 30, one line (Sound, File organizer, Clipboard) |
| Table rows | 28 |
| Switch | 34 × 18 |
| Slider thumb | 16 |

Add these as shared island resources in IslandStyles.xaml (sizes, IslandCardStyle padding, row and button styles) instead of repeating numbers in each feature. The mockup's white tints (4 % to 16 %) map onto the existing Island brushes; add a brush only when no existing one is close.

## Part A: One island size (src/Chippi.App/Island)
1. **One size.**
   - In IslandPanelWindow.xaml.cs, replace GridWidth (328), NowPlayingWidth (440) and PageWidth(key) with one fixed page size of 520 × 360 DIP for the grid and every page.
   - Reshape stops measuring the card's height and uses 360.
   - Inside the 12 inset and the 24 + 8 header, each page gets a 496 × 304 body: six 76 DIP columns with 8 DIP gaps, which every page lines up to.
2. **Remove PageWidth** from IslandTileSpec and IslandTileGroup (src/Chippi.Core/Modules/IslandTiles.cs), and from every module that sets it: SoundTile.cs, ClipboardFeatures.cs and CleanUrlModule.cs, ScratchpadModule.cs, and SystemMonitorModule.cs with SystemMonitorView.PageWidth. Also update Widest().
3. **Motion.**
   - Opening and closing animate as now (220 / 160 ms).
   - Page to page, the shape doesn't change. The old page fades out over 90 ms while the new one fades in over 180 ms, with no blank gap. Today the gap is a 60 ms PageFadeDelay at lines 42–43.
   - Update the Morph comments to match.
4. **Nothing scrolls. Pages adapt to fit the body.**
   - The one exception is the text inside a Scratchpad note.
   - When content grows, the page adds columns, shortens rows, narrows strips and shrinks tiles.
   - Lists show whole rows only. A row that doesn't fully fit is left out, never cut off.
   - Whatever still can't fit is in that feature's Settings page. Parts B to D say how each page adapts.
   - Lists start at the top. Content that never grows (Keep awake's choices, empty states) sits in the vertical middle.
   - Content never grows the island, and no page has a ScrollViewer.
5. **Orbit buttons:**
   - 32 DIP circles with a 14 glyph, 10 off the shape, 6 between the two buttons of a pair. The live button is also 32.
   - Positions follow the existing one-rule-on-every-edge layout.
6. **No docked panel; the tray opens Settings (Travis, 2026-10-28).** Only two surfaces remain: the island and the Settings window.
   - Remove the docked-by-tray mode: its card, its footer and its geometry (IslandPanelWindow, IslandGeometry.cs, IslandHost.cs, TrayController.cs, TrayIcon.cs, ShellCommands.cs).
   - A left click on the tray icon opens Settings on the last page used, whether or not the island shows. With the island off, or hidden for a fullscreen app, that's the way in.
   - The right-click menu stays: Open settings, Quit.
7. **Grid** (IslandGrid.cs). The tiles always fill the body, 8 DIP apart (about 160 × 96 at 3 × 3), and the layout follows the tile count. See fixed-size shots\g1.png and g2.png.

   | Tiles | Layout | Tile content |
   |---|---|---|
   | 1, 2 or 3 | One row: 1 × 1, 2 × 1 or 3 × 1 | Glyph (20), name (12 / 600) and status (11, tertiary), centered |
   | Up to 4 | 2 × 2 | Same |
   | Up to 6 | 3 × 2 | Same |
   | Up to 9 | 3 × 3 | Same |
   | Up to 12 | 4 × 3 | Same |
   | Up to 15 | 5 × 3 | Glyph 16, name 11 / 600, status 10 |
   | Up to 20 | 5 × 4 | Glyph 16, name 11 / 600, status 10 |
   | Up to 24 | 6 × 4 | Glyph 14 and name 10 / 600, no status |
   | Up to 30 | 6 × 5 | Glyph 14 and name 10 / 600, no status |

   Arrow keys move through whatever columns are showing.

   Tile states (see fixed-size shots\st.png):
   - Hover fills to white 10%, pressed to 4%.
   - A live tile (Now Playing playing, Keep awake on) gets a 6 DIP accent dot, 8 from its top-right.
   - A tile that needs attention gets the dot, its status and its glyph in Caution: Sound shows the mic-off glyph and "Mic muted".
   - The round button for the open page gets the 0.5 white ring, as now.
8. **Fit:**
   - The island and its orbit buttons fit on the top, left and right edges down to a 960 × 540 DIP screen (1080p at 200%).
   - Update the smoke tests' CheckBounds, which expect 440 and 328 wide, to expect 520 × 360. Each smoke test should also check that the body is 496 × 304.

## Part B: Sound page (src/Features/Chippi.Features.Sound)
1. **Output card** (Routing/RoutingQuickView.cs, OutputCard). One 44 DIP row, the same as the mic card:
   - left: speaker glyph. While muted it's VolumeMuted in WarningBrush.
   - middle: the system volume slider, then the percentage (12, tabular, 34 wide, right-aligned). While muted the percentage reads "Muted" in WarningTextBrush.
   - right: a Mute / Unmute text button, RoutingUi.Button, like the mic card's.
   - Remove the device name, since the selected row already shows it. Remove the mute icon button.
2. **Buttons line up.** The Output and mic cards' buttons share MinWidth 64 (Mute/MicMuteView.cs, MicCard).
3. **Rows.** IslandRowButtonStyle (src/Chippi.App/Island/IslandStyles.xaml): one line, 30 tall, Padding 10, so each radio (14) lines up with the glyph in the card above it. Only RoutingQuickView uses this style.
   - Drop the second line ("Connected", "Playing now", "In use"; RoutingQuickView.cs line 139). Only connected devices show, and the radio and highlight already mark the one in use.
   - AudioRoutingModule.Status stays for Settings › Output and Microphone (RoutingPage.cs line 102).
   - The rows start 8 below the control card, the one gap.
4. **Mic processing card** (Processing/ProcessingViews.cs, ProcessingQuickView). An IslandCardStyle card, 44 DIP like the others, with 8 DIP above it:
   - left: an AudioWaveform glyph in the accent. Add it to LucideIcons; Sliders is App volume's glyph.
   - middle: "Mic processing". While it's live, the level meter (4 DIP, like the slider track) follows the label.
   - right: the switch.
   - The warning line stays below the card.
5. **Rename "Clean-up" to "Mic processing" everywhere it shows:**
   - ProcessingViews.cs lines 166, 263 and 266 (the label and the automation name).
   - SettingsWindow.xaml.cs line 52 (the Settings › Microphone section).
   - MicProcessingModule.cs line 302, the tile status. Make it "Mic stopped" (Caution), not "Mic processing stopped": a tile fits about 14 characters, and even today's "Clean-up stopped" is cut off to "Clean-up stop…". The page's warning line keeps the full text.
   - The comments that mention it, and the tests: SoundShellTests.cs lines 82–84, MicMuteTests.cs lines 77–93, and the comment in ProcessingTests.cs.
6. **At the fixed size:**
   - Two columns, 8 apart, each 244 wide (three of the six columns). Long device names trim. Both columns start at the top.
   - Rows are one line at every count, so a crowded column looks the same as a short one. The Output column fits 7 devices, the Microphone column 5 above the Mic processing card. See fixed-size shots\f1.png.
   - Devices that still don't fit appear only in Settings › Output or Settings › Microphone, which already list every device.
   - No microphone: the mic card shows the MicOff glyph and "No microphone" in tertiary, Mute is disabled, and there are no rows. The Mic processing card stays. See fixed-size shots\ps2.png. "No output" works the same way in the Output column, with the slider hidden.

## Part C: System monitor page (src/Features/Chippi.Features.SystemMonitor/Views)
1. **Layout.**
   - Two cards stacked full width: the readings card, an 8 DIP gap, then the Disks card. See fixed-size shots\c.png.
   - Both cards are IslandCardStyle with Padding 10,2.
   - Rows are 28 DIP, with a hairline divider (IslandPanelBrush) between rows.
   - When the drives don't fit at 28 DIP, every Disks row shrinks to 22 DIP. That fits 7 drives on a desktop, and fewer once Battery or Thermal zone rows appear. See fixed-size shots\f1.png.
   - Drives that still don't fit are listed in a new Disks section of Settings › System monitor, with the same Used, Free and Total columns. Part F draws it with every drive, at the top of that page.
2. **Readings rows.**
   - Order: CPU, GPU, Memory, Network. Battery and Thermal zone follow only on PCs that have them.
   - Columns:
     - Name: 80 DIP, 12, tertiary.
     - Detail: 11, tertiary, trimmed.
     - Value: 12 / 600, tabular, right-aligned. It turns Caution while its alert is on, as it does now.
   - CPU: "5.29 GHz" | "18%".
   - GPU: temperature · dedicated memory, e.g. "125 °F · 2.0 of 15.7 GiB" | load %.
     - With more than one GPU, a chevron after "GPU" opens the existing picker menu, with the current GPU in semibold.
     - The GPU's name no longer shows on the page.
   - Memory: "29.0 of 61.7 GiB" | %.
   - Network: "↓ 1 KiB/s   ↑ 0 KiB/s", right-aligned, with the arrows in tertiary.
     - Add ArrowDown and ArrowUp to LucideIcons; the existing Up and Down are chevrons.
     - A chevron picker shows only with more than one adapter.
     - While unplugged it reads "Not connected".
   - Battery: state · rate · time left | %.
   - Thermal zone: "Thermal zone", or "Thermal zone 1", "Thermal zone 2" when there are several | temperature.
   - **Status rules (keep the current ones):**
     - A failed read shows "Unavailable" as the detail and no value.
     - A rate that's starting over shows a blank value, never the old one.
3. **Disks card.**
   - Header row: 24 DIP. "Disks" (11 / 600, tertiary), then "Used", "Free" and "Total" (11, tertiary).
   - One 28 DIP row per volume, sorted by letter: letter (tertiary) | used (Total − Free) | free | total.
     - Free turns Caution while that volume's low disk space alert is on.
   - Columns: 80 DIP, then three equal columns, right-aligned.
   - The card shows "Unavailable" when the disk read fails.
   - DiskTable.cs is rewritten for this or replaced.
4. **Remove from the page:**
   - The graphs.
   - The "Last 4 min" page note (SystemMonitorModule.cs lines 85 and 125, and Format.Span).
   - The logical processor strip and its button (ProcessorsOpen, and the page's PerProcessor need).
   - Committed, Shared and GPU fan.
   - Disk active time, read and write.
   - The network scale and its legend.
   - Delete any code only the old page used. Keep the sampler, alerts, tile status and Settings working as they do now.

## Part D: Every other page at 520 × 360 (see fixed-size shots\a.png, b.png, d.png and ps1.png to ps3.png)
1. **Now Playing:**
   - Left: the artwork, 304 × 304 (the body's full height), then an 8 DIP gap.
   - Right column (184 wide), top: the source picker, the title (16 / 600) and the artist (tertiary).
   - Right column, bottom: the progress bar with the times, then previous / play-pause (a 40 DIP accent circle) / next. The play button's bottom lines up with the art's bottom.
   - Nothing playing: the placeholder artwork with the music glyph, and "Nothing playing" as the title. The source picker, artist, progress and times hide, and the three buttons show disabled, as now. See shots\ps2.png.
2. **App volume:**
   - Each app is a 56 DIP strip, 8 DIP apart, and the group is centered.
   - Each strip: the app's icon, a 32 DIP wide bar that fills the body's height, the percentage, the name and ⋯.
   - **The icon** (20 DIP) replaces the monogram and is still the mute button (VolumeView.cs, Mute).
     - It's the app's own icon, from its program file (the audio session's process). System sounds uses the Volume glyph.
     - With no icon, two letters of the app's name (10 / 600) on a white 12 % tile, so apps that share a first letter stay apart.
     - The mockup's colored squares only stand in for real icons.
   - **Names** wrap to two lines at spaces, as Shelf's do, and every strip keeps room for two lines, so the bars stay level.
   - **Muted:** VolumeMuted in WarningBrush replaces the icon, the bar fills white 28 %, and the percentage reads "Muted" in WarningTextBrush. See shots\b.png.
   - **Widths** (n strips and n − 1 gaps must fit 496):
     - Up to 7 apps: 56 DIP strips.
     - 8 to 12 apps: 34 DIP strips with a 24 DIP bar. The name hides; the icon, percentage and ⋯ stay. See shots\f1.png.
     - 13 or 14 apps: 28 DIP strips with the icon, bar and ⋯.
     - Beyond 14, the apps playing sound now come first, and the rest are only in Settings › App volume.
   - No apps playing: the Sliders glyph (20) and "No apps playing" (VolumeView.cs line 74) in the middle of the body. See shots\ps1.png.
3. **Keep awake:** the six choices become cards the size of a grid tile (3 × 2, about 160 × 96), with the label at 14, sitting in the vertical middle of the body. The status stays in the header ("52 min left").
   - The lit choice fills with the accent, with black text, as DurationChipStyle does now.
   - Pointing at it turns its label to "Stop" on the lighter accent, since clicking it stops Keep awake. See shots\ps1.png.
4. **File organizer:**
   - Top: the folder card, a 44 DIP control card like Sound's, with the folder glyph (bare, in the accent, no tile), the folder picker, and Auto / Manual / Paused at its right.
   - **Rename the Silent mode to "Auto"** (Travis, 2026-10-28). Change the labels only:
     - The segments: TidyView.xaml line 51 and SettingsSection.cs line 65.
     - The three places that show `folder.Mode.ToString()`: DownloadsModule.cs line 187 (the tile status), SettingsSection.cs line 103 and TidyView.xaml.cs line 76. Give FolderMode one display-name helper instead.
     - The History run header, HistorySection.cs line 135 ("Auto · Downloads").
     - DESIGN.md lines 204, 242, 301, 310 and 313, and PLAN.md line 71.
     - The tests that expect "Silent": IslandTileTests.cs lines 37 and 63, TidyTests.cs line 49 and IslandPanelShots.cs line 45.
     - Keep the FolderMode.Silent and RunTrigger.Silent names. Saved settings store "silent", so renaming them would reset every folder's mode.
   - Middle: the recent files list, 30 DIP rows with content as now. It shows as many whole rows as fit, newest first. History › has the rest.
   - No recent files: the File glyph (20) and "No recent files" (TidyView.xaml line 67) in the middle of the list card. See shots\ps3.png.
   - Bottom edge: History › and Organize now. History's text starts 10 in, in line with the cards' content.
5. **Clipboard:**
   - A 28 DIP search box, then 30 DIP rows: Pinned first, then Recent, as many whole rows as fit. An expanded entry counts at its full height.
   - A selected row's preview shows at most 3 lines, the last one trimmed with "…".
   - In the expanded entry, Paste and Copy are 8 apart, and Pin and Delete are icon buttons (28 × 24), 2 apart, like the header's.
   - Search shows the best matches that fit, newest first.
   - The full history is in a new list in Settings › Clipboard history, with the same Paste, Copy, Pin and Delete actions. Part F has its design.
   - With recording off, the glyph (20), "Recording off" and Turn on are centered in the body.
   - Clean URL keeps the field, the result and Copy at the top.
6. **Scratchpad:**
   - Top: the tabs row (30). Tabs that don't fit go under the approved "+N ⌄" menu.
   - Middle: the editor or preview, which fills the body. A long note's text scrolls inside the editor, the island's only scrolling, and notes have no length cap.
   - Bottom edge: Edit / Preview and ⋯.
   - "Save failed", "Load failed" (with Retry) and "Export failed" show in the header in WarningTextBrush, as now. See shots\ps3.png.
7. **Shelf** (ShelfPage.cs):
   - The tiles fill the space above the action row and shrink as items pile up. See fixed-size shots\d.png and f2.png.

     | Items | Layout | Tile content |
     |---|---|---|
     | Up to 18 | 6 × 3, about 76 × 66 | Glyph and the name on up to 2 lines. A last line only for a state: "Unavailable" or "Online only" |
     | Up to 28 | 7 × 4 | Glyph and the name on up to 2 lines |
     | Up to 40 | 8 × 5 | Glyph only, with the name as its tooltip |
   - Tile content starts 7 from the top: a 20 DIP slot for the glyph (16) or the picture (36 × 20), 4 below it, then the name. Glyphs and names line up across a row. At 8 × 5 the glyph is centered.
   - With a state line, the name takes one line.
   - Names wrap at spaces and hyphens (TextWrapping WrapWithOverflow, TextTrimming CharacterEllipsis, at most 2 lines). A single long word stays on one line and trims.
   - Size and type ("PDF · 2.1 MB", "Link", "1280 × 720") move to the tile's tooltip, with the full name (ShelfFormat.Caption).
   - At 40 items the Shelf is full. "Shelf full" (Caution) shows at the left of the action row, and new drops are refused until you remove something. It never throws items away, as now.
   - Copy, Pin and Remove sit at the bottom edge, on the right.
   - Empty, the dashed drop zone fills the body, with the Inbox glyph (20) and "No items" (12, tertiary) in its middle. See shots\ps2.png.
8. **Keyboard focus and the Phase 5 hooks** (CreateHeader, InitialFocus, HandleEscape, OpensOnDrag) work as now.

## Part E: Phase 5 fixes (from the 2026-10-28 review of the installed build)
1. **Clean URL field** (src/Features/Chippi.Features.Clipboard/Views/CleanUrlView.cs, line 23). When a long link wraps, keep the same padding above and below the text as for one line, and let the field grow to fit up to 3 lines. Right now the second line sits on the field's bottom edge.
   - Past 3 lines, the field shows the link's start and end with "…" in the middle whenever it isn't focused. Focusing it selects the whole link, so a paste replaces it. Nothing scrolls.
   - The result trims the same way, and Copy copies the whole cleaned link. See fixed-size shots\ps3.png.
2. **Scratchpad Preview checkbox** (src/Features/Chippi.Features.Scratchpad/ScratchpadMarkdown.cs, line 28). Size the box for "- [ ]" to the text (14 DIP) and center it on the line's text. Right now it's larger and sits above the line.
3. **Settings › Clean URL empty state.** An empty "Sites to skip" shows a "No sites" row, the same as Clipboard history's "No apps" (ClipboardHistorySettingsPage.cs, line 50). Part F later restyles every empty state as one caption-color row. Add "No sites" to the Settings row of the strings table in docs/phase-5/design/notes.md.
4. **Settings sidebar order** under Clipboard & text: Clipboard history, Paste as plain text, then Clean URL, as on the approved settings board.
   - The order comes from registration in src/Chippi.App/App.xaml.cs, lines 196–199.
   - Check that the Clipboard tile, its page and Add features look the same afterwards.
5. **Debug settings snapshot** (src/Chippi.App/Settings/Pages/AddFeaturesPage.cs, line 291).
   - The self-check searches for "Clipboard history" as an upcoming feature, but it has shipped, so `--debug --settings-snapshot` throws "Feature search did not reveal the matching upcoming feature".
   - Search for a feature that's still upcoming instead, then check that the snapshot run finishes.
6. **Tests:** add checks for the "No sites" card, the sidebar order and the snapshot self-check.

## Part F: The Settings window (src/Chippi.App/Settings, each feature's settings page, Chippi.UI/Controls)
Travis approved this on 2026-10-28, after two critiques of the current Settings window (shell 24/36, pages 28/40). Build from X:\code\chippi-scratch\settings-shell\:
- s1–s5: the shell;
- c1–c9: the pages;
- the caption in index.html lists the rules.

**The new rules (Travis, 2026-10-28):**
- Chippi has two surfaces, the island and Settings. Settings can do everything the island can.
  - This replaces DESIGN §5's "Settings never repeats a daily control".
  - The island itself is unchanged: its pages show live state and controls only.
- The tray icon opens Settings (Part A #6).
- Copy rules that still hold:
  - no page intros;
  - no info (i) buttons;
  - subtext only when a label can't stand alone;
  - captions show state or data, never explanations.
- Settings pages may scroll. "Nothing scrolls" is an island rule.

### F1: The shell (shots s1–s5). May start after Part A.
1. **Sidebar: one list.**
   - Replace the per-group ListBoxes (SettingsWindow.xaml.cs:107-131) and the Chippi list (SettingsWindow.xaml:72) with one ListBox, grouped through a CollectionViewSource with a GroupStyle header.
   - Drop the outer ScrollViewer (xaml:67). Each nested ListBox's ScrollViewer swallows the mouse wheel today. One list gives one scroller, one Tab stop, and arrow keys that cross groups.
   - Rows are 32 tall and 36 apart: NavItemStyle Height 32, Margin 0,2 (Chippi.UI/Controls/Controls.xaml:402-405).
   - Group labels have 12 above (the first has 0) and 4 below. Today it's Margin 12,20,0,6 (xaml:71, xaml.cs:119 and 130).
   - The search box is 32 tall with 12 below (xaml:65).
   - Selecting a page scrolls its row into view (Select(), xaml.cs:192-199).
   - Labels trim with "…" (NavItemTemplate, Controls.xaml:437-443).
   - Result:
     - With 7 features at 1120 × 720, all 12 rows fit, About included (s1).
     - With every feature installed (19 rows) the list scrolls, and the open row stays in view (s3).
   - Order:
     - Files: File organizer, Shelf.
     - Sound: Now Playing, Output, Microphone, App volume.
     - System: Keep awake, System monitor.
     - Clipboard & text: Clipboard history, Clean URL, Paste as plain text, Scratchpad.
     - Chippi: General, Island & pet, Notifications, Shortcuts, Permissions, Add features, About.
   - Now Playing is shell-owned, so the Sound group always shows.
2. **The window behaves like Windows 11 (s4):**
   - **No line under the titlebar.** Set BorderThickness 0 (xaml:29).
   - **Snap layouts.**
     - In SourceInitialized, add an HwndSource hook. Return HTMAXBUTTON (9) from WM_NCHITTEST over MaximizeButton.
     - Drive its hover and pressed states from WM_NCMOUSEMOVE, WM_NCLBUTTONDOWN and WM_NCLBUTTONUP.
     - Pointing at maximize must show Windows' flyout.
   - **Esc does nothing.** Delete OnKeyDown (xaml.cs:387-395). Esc in the search box still clears it.
   - **The window remembers its size, position and maximized state.**
     - Save RestoreBounds and WindowState on close, and restore them on open.
     - The first run is 1120 × 720, centered.
     - If the saved spot is off every monitor, center on the main display.
3. **Width.**
   - PageHost gets MaxWidth 1080 (1000 of content plus its 40 + 40 margins) and HorizontalAlignment Left (xaml:83). Maximized, the content stops at 1000, aligned left (s4).
   - Below a window width of 1008, the sidebar becomes a 48 px icon rail (s5):
     - a menu button, then search as an icon;
     - the page icons, with a hairline between groups and the 3 × 16 accent pill on the open page.
     - The menu button opens the full sidebar over the page, with a shadow. It closes on a pick, on Esc, or on a click outside it.
   - The minimum width stays 860.
4. **Scrollbars float.** Write a ScrollViewer template where the bar overlays the content: a 2 px line at rest, 6 px with a faint track under the pointer, as in Windows 11. The shared ScrollBar style is at SharedStyles.xaml:557-570. Content keeps its full width, so cards are 768 wide on every page at 1120 (s2), and the sidebar rows stay full width.
5. **DESIGN.md:**
   - Fix the tokens to match the build: search 32, and settings-content as 40 / 4 / 40 / 28, written in that order (left, top, right, bottom).
   - Nav rows are 32.

### F2: One page system (every page; shots c1–c9)
1. **One shared Settings toolkit.**
   - Every feature's page uses the same Section, Card, Row, Empty and Expander helpers from one place. Today there are four helper sets: Chippi.App SettingsUi, Clipboard Views/SettingsUi, RoutingUi and Downloads Ui. System monitor and Paste as plain text also have private helpers.
   - **Sections:** 24 above the header and 8 below, so cards sit 52 apart. Headers sit at the card edge.
     - A header's button, such as "+ Add site", sits in the header row without adding height.
     - Notifications today uses Margin 15,22,15,8 (NotificationsPage.cs:103) and an 18 title gap (:37). Make them match: headers at the edge, and 14 from the title to the first card.
   - **Rows:** 48 tall, or 60 with a caption, whatever the control (Padding 16,8). A 32 px combo, a button or the segmented control all fit 48.
   - **Combos:** 180 wide.
   - **Add buttons:** "+ Add <thing>" with the Plus glyph, for example "+ Add site", "+ Add app", "+ Add device", "+ Add mic", "+ Add folder", "+ Add rule".
   - **Empty state:** one row inside the card, in caption color: "No sites", "No apps", "No devices", "No folders yet", "No rules yet", "Nothing organized yet", "No items". Clean URL's empty list collapses today (CleanUrlSettingsPage.cs:80).
   - **Dependent settings fold into expandable rows** (ExpanderRow, Chippi.UI/Controls/Rows.cs:175):
     - the header holds the name, the current value in secondary text (for example "90%" or "10% free"), the On/Off switch and the chevron;
     - the dependent rows sit inside, indented 50.
2. **Labels that stand alone (c1, c2, c3):**
   - **Communications device** (RoutingPage.cs:43-46, Output and Microphone): add the caption "Calls follow this list".
   - **Microphone › Mic processing** (ProcessingViews.cs:164-176):
     - The first row is "Use mic processing", so it doesn't repeat the section header.
     - "Default device" moves right under it, with the caption "Makes CABLE Output the default mic".
     - The Level meter shows only while processing runs.
     - Noise gate (Threshold) and Auto gain (Target level, Max gain) become expandable rows.
     - Input device stays last.
   - **App volume** (VolumeView.cs:65-66): rename the rows to "Show apps that aren't playing" and "Remember each app's output".
   - **Microphone page:** the routing section loses its "Mic in use" title (SettingsWindow.xaml.cs:52). That name moves to F3's new radio list. The page reads: Microphone (Mute), Mic in use, Priority, Mic processing, Mute shortcut.
3. **System monitor alerts** (SystemMonitorSettingsPage.cs:145-187): each alert is an expandable row with its threshold inside. Alerts drops from up to 12 rows to about 6. Repeat interval uses "1 hour" style values, like File organizer's.
4. **Add features** (AddFeaturesPage.cs):
   - Remove the "Overlaps …" badges: FeatureBadge.Overlap in KeepAwakeModule.cs:48, AppVolumeModule.cs:30, MicMuteModule.cs:42, SystemMonitorModule.cs:42, ScratchpadModule.cs:20, ClipboardHistoryModule.cs:37, PlainPasteModule.cs:38 and FeatureCatalog.cs:82.
   - Remove BadgeKind.Overlap if nothing else uses it, and update DESIGN §6.
   - Failures show in WarningTextBrush with a reason: "Start failed · Device in use" instead of the plain "Start failed" (:170). "Driver disabled" (:236) gets the same treatment.
   - Rows are 60, using the toolkit's caption row.
   - Fix "Install Driver" to "Install driver" (:225).
5. **Shortcuts and Permissions pages appear once a feature needs them**, as DESIGN says. Today nothing routes to them: SettingsWindow.xaml.cs:36-43 and View(). Show them in the Chippi group, after Notifications.
   - **Shortcuts:** one row per global shortcut. Each row shows the feature's name, then the keys (or "Not set" in secondary text), Change or Set, and Remove. Today: Mic mute shortcut and Paste as plain text.
   - **Permissions** (c6):
     - One card, one 60 row per permission in use.
     - The name, for example "Keyboard and mouse hooks", over a caption naming the features that use it.
     - "In use" with the green dot on the right; unused ones show "Unused" and Dismiss.
     - Drop the explanation sentences in the captions (PermissionsPage.cs:62-66).
6. **File organizer's first run** (Preview, PreviewView.cs:113-150; c4):
   - Files with no matching rule fold into one row: "19 files · no rule matches", with the caption "They stay in Downloads" and a chevron that lists them.
   - With nothing to approve, the footer reads "Nothing to organize", with Cancel and an accent "+ Add rule" that opens Rules with a new draft.
   - With matches, those are listed and the rest are folded.
7. **Smaller fixes:**
   - **Import asks first (c7).** After the file is chosen, a row reads "Replace every setting with <file name>?" with Replace (accent) and Cancel (GeneralPage.cs:146-178).
   - **No raw exception text.** Replace "Couldn't export · {ex.Message}" and "Not imported · {ex.Message}" (GeneralPage.cs:142, 161, 167), and the same pattern in Keep awake and the File organizer save path, with a short plain reason.
   - **Notifications** hides Low battery and GPU temperature where the PC has no battery or GPU sensor, as System monitor already does.
   - **Copy:**
     - "Prefer" → "Move to top" (RoutingPage.cs:110).
     - "Not set" in secondary text beside Set (PlainPasteView.cs:37).
     - "Stop watching this folder" → "Stop watching" (SettingsSection.cs:95).
     - The Mic mute shortcut can be removed, like Paste as plain text's.
   - **Island & pet's preview** (IslandPetPage.cs:306-359) draws the 520 × 360 island from Part A.
   - **About's VB-CABLE row:** the caption names the site Donate opens, vb-audio.com (SimplePages.cs:49, 54).

### F3: Everything the island does, in Settings (c1, c2, c3, c5, c8, c9)
Use the same module calls the island pages use, and put each page's island controls at the top.
1. **Output (c3):**
   - A Volume row: slider, percentage and Mute.
   - "Output in use": one radio row per connected output. Picking one sets it in use, as the island's rows do.
   - "Priority" (+ Add device): Priority switching, Communications device and the ordered device rows (48 tall).
2. **Microphone (c1):**
   - A Microphone row with its state as the caption (Live, or Muted in WarningTextBrush) and Mute / Unmute.
   - "Mic in use" radio rows.
   - Then Priority (+ Add mic), Mic processing and Mute shortcut, as in F2.
3. **App volume (c2):**
   - One row per app:
     - the app's icon (Part D's icon, or two letters when there isn't one);
     - the name;
     - a 200 px volume slider and the percentage ("Muted" in WarningTextBrush);
     - a mute icon button;
     - an output combo ("Default output" or a device).
   - Every app is listed, with no limit: this is where the island's apps beyond 14 go.
   - Then an "Options" section with the two renamed switches.
4. **Keep awake (c2):**
   - A "Keep awake" row with the state caption ("Off", "On · 52 min left" or "On · no time limit") and Stop while it's on.
   - A "Length" row with the six buttons: 30 min, 1 hr, 2 hr, 4 hr, No limit, Until…. The running one is lit in the accent, and Until… opens the time picker as on the island.
   - Keep screen on.
   - Remove "Default length", "After a restart · Starts off" and the session switch (KeepAwakePage.xaml:17-24, KeepAwakePage.xaml.cs:45).
5. **Clean URL (c2):**
   - A card at the top: the link field, then a row with the cleaned link, the caption "Removed · …" and Copy (accent).
   - Then Automatic cleaning, and Sites to skip.
6. **Clipboard history (c3):**
   - Recording.
   - Apps to skip.
   - A "History" section:
     - Pause / Resume in its header, the search box with the item count, then the full list (Part D): 48 rows, pinned first.
     - Each row's ⋯ opens Paste, Copy, Pin / Unpin and Delete.
     - The list grows as long as the history; the page scrolls.
7. **System monitor (c5):**
   - The island's readings at the top: CPU, GPU, Memory and Network, each with its name, detail and bold value.
   - Then "Disks", with every drive's Used, Free and Total.
   - Then "General" (Update speed, Tile status), Readings, Units and Alerts.
8. **File organizer (c9):** each watched folder row shows:
   - the folder glyph;
   - the name over its path;
   - the mode in secondary text ("Auto", "Manual" or "Paused"; Part D renames Silent);
   - Organize now;
   - the chevron, which opens Mode, Wait before moving, Ignore list and Stop watching.
9. **New page: Now Playing (c8, Sound group, first).**
   - One card: 96 px artwork, the title (16 / 600), the artist, the progress bar with times, then previous / play-pause (40 px accent circle) / next.
   - Then a Source row (combo).
   - Nothing playing: placeholder art, "Nothing playing" and disabled buttons, as on the island.
10. **New page: Scratchpad (c8, Clipboard & text group, last).**
    - The title with the save status at its right ("Saved", or "Save failed" in WarningTextBrush).
    - The note tabs and +.
    - The editor card, then Edit / Preview, Export… and Delete note.
    - Use the same session as the island page, so an edit shows in both at once.
11. **New page: Shelf (c8, Files group, after File organizer).**
    - One 60 row per item: glyph, name, a kind caption ("PDF · 2.1 MB", "Online only", "Unavailable"), the pin glyph when pinned, Copy, and ⋯ (Pin / Unpin, Remove).
    - Unavailable items are dimmed.
    - Rows drag out like the island's tiles.
    - Empty: a "No items" row.

### F tests and checks
- **Tests:**
  - one sidebar list, with 32 rows 36 apart;
  - the open row scrolls into view;
  - Esc doesn't close Settings;
  - window bounds and state are restored;
  - PageHost MaxWidth;
  - the rail below 1008;
  - cards are the same width on a scrolling and a short page;
  - section gaps of 52;
  - empty-state rows;
  - the Shortcuts and Permissions routes;
  - no Overlap badges;
  - the folded preview row and "Nothing to organize";
  - the Import confirm;
  - each F3 control calls the same module method as its island twin.
- **Check:**
  - Run `--debug --settings-snapshot` once Part E #5 is in, in dark and light, at 1120 × 720 and at 900 wide.
  - Compare against the settings-shell shots.
  - Run the self-test with every feature installed in a CHIPPI_DEV_PROFILE. Never point the File organizer at Travis's real Downloads folder in tests.

## All parts
- **Docs:**
  - Update DESIGN.md §5:
    - One 520 × 360 size for every page, instead of the per-page widths.
    - The fit rules: nothing scrolls except a Scratchpad note's text, pages adapt to fit, and overflow goes to Settings. Settings may now hold information for this, not just configuration. Update the Surfaces row in PLAN.md to match.
    - The compact sizes above, and the orbit buttons (32, 10 off, 6 between).
    - The Morph bullet (page to page: a crossfade, the shape stays).
    - The Docked bullet: remove it, and say the tray icon opens Settings.
    - The grid's tile size.
    - The Sound bullets, and the System monitor section's Page, Typography, Graphs, Details and Disks bullets.
    - Mark each "(revised 2026-10-28)".
  - In PLAN.md's Island decision row, add "one fixed size, 520 × 360 (2026-10-28)".
  - Copy the mockup folders to docs/refresh-2026-10-28/ (fixed-size/ without index-640.html or shots-640, sound-monitor/, and settings-shell/) and link them from DESIGN.md.
- **Copy rules:** follow DESIGN.md §3. Quick pages show live state and controls only, with nothing restated.
- **Tests:**
  - Update PageTests, the island smoke tests (Clipboard, Scratchpad, Shelf, Plain paste, System monitor, Sound) and IslandPanelTests.
  - Add checks for:
    - one size for the grid and every page;
    - no shape change between pages;
    - Used = Total − Free, and volumes sorted by letter;
    - the "Mic processing" label and the "Mic stopped" tile status;
    - no page note;
    - no ScrollViewer in any island page except the Scratchpad editor;
    - the grid layout for 4, 6, 9, 12, 15 and 20 tiles;
    - Sound's one-line rows;
    - App volume's strip widths at 7, 12 and 14 apps, and the two-letter fallback;
    - lists leave out a row that doesn't fully fit;
    - Shelf's three sizes, and "Shelf full" refusing a drop at 40 items.
- **Check:**
  - Build, then run all the tests.
  - Run the app with CHIPPI_DEV_PROFILE set, with `--debug --island-pages <dir>`. Compare each page in the island with the fixed-size shots.
  - For Part E, run `--debug --settings-snapshot`.
  - Clipboard recording stays off in the dev profile. Never read Travis's real clipboard.
- **Ship:**
  - Each PR body follows the change-summary format, with before and after screenshots taken from main and from your branch.
  - A second member reviews it. Marge merges and pushes main; nobody else merges.
  - Don't install anything on Travis's PC without asking him first.
- **Working in parallel:** after Part A merges, Parts B to E touch separate files, except DESIGN.md (each part edits only its own bullets). Part A copies the docs folders and adds the shared compact sizes. Part F comes last, because it touches every feature's settings page.
