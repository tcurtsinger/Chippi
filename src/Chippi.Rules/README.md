# Chippi.Rules

Pure .NET 10 library: no project/package dependencies, disk access, OS calls, ambient clock, WPF, logging or file mutation. Windows paths are handled lexically, even on a non-Windows test host. The module supplies facts; the library proposes an operation.

## API

- `Rule(Id, Name, Enabled, Order, Conditions, Action)`; IDs and integer orders must be unique across the list, including disabled rules.
- `Conditions`: optional `Extensions`, `NameRegex`, `MinBytes`, `MaxBytes`, `ModifiedAfter`, `ModifiedBefore`. Supplied conditions combine with AND. Null extensions means unrestricted; `""` explicitly matches extensionless names, `"*"` matches all. Extension entries have no dot; a non-null empty list is invalid.
- `RuleAction`: `MoveTo(DestinationTemplate)`, `RenameTo(NameTemplate)`, `Hold(Reason)`. No delete/trash action.
- `FileFacts(Name, FullPath, Bytes, Created, Modified, Attributes)`: module gathers metadata; Rules never opens the file. Name and path must agree, and the source must be an immediate child of the context folder.
- `PlanContext(WatchedFolder, PlanningTime, TimeZone, OccupiedPaths)`, with optional parsed `IgnorePatterns` and `Preview` (default false). Pass a snapshot of occupied files and the intended local time zone; nothing is inferred from the current environment. Date conditions are inclusive `DateOnly` values compared with Modified converted into that zone. Placeholder dates use PlanningTime converted into the same zone.
- `RuleValidator.Validate(rules)` returns field errors for editor/storage validation. `Planner.Plan` also validates and throws `ArgumentException` for invalid rule configurations. File-specific path errors produce held plans. Negative file sizes are invalid caller facts and throw.
- `Planner.Plan(file, rules, context)` returns `PlannedOperation(Kind, Source, RuleId, Destination, Warnings)`, plus `HoldReason` for an explicit Hold rule. Kind is Move, Rename, Hold or NoMatch. Sorts by Order; stops at first enabled match. It does not try another rule after a matched rule's destination is invalid.
- `Planner.PlanBatch(files, rules, context)` preserves input order, reserves source paths and earlier destinations, and rejects duplicate source paths. It does not modify the caller's collections. `OccupiedPaths` for individual planning must include prior proposals if planning a batch manually.
- `PlanWarning` has a machine-readable `WarningKind` and detail. A regex timeout produces `RegexTimeout` with the offending rule ID and proceeds to later rules. Caller logs the diagnostic. Regex uses IgnoreCase + CultureInvariant with a 50 ms timeout.
- `Templates.Render` and `Templates.Validate`: `{year}`, `{month}`, `{day}`, `{extension}`, `{filename}`, `{stem}`. Month/day are padded; extension is lowercase without the dot; filename retains the original suffix and case. Real filename braces are never interpreted as new tokens.
- `WindowsPaths`: absolute drive/UNC paths, canonical separators, case-insensitive comparison at planning sites, reserved-name/ADS/control-character/traversal validation. No drive-relative, root-relative, device or extended-device paths. Ordinary paths longer than 260 characters are supported; individual components are limited to 255 characters. UNC server and share are required.
- `IgnoreFile.Parse(text)` / `Matches(name, pattern)`: filenames, star wildcards, case-insensitive matching, blank/comment lines, inline comments and escaped hashes. Repeated separated stars are supported; consecutive double-star, negation, question-mark and directory patterns are rejected. Parse returns `IgnoreParseResult(Patterns, Errors)`: unsupported lines are omitted individually and reported with one-based physical line numbers; valid patterns always survive. Pass `.Patterns` into PlanContext and display `.Errors` separately. Read/write `.chippiignore` in the module, not here.
- `FileSafety.Exclusion(facts)` / `IsMisleadingName(name)`: built-in/system/temp/partial exclusions, supplied hidden/system/directory/reparse attributes, cloud placeholders, zero-byte files, executable disguise/bidi/space checks.

## Important caller obligations

A plan is not an authorization or filesystem reservation. The executor must recheck file identity/content, readiness, destination vacancy, permissions and archive constraints immediately before mutation. It owns collision races, copy/verification, history and undo.

`Preview=true` permits planning a misleading executable with its warning intact so the view can show it initially unchecked. It does not authorize execution. Automatic calls must leave Preview false. Built-in/ignore/empty/cloud exclusions never acquire a destination even in preview. Hold warning reasons distinguish Waiting (empty/unavailable data) from exclusions and suspicious names; the module chooses their presentation.

Relative move destinations resolve beneath WatchedFolder; absolute destinations may be another drive or UNC share. Moving back to the source folder or renaming to the same Windows name returns NoMatch with the matched RuleId and no invalid-path warning; it still ends first-match selection. Numeric collision names start at (2) and preserve the original suffix's case. No file is overwritten or moved by this library. Existing files and reserved planned paths must be included in OccupiedPaths.

The module owns grace periods, file stability and lock probes, retries, initial-folder baselines, mode routing, manual approval, notification delivery, archive extraction and size limits, and history. None of those are hidden in this planner. Pure source/template validation cannot guarantee that a network share exists, a junction resolves safely, or the user has write access.

## Checks

`dotnet test tests/Chippi.Rules.Tests/Chippi.Rules.Tests.csproj -c Release`

117 tests cover ordering, combined conditions, local dates/DST, timeout fallthrough, placeholders, rename, collisions/reservations, exclusion/ignore reasons, misleading names, invalid configurations, device/UNC/long paths and caller-state preservation. No tests create files or read the current clock.
