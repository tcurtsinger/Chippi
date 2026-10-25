// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.App.Flyout;

/// <summary>
/// One flyout tab: a module's own panel, or the sections several modules add to a shared tab (the
/// Sound tab), in section order. Named and drawn by its first contribution in sidebar order.
/// </summary>
internal sealed record FlyoutTab(string Key, string Name, string Icon, IReadOnlyList<FlyoutPanelContribution> Parts)
{
    /// <summary>The tabs for <paramref name="panels"/> (in sidebar order), each where its first contribution sits.</summary>
    public static IReadOnlyList<FlyoutTab> Group(IEnumerable<FlyoutPanelContribution> panels) =>
        [.. panels
            .GroupBy(FlyoutPages.Key, StringComparer.Ordinal)
            .Select(g => new FlyoutTab(g.Key, g.First().Name, g.First().Icon, [.. g.OrderBy(p => p.Order)]))];
}

/// <summary>
/// Builds the flyout's pages: the tab each running module contributes, and the empty state.
/// Panels are created once and reused across rebuilds, so their state and focus survive a
/// registry change.
/// </summary>
internal sealed class FlyoutPages(FlyoutWindow owner)
{
    private readonly Dictionary<FlyoutPanelContribution, FrameworkElement> _views = [];
    private readonly List<Panel> _attached = [];

    /// <summary>Identifies a tab across module restarts, which create a new contribution.</summary>
    public static string Key(FlyoutPanelContribution panel) => panel.TabKey is { } tab ? $"tab:{tab}" : $"{panel.ModuleId}/{panel.Name}";

    public FrameworkElement Build(FlyoutTab tab, IReadOnlyList<FeatureEntry> running)
    {
        Detach();
        Prune(running);
        var page = new StackPanel();
        _attached.Add(page);
        foreach (var part in tab.Parts)
        {
            if (View(part) is not { } view)
                continue;
            view.Margin = page.Children.Count == 0 ? default : new Thickness(0, 12, 0, 0);
            page.Children.Add(view);
        }
        return page;
    }

    /// <summary>No module contributes a panel.</summary>
    public FrameworkElement BuildNothingInstalled()
    {
        Detach();
        var state = EmptyState(LucideIcons.LayoutGrid, "No features on");
        var add = new Button { Content = "Add features", Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        add.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButtonStyle");
        add.Click += (_, _) => owner.OpenSettings("features");
        state.Children.Add(add);
        return state;
    }

    /// <summary>The More tab: a row per tab that didn't fit in the tab strip.</summary>
    public FrameworkElement BuildMore(IReadOnlyList<FlyoutTab> tabs, Action<FlyoutTab> open)
    {
        Detach();
        var card = new Card();
        card.SetResourceReference(FrameworkElement.StyleProperty, "FlyoutCardStyle");
        foreach (var panel in tabs)
        {
            var glyph = new LucideIcon { Data = LucideIcons.Get(panel.Icon), Size = 16, Margin = new Thickness(0, 0, 12, 0) };
            glyph.SetResourceReference(LucideIcon.BrushProperty, "TextSecondaryBrush");
            var chevron = new LucideIcon { Data = LucideIcons.ChevronRight, Size = 16 };
            chevron.SetResourceReference(LucideIcon.BrushProperty, "TextSecondaryBrush");
            var row = new DockPanel();
            DockPanel.SetDock(glyph, Dock.Left);
            DockPanel.SetDock(chevron, Dock.Right);
            row.Children.Add(glyph);
            row.Children.Add(chevron);
            row.Children.Add(new TextBlock { Text = panel.Name, VerticalAlignment = VerticalAlignment.Center });

            var button = new Button { Content = row };
            button.SetResourceReference(FrameworkElement.StyleProperty, "FlyoutRowButtonStyle");
            AutomationProperties.SetName(button, panel.Name);
            button.Click += (_, _) => open(panel);
            card.Children.Add(button);
        }
        return card;
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
