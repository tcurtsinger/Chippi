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

    /// <summary>Optional flyout tile, flyout tab and settings page.</summary>
    public IModuleUi Ui { get; } = ui;
}

/// <summary>Typed access to a module's config section (<c>modules.&lt;id&gt;</c> in settings.json).</summary>
public interface IModuleSettings
{
    /// <summary>Reads the section, or a new <typeparamref name="T"/> when there is none yet.</summary>
    T Get<T>() where T : class, new();

    /// <summary>Writes the section and saves settings.json. Unknown properties already in the section are kept.</summary>
    void Save<T>(T value) where T : class;
}
