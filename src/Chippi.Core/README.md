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
| `IModuleUi` | `AddToolTile(ToolTileModel)`, `AddFlyoutTab(FlyoutTab, Func<object>)`, `SetSettingsPage(Func<object>)`. All removed automatically when the module stops. |
| `ToolTileModel` | INotifyPropertyChanged: `Name`, `Icon`, `IsOn`, `StateLine`, `ToggleAsync()`. The shell renders it. |
| `FlyoutTab` | `Sound, System, Tidy, Tools`. |
| `FlyoutTabContribution`, `SettingsPageContribution` | What the shell reads back per feature. |
| `ModuleRegistry` | `Register(module)`, `RegisterComingSoon(descriptor)`, `StartAsync()`, `SetInstalledAsync(id, bool)` (switches preset to Custom), `ApplyPresetAsync(preset)`, `ReloadAsync()` (after import), `StopAllAsync()` (exit), `Features`, `Find(id)`, `Preset`, `InstalledCount`, `PresetMembers(preset)`, `GetPermissions()`, `DismissUnusedCapability(id)`, event `Changed` (any thread). First run applies Essentials. |
| `FeatureEntry` | `Descriptor`, `Id`, `IsComingSoon`, `IsInstalled`, `IsRunning`, `LastError`, `ToolTiles`, `FlyoutTabs`, `SettingsPage`. |

**Why UI contributions look like this.** Core cannot reference WPF. Tiles are uniform, so the
module hands over a plain model and the shell draws every tile identically per DESIGN.md §6.
Tabs and settings pages are free-form, so the module passes a factory returning `object` and the
App casts it to `FrameworkElement`. Only a module that ships its own views references WPF, and
then it references `Chippi.UI` (below), never `Chippi.App`.

## Chippi.UI (the WPF design system, `src/Chippi.UI`)

Not part of Core, listed here so module authors find it. Reference `Chippi.Core` and `Chippi.UI`;
set `<UseWPF>true</UseWPF>`. In XAML write `xmlns:c="urn:chippi-ui"`.

- Resources (merged by App.xaml, resolve with `DynamicResource`): Hopper's theme keys
  (`WindowBackgroundBrush`, `SurfaceBrush`, `TextTertiaryBrush`, `AccentGradientBrush`, ...) plus
  `AccentGradientDeepBrush`, `DangerDeepBrush`, `Island*`, `Pet*`, `Badge*`, type styles
  (`DisplayTextStyle` ... `MonoReadoutTextStyle`), `FocusRing*`, `ChippiWindowStyle` (every window
  uses it), and the keyed control styles `PrimaryButtonStyle`, `SecondaryButtonStyle`,
  `SwitchStyle`, `SegmentButtonStyle`, `HopperSliderStyle`, `HopperComboBoxStyle`,
  `TabSegmentedControlStyle`.
- Controls (`Chippi.UI.Controls`): `IconButton`, `IconToggleButton`, `Badge`, `StatusDot`,
  `Eyebrow`, `SearchBox`, `SegmentedControl` + `SegmentItem`, `AppVolumeRow`, `StatTile`,
  `StashRow`, `ToolTile`, `FeatureCard`, `LucideIcon` + `LucideIcons`, converters.
- `Chippi.UI.Themes`: `ThemeManager.Apply(ThemePreference)`, `ThemeManager.ThemeChanged`,
  `Motion.IsReduced` / `Motion.Changed` / `Motion.Override`.
- Debug builds only: `ControlGalleryWindow` (`Chippi.exe --gallery [--snapshot <dir>]`).

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
| `AppSettings` | Section `app`: `Theme` (`ThemePreference`), `IslandEnabled`, `PetColor`, `ReduceMotion` (null = follow Windows), `IslandDisplay` (`FollowActiveWindow`, `MainDisplay`, `SpecificMonitor`; default in `AppSettings.DefaultIslandDisplay`), `IslandMonitorId` (the monitor's stable device path for `SpecificMonitor`). |
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
