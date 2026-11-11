// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Chippi.App.Island;
using Chippi.UI.Pet;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Chippi.UI.Themes;

namespace Chippi.App.Settings.Pages;

/// <summary>
/// Settings › Island &amp; pet (DESIGN.md §5): the island and the tray icon, where the island opens and
/// sits, its tiles (order, show or hide, with a preview), then Chippi's color. Reduce motion is General's.
/// </summary>
internal sealed class IslandPetPage : UserControl
{
    private const double RowHeight = 48;

    private readonly ShellContext _shell;
    private readonly PeekSettingsSection _peek;
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

        _peek = new PeekSettingsSection(shell.Config, shell.Shortcuts,
            () => IslandTileContribution.Group(shell.Registry.Features.Where(f => f.IsRunning).SelectMany(f => f.IslandTiles).Concat(shell.Services.SharedIslandTiles())),
            () => shell.Commands.OpenSettings($"{ModuleContext.ShortcutsPage}/peek/Peek"));
        page.Children.Add(_peek);

        page.Children.Add(SettingsUi.Section("Tiles"));
        var tiles = new Grid { ColumnDefinitions = { new(), new() { Width = new GridLength(8) }, new() { Width = GridLength.Auto } } };
        var preview = new Card { Children = { _preview } };
        Grid.SetColumn(preview, 2);
        tiles.Children.Add(_tiles);
        tiles.Children.Add(preview);
        page.Children.Add(tiles);

        page.Children.Add(SettingsUi.Section("Chippi"));
        page.Children.Add(SettingsUi.Group(ColorRow(app)));
        Content = page;

