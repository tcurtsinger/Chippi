// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Modules;

// Why "object" view factories:
// Core must not reference WPF, so it cannot hand out FrameworkElements. Flyout panels, island
// views and settings pages are free-form, so the module supplies a factory returning `object`,
// which the App casts to a FrameworkElement. A module that contributes one references WPF in its
// own project; Core still does not.

/// <summary>UI a running module contributes. Everything registered here is removed when the module stops.</summary>
public interface IModuleUi
{
    /// <summary>
    /// Adds a flyout panel: a tab named <paramref name="name"/> with the Lucide <paramref name="icon"/>,
    /// shown in sidebar order. <paramref name="createIslandView"/> is the compact version the island
    /// shows; a feature without one doesn't appear there. Both must return a WPF FrameworkElement.
    /// </summary>
    void AddFlyoutPanel(string name, string icon, Func<object> createView, Func<object>? createIslandView = null);

    /// <summary>Supplies the module's page in Settings. <paramref name="createView"/> must return a WPF FrameworkElement.</summary>
    void SetSettingsPage(Func<object> createView) => SetSettingsPage(_ => createView());

    /// <summary>
    /// A settings page with sections the shell can open directly: <c>OpenSettings("module:&lt;id&gt;/history")</c>
    /// passes "history"; the sidebar and the hub pass null (the page's first section).
    /// </summary>
    void SetSettingsPage(Func<string?, object> createView);
}

/// <param name="Icon">Lucide icon key, as in <see cref="FeatureDescriptor.Icon"/>.</param>
/// <param name="CreateIslandView">The island's compact view, or null to stay out of the island.</param>
public sealed record FlyoutPanelContribution(string ModuleId, string Name, string Icon, Func<object> CreateView, Func<object>? CreateIslandView);

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
    private readonly List<FlyoutPanelContribution> _panels = [];
    private SettingsPageContribution? _settingsPage;

    public IReadOnlyList<FlyoutPanelContribution> Panels
    {
        get { lock (_gate) return [.. _panels]; }
    }

    public SettingsPageContribution? SettingsPage
    {
        get { lock (_gate) return _settingsPage; }
    }

    public void AddFlyoutPanel(string name, string icon, Func<object> createView, Func<object>? createIslandView = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(icon);
        ArgumentNullException.ThrowIfNull(createView);
        lock (_gate) _panels.Add(new FlyoutPanelContribution(moduleId, name, icon, createView, createIslandView));
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
            _panels.Clear();
            _settingsPage = null;
        }
        changed();
    }
}
