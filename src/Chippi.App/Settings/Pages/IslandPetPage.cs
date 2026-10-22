// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Chippi.App.Island;
using Chippi.App.Pet;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.UI.Controls;
using Chippi.UI.Themes;

namespace Chippi.App.Settings.Pages;

/// <summary>Island & pet (brief §4.5): island on/off, pet color, reduce motion, and a 112px Gumdrop cycling its moods.</summary>
internal sealed class IslandPetPage : UserControl
{
    private static readonly PetMood[] Cycle = [PetMood.Idle, PetMood.Celebrating, PetMood.Sleepy, PetMood.WideAwake];

    private readonly ShellContext _shell;
    private readonly Gumdrop _preview;
    private readonly TextBlock _moodLabel;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private int _mood;

    public IslandPetPage(ShellContext shell)
    {
        _shell = shell;
        var app = SettingsUi.ReadApp(shell.Config);
        _preview = new Gumdrop { Size = 112, PetColor = app.PetColor, HorizontalAlignment = HorizontalAlignment.Center };

        var page = new StackPanel();
        page.Children.Add(SettingsUi.Header("Island & pet", "The island floats at the top of your screen. Gumdrop lives in it and reacts when Chippi finishes something for you."));

        var island = SettingsUi.Switch(app.IslandEnabled);
        SettingsUi.OnSwitched(island, on => SettingsUi.SaveApp(shell.Config, s => s.IslandEnabled = on));
        page.Children.Add(SettingsUi.Row("Island", "Shows what is live (media, timers, Keep awake) and hides over fullscreen apps.", island));
        page.Children.Add(DisplayRow(app));

        var color = SettingsUi.Segments("Pet color", Enum.GetValues<PetColor>().Select(c => (c.ToString(), c)), app.PetColor);
        color.Width = 480;
        color.SelectionChanged += (_, _) =>
        {
            if (color.SelectedItem is SegmentItem { Value: PetColor choice })
            {
                _preview.PetColor = choice;
                SettingsUi.SaveApp(shell.Config, s => s.PetColor = choice);
            }
        };
        page.Children.Add(SettingsUi.Row("Pet color", "Synth is Chippi's gradient. The face stays dark on every color.", color));

        var motion = SettingsUi.Segments("Reduce motion",
            [("Follow Windows", (bool?)null), ("On", (bool?)true), ("Off", (bool?)false)], app.ReduceMotion);
        motion.Width = 440;
        motion.SelectionChanged += (_, _) =>
        {
            if (motion.SelectedItem is SegmentItem item)
            {
                var choice = (bool?)item.Value;
                Motion.Override = choice;
                SettingsUi.SaveApp(shell.Config, s => s.ReduceMotion = choice);
            }
        };
        page.Children.Add(SettingsUi.Row("Reduce motion",
            "On stops Gumdrop's blink and squish and swaps moods instantly. Follow Windows uses Animation effects.", motion));

        // On Panel, not the island's near-black: at 112px DESIGN.md §7 draws a ground shadow (black
        // at 35%), and it only shows on a lighter surface. The caption is the themed tertiary ink.
        _moodLabel = SettingsUi.Styled(new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) }, "CaptionTextStyle");
        var stage = new StackPanel { Margin = new Thickness(0, 22, 0, 18) };
        stage.Children.Add(_preview);
        stage.Children.Add(_moodLabel);
        var card = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Child = stage, Margin = new Thickness(0, 4, 0, 0) };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        page.Children.Add(SettingsUi.Eyebrow("Preview"));
        page.Children.Add(card);

        Content = page;
        ShowMood();
        _timer.Tick += (_, _) =>
        {
            _mood = (_mood + 1) % Cycle.Length;
            ShowMood();
        };
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary>
    /// Island display: follow the active window, stay on the main display, or stay on one monitor
    /// picked by name. Saved to AppSettings; the island applies it live through SectionChanged.
    /// </summary>
    private Border DisplayRow(AppSettings app)
    {
        var monitors = Displays.Connected();
        var mode = SettingsUi.Segments("Island display",
            [("Follow active window", IslandDisplay.FollowActiveWindow), ("Main display", IslandDisplay.MainDisplay), ("One monitor", IslandDisplay.SpecificMonitor)],
            app.IslandDisplay);
        mode.Width = 480;

        var picker = new ComboBox { Width = 320, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), Tag = "Choose a monitor" };
        picker.SetResourceReference(StyleProperty, "HopperComboBoxStyle");
        AutomationProperties.SetName(picker, "Island monitor");
        foreach (var monitor in monitors)
            picker.Items.Add(new ComboBoxItem { Content = monitor.Name, Tag = monitor.Id });
        picker.SelectedItem = picker.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals((string)i.Tag, app.IslandMonitorId, StringComparison.OrdinalIgnoreCase));

        var note = SettingsUi.Message();
        void UpdatePicker()
        {
            bool one = mode.SelectedItem is SegmentItem { Value: IslandDisplay.SpecificMonitor };
            picker.Visibility = one ? Visibility.Visible : Visibility.Collapsed;
            string? saved = SettingsUi.ReadApp(_shell.Config).IslandMonitorId;
            if (one && !string.IsNullOrEmpty(saved) && picker.SelectedItem is null)
                SettingsUi.Show(note, "That monitor isn't connected, so the island is on the main display until it is.", problem: true);
            else if (one && string.IsNullOrEmpty(saved))
                SettingsUi.Show(note, "Choose a monitor. Until then the island stays on the main display.", problem: false);
            else
                note.Visibility = Visibility.Collapsed;
        }

        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedItem is SegmentItem { Value: IslandDisplay choice })
            {
                SettingsUi.SaveApp(_shell.Config, s => s.IslandDisplay = choice);
                UpdatePicker();
            }
        };
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is ComboBoxItem { Tag: string id })
            {
                SettingsUi.SaveApp(_shell.Config, s => s.IslandMonitorId = id);
                UpdatePicker();
            }
        };
        UpdatePicker();

        var below = new StackPanel();
        below.Children.Add(picker);
        below.Children.Add(note);
        return SettingsUi.Row("Island display", "Where the island sits. Following the active window moves it to whichever screen you're working on.", mode, below);
    }

    private void ShowMood()
    {
        var mood = Cycle[_mood];
        _preview.Mood = mood;
        string words = Gumdrop.Describe(mood);
        _moodLabel.Text = char.ToUpperInvariant(words[0]) + words[1..];
    }
}