        RefreshTiles();
        Loaded += (_, _) =>
        {
            _shell.Registry.Changed += OnRegistryChanged;
            RefreshTiles();
        };
        Unloaded += (_, _) => _shell.Registry.Changed -= OnRegistryChanged;
    }

    private void OnRegistryChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { RefreshTiles(); _peek.Refresh(); });

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

    private IReadOnlyList<IslandTool> AllTiles() => IslandLayout.From(SettingsUi.ReadApp(_shell.Config)).Tools(
        IslandTileContribution.Group(_shell.Registry.Features.Where(f => f.IsRunning).SelectMany(f => f.IslandTiles)));

    /// <summary>A row per tile in the island's order, with its grip and its switch; then the preview.</summary>
    private void RefreshTiles()
    {
        var all = AllTiles();
        var order = all.Select(t => t.Key).ToList();
        string keys = string.Join("|", all.Select(t => $"{t.Key}:{t.Visible}"));
        if (keys != _tileKeys)
        {
            _tileKeys = keys;
            _tiles.Children.Clear();
            for (int i = 0; i < order.Count; i++)
                _tiles.Children.Add(TileRow(all[i], i, order));
        }
        RefreshPreview();
    }

    private FrameworkElement TileRow(IslandTool tile, int index, IReadOnlyList<string> order)
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
        var glyph = new LucideIcon { Data = LucideIcons.Get(tile.Icon), Size = 16, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "TextSecondaryBrush");
        var show = SettingsUi.Switch(tile.Visible);
        AutomationProperties.SetName(show, $"Show {tile.Name}");
        SettingsUi.OnSwitched(show, on => Save(s => s.IslandHiddenTiles = IslandLayout.Switched(s.IslandHiddenTiles ?? [], tile.Key, on)));
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
    /// A small desktop with the open gallery above the bottom-right puck, showing the chosen order.
    /// </summary>
    private void RefreshPreview()
    {
        const double width = 260, height = 160, edge = 10, puck = 30, panelWidth = 176, inset = 4, gap = 2;
        var shown = AllTiles().Where(t => t.Visible).ToList();
        int rows = Math.Max(1, (shown.Count + IslandGrid.Columns - 1) / IslandGrid.Columns);
        const double header = 13, tile = 24, footer = 14;
        double panelHeight = header + rows * (tile + gap) + footer + 2 * inset;
        var canvas = new Canvas { Width = width, Height = height };

        var screen = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        screen.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        screen.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        canvas.Children.Add(screen);

        var panel = new Border { Width = panelWidth, Height = panelHeight, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        panel.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        var title = new TextBlock { Text = "Tools", FontSize = 7, FontWeight = FontWeights.SemiBold, Height = header };
        title.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextBrush");
        var grid = new UniformGrid { Columns = IslandGrid.Columns, Margin = new Thickness(0, 0, -gap, 0) };
        foreach (var t in shown)
        {
            var glyph = new LucideIcon { Data = LucideIcons.Get(t.Icon), Size = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextBrush");
            var cell = new Border { Height = tile, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, gap, gap), Child = glyph };
            cell.SetResourceReference(Border.BackgroundProperty, "IslandPanelBrush");
            grid.Children.Add(cell);
        }
        var music = new LucideIcon { Data = LucideIcons.Music, Size = 7, Margin = new Thickness(0, 0, 3, 0) };
        music.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        var label = new TextBlock { Text = "Now Playing", FontSize = 6, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextSecondaryBrush");
        var media = new StackPanel { Height = footer, Orientation = Orientation.Horizontal, Children = { music, label } };
        panel.Child = new StackPanel { Margin = new Thickness(inset), Children = { title, grid, media } };
        Canvas.SetLeft(panel, width - edge - panelWidth);
        Canvas.SetTop(panel, height - edge - puck / 2 - panelHeight);
        canvas.Children.Add(panel);

        // Chippi at his share of the real puck (40 of 50), so the preview draws the puck's face, not the small form's.
        var button = new Border { Width = puck, Height = puck, CornerRadius = new CornerRadius(puck / 2), BorderThickness = new Thickness(1), Child = new ChippiPet { Size = puck * PetSize.Puck / IslandGeometry.Puck, Mood = PetMood.Idle, IsDecorative = true, PetColor = SettingsUi.ReadApp(_shell.Config).PetColor, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        button.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        button.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        Canvas.SetLeft(button, width - edge - puck);
        Canvas.SetTop(button, height - edge - puck);
        canvas.Children.Add(button);

        _preview.Child = canvas;
    }

    // ----- ChippiPet -----

    /// <summary>The moods the preview steps through when clicked, so each can be seen in the color.</summary>
    internal static readonly PetMood[] PreviewMoods = [PetMood.Idle, PetMood.Sleepy, PetMood.Listening, PetMood.WideAwake, PetMood.Celebrating];

    /// <summary>
    /// Color: Chippi at his puck size beside the six swatches, in a 44 button so the row keeps a captioned row's 60.
    /// Picking a color, he celebrates in it; clicking him shows his next mood.
    /// </summary>
    private SettingRow ColorRow(AppSettings app)
    {
        var pet = new ChippiPet { Size = PetSize.Preview, PetColor = app.PetColor, IsDecorative = true };
        var preview = SettingsUi.Styled(new Button { Content = pet, Width = 44, Height = 44, Padding = new Thickness(0), Margin = new Thickness(0, 0, 16, 0), ToolTip = "Next mood" }, "SubtleButtonStyle");
        AutomationProperties.SetName(preview, "Next mood");
        int shown = 0;
        preview.Click += (_, _) =>
        {
            shown = (shown + 1) % PreviewMoods.Length;
            if (PreviewMoods[shown] == PetMood.Celebrating)
                pet.Celebrate();
            else
                pet.Mood = PreviewMoods[shown];
        };
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(preview);
        var row = new SettingRow { Header = "Color", Description = app.PetColor.ToString(), Content = controls };
        foreach (var color in Enum.GetValues<PetColor>())
        {
            var swatch = SettingsUi.Styled(new RadioButton { GroupName = "PetColor", IsChecked = color == app.PetColor, ToolTip = color.ToString(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) }, "SwatchStyle");
            AutomationProperties.SetName(swatch, color.ToString());
            swatch.Background = new SolidColorBrush(ChippiPet.ColorOf(color));
            if (color == PetColor.Frost)
                swatch.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            swatch.Checked += (_, _) =>
            {
                pet.PetColor = color;
                pet.Celebrate();
                row.Description = color.ToString();
                Save(s => s.PetColor = color);
            };
            controls.Children.Add(swatch);
        }
        return row;
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
