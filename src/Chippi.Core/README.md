# Chippi.Core

Contracts and services shared by the shell and every feature module. No WPF reference.
Contract changes after WS0 go through Kayla.

## Modules (`Chippi.Core.Modules`)

| Type | What it is |
|---|---|
| `IFeatureModule` | `Descriptor`, `StartAsync(ModuleContext, ct)`, `StopAsync(ct)`. The constructor must acquire nothing: uninstalled modules are constructed (so the hub can read the descriptor) but never started. |
| `FeatureDescriptor` | Record: `Id` (kebab-case, also the config section), `Name`, `Description`, `Group`, plus `Requires`, `Badges`, `Presets`, `Icon` (a `LucideIcons` property name). |
| `FeatureGroup` | `Files, Sound, System, ClipboardText, Windows, Capture, Apps` (hub order). `DisplayName()` gives "Clipboard & text". |
| `Capability` | Record, equal by `Id`. Built-ins: `Admin`, `VbCable`, `InputHooks`, `ScreenCapture`. |
| `FeatureBadge`, `BadgeKind` | `Source` / `Overlap` / `Needs` + text. Factories `FeatureBadge.Source("From Hopper")` and so on. |
| `FeaturePreset`, `PresetMembership` | Presets `Essentials, Audio, PowerUser, Everything, Custom`; a descriptor opts into the first three with flags. Everything = every installable feature. |
| `ModuleContext` | `ModuleId`, `Settings` (`IModuleSettings`), `Logger` (`ILogger`), `Island` (`IIslandPublisher`), `Ui` (`IModuleUi`). |
| `IModuleSettings` | `Get<T>()`, `Save<T>(value)`. Section `modules.<id>` in settings.json, kept after uninstall. |
| `IModuleUi` | `AddFlyoutPanel(name, icon, createView, createIslandView = null)`, `SetSettingsPage(Func<object>)`. All removed automatically when the module stops. The flyout shows a tab per panel in sidebar order; only panels with an island view appear in the island. |
| `FlyoutPanelContribution`, `SettingsPageContribution` | What the shell reads back per feature. |
| `ModuleRegistry` | `Register(module)`, `RegisterComingSoon(descriptor)`, `StartAsync()`, `SetInstalledAsync(id, bool)` (switches preset to Custom), `ApplyPresetAsync(preset)`, `ReloadAsync()` (after import), `StopAllAsync()` (exit), `Features`, `Find(id)`, `Preset`, `InstalledCount`, `PresetMembers(preset)`, `GetPermissions()`, `DismissUnusedCapability(id)`, event `Changed` (any thread). First run applies Essentials. |
| `FeatureEntry` | `Descriptor`, `Id`, `IsComingSoon`, `IsInstalled`, `IsRunning`, `LastError`, `FlyoutPanels`, `SettingsPage`. |

**Why UI contributions look like this.** Core cannot reference WPF. Panels, island views and
settings pages are free-form, so the module passes a factory returning `object` and the App casts
it to `FrameworkElement`. Only a module that ships its own views references WPF, and
then it references `Chippi.UI` (below), never `Chippi.App`.

## Chippi.UI (the WPF design system, `src/Chippi.UI`)

Not part of Core, listed here so module authors find it. Reference `Chippi.Core` and `Chippi.UI`;
set `<UseWPF>true</UseWPF>`. In XAML write `xmlns:c="urn:chippi-ui"`.

