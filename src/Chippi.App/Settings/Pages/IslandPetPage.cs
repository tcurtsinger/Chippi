// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.App.Island;
using Chippi.App.Pet;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.UI.Controls;
using Chippi.UI.Themes;

namespace Chippi.App.Settings.Pages;

internal sealed class IslandPetPage : UserControl
{
    private readonly ShellContext _shell;

    public IslandPetPage(ShellContext shell)
    {
        _shell = shell;
        var app = SettingsUi.ReadApp(shell.Config);
        var page = new StackPanel();
        page.Children.Add(SettingsUi.Header("Island & pet", ""));
        var island = SettingsUi.Switch(app.IslandEnabled);
        SettingsUi.OnSwitched(island, on => SettingsUi.SaveApp(shell.Config, s => s.IslandEnabled = on));
        page.Children.Add(SettingsUi.Group(
            SettingsUi.Row("Island", "", island),
            PositionRow(app), DisplayRow(app)));
        page.Children.Add(SettingsUi.Section("Gumdrop"));
        page.Children.Add(SettingsUi.Group(ColorRow(app), MotionRow(app)));
        Content = page;
    }

    private Border PositionRow(AppSettings app)
    {
        var picker = Combo("Position", new[] { ("Top", IslandEdge.Top), ("Left edge", IslandEdge.Left), ("Right edge", IslandEdge.Right) }, app.IslandEdge);
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is ComboBoxItem { Tag: IslandEdge edge })
                SettingsUi.SaveApp(_shell.Config, s => s.IslandEdge = edge);
        };
        var reset = SettingsUi.Button("Reset");
        reset.Click += (_, _) =>
        {
            picker.SelectedIndex = 0;
            SettingsUi.SaveApp(_shell.Config, s => { s.IslandEdge = IslandEdge.Top; s.IslandPlace = 0.5; });
        };
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(picker);
        controls.Children.Add(reset);
        return SettingsUi.Row("Position", "", controls);
    }

    private Border DisplayRow(AppSettings app)
    {
        var mode = Combo("Island display", new[] { ("Follow active window", IslandDisplay.FollowActiveWindow), ("Main display", IslandDisplay.MainDisplay), ("One monitor", IslandDisplay.SpecificMonitor) }, app.IslandDisplay);
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

    private SettingRow ColorRow(AppSettings app)
    {
        var preview = new Gumdrop { Size = 48, PetColor = app.PetColor, Margin = new Thickness(0, 0, 20, 0) };
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
