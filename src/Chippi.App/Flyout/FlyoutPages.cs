// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.App.Flyout;

/// <summary>
/// Builds the flyout's pages: the Tools grid from every running module's <see cref="ToolTileModel"/>s,
/// the views modules contribute to a tab, and the empty states. Tiles and contributed views are
/// created once and reused across rebuilds, so their state and focus survive a registry change.
/// </summary>
internal sealed class FlyoutPages(FlyoutWindow owner)
{
    private readonly Dictionary<ToolTileModel, ToolTile> _tiles = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FlyoutTabContribution, FrameworkElement> _views = [];
    private readonly List<Panel> _attached = [];

    public static string TabName(FlyoutTab tab) => tab.ToString();

    public static Geometry TabIcon(FlyoutTab tab) => tab switch
    {
        FlyoutTab.Sound => LucideIcons.Volume,
        FlyoutTab.System => LucideIcons.Cpu,
        FlyoutTab.Tidy => LucideIcons.Folder,
        _ => LucideIcons.Wrench,
    };

    public FrameworkElement Build(FlyoutTab tab, IReadOnlyList<FeatureEntry> running)
    {
        Detach();
        var page = new StackPanel();
        _attached.Add(page);

        var models = tab == FlyoutTab.Tools ? running.SelectMany(f => f.ToolTiles).ToList() : [];
        var contributions = running.SelectMany(f => f.FlyoutTabs).Where(c => c.Tab == tab).ToList();
        Prune(running);

        if (models.Count > 0)
            page.Children.Add(TileGrid(models));

        foreach (var contribution in contributions)
        {
            var view = View(contribution);
            if (view is null)
                continue;
            var slot = new StackPanel { Margin = new Thickness(0, page.Children.Count > 0 ? 12 : 0, 0, 0) };
            slot.Children.Add(view);
            _attached.Add(slot);
            page.Children.Add(slot);
        }

        if (page.Children.Count == 0)
            page.Children.Add(EmptyState(TabIcon(tab), "Nothing here yet", $"{TabName(tab)} features you install show up here."));
        return page;
    }

    /// <summary>No module contributes anything and debug mode is off.</summary>
    public FrameworkElement BuildNothingInstalled()
    {
        Detach();
        var state = EmptyState(LucideIcons.LayoutGrid, "No features on yet", "Pick the ones you want in Settings.");
        var browse = new Button { Content = "Browse features", Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        browse.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButtonStyle");
        browse.Click += (_, _) => owner.OpenSettings("features");
        ((Panel)state).Children.Add(browse);
        return state;
    }

    private UniformGrid TileGrid(IReadOnlyList<ToolTileModel> models)
    {
        // Three columns with 8px gutters: each tile carries 4px on every side, the grid takes it back.
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-4) };
        foreach (var model in models)
            grid.Children.Add(Tile(model));
        _attached.Add(grid);
        return grid;
    }

    private ToolTile Tile(ToolTileModel model)
    {
        if (_tiles.TryGetValue(model, out var existing))
            return existing;

        var tile = new ToolTile { DataContext = model, Margin = new Thickness(4), Icon = LucideIcons.Get(model.Icon) };
        tile.SetBinding(ToolTile.TitleProperty, nameof(ToolTileModel.Name));
        tile.SetBinding(ToolTile.StateLineProperty, nameof(ToolTileModel.StateLine));
        tile.SetBinding(AutomationProperties.HelpTextProperty, nameof(ToolTileModel.StateLine));
        tile.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(ToolTileModel.IsOn)) { Mode = BindingMode.OneWay });
        tile.Click += async (_, _) =>
        {
            // The module owns the state: undo the button's own flip and let the model's IsOn decide.
            tile.SetCurrentValue(ToggleButton.IsCheckedProperty, model.IsOn);
            try
            {
                await model.ToggleAsync();
            }
            catch (Exception ex)
            {
                Log.Error($"Flyout: toggling '{model.Name}' failed", ex);
            }
        };
        _tiles[model] = tile;
        return tile;
    }

    private FrameworkElement? View(FlyoutTabContribution contribution)
    {
        if (_views.TryGetValue(contribution, out var view))
            return view;
        try
        {
            if (contribution.CreateView() is FrameworkElement created)
            {
                _views[contribution] = created;
                return created;
            }
            Log.Warn($"Flyout: {contribution.ModuleId} returned a {contribution.Tab} view that is not a FrameworkElement");
        }
        catch (Exception ex)
        {
            Log.Error($"Flyout: {contribution.ModuleId} failed to build its {contribution.Tab} view", ex);
        }
        return null;
    }

    private Panel EmptyState(Geometry icon, string title, string caption)
    {
        var chip = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Center };
        chip.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
        var glyph = new LucideIcon { Data = icon, Size = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "TextTertiaryBrush");
        chip.Child = glyph;

        var heading = new TextBlock { Text = title, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        heading.SetResourceReference(FrameworkElement.StyleProperty, "TitleTextStyle");
        var line = new TextBlock { Text = caption, Margin = new Thickness(0, 3, 0, 0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        line.SetResourceReference(FrameworkElement.StyleProperty, "CaptionTextStyle");

        var panel = new StackPanel { Margin = new Thickness(0, 22, 0, 22), HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 280 };
        panel.Children.Add(chip);
        panel.Children.Add(heading);
        panel.Children.Add(line);
        return panel;
    }

    /// <summary>Takes reused tiles and views out of the previous page so the next one can parent them.</summary>
    private void Detach()
    {
        foreach (var panel in _attached)
            panel.Children.Clear();
        _attached.Clear();
    }

    /// <summary>Forgets tiles and views whose module stopped.</summary>
    private void Prune(IReadOnlyList<FeatureEntry> running)
    {
        var liveTiles = running.SelectMany(f => f.ToolTiles).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var model in _tiles.Keys.Where(m => !liveTiles.Contains(m)).ToList())
            _tiles.Remove(model);
        var liveViews = running.SelectMany(f => f.FlyoutTabs).ToHashSet();
        foreach (var contribution in _views.Keys.Where(c => !liveViews.Contains(c)).ToList())
            _views.Remove(contribution);
    }
}
