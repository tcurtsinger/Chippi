// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Chippi.Core.Diagnostics;

namespace Chippi.Features.KeepAwake;

/// <summary>
/// Keep awake's island page (DESIGN.md §5 Island): six chips, three across (30 min · 1 hr · 2 hr ·
/// 4 hr · No limit · Until…). A chip starts its length and the lit one stops it; Until… asks for the
/// end time first. The page's header shows the status. The module owns the state.
/// </summary>
public sealed class KeepAwakeIslandView : UserControl
{
    private const string UntilLabel = "Until…";

    private readonly KeepAwakeModule _module;
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    private readonly List<(ToggleButton Chip, KeepAwakeSettings? Length, string Label)> _chips = [];
    private readonly ComboBox _until = new() { Width = 140 };
    private readonly Popup _untilPopup = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true, PopupAnimation = PopupAnimation.None };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(15) };

    public KeepAwakeIslandView(KeepAwakeModule module)
    {
        _module = module;
        Resources["ChoiceStyle"] = System.Windows.Markup.XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ToggleButton">
             <Setter Property="Foreground" Value="{DynamicResource IslandTextBrush}"/>
             <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ToggleButton">
              <Border Name="Card" CornerRadius="8" Background="{DynamicResource IslandPanelBrush}"><Grid><Border Name="Hover" CornerRadius="8" Background="White" Opacity="0" IsHitTestVisible="False"/><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Grid></Border>
              <ControlTemplate.Triggers>
               <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Card" Property="Background" Value="{DynamicResource IslandHoverBrush}"/></Trigger>
               <Trigger Property="IsChecked" Value="True"><Setter TargetName="Card" Property="Background" Value="{DynamicResource IslandAccentFillBrush}"/><Setter Property="Foreground" Value="{DynamicResource IslandOnAccentBrush}"/></Trigger>
               <MultiTrigger><MultiTrigger.Conditions><Condition Property="IsChecked" Value="True"/><Condition Property="IsMouseOver" Value="True"/></MultiTrigger.Conditions><Setter TargetName="Hover" Property="Opacity" Value="0.12"/></MultiTrigger>
              </ControlTemplate.Triggers>
             </ControlTemplate></Setter.Value></Setter>
            </Style>
            """);
        _error.SetResourceReference(StyleProperty, "CaptionTextStyle");
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_error, System.Windows.Automation.AutomationLiveSetting.Polite);

        // Six compact cards, with an 8 DIP gap in each direction.
        var chips = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -8, -8) };
        foreach (var (label, length) in KeepAwakeText.Lengths)
            chips.Children.Add(Chip(label, length));
        chips.Children.Add(Chip(UntilLabel, null));
        System.Windows.Automation.AutomationProperties.SetName(chips, "Keep awake length");

        // "Until…": pick a time, then Start. Minute precision, as in Settings.
        for (int minute = 0; minute < 1440; minute++)
            _until.Items.Add(new ComboBoxItem { Content = KeepAwakeText.Until(minute), Tag = minute });
        _until.SetResourceReference(StyleProperty, "HopperComboBoxStyle");
        System.Windows.Automation.AutomationProperties.SetName(_until, "End time");
        var start = new Button { Content = "Start", Margin = new Thickness(8, 0, 0, 0) };
        start.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        start.Click += (_, _) =>
        {
            _untilPopup.IsOpen = false;
            Run(_module.StartSessionAsync(new KeepAwakeSettings { Mode = AwakeMode.UntilTime, UntilMinute = Math.Max(0, _until.SelectedIndex) }));
        };
        var picker = new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12), Margin = new Thickness(0, 4, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _until, start } },
        };
        picker.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        picker.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        _untilPopup.Child = picker;

        Content = new Grid { Children = { new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { chips, _error } }, _untilPopup } };
        System.Windows.Automation.AutomationProperties.SetName(this, "Keep awake");

        _tick.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { _module.Changed += Refresh; _tick.Start(); Refresh(); };
        Unloaded += (_, _) => { _module.Changed -= Refresh; _tick.Stop(); _untilPopup.IsOpen = false; };
        Refresh();
    }

    private ToggleButton Chip(string label, KeepAwakeSettings? length)
    {
        var chip = new ToggleButton { Content = label, Height = 96, FontSize = 14, Margin = new Thickness(0, 0, 8, 8) };
        chip.SetResourceReference(StyleProperty, "ChoiceStyle");
        chip.MouseEnter += (_, _) => Refresh();
        chip.MouseLeave += (_, _) => Refresh();
        OnFlip(chip, () => OnChip(chip, length));
        _chips.Add((chip, length, label));
        return chip;
    }

    /// <summary>
    /// Runs <paramref name="changed"/> when the button's state flips by mouse, keyboard or UI
    /// Automation (whose Toggle raises no Click). Handlers compare with the module's state, so
    /// Refresh writing that state back does nothing.
    /// </summary>
    internal static void OnFlip(ToggleButton button, Action changed)
    {
        button.Checked += (_, _) => changed();
        button.Unchecked += (_, _) => changed();
    }

    private bool IsSelected(KeepAwakeSettings? length) => _module.Active is { } active && (length is null
        ? active.Mode == AwakeMode.UntilTime
        : KeepAwakeText.SameLength(active, length));

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _error.Text = _module.Error ?? "";
        _error.Visibility = _error.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (chip, length, label) in _chips)
        {
            bool selected = IsSelected(length);
            chip.IsChecked = selected;
            chip.Content = selected && chip.IsMouseOver ? "Stop" : length is null && selected
                ? $"Until {KeepAwakeText.Until(_module.Active!.UntilMinute)}" : label;
        }
    }

    /// <summary>A chip starts its length; the lit chip stops. Compared with the module, so Refresh writing state back does nothing.</summary>
    private void OnChip(ToggleButton chip, KeepAwakeSettings? length)
    {
        bool selected = IsSelected(length);
        if (chip.IsChecked == selected)
            return;
        Refresh(); // the module's state shows until the change lands
        if (selected)
            Run(_module.StopSessionAsync());
        else if (length is not null)
            Run(_module.StartSessionAsync(length));
        else
        {
            _until.SelectedIndex = _module.Settings.UntilMinute;
            _untilPopup.PlacementTarget = chip;
            _untilPopup.IsOpen = true;
            _until.Focus();
        }
    }

    private async void Run(Task change)
    {
        try { await change; }
        catch (Exception ex)
        {
            Log.Error("Keep awake: a change from the island failed", ex);
            Refresh();
            _error.Text = ex.Message;
            _error.Visibility = Visibility.Visible;
            return;
        }
        Refresh();
    }
}
