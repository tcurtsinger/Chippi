// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Chippi.UI.Controls;
using Microsoft.Win32;

namespace Chippi.Features.SmoothScrolling;

/// <summary>
/// Smooth scrolling's rows for Settings › Windows › Mouse, under the shell's "Scrolling" heading (wave-1.html, settings-mouse):
/// both switches, Response, Coast, Distance, a sample to scroll, and Scrolling per app with each app's real icon.
/// </summary>
public sealed class SmoothScrollingRows : UserControl
{
    private const double IconSize = 20;
    private static readonly int[] Speeds = [50, 75, 100, 150, 200, 300];

    private readonly SmoothScrollingModule _module;
    private readonly ToggleButton _smooth = Switch(), _linear = Switch();
    private readonly SettingRow _appsRow;
    private readonly Slider _response, _coast, _distance;
    private readonly TextBlock _responseValue, _coastValue, _distanceValue;
    private readonly FrameworkElement _responseRow, _coastRow;
    private readonly Card _apps = new();
    private readonly Button _add;
    private readonly Border _error;
    private readonly TextBlock _errorText = new();
    private bool _updating;

    public SmoothScrollingRows(SmoothScrollingModule module)
    {
        _module = module;
        AutomationProperties.SetName(_smooth, "Smooth scrolling");
        AutomationProperties.SetName(_linear, "Linear scrolling");
        _response = MakeSlider("Response", SmoothScrollingSettings.ResponseMin, SmoothScrollingSettings.ResponseMax, 10, out _responseValue);
        _coast = MakeSlider("Coast", 0, SmoothScrollingSettings.CoastMax, 10, out _coastValue);
        _distance = MakeSlider("Distance", SmoothScrollingSettings.DistanceMin, SmoothScrollingSettings.DistanceMax, 5, out _distanceValue);
        _responseRow = Dependent("Response", _response, _responseValue);
        _coastRow = Dependent("Coast", _coast, _coastValue);
        var distanceRow = SettingsLayout.Row("Distance", Pair(_distance, _distanceValue), $"Windows: {SmoothScrollingModule.WindowsLinesPerNotch()} lines per notch");
        _response.ValueChanged += (_, e) => Slide(s => s with { ResponseMs = (int)e.NewValue });
        _coast.ValueChanged += (_, e) => Slide(s => s with { CoastMs = (int)e.NewValue });
        _distance.ValueChanged += (_, e) => Slide(s => s with { DistancePercent = (int)e.NewValue });

        _errorText.SetResourceReference(StyleProperty, "CaptionTextStyle");
        _errorText.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_errorText, AutomationLiveSetting.Polite);
        _error = new Border { MinHeight = SettingsLayout.RowHeight, Padding = SettingsLayout.RowPadding, Child = _errorText };

        _add = SettingsLayout.AddButton("app", () => AddMenu().IsOpen = true);
        _appsRow = SettingsLayout.Row("Scrolling per app", _add);

        var cards = new StackPanel();
        cards.Children.Add(SettingsLayout.Card(SettingsLayout.Row("Smooth scrolling", _smooth), _responseRow, _coastRow,
            SettingsLayout.Row("Linear scrolling", _linear), distanceRow, PreviewRow(), _error));
        cards.Children.Add(new Border { Height = 8 });
        _apps.Children.Add(_appsRow);
        cards.Children.Add(_apps);
        Content = cards;

