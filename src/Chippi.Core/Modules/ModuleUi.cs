// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Chippi.Core.Modules;

// Why descriptors plus "object" view factories:
// Core must not reference WPF, so it cannot hand out FrameworkElements. Tiles are simple and
// uniform (icon, name, state line, on/off), so the shell renders them itself from a plain
// INotifyPropertyChanged model; that keeps every tile pixel-identical to DESIGN.md §6 and lets
// a module stay UI-free if a tile is all it needs. Tabs and settings pages are free-form, so the
// module supplies a factory returning `object`, which the App casts to a FrameworkElement. A
// module that contributes one of those references WPF in its own project; Core still does not.

/// <summary>The four flyout tabs, in display order.</summary>
public enum FlyoutTab
{
    Sound,
    System,
    Tidy,
    Tools,
}

/// <summary>UI a running module contributes. Everything registered here is removed when the module stops.</summary>
public interface IModuleUi
{
    /// <summary>Adds a tile to the flyout's Tools grid.</summary>
    void AddToolTile(ToolTileModel tile);

    /// <summary>Supplies the content of a flyout tab. The tab appears only while some running module contributes it.</summary>
    void AddFlyoutTab(FlyoutTab tab, Func<object> createView);

    /// <summary>Supplies the module's page in Settings. <paramref name="createView"/> must return a WPF FrameworkElement.</summary>
    void SetSettingsPage(Func<object> createView) => SetSettingsPage(_ => createView());

    /// <summary>
    /// A settings page with sections the shell can open directly: <c>OpenSettings("module:&lt;id&gt;/history")</c>
    /// passes "history"; the sidebar and the hub pass null (the page's first section).
    /// </summary>
    void SetSettingsPage(Func<string?, object> createView);
}

public sealed record FlyoutTabContribution(string ModuleId, FlyoutTab Tab, Func<object> CreateView);

public sealed record SettingsPageContribution(string ModuleId, Func<string?, object> CreateSectionView)
{
    /// <summary>The page, opened on <paramref name="section"/> when the module knows it.</summary>
    public object CreateView(string? section = null) => CreateSectionView(section);
}

/// <summary>
/// A flyout Tools tile, rendered by the shell (DESIGN.md §6 "Tool tile"). The module owns the
/// state; the shell binds to it and calls <see cref="ToggleAsync"/> on click.
/// </summary>
/// <remarks>Property changes may be raised from any thread; WPF marshals scalar bindings.</remarks>
public sealed class ToolTileModel(string name, string icon, Func<CancellationToken, Task> toggle) : INotifyPropertyChanged
{
    private bool _isOn;
    private string? _stateLine;

    public string Name { get; } = name;

    /// <summary>Lucide icon key, as in <see cref="FeatureDescriptor.Icon"/>.</summary>
    public string Icon { get; } = icon;

    public bool IsOn
    {
        get => _isOn;
        set => Set(ref _isOn, value);
    }

    /// <summary>The line under the name: state or shortcut, e.g. "Until 6:00 PM".</summary>
    public string? StateLine
    {
        get => _stateLine;
        set => Set(ref _stateLine, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Task ToggleAsync(CancellationToken ct = default) => toggle(ct);

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>Collects one module's contributions for the lifetime of a start.</summary>
internal sealed class ModuleUi(string moduleId, Action changed) : IModuleUi
{
    private readonly Lock _gate = new();
    private readonly List<ToolTileModel> _tiles = [];
    private readonly List<FlyoutTabContribution> _tabs = [];
    private SettingsPageContribution? _settingsPage;

    public IReadOnlyList<ToolTileModel> ToolTiles
    {
        get { lock (_gate) return [.. _tiles]; }
    }

    public IReadOnlyList<FlyoutTabContribution> Tabs
    {
        get { lock (_gate) return [.. _tabs]; }
    }

    public SettingsPageContribution? SettingsPage
    {
        get { lock (_gate) return _settingsPage; }
    }

    public void AddToolTile(ToolTileModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        lock (_gate) _tiles.Add(tile);
        changed();
    }

    public void AddFlyoutTab(FlyoutTab tab, Func<object> createView)
    {
        ArgumentNullException.ThrowIfNull(createView);
        lock (_gate) _tabs.Add(new FlyoutTabContribution(moduleId, tab, createView));
        changed();
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
            _tabs.Clear();
            _settingsPage = null;
        }
        changed();
    }
}
