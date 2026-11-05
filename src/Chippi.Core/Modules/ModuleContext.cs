// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Island;
using Microsoft.Extensions.Logging;

namespace Chippi.Core.Modules;

/// <summary>What a running module gets from the shell. One context per start; it is dead after stop.</summary>
public sealed class ModuleContext(
    string moduleId,
    IModuleSettings settings,
    ILogger logger,
    IIslandPublisher island,
    IModuleUi ui)
{
    public string ModuleId { get; } = moduleId;

    /// <summary>The module's own config section. Kept after uninstall, so reinstalling restores it.</summary>
    public IModuleSettings Settings { get; } = settings;

    public ILogger Logger { get; } = logger;

    /// <summary>Publish island activities and report successes (which make Chippi celebrate).</summary>
    public IIslandPublisher Island { get; } = island;

    /// <summary>Optional island tiles and settings page.</summary>
    public IModuleUi Ui { get; } = ui;

    /// <summary>
    /// The module's own folder for databases and caches (<c>%LOCALAPPDATA%\Chippi\data\&lt;id&gt;</c>, or
    /// the dev profile's). Created before StartAsync and kept after uninstall, like settings.
    /// </summary>
    public string DataDirectory { get; init; } = Path.Combine(ModuleServices.DefaultDataRoot, moduleId);

    /// <summary>Windows and island notices, each event kind going to the places the user chose.</summary>
    public Notifications.INotifier Notifier { get; init; } = Notifications.NullNotifier.Instance;

    /// <summary>Verbs this module handles from launches, the Explorer menu and notice clicks.</summary>
    public IModuleActivation Activation { get; init; } = new ActivationRouter.ModuleActivation(new ActivationRouter(), moduleId);

    /// <summary>Opens Settings on this module's page, at <c>section</c> when given (a notice click opening History).</summary>
    public Action<string?> OpenSettings { get; init; } = _ => { };

    /// <summary>The <see cref="OpenSettings"/> section that opens Add features, where the module's requirement installs.</summary>
    public const string RequirementSection = "requirement";

    /// <summary>Opens the island on a tile's page (a notice click opening the System page).</summary>
    public Action<string> OpenIslandPage { get; init; } = _ => { };

    /// <summary>
    /// Opens the island on a tile's page and says whether it could: false while the island is off or hidden for a
    /// fullscreen app, when nothing opens (no Settings instead). A Quick actions shortcut that needs its confirm.
    /// </summary>
    public Func<string, bool> TryOpenIslandPage { get; init; } = _ => false;

    /// <summary>
    /// Opens the island on a tile's page with the keyboard, even over a fullscreen app or game, on the widget's screen (the
    /// Command bar's shortcut). False only while the island is off.
    /// </summary>
    public Func<string, bool> SummonIslandPage { get; init; } = _ => false;

    /// <summary>Everything Chippi does, from every running feature, with the island's pages and Settings (the Command bar).</summary>
    public Func<IReadOnlyList<ChippiCommand>> Commands { get; init; } = () => [];

    /// <summary>What every running feature makes of the Command bar's text: values typed in, and its own matches.</summary>
    public Func<string, IReadOnlyList<ChippiCommand>> SearchCommands { get; init; } = _ => [];

    /// <summary>Chippi's colour, as Settings › Island &amp; pet picked it, for a Chippi outside the island (Cleaning mode's).</summary>
    public Func<Config.PetColor> ChippiColor { get; init; } = () => Config.PetColor.Synth;

    /// <summary>The shared clipboard service; no module opens the clipboard itself. Null when the shell has none (tests pass a fake).</summary>
    public Clipboard.IClipboardHub? Clipboard { get; init; }

    /// <summary>The shared keyboard and mouse hooks; no module hooks input itself. Null when the shell has none (tests pass fakes).</summary>
    public Input.IInputHub? Input { get; init; }

    /// <summary>The shell's one list of shortcuts; no module registers a hotkey itself. Without a shell, every shortcut is refused.</summary>
    public Chippi.Core.Shortcuts.IShortcutService Shortcuts { get; init; } = new Chippi.Core.Shortcuts.ShortcutService(Chippi.Core.Shortcuts.RefusingRegistrar.Instance);

    /// <summary>
    /// The PC side of actions (Chippi.Core.Actions): no module sends keys, moves windows or opens programs for an action itself.
    /// It runs them with an <see cref="Chippi.Core.Actions.ActionRunner"/> over this. Null when the shell has none (tests pass a fake).
    /// </summary>
    public Chippi.Core.Actions.IActionPc? Actions { get; init; }

    /// <summary>Keep the island open during a dialog or drag. Dispose the scope when it ends.</summary>
    public Func<IDisposable> HoldIslandOpen { get; init; } = () => new IslandOpenHold().Hold();

    /// <summary>The app that was in front before the island took the keyboard or opened docked, for a page's Paste; null when there was none.</summary>
    public Func<Clipboard.PasteTarget?> IslandPasteTarget { get; init; } = () => null;

    /// <summary>The app Settings was last switched from, for a Settings page's Paste; null when there was none.</summary>
    public Func<Clipboard.PasteTarget?> SettingsPasteTarget { get; init; } = () => null;

    /// <summary>Closes the island, after a page's paste was sent.</summary>
    public Action CloseIsland { get; init; } = () => { };

    /// <summary>Whether the widget can show feedback now. Call on the UI dispatcher.</summary>
    public Func<bool> CanShowIslandFeedback { get; init; } = () => false;
}

/// <summary>Typed access to a module's config section (<c>modules.&lt;id&gt;</c> in settings.json).</summary>
public interface IModuleSettings
{
    /// <summary>Reads the section, or a new <typeparamref name="T"/> when there is none yet.</summary>
    T Get<T>() where T : class, new();

    /// <summary>Writes the section and saves settings.json. Unknown properties already in the section are kept.</summary>
    void Save<T>(T value) where T : class;
}