- Resources (merged by App.xaml, resolve with `DynamicResource`): the theme keys from DESIGN.md's
  front matter (`WindowBackgroundBrush`, `SurfaceBrush` is the card, `PanelBrush` the flyout card,
  `TextTertiaryBrush` the caption, `AccentBrush` / `OnAccentBrush`, ...), `DangerDeepBrush`,
  `Island*` (the island uses `IslandAccentBrush`), `Pet*`, the fonts `UiFont`, `DisplayFont` and
  `CodeFont`, type styles (`DisplayTextStyle`, `HeadlineTextStyle`, `BodyTextStyle`,
  `CaptionTextStyle`, `NavGroupTextStyle`, `NumberTextStyle`), `FocusRing4` / `FocusRing8` /
  `FocusRingPill`, `ChippiWindowStyle` (every window uses it), and the keyed control styles
  `PrimaryButtonStyle`, `SecondaryButtonStyle`, `CriticalButtonStyle`, `SubtleButtonStyle`,
  `SwitchStyle`, `SwitchWithLabelStyle`, `HopperSliderStyle`, `HopperComboBoxStyle`,
  `TextFieldStyle`, `FlyoutSegmentedControlStyle`, `SelectorBarStyle`, `FlyoutTabsStyle`,
  `NavItemStyle` + `NavItemTemplate`, `DurationChipStyle`, `SwatchStyle`, `SwatchPillStyle`,
  `FlyoutCardStyle` and `CardRowsStyle`.
- Controls (`Chippi.UI.Controls`): `Card` (a section's rows with dividers), `SettingRow`,
  `ExpanderRow`, `IconButton`, `IconChip`, `Badge`, `StatusDot`, `SearchBox`, `SegmentedControl` +
  `SegmentItem`, `DragHandle`, `LucideIcon` + `LucideIcons`, converters. `ToolTile` and
  `FeatureCard` stay until Phase 2.5's flyout and Settings work replaces them.
- `Chippi.UI.Themes`: `ThemeManager.Apply(ThemePreference)`, `ThemeManager.SetAccent(dark, light)`,
  `ThemeManager.NamedAccent(AccentChoice)`, `ThemeManager.ThemeChanged`,
  `Motion.IsReduced` / `Motion.Changed` / `Motion.Override`.
- Debug builds only: `ControlGalleryWindow` (`Chippi.exe --gallery [--snapshot <dir>]`).

## Phase 2 additions (`Chippi.Core.Modules`, `Chippi.Core.Notifications`)

| Type | What it is |
|---|---|
| `ModuleContext.DataDirectory` | The module's own folder for databases and caches: `%LOCALAPPDATA%\Chippi\data\<id>` (a dev profile's under `Chippi-dev\<profile>\data`). Created before `StartAsync`, kept after uninstall. |
| `ModuleContext.Notifier` (`INotifier`) | `Declare(kinds)`, then `Show(Notice)`. Each `NoticeKind` goes to the `NoticePlaces` the user set (Windows, Island, both, none; stored in `modules.<id>.notices`), else its default. `PlacesFor` / `SetPlaces` back the settings grid. Never throws for a missing or broken Windows backend. Island notices clear themselves after a few seconds; `Celebrate` makes Gumdrop celebrate. The module's own flyout tab lists every event regardless. |
| `FeatureEntry.Notifier` | The running feature's `INotifier` (kinds, `PlacesFor`, `SetPlaces`), for the app-wide Settings › Notifications page. Null while not running. |
| `IToastPresenter` | The Windows backend, set by the shell on `ModuleServices.Toasts` (P2-D). Until then notices reach the island only. |
| `ModuleContext.Activation` (`IModuleActivation`) | `Register(verb, handler)`: `Chippi.exe --<verb> values...` from a second launch, the Explorer menu or a notice click reaches the handler on the UI thread. One module per verb; removed on stop. Anything unrouted opens the flyout. |
| `ModuleServices` | Shell-side: `DataRoot`, `Activation` (`ActivationRouter.TryRoute(args)`), `Toasts`, `IslandNoticeDuration`, `Time`. Passed to `ModuleRegistry`'s new optional parameter. |
| `IModuleUi.SetSettingsPage(Func<string?, object>)` | A page with sections; `OpenSettings("module:<id>/<section>")` opens it there. The Phase 1 `Func<object>` overload still works. |
| `Capability.ShellIntegration` | "Explorer menu entry", for the Permissions page. |
| `SingleInstance` | A second launch now sends its arguments over a per-user, per-session named pipe; `ActivationRequested` carries them (`ActivationEventArgs.Arguments`). Requests before the first subscriber are held in order. |
## Island (`Chippi.Core.Island`)

