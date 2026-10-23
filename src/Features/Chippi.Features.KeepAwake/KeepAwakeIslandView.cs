// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Chippi.Core.Diagnostics;

namespace Chippi.Features.KeepAwake;

/// <summary>
/// Keep awake in the island (DESIGN.md §5 Island): its status and four chips (30 min · 1 hr · 2 hr ·
/// No limit) in one inner card. Like the flyout panel, the module owns the state.
/// </summary>
public sealed class KeepAwakeIslandView : UserControl
{
    private static readonly string[] Chosen = ["30 min", "1 hr", "2 hr", "No limit"];

    private readonly KeepAwakeModule _module;
    private readonly TextBlock _status = new() { Text = "Keep awake" };
    private readonly TextBlock _detail = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly List<(ToggleButton Chip, KeepAwakeSettings Length)> _chips = [];
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(15) };

    public KeepAwakeIslandView(KeepAwakeModule module)
    {
        _module = module;
        _detail.SetResourceReference(StyleProperty, "CaptionTextStyle");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_detail, System.Windows.Automation.AutomationLiveSetting.Polite);

        // Four columns with 8px gaps: each chip carries 8px on its right, the grid takes it back.
        var chips = new UniformGrid { Columns = Chosen.Length, Margin = new Thickness(0, 10, -8, 0) };
        foreach (var (label, length) in KeepAwakeText.Lengths.Where(l => Chosen.Contains(l.Label)))
        {
            var chip = new ToggleButton { Content = label, Margin = new Thickness(0, 0, 8, 0) };
            chip.SetResourceReference(StyleProperty, "DurationChipStyle");
            KeepAwakePanel.OnFlip(chip, () => OnChip(chip, length));
            _chips.Add((chip, length));
            chips.Children.Add(chip);
        }

        // The name on the left and the status on the right, the chips under them.
        _detail.VerticalAlignment = VerticalAlignment.Center;
        _detail.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(_detail, 1);
        var heading = new Grid { ColumnDefinitions = { new(), new() { Width = GridLength.Auto } }, Children = { _status, _detail } };
        var card = new Border { Child = new StackPanel { Children = { heading, chips } } };
        card.SetResourceReference(StyleProperty, "IslandCardStyle");
        Content = card;
        System.Windows.Automation.AutomationProperties.SetName(this, "Keep awake");

        _tick.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { _module.Changed += Refresh; _tick.Start(); Refresh(); };
        Unloaded += (_, _) => { _module.Changed -= Refresh; _tick.Stop(); };
        Refresh();
    }

    private bool IsSelected(KeepAwakeSettings length) => _module.Active is { } active && KeepAwakeText.SameLength(active, length);

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _detail.Text = _module.Error ?? KeepAwakeText.Status(_module, DateTimeOffset.Now);
        foreach (var (chip, length) in _chips)
            chip.IsChecked = IsSelected(length);
    }

    /// <summary>A chip starts its length; the selected chip stops. Compared with the module, so Refresh writing state back does nothing.</summary>
    private void OnChip(ToggleButton chip, KeepAwakeSettings length)
    {
        bool selected = IsSelected(length);
        if (chip.IsChecked == selected)
            return;
        Refresh(); // the module's state shows until the change lands
        Run(selected ? _module.StopSessionAsync() : _module.StartSessionAsync(length));
    }

    private async void Run(Task change)
    {
        try { await change; }
        catch (Exception ex)
        {
            Log.Error("Keep awake: a change from the island failed", ex);
            Refresh();
            _detail.Text = ex.Message;
            return;
        }
        Refresh();
    }
}
