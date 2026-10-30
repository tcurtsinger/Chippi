// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Chippi.App.Island;
using Chippi.App.Pet;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Chippi.UI.Themes;

namespace Chippi.App.Settings.Pages;

/// <summary>
/// Settings › Island &amp; pet (DESIGN.md §5): the island and the tray icon, where the island opens and
/// sits, its tiles (order, show or hide, with a preview), then Chippi.
/// </summary>
internal sealed class IslandPetPage : UserControl
{
    private const double RowHeight = 48;

    private readonly ShellContext _shell;
    private readonly Card _tiles = new();
    private readonly DecorativeHost _preview = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16) };
    private string _tileKeys = "";

    public IslandPetPage(ShellContext shell)
    {
        _shell = shell;
        var app = SettingsUi.ReadApp(shell.Config);
        var page = new StackPanel();
        page.Children.Add(SettingsUi.Header("Island & pet", ""));
        page.Children.Add(SettingsUi.Group([.. DoorRows(app), OpensOnRow(app), PositionRow(), DisplayRow(app)]));

        page.Children.Add(SettingsUi.Section("Tiles"));
        var tiles = new Grid { ColumnDefinitions = { new(), new() { Width = new GridLength(8) }, new() { Width = GridLength.Auto } } };
        var preview = new Card { Children = { _preview } };
        Grid.SetColumn(preview, 2);
        tiles.Children.Add(_tiles);
        tiles.Children.Add(preview);
        page.Children.Add(tiles);

        page.Children.Add(SettingsUi.Section("Chippi"));
        page.Children.Add(SettingsUi.Group(ColorRow(app), MotionRow(app)));
        Content = page;

        RefreshTiles();
        Loaded += (_, _) =>
        {
            _shell.Registry.Changed += OnRegistryChanged;
            RefreshTiles();
        };
        Unloaded += (_, _) => _shell.Registry.Changed -= OnRegistryChanged;
    }

    private void OnRegistryChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshTiles);

    private void Save(Action<AppSettings> change)
    {
        SettingsUi.SaveApp(_shell.Config, change);
        RefreshPreview();
    }

    // ----- The island and the tray icon -----

    /// <summary>
    /// Island, and Hide tray icon, which only applies while the island is on: the island is then
    /// the only way in.
    /// </summary>
    private Border[] DoorRows(AppSettings app)
    {
        var island = SettingsUi.Switch(app.IslandEnabled);
        AutomationProperties.SetName(island, "Island");
        var hide = SettingsUi.Switch(app.HideTrayIcon);
        AutomationProperties.SetName(hide, "Hide tray icon");
        hide.IsEnabled = island.IsChecked == true;
        SettingsUi.OnSwitched(island, on =>
        {
            Save(s => s.IslandEnabled = on);
            hide.IsEnabled = on;
        });
        SettingsUi.OnSwitched(hide, on => Save(s => s.HideTrayIcon = on));
        return [SettingsUi.Row("Island", "", island), SettingsUi.Row("Hide tray icon", "", hide)];
    }

    private Border OpensOnRow(AppSettings app)
    {
        var picker = Combo("Opens on", new[] { ("Last page used", IslandOpensOn.LastPage), ("All features", IslandOpensOn.AllFeatures) }, app.IslandOpensOn);
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is ComboBoxItem { Tag: IslandOpensOn choice })
                Save(s => s.IslandOpensOn = choice);
        };
        return SettingsUi.Row("Opens on", "", picker);
    }

    // Dragging the widget moves it; Reset puts it back where it starts, 24 in from the bottom-right corner.
    private Border PositionRow()
    {
        var reset = SettingsUi.Button("Reset");
        reset.Click += (_, _) => SettingsUi.SaveApp(_shell.Config, s => { s.IslandX = null; s.IslandY = null; });
        return SettingsUi.Row("Position", "", reset);
    }

    // Follow active window is retired (2026-10-29): a widget you place yourself stays put. A saved one shows as Main display.
    private Border DisplayRow(AppSettings app)
    {
        var shown = app.IslandDisplay == IslandDisplay.FollowActiveWindow ? IslandDisplay.MainDisplay : app.IslandDisplay;
        var mode = Combo("Island display", new[] { ("Main display", IslandDisplay.MainDisplay), ("One monitor", IslandDisplay.SpecificMonitor) }, shown);
        var picker = SettingsUi.Styled(new ComboBox { MinWidth = 180, MaxWidth = 300, Tag = "Choose a monitor", Margin = new Thickness(0, 8, 0, 0) }, "HopperComboBoxStyle");
        AutomationProperties.SetName(picker, "Island monitor");
        foreach (var monitor in Displays.Connected())
            picker.Items.Add(new ComboBoxItem { Content = monitor.Name, Tag = monitor.Id });
        picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, app.IslandMonitorId, StringComparison.OrdinalIgnoreCase));
        var note = SettingsUi.Message();
        void Update()
        {
            bool specific = mode.SelectedItem is ComboBoxItem { Tag: IslandDisplay.SpecificMonitor };
            picker.Visibility = specific ? Visibility.Visible : Visibility.Collapsed;
            if (specific && picker.SelectedItem is null)
                SettingsUi.Show(note, "Monitor not connected · using main display", false);
            else
                note.Visibility = Visibility.Collapsed;
        }
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedItem is ComboBoxItem { Tag: IslandDisplay value })
            {
                SettingsUi.SaveApp(_shell.Config, s => s.IslandDisplay = value);
                Update();
            }
        };
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is ComboBoxItem { Tag: string id })
            {
                SettingsUi.SaveApp(_shell.Config, s => s.IslandMonitorId = id);
                Update();
            }
        };
        Update();
        var controls = new StackPanel();
        controls.Children.Add(mode);
        controls.Children.Add(picker);
        return SettingsUi.Row("Display", "", controls, note);
    }

    // ----- Tiles -----

    private sealed record Tile(string Key, string Name, Geometry Icon);

    /// <summary>The running features' home buttons, in their own order. Now Playing is a fixed card.</summary>
    private List<Tile> AllTiles() =>
    [
        .. IslandTileContribution.Group(_shell.Registry.Features.Where(f => f.IsRunning).SelectMany(f => f.IslandTiles))
            .Where(g => g.Key != IslandPanelWindow.NowPlayingKey)
            .Select(g => new Tile(g.Key, g.Name, LucideIcons.Get(g.Icon))),
    ];

    private IslandLayout Layout() => IslandLayout.From(SettingsUi.ReadApp(_shell.Config));

    /// <summary>A row per tile in the island's order, with its grip and its switch; then the preview.</summary>
    private void RefreshTiles()
    {
        var all = AllTiles();
        var layout = Layout();
        var order = layout.Sorted(all.Select(t => t.Key));
        string keys = string.Join("|", order) + "/" + string.Join("|", layout.Hidden);
        if (keys != _tileKeys)
        {
            _tileKeys = keys;
            _tiles.Children.Clear();
            for (int i = 0; i < order.Count; i++)
                _tiles.Children.Add(TileRow(all.First(t => t.Key == order[i]), i, order, !layout.Hidden.Contains(order[i])));
        }
        RefreshPreview();
    }

    private FrameworkElement TileRow(Tile tile, int index, IReadOnlyList<string> order, bool shown)
    {
        var handle = new DragHandle { Width = 16, Height = 24, Margin = new Thickness(0, 0, 16, 0), ToolTip = "Reorder" };
        AutomationProperties.SetName(handle, $"Reorder {tile.Name}");
        handle.MoveRequested += (_, delta) => Move(order, index, index + delta, tile.Key);
        double distance = 0;
        int target = index;
        handle.DragStarted += (_, _) => { distance = 0; target = index; };
        handle.DragDelta += (_, e) =>
        {
            distance += e.VerticalChange;
            target = Math.Clamp(index + (int)Math.Round(distance / RowHeight), 0, order.Count - 1);
            handle.ToolTip = $"Position {target + 1}";
        };
        handle.DragCompleted += (_, e) =>
        {
            if (!e.Canceled)
                Move(order, index, target, tile.Key);
        };
        var glyph = new LucideIcon { Data = tile.Icon, Size = 16, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "TextSecondaryBrush");
        var show = SettingsUi.Switch(shown);
        AutomationProperties.SetName(show, $"Show {tile.Name}");
        SettingsUi.OnSwitched(show, on => Save(s =>
        {
            var hidden = (s.IslandHiddenTiles ?? []).Where(k => k != tile.Key).ToList();
            if (!on)
                hidden.Add(tile.Key);
            s.IslandHiddenTiles = hidden;
        }));
        var name = new TextBlock { Text = tile.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        DockPanel.SetDock(handle, Dock.Left);
        DockPanel.SetDock(glyph, Dock.Left);
        DockPanel.SetDock(show, Dock.Right);
        return new Border { MinHeight = RowHeight, Padding = new Thickness(16, 0, 16, 0), Child = new DockPanel { Children = { handle, glyph, show, name } } };
    }

    /// <summary>Saves the order with the tile at <paramref name="from"/> moved to <paramref name="to"/>, keeping the grip's focus.</summary>
    private void Move(IReadOnlyList<string> order, int from, int to, string key)
    {
        if (to < 0 || to >= order.Count || to == from)
            return;
        var moved = order.ToList();
        moved.RemoveAt(from);
        moved.Insert(to, key);
        Save(s => s.IslandTileOrder = moved);
        RefreshTiles();
        if (_tiles.Children.OfType<Border>().ElementAtOrDefault(to)?.Child is DockPanel row)
            row.Children.OfType<DragHandle>().FirstOrDefault()?.Focus();
    }

    // ----- Preview -----

    /// <summary>
    /// A small desktop with the widget at its bottom right: the open panel over the puck, the shown tiles four across.
    /// </summary>
    private void RefreshPreview()
    {
        const double width = 260, height = 160, edge = 10, puck = 30, panelWidth = 176, inset = 6, gap = 4;
        var all = AllTiles();
        var layout = Layout();
        var shown = layout.Shown(all.Select(t => t.Key)).Select(k => all.First(t => t.Key == k)).Take(IslandGrid.MaxTiles).ToList();
        int rows = Math.Max(1, (shown.Count + IslandGrid.Columns - 1) / IslandGrid.Columns);
        double tile = 18;
        double panelHeight = 2 * inset + 22 + rows * tile + (rows - 1) * gap + 14;
        var canvas = new Canvas { Width = width, Height = height };

        var screen = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        screen.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        screen.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        canvas.Children.Add(screen);

        var panel = new Border { Width = panelWidth, Height = panelHeight, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        panel.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        var card = new Border { Height = 22, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, gap) };
        card.SetResourceReference(Border.BackgroundProperty, "IslandPanelBrush");
        var grid = new UniformGrid { Columns = IslandGrid.Columns, Margin = new Thickness(0, 0, -gap, 0) };
        foreach (var t in shown)
        {
            var glyph = new LucideIcon { Data = t.Icon, Size = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextBrush");
            var cell = new Border { Height = tile, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, gap, gap), Child = glyph };
            cell.SetResourceReference(Border.BackgroundProperty, "IslandPanelBrush");
            grid.Children.Add(cell);
        }
        panel.Child = new StackPanel { Margin = new Thickness(inset, inset, inset, 0), Children = { card, grid } };
        Canvas.SetLeft(panel, width - edge - panelWidth);
        Canvas.SetTop(panel, height - edge - puck / 2 - panelHeight);
        canvas.Children.Add(panel);

        var button = new Border { Width = puck, Height = puck, CornerRadius = new CornerRadius(puck / 2), BorderThickness = new Thickness(1), Child = new ChippiPet { Size = 22, Mood = PetMood.Idle, IsDecorative = true, PetColor = SettingsUi.ReadApp(_shell.Config).PetColor, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        button.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        button.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        Canvas.SetLeft(button, width - edge - puck);
        Canvas.SetTop(button, height - edge - puck);
        canvas.Children.Add(button);

        _preview.Child = canvas;
    }

    // ----- ChippiPet -----

    private SettingRow ColorRow(AppSettings app)
    {
        var preview = new ChippiPet { Size = 48, PetColor = app.PetColor, Margin = new Thickness(0, 0, 20, 0) };
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(preview);
        var row = new SettingRow { Header = "Color", Description = app.PetColor.ToString(), Content = controls };
        foreach (var color in Enum.GetValues<PetColor>())
        {
            var swatch = SettingsUi.Styled(new RadioButton { GroupName = "PetColor", IsChecked = color == app.PetColor, ToolTip = color.ToString(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) }, "SwatchStyle");
            AutomationProperties.SetName(swatch, color.ToString());
            swatch.SetResourceReference(Control.BackgroundProperty, color == PetColor.Synth ? "PetSynthBrush" : "Pet" + color + "Brush");
            if (color == PetColor.Frost)
                swatch.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            swatch.Checked += (_, _) =>
            {
                preview.PetColor = color;
                row.Description = color.ToString();
                SettingsUi.SaveApp(_shell.Config, s => s.PetColor = color);
            };
            controls.Children.Add(swatch);
        }
        return row;
    }

    private Border MotionRow(AppSettings app)
    {
        var motion = Combo("Reduce motion", new[] { ("Follow Windows", (bool?)null), ("On", (bool?)true), ("Off", (bool?)false) }, app.ReduceMotion);
        motion.SelectionChanged += (_, _) =>
        {
            if (motion.SelectedItem is ComboBoxItem item)
            {
                var choice = (bool?)item.Tag;
                Motion.Override = choice;
                SettingsUi.SaveApp(_shell.Config, s => s.ReduceMotion = choice);
            }
        };
        return SettingsUi.Row("Reduce motion", "", motion);
    }

    private static ComboBox Combo<T>(string name, IEnumerable<(string Label, T Value)> choices, T selected)
    {
        var combo = SettingsUi.Styled(new ComboBox { MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Left }, "HopperComboBoxStyle");
        foreach (var (label, value) in choices)
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, selected));
        AutomationProperties.SetName(combo, name);
        return combo;
    }
}
