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

    /// <summary>Publish island activities and report successes (which make Gumdrop celebrate).</summary>
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

    /// <summary>Opens the island on a tile's page (a notice click opening the System monitor page): in the island, or docked by the tray.</summary>
    public Action<string> OpenIslandPage { get; init; } = _ => { };

    /// <summary>The shared clipboard service; no module opens the clipboard itself. Null when the shell has none (tests pass a fake).</summary>
    public Clipboard.IClipboardHub? Clipboard { get; init; }

    /// <summary>The app that was in front before the island took the keyboard or opened docked, for a page's Paste; null when there was none.</summary>
    public Func<Clipboard.PasteTarget?> IslandPasteTarget { get; init; } = () => null;

    /// <summary>Closes the island, after a page's paste was sent.</summary>
    public Action CloseIsland { get; init; } = () => { };
}

/// <summary>Typed access to a module's config section (<c>modules.&lt;id&gt;</c> in settings.json).</summary>
public interface IModuleSettings
{
    /// <summary>Reads the section, or a new <typeparamref name="T"/> when there is none yet.</summary>
    T Get<T>() where T : class, new();

    /// <summary>Writes the section and saves settings.json. Unknown properties already in the section are kept.</summary>
    void Save<T>(T value) where T : class;
}