| Type | What it is |
|---|---|
| `IslandActivity` | Record: `Key`, `Kind`, `Title`, `Source`, `Progress` (0..1 or null), `ModuleId` (set by the hub). |
| `IslandActivityKind` | `Media, Timer, Download, Stash, Agent, Status`. `Priority()`: media 0, timer 1, download = stash 2, agent 3, status 4. Status is background state such as Keep awake. |
| `IIslandPublisher` | `Publish(activity)` (upsert by key), `Clear(key)`, `ReportSuccess(message)`. |
| `IslandActivityHub` | `Current` (highest priority, newest within a priority), `Activities`, `CreatePublisher(moduleId)` (the debug menu uses its own id), `ClearModule(id)`, events `Changed` and `Succeeded` (`ModuleSucceededEventArgs`: Celebrate). Thread-safe; events fire on the publisher's thread. |

## Config (`Chippi.Core.Config`)

| Type | What it is |
|---|---|
| `ConfigStore` | `%APPDATA%\Chippi\settings.json`. `Load()`, `Save()`, `HasSection`, `GetSection<T>` / `SetSection<T>` (merges, saves), `GetModuleSettings<T>` / `SetModuleSettings<T>` / `HasModuleSettings`, `Export(path)`, `Import(path)` (throws `InvalidDataException` and changes nothing on a bad file), events `Imported` and `SectionChanged` (args `Name`: "app", "features" or "modules.<id>"; raised after the file is written, on the saving thread, for writes through this store only). Versioned (`version: 1`), atomic writes, unknown sections and properties survive, a corrupt file is moved aside. |
| `AppSettings` | Section `app`: `Theme` (`ThemePreference`, default `System`), `Accent` (`AccentChoice`: `FollowWindows` (default), `Blue`, `Violet`, `Green`, `Pink`), `IslandEnabled`, `IslandEdge` (`Top` (default), `Left`, `Right`), `IslandPlace` (0 to 1 along the edge, default 0.5), `PetColor`, `ReduceMotion` (null = follow Windows), `IslandDisplay` (`FollowActiveWindow`, `MainDisplay`, `SpecificMonitor`; default in `AppSettings.DefaultIslandDisplay`), `IslandMonitorId` (the monitor's stable device path for `SpecificMonitor`). |
| `ThemePreference` | `Dark, Light, System`. (Not `ThemeMode`: WPF 10 has its own `System.Windows.ThemeMode`.) |
| `PetColor` | `Synth, Iris, Azure, Signal, Ember, Frost`. |

## Permissions (`Chippi.Core.Permissions`)

`CapabilityUsage(Capability, UsedBy, IsUnused)` from `ModuleRegistry.GetPermissions()`. Lists every
capability any registered feature declares. `UsedBy` = installed features needing it. `IsUnused` =
something installed needed it before and nothing installed needs it now (persisted, so it survives
restarts). Never-used capabilities are not flagged.

## Diagnostics and system (`Chippi.Core.Diagnostics`, `Chippi.Core.SystemIntegration`)

| Type | What it is |
|---|---|
| `RollingFileLog` | `%LOCALAPPDATA%\Chippi\logs\chippi.log`, rolls at 1 MB to `chippi.1.log` ... keeps 5. Never throws. |
| `RollingFileLoggerProvider` | `ILoggerProvider` over it. |
| `Log` | Static `Info/Warn/Error` for startup code before DI exists. |
| `SingleInstance` | `TryAcquire(name)`: owner or null (after signalling the owner). The second launch calls `AllowSetForegroundWindow` for the owner first, so the owner may take the foreground. Event `ActivationRequested` (background thread; a request before the first subscriber is held). |
| `StartupRegistration` | HKCU Run, value `Chippi`, command `"<exe>" --startup`. `IsEnabled()`, `SetEnabled(bool, exePath?)`, `GetCommand()`. |
