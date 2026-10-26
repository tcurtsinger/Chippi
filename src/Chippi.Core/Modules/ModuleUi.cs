// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Modules;

// Why "object" view factories:
// Core must not reference WPF, so it cannot hand out FrameworkElements. Island pages and settings
// pages are free-form, so the module supplies a factory returning `object`,
// which the App casts to a FrameworkElement. A module that contributes one references WPF in its
// own project; Core still does not.

/// <summary>UI a running module contributes. Everything registered here is removed when the module stops.</summary>
public interface IModuleUi
{
    /// <summary>
    /// Adds an island tile with its page, Chippi's only quick panel: the island and the tray icon both
    /// open it. Modules that use the same <see cref="IslandTileSpec.Key"/>
    /// share one tile, and their sections make one page. The handle sets the tile's status line.
    /// </summary>
    IIslandTile AddIslandTile(IslandTileSpec spec);

    /// <summary>Supplies the module's page in Settings. <paramref name="createView"/> must return a WPF FrameworkElement.</summary>
    void SetSettingsPage(Func<object> createView) => SetSettingsPage(_ => createView());

    /// <summary>
    /// A settings page with sections the shell can open directly: <c>OpenSettings("module:&lt;id&gt;/history")</c>
    /// passes "history"; the sidebar and the hub pass null (the page's first section).
    /// </summary>
    void SetSettingsPage(Func<string?, object> createView);
}

public sealed record SettingsPageContribution(string ModuleId, Func<string?, object> CreateSectionView)
{
    /// <summary>The page, opened on <paramref name="section"/> when the module knows it.</summary>
    public object CreateView(string? section = null) => CreateSectionView(section);
}

/// <summary>
/// A Settings view holding work that leaving would lose. Settings asks before it shows another page
/// or closes. The view can say no, ask the person in place, and call <c>leave</c> itself if they discard.
/// </summary>
public interface ISettingsLeaveGuard
{
    bool ConfirmLeave(Action leave);
}

/// <summary>Collects one module's contributions for the lifetime of a start.</summary>
internal sealed class ModuleUi(string moduleId, Action changed) : IModuleUi
{
    private readonly Lock _gate = new();
    private readonly List<IslandTileContribution> _tiles = [];
    private SettingsPageContribution? _settingsPage;

    public IReadOnlyList<IslandTileContribution> Tiles
    {
        get { lock (_gate) return [.. _tiles]; }
    }

    public SettingsPageContribution? SettingsPage
    {
        get { lock (_gate) return _settingsPage; }
    }

    public IIslandTile AddIslandTile(IslandTileSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Icon);
        ArgumentNullException.ThrowIfNull(spec.CreateSection);
        var tile = new IslandTileContribution(moduleId, spec);
        lock (_gate) _tiles.Add(tile);
        changed();
        return tile;
    }

    public void SetSettingsPage(Func<string?, object> createView)
    {
        ArgumentNullException.ThrowIfNull(createView);
        lock (_gate) _settingsPage = new SettingsPageContribution(moduleId, createView);
        changed();
    }

    public void Clear()
    {
        lock (_gate)
        {
            _tiles.Clear();
            _settingsPage = null;
        }
        changed();
    }
}
