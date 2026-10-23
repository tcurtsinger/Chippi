// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.App.Flyout;

/// <summary>
/// Builds the flyout's pages: the panel each running module contributes, and the empty state.
/// Panels are created once and reused across rebuilds, so their state and focus survive a
/// registry change.
/// </summary>
internal sealed class FlyoutPages(FlyoutWindow owner)
{
    private readonly Dictionary<FlyoutPanelContribution, FrameworkElement> _views = [];
    private readonly List<Panel> _attached = [];

    /// <summary>Identifies a panel across module restarts, which create a new contribution.</summary>
    public static string Key(FlyoutPanelContribution panel) => $"{panel.ModuleId}/{panel.Name}";

    public FrameworkElement Build(FlyoutPanelContribution panel, IReadOnlyList<FeatureEntry> running)
    {
        Detach();
        Prune(running);
        var page = new StackPanel();
        _attached.Add(page);
        if (View(panel) is { } view)
            page.Children.Add(view);
        return page;
    }

    /// <summary>No module contributes a panel.</summary>
    public FrameworkElement BuildNothingInstalled()
    {
        Detach();
        var state = EmptyState(LucideIcons.LayoutGrid, "No features on yet");
        var add = new Button { Content = "Add features", Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        add.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButtonStyle");
        add.Click += (_, _) => owner.OpenSettings("features");
        state.Children.Add(add);
        return state;
    }

    private FrameworkElement? View(FlyoutPanelContribution panel)
    {
        if (_views.TryGetValue(panel, out var view))
            return view;
        try
        {
            if (panel.CreateView() is FrameworkElement created)
            {
                _views[panel] = created;
                return created;
            }
            Log.Warn($"Flyout: {panel.ModuleId} returned a {panel.Name} panel that is not a FrameworkElement");
        }
        catch (Exception ex)
        {
            Log.Error($"Flyout: {panel.ModuleId} failed to build its {panel.Name} panel", ex);
        }
        return null;
    }

    private static StackPanel EmptyState(Geometry icon, string title)
    {
        var glyph = new LucideIcon { Data = icon, Size = 24, HorizontalAlignment = HorizontalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "TextTertiaryBrush");
        var heading = new TextBlock { Text = title, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        heading.SetResourceReference(FrameworkElement.StyleProperty, "BodyTextStyle");

        var panel = new StackPanel { Margin = new Thickness(0, 24, 0, 24), HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(glyph);
        panel.Children.Add(heading);
        return panel;
    }

    /// <summary>Takes reused views out of the previous page so the next one can parent them.</summary>
    private void Detach()
    {
        foreach (var panel in _attached)
            panel.Children.Clear();
        _attached.Clear();
    }

    /// <summary>Forgets views whose module stopped.</summary>
    private void Prune(IReadOnlyList<FeatureEntry> running)
    {
        var live = running.SelectMany(f => f.FlyoutPanels).ToHashSet();
        foreach (var panel in _views.Keys.Where(p => !live.Contains(p)).ToList())
            _views.Remove(panel);
    }
}
