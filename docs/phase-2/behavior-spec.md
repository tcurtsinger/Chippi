Yes: an unsigned, unpackaged .NET 10 WPF app installed by Velopack can show local Windows app notifications, provided it registers notification identity/activation through a supported desktop API, deploys that API's dependencies, runs unelevated, and Windows permits notifications. MSIX identity and Authenticode signing are not prerequisites for the local-notification routes below.

# Phase 2 behavior research

Research date: 2026-10-22. Scope: behavior research only, no implementation, dependency changes, installation, or notification registration performed.

Evidence labels:
- **Docs:** stated in Mouzi README or documentation.
- **Source:** behavior directly observed in named functions, without running Mouzi. This is stronger than inference but is not a runtime test.
- **Inference/proposal:** a conclusion or proposed Chippi requirement, not a claim that Mouzi guarantees it.

Mouzi reference: public docs label themselves v0.2.1; source inspected at commit `126f29701c38a692cde63fa429df6364bd196226`. Read source solely to understand behavior. No source code, UI copy, translations, or assets are reproduced here or proposed for reuse. Links below identify the original sources.

## 1. Watched folders and modes

**Docs:** Multiple watched folders share global rules. Only immediate child files are processed, not recursive subdirectories. Relative destinations resolve beneath the source folder. Silent queues arrivals for automatic processing after a delay. Manual holds them for preview and approval. Paused disables processing and is excluded from manual organization. The current detailed docs also describe Suggest, an additional approval mode; it is beyond PLAN.md's three-mode list. Silent startup includes files already present. An optional per-folder new-files-only baseline persists across restarts and is based on names, not modification. [Folder behavior](https://mouzi.cc/docs/folders-and-modes).