        // A UIA Toggle raises Checked and Unchecked, not Click.
        _smooth.Checked += (_, _) => Slide(s => s with { SmoothEnabled = true });
        _smooth.Unchecked += (_, _) => Slide(s => s with { SmoothEnabled = false });
        _linear.Checked += (_, _) => Slide(s => s with { LinearEnabled = true });
        _linear.Unchecked += (_, _) => Slide(s => s with { LinearEnabled = false });
        Loaded += (_, _) => { module.Changed += OnChanged; Refresh(); };
        Unloaded += (_, _) => module.Changed -= OnChanged;
        Refresh();
    }

    private static ToggleButton Switch()
    {
        var toggle = new ToggleButton { VerticalAlignment = VerticalAlignment.Center };
        toggle.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
        return toggle;
    }

    private void Slide(Func<SmoothScrollingSettings, SmoothScrollingSettings> change)
    {
        if (!_updating)
            Save(change);
    }

    private void Save(Func<SmoothScrollingSettings, SmoothScrollingSettings> change)
    {
        try { _module.Update(change); }
        catch (Exception ex) { Chippi.Core.Diagnostics.Log.Warn("Smooth scrolling: saving failed", ex); }
        Refresh();
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        var s = _module.Settings;
        _updating = true;
        try
        {
            _smooth.IsChecked = s.SmoothEnabled;
            _smooth.Content = s.SmoothEnabled ? "On" : "Off";
            _linear.IsChecked = s.LinearEnabled;
            _linear.Content = s.LinearEnabled ? "On" : "Off";
            _response.Value = s.ResponseMs;
            _responseValue.Text = $"{s.ResponseMs} ms";
            _coast.Value = s.CoastMs;
            _coastValue.Text = $"{s.CoastMs} ms";
            _distance.Value = s.DistancePercent;
            _distanceValue.Text = $"{s.DistancePercent}%";
            foreach (var row in new[] { _responseRow, _coastRow })
            {
                row.IsEnabled = s.SmoothEnabled;
                row.Opacity = s.SmoothEnabled ? 1 : 0.45;
            }
            _errorText.Text = _module.Error ?? "";
            _error.Visibility = _module.Error is null ? Visibility.Collapsed : Visibility.Visible;
            AutomationProperties.SetItemStatus(_smooth, _module.Error ?? (s.SmoothEnabled ? "On" : "Off"));
            RebuildApps(s.Apps);
        }
        finally { _updating = false; }
    }

    private void RebuildApps(AppScroll[] apps)
    {
        _appsRow.Description = apps.Length switch { 0 => "", 1 => "1 app", int n => $"{n} apps" };
        while (_apps.Children.Count > 1)
            _apps.Children.RemoveAt(1);
        foreach (var app in apps)
            _apps.Children.Add(AppRow(app));
    }

    private Border AppRow(AppScroll app)
    {
        string name = AppName(app.App);
        var items = Speeds.Select(Label).ToList();
        if (app.Percent != 0 && !Speeds.Contains(app.Percent))
            items.Add(Label(app.Percent));
        items.Add("Leave alone");
        var combo = SettingsLayout.Combo(items);
        combo.Width = 130;
        combo.SelectedItem = app.Percent == 0 ? "Leave alone" : Label(app.Percent);
        AutomationProperties.SetName(combo, $"Scrolling in {name}");
        combo.SelectionChanged += (_, _) =>
        {
            if (_updating || combo.SelectedItem is not string text) return;
            int percent = text == "Leave alone" ? 0 : int.Parse(text.TrimEnd('%'));
            Save(s => s with { Apps = [.. s.Apps.Select(a => SameApp(a.App, app.App) ? a with { Percent = percent } : a)] });
        };
        var remove = new Button { Content = "Remove", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        remove.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        AutomationProperties.SetName(remove, $"Remove {name}");
        remove.Click += (_, _) => Save(s => s with { Apps = [.. s.Apps.Where(a => !SameApp(a.App, app.App))] });

        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Icon(app.App, name));
        var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        label.SetResourceReference(StyleProperty, "BodyTextStyle");
        title.Children.Add(label);
        return Dependent(title, Pair(combo, remove));
    }

    private static string Label(int percent) => $"{percent}%";

    private static bool SameApp(string a, string b) => string.Equals(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>The app's own Windows icon; a letter only until it loads, or when Windows has none (Travis, task 264).</summary>
    private static FrameworkElement Icon(string path, string name)
    {
        var letter = new TextBlock
        {
            Text = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        letter.SetResourceReference(StyleProperty, "CaptionTextStyle");
        var monogram = new Border { Width = IconSize, Height = IconSize, CornerRadius = new CornerRadius(4), Child = letter };
        monogram.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
        return AppIcon.View(path, IconSize, monogram);
    }

    internal static string AppName(string path)
    {
        try
        {
            if (File.Exists(path) && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description)
                return description.Trim();
        }
        catch (Exception)
        {
            // Unreadable version info.
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    private void AddApp(string path) =>
        Save(s => s.Apps.Any(a => SameApp(a.App, path)) ? s : s with { Apps = [.. s.Apps, new AppScroll(path, 100)] });

    /// <summary>Add app: the apps open now, with their icons, then Browse… for one that isn't running.</summary>
    private Popup AddMenu()
    {
        var listed = _module.Settings.Apps;
        var stack = new StackPanel();
        var border = new Border { Child = stack, Padding = new Thickness(4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), MinWidth = 240 };
        border.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var popup = new Popup { Child = border, PlacementTarget = _add, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var buttons = new List<Button>();
        foreach (var (name, path) in RunningApps().Where(a => !listed.Any(l => SameApp(l.App, a.Path))))
            buttons.Add(Item(Icon(path, name), name, () => AddApp(path)));
        var folder = new LucideIcon { Data = LucideIcons.Folder, Size = 16, Width = IconSize };
        folder.SetResourceReference(LucideIcon.BrushProperty, "TextPrimaryBrush");
        buttons.Add(Item(folder, "Browse…", () =>
        {
            var dialog = new OpenFileDialog { Filter = "Apps (*.exe)|*.exe", CheckFileExists = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                AddApp(dialog.FileName);
        }));
        border.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { popup.IsOpen = false; _add.Focus(); e.Handled = true; }
            else if (e.Key is Key.Up or Key.Down)
            {
                int index = buttons.FindIndex(b => b.IsKeyboardFocusWithin);
                buttons[(index + (e.Key == Key.Up ? buttons.Count - 1 : 1)) % buttons.Count].Focus();
                e.Handled = true;
            }
        };
        popup.Opened += (_, _) => buttons[0].Focus();
        return popup;

        Button Item(FrameworkElement lead, string text, Action click)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(lead);
            content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
            var button = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Left };
            button.SetResourceReference(StyleProperty, "FlyoutRowButtonStyle");
            AutomationProperties.SetName(button, text);
            button.Click += (_, _) => { popup.IsOpen = false; click(); };
            stack.Children.Add(button);
            return button;
        }
    }

    /// <summary>Apps with a window, by name, Chippi left out. One that can't be read (elevated, another user's) is left out too.</summary>
    private static List<(string Name, string Path)> RunningApps()
    {
        var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int self = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == self || process.MainWindowHandle == 0 || process.MainModule?.FileName is not { } path)
                        continue;
                    apps.TryAdd(path, AppName(path));
                }
                catch (Exception)
                {
                    // Access denied, or gone.
                }
            }
        }
        return [.. apps.Select(a => (a.Value, a.Key)).OrderBy(a => a.Value, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static Slider MakeSlider(string name, int min, int max, int step, out TextBlock value)
    {
        var slider = new Slider { Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 5, IsSnapToTickEnabled = true, TickFrequency = step, Width = 240, VerticalAlignment = VerticalAlignment.Center };
        slider.SetResourceReference(StyleProperty, "HopperSliderStyle");
        AutomationProperties.SetName(slider, name);
        value = new TextBlock { MinWidth = 60, TextAlignment = TextAlignment.Right, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        value.SetResourceReference(StyleProperty, "NumberTextStyle");
        value.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return slider;
    }

    private static StackPanel Pair(params UIElement[] items)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }

    private static Border Dependent(string title, Slider slider, TextBlock value)
    {
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(StyleProperty, "BodyTextStyle");
        return Dependent(label, Pair(slider, value));
    }

    /// <summary>A row inset like the dependent rows of a switch: a label at the left, the control at the right.</summary>
    private static Border Dependent(UIElement left, UIElement control)
    {
        var dock = new DockPanel();
        DockPanel.SetDock(control, Dock.Right);
        dock.Children.Add(control);
        dock.Children.Add(left);
        return new Border { MinHeight = SettingsLayout.RowHeight, Padding = SettingsLayout.DependentPadding, Child = dock };
    }

    /// <summary>A short list to scroll with the wheel, so a change is felt before leaving Settings.</summary>
    private static SettingRow PreviewRow()
    {
        var lines = new StackPanel { Margin = new Thickness(12, 4, 12, 4) };
        for (int i = 1; i <= 120; i++)
        {
            var line = new TextBlock { Text = $"Line {i}", Height = 24 };
            line.SetResourceReference(StyleProperty, "BodyTextStyle");
            lines.Children.Add(line);
        }
        var viewer = new ScrollViewer { Content = lines, Height = 110, Width = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetName(viewer, "Scrolling preview");
        var frame = new Border { Child = viewer, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 6, 0, 6) };
        frame.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
        return SettingsLayout.Row("Preview", frame);
    }
}