**Source:** `FolderWatcher::start` omits paused folders from watcher registration and scans existing top-level entries. Manual and suspicious entries go to the review queue; other eligible entries get a grace deadline. [Watcher](https://github.com/hsr88/mouzi/blob/126f29701c38a692cde63fa429df6364bd196226/src-tauri/src/watcher.rs).

**Conflict:** README describes Paused as still watching; detailed docs and source say no watcher. Do not copy the README claim into the brief. **Chippi proposal:** retain exactly Silent/Manual/Paused unless Travis expands scope. Define startup backlog handling and first-run mode explicitly; the decision is pending, not inherited from Mouzi. [README](https://github.com/hsr88/mouzi/blob/126f29701c38a692cde63fa429df6364bd196226/README.md).

## 2. Rule conditions, ordering, destinations and rename

**Docs:** Enabled rules are ordered by increasing numeric priority, with the first match terminating selection. Extension matching ignores case and accepts an explicit catch-all. Regex evaluates the complete filename, including suffix; default regex case sensitivity differs from extension matching. Extension, regex, byte-size bounds and modification-date conditions combine with AND. Dates are inclusive UTC days, not creation dates. Editor validation rejects malformed rules. Missing destination folders are created; existing files are retained and collisions get a numbered name. A conflict arising after preview requires a fresh plan. [Rule documentation](https://mouzi.cc/docs/rules).

**Source:** `matches_rule` tests the conjunction; `process_file_in_run` selects one matching rule. Mouzi uses Rust regex, so a .NET implementation must define its own supported syntax and time limits rather than promise identical regex behavior. [Rules source](https://github.com/hsr88/mouzi/blob/126f29701c38a692cde63fa429df6364bd196226/src-tauri/src/rules.rs).

**Docs:** Destination tokens are `{year}`, `{month}`, `{day}`, `{extension}`, `{filename}`. Year/month/day use the current UTC organization date; month/day are zero-padded. Extension is lowercase without a dot. Filename includes the extension, so using it as a destination component makes a directory named after the whole file. Absolute destinations may cross drives; relative paths stay under the supplying watched folder. [Dynamic paths](https://mouzi.cc/docs/dynamic-paths).

**Docs:** Rename-only keeps the parent directory and supports `{stem}` in addition to those filename/date tokens. Invalid characters, traversal, unknown tokens and reserved names are rejected; collisions use a numeric suffix and new rename records support undo. **Source:** template validation also rejects control characters, trailing dot/space, and overly long names. [Rules documentation](https://mouzi.cc/docs/rules), [template and destination behavior](https://github.com/hsr88/mouzi/blob/126f29701c38a692cde63fa429df6364bd196226/src-tauri/src/operations.rs).

**Chippi proposal:** specify date basis, regex case handling/timeouts, equal-priority tie order, extensionless inputs, path canonicalization and collision policy before coding. These choices are not all settled by PLAN.md. Treat a preview's destination as fixed until it expires; do not silently recalculate at execution.

## 3. Ignore file and readiness

**Docs:** Built-in exclusions precede per-folder `.mouziignore`. They cover common OS names, dot-prefixed names, hidden/system Windows attributes, and partial-download suffixes such as `.part`, `.tmp`, `.download`, `.crdownload`. The ignore file is filename-oriented, with exact names, repeated `*`, blank lines, comments and escaped literal hashes. Windows matching ignores case. Directory entries do not imply recursive protection. Default Silent delay is five minutes, configurable from zero to seven days. Lock checking is normally on; explicit organization bypasses the delay but retains exclusions and lock checks. [Exclusions](https://mouzi.cc/docs/exclusions), [settings](https://mouzi.cc/docs/settings-and-data).

**Source:** `ignore.rs` implements a small star-wildcard matcher, not a complete gitignore engine. Do not assume negation, question-mark matching or recursive glob semantics. `is_file_locked` attempts a write-open; failure is treated as unavailable, which can include permissions as well as locks. Automatic queued deadlines use a monotonic clock and are reconsidered against the folder mode before execution. [Ignore parser](https://github.com/hsr88/mouzi/blob/126f29701c38a692cde63fa429df6364bd196226/src-tauri/src/ignore.rs), [readiness checks](https://github.com/hsr88/mouzi/blob/126f29701c38a692cde63fa429df6364bd196226/src-tauri/src/rules.rs), [queue](https://github.com/hsr88/mouzi/blob/126f29701c38a692cde63fa429df6364bd196226/src-tauri/src/watcher.rs).

**Important conflict:** README claims zero-byte placeholders are skipped. Detailed exclusions docs explicitly say otherwise. The inspected `should_ignore_file`, `scan_file` and `process_file_in_run` path does not exclude a zero-length file merely for being empty. Do not describe automatic zero-byte skipping as verified Mouzi behavior. PLAN.md explicitly requires it for Chippi. [README](https://github.com/hsr88/mouzi/blob/126f29701c38a692cde63fa429df6364bd196226/README.md), [exclusions docs](https://mouzi.cc/docs/exclusions), [source](https://github.com/hsr88/mouzi/blob/126f29701c38a692cde63fa429df6364bd196226/src-tauri/src/rules.rs).

**Inference/proposal:** A successful lock probe or elapsed grace period does not prove a download is finished. Chippi needs revalidation immediately before mutation, bounded retries for unavailable files, and a policy for zero-byte placeholders becoming nonempty. Do not promise indefinite automatic retry from Mouzi's documentation; a skipped queue item is not a documented durable retry job. Choose Chippi's ignore filename independently; supporting Mouzi's branded filename is not yet requested.

## 4. Preview and approval

**Docs:** Preview lists source, selected rule, destination and errors before changes. Users select operations, review and confirm; failures are reported per item. Plans expire after 30 minutes and are single-use. Changed inputs and new destination conflicts are rejected at execution. Suspicious executable names and trash operations begin unselected. Suggest remains an approval queue even during scheduled work. [Preview documentation](https://mouzi.cc/docs/organization-preview).

**Source:** cached plans include verification information, are guarded by an operation lock, and use file fingerprints for change checks. This is source evidence, not a guarantee that every filesystem race is eliminated. [Operation planning and execution](https://github.com/hsr88/mouzi/blob/126f29701c38a692cde63fa429df6364bd196226/src-tauri/src/operations.rs).

**Chippi proposal:** separate planning from applying. Approval should reference a plan/run ID and selected operation IDs. A changed rule, setting, file or destination must invalidate affected operations rather than authorize a newly computed action. UI design and exact expiry are still brief decisions.

## 5. History and verified undo

**Docs:** SQLite stores local actions, paths, rules, timestamps and organization-run grouping. Users filter/search and undo selected entries or a run after review. Undo refuses an occupied original path, missing organized file or changed contents; it never overwrites the original location. Restored files avoid immediate automatic reprocessing until their contents change. Older entries without verification cannot use safe undo. Clearing history does not mutate files but removes undo capability. Trash restoration belongs to the OS. [History](https://mouzi.cc/docs/history-and-undo), [local database](https://mouzi.cc/docs/settings-and-data).

**Source:** `fingerprint` hashes file contents with SHA-256; `undo_one` compares the stored fingerprint with the organized file, checks source absence, then moves without overwrite and records restoration. The filesystem change and database commit are separate steps, so crash consistency must be designed explicitly for Chippi. [Undo implementation behavior](https://github.com/hsr88/mouzi/blob/126f29701c38a692cde63fa429df6364bd196226/src-tauri/src/operations.rs).

**Chippi proposal:** record planned/started/completed/failed states and reconcile interrupted operations. Treat verified undo as a new checked operation, not a blind inverse move. Keep the storage decision behind a store abstraction; Travis's SQLite/JSON task remains authoritative despite PLAN.md currently naming SQLite.

## 6. Archive import

**Docs:** ZIP, TGZ and TAR.GZ imports create unique cache staging, flatten member directories and number duplicate filenames. Current global rules then organize extracted files; relative destinations remain in staging and absolute destinations can leave it. The source archive is preserved. Unmatched files remain staged; users retain responsibility for removing imported data. Import is separate from watched-folder modes, not automatically preview-only because a watched folder is Manual. Unsafe escape paths are rejected. [Archive docs](https://mouzi.cc/docs/archive-import).

The website result was stale (labelled v0.1.5); these details were cross-checked against the current pinned documentation source. [Current archive document](https://github.com/hsr88/mouzi/blob/126f29701c38a692cde63fa429df6364bd196226/website/src/content/docs/archive-import.mdx).

**Source:** ZIP paths use an enclosed-path check; TAR extraction skips entries other than regular files. **Chippi proposal:** independently define traversal/link rejection, total expanded bytes/member-count limits, cancellation, partial-failure staging retention, and whether import must enter preview. Do not infer zip-bomb limits from a path traversal check. [Archive behavior source](https://github.com/hsr88/mouzi/blob/126f29701c38a692cde63fa429df6364bd196226/src-tauri/src/archive.rs).

## 7. Misleading executable names

**Docs:** A name resembling a document with a trailing executable suffix is held for review and warned about locally, rather than automatically organized; its preview starts unselected. This is a filename heuristic, not malware scanning or quarantine. [Preview warnings](https://mouzi.cc/docs/organization-preview).

**Source:** the heuristic lowercases and splits the filename, checks an executable/script/install suffix, and looks for a preceding document/media/archive-like suffix. Automatic processing rejects the warning case. This does not establish PE-content inspection, Unicode spoof detection or reputation checks. [Name-warning predicate](https://github.com/hsr88/mouzi/blob/126f29701c38a692cde63fa429df6364bd196226/src-tauri/src/operations.rs).

**Chippi proposal:** use warning/review semantics and test false positives; do not run an executable from a warning or offer a notification action that directly launches it.

## 8. Windows notifications: feasible without MSIX or signing

### Supported routes and their conditions

**Modern Windows App SDK route:** Microsoft recommends `Microsoft.Windows.AppNotifications.AppNotificationManager` for new WPF/unpackaged desktop work. The .NET walkthrough covers .NET 6 or later, which includes .NET 10. Subscribe to `NotificationInvoked` before calling `Register`, and register before showing notifications. For unpackaged apps, registration sets up COM activation and identity automatically, so a hand-authored Start-menu AUMID and activator are not requirements of this route. Apps must run unelevated. This is supported-platform research; Chippi has not integrated or runtime-tested it. [API choice](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/), [.NET WPF walkthrough](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet).

**Classic Win32/WinRT route:** a Start-menu shortcut carries `System.AppUserModel.ID` and the toast-activator CLSID; the process uses that same AUMID. A registered COM local server/notification activator handles clicks, including cold activation after exit. Creating only an AUMID does not implement activation. This desktop route does not require an MSIX package or a signed application, but registration and a stable executable location are required. [Microsoft desktop toast sample and registration](https://github.com/microsoft/Windows-classic-samples/tree/main/Samples/DesktopToasts), [shortcut identity property](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-id).

**Dependency caveat:** .NET self-contained publish is not Windows App SDK self-contained deployment. Adding the modern API requires selecting and deploying its Windows App Runtime dependencies, or configuring its supported self-contained mode, for both x64 and ARM64. Current Microsoft pages disagree about the additional Singleton package: the Register reference mentions a dependency, while the self-contained deployment guide says local AppNotificationManager does not require it (push does). Pin a stable SDK version and prove its deployment on a clean VM before deciding package size or prerequisite policy. Do not reuse Phase 1's single-file assumptions without a deployment check. [Existing WPF integration](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/use-windows-app-sdk-in-existing-project), [self-contained deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps), [Register reference](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.register).

### What Velopack already provides

**Docs:** Velopack creates Desktop and StartMenuRoot shortcuts by default, names them from packTitle, and removes its shortcuts on uninstall. [Velopack shortcuts](https://docs.velopack.io/integrating/shortcuts).

**Source, pinned to Chippi's 1.2.161:** Windows packing supplies `shortcutAumid` from the explicit AUMID option or its package-ID-derived default. The native shortcut writer sets `PKEY_AppUserModel_ID`. It does not in that writer set the toast-activator property or implement Chippi's notification COM callback. [Packing identity](https://github.com/velopack/velopack/blob/1.2.161/src/vpk/Velopack.Packaging.Windows/Commands/WindowsPackCommandRunner.cs), [shortcut writer](https://github.com/velopack/velopack/blob/1.2.161/src/bins/src/windows/shortcuts.rs).

**Local evidence:** inspected the retained ChippiApp 0.1.0 x64 nupkg without executing it. Its nuspec contains `shortcutLocations=Desktop,StartMenuRoot` and `shortcutAumid=velopack.ChippiApp`. Therefore the current installer already supplies a shortcut AUMID; the earlier assumption that Velopack supplies none is incorrect. This is metadata/source evidence, not a fresh inspection of an installed .lnk. Keep the identity stable across updates; dev profiles must not share production registration.

### What clicking can do in Chippi

Microsoft's activation API can deliver arguments and user input to the running process or start the app after exit. The handler can choose UI navigation or a background action. Unregister releases runtime resources on exit; final removal of notification registrations belongs to uninstall. [Registration lifecycle](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.register), [cleanup](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.unregisterall).

**Chippi proposal:** body click opens that organization run in History; buttons can open the preview or a verified destination folder. Undo should open a review or use the same verified undo service, never a raw reverse path move. Dispatch UI work with BeginInvoke. Route typed activation data to the existing process, preserving run/operation IDs: Phase 1's generic second-instance signal alone would lose toast arguments. Process notification activation before a second COM-activated process exits, without showing the flyout for a background-only action. Persist enough context for clicks after restart; reject stale or unknown IDs.

No local toast needs a cloud push service. Respect the app's notification setting and Windows suppression; retain history even when a toast cannot be displayed. A disabled notification must not make file organization fail. [Notification setting](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.setting), [local versus push](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/).

## 9. Questions the handoff must settle

1. Keep PLAN's three folder modes; decide explicitly whether Suggest is out of scope. Fix first-run and existing-file behavior through Travis's task answer.
2. Zero-byte skip is a Chippi requirement despite the Mouzi conflict. Define recheck/retry and writer-stability policy.
3. Decide ignore filename/syntax, rule ordering ties, date basis and regex compatibility rather than claiming complete Mouzi equivalence.
4. Approve preview/rules-editor designs and archive preview policy before UI implementation. Do not add trash, normalization, baseline or scheduling merely because Mouzi has them.
5. Decide history backend through the pending task. No choice has been made by this research.
6. Local toasts are technically possible without MSIX/signing. Choose a route after a small x64/ARM64 publish/registration/cold-click/uninstall spike. The modern API is Microsoft's recommendation; the classic route trades manual registration for different deployment dependencies.
7. Separate the Explorer-menu decision from notifications. Mouzi's current integration is the classic Windows 11 extra-options menu and opens a folder-add confirmation; it does not establish support for the top-level menu. [Windows integration behavior](https://mouzi.cc/docs/settings-and-data).