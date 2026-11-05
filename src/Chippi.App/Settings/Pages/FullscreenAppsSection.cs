// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.UI.Controls;
using Microsoft.Win32;

namespace Chippi.App.Settings.Pages;

/// <summary>
/// Settings › General › Fullscreen apps (docs/phase-6/input-layer.md; wave-1.html, settings-general): Always on, and Pause in
/// these apps with each app's real icon. Shown once a Phase 6 input feature is installed. The list dims while Always on is
/// on, since it does nothing then, and is kept.
/// </summary>
internal sealed class FullscreenAppsSection : StackPanel
{
    private const double IconSize = 20;

    private readonly ShellContext _shell;
    private readonly ToggleButton _alwaysOn;
    private readonly Card _list = new();
    private readonly Button _add;

    public FullscreenAppsSection(ShellContext shell)
    {
        _shell = shell;
        var app = SettingsUi.ReadApp(shell.Config);
        _alwaysOn = SettingsUi.Switch(app.InputAlwaysOn);
        AutomationProperties.SetName(_alwaysOn, "Always on");
        SettingsUi.OnSwitched(_alwaysOn, on =>
        {
            SettingsUi.SaveApp(_shell.Config, s => s.InputAlwaysOn = on);
            _list.IsEnabled = !on;
        });
        _add = SettingsUi.Button("Add app");
        _add.Click += (_, _) => AddMenu(RunningApps()).IsOpen = true;

        Children.Add(SettingsUi.Section("Fullscreen apps"));
        Children.Add(SettingsUi.Group(SettingsUi.Row("Always on", "", _alwaysOn)));
        _list.Margin = new Thickness(0, 8, 0, 0);
        _list.IsEnabled = !app.InputAlwaysOn;
        Children.Add(_list);
        RenderList(app.PauseApps);
        Loaded += (_, _) => { _shell.Registry.Changed += OnFeaturesChanged; OnFeaturesChanged(null, EventArgs.Empty); };
        Unloaded += (_, _) => _shell.Registry.Changed -= OnFeaturesChanged;
        OnFeaturesChanged(null, EventArgs.Empty);
    }

    /// <summary>Tests and shots show the section without installing a Phase 6 feature.</summary>
    internal static bool? ShownOverride { get; set; }

    /// <summary>Shown once a Phase 6 input feature is installed.</summary>
    internal bool Shown => ShownOverride ?? _shell.Registry.Features.Any(f => f.IsInstalled && f.Descriptor.PausesInFullscreen);

    private void OnFeaturesChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnFeaturesChanged(sender, e));
            return;
        }
        Visibility = Shown ? Visibility.Visible : Visibility.Collapsed;
    }

    private List<string> Apps => SettingsUi.ReadApp(_shell.Config).PauseApps;

    private void Save(IEnumerable<string> apps)
    {
        List<string> list = [.. apps];
        SettingsUi.SaveApp(_shell.Config, s => s.PauseApps = list);
        RenderList(list);
    }

    private void RenderList(List<string> apps)
    {
        _list.Children.Clear();
        string caption = apps.Count switch { 0 => "", 1 => "1 app", int n => $"{n} apps" };
        _list.Children.Add(SettingsUi.Row("Pause in these apps", caption, _add));
        foreach (var path in apps)
            _list.Children.Add(AppRow(path));
    }

    /// <summary>The app's icon and name over its exe, indented as a dependent row, with Remove.</summary>
    private Border AppRow(string path)
    {
        string name = AppName(path);
        var remove = SettingsUi.Button("Remove");
        AutomationProperties.SetName(remove, $"Remove {name}");
        remove.Click += (_, _) => Save(Apps.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)));
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Icon(path, name));
        title.Children.Add(SettingsUi.Styled(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) }, "BodyTextStyle"));
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(title);
        label.Children.Add(SettingsUi.Styled(new TextBlock { Text = Path.GetFileName(path) }, "CaptionTextStyle"));
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
        Grid.SetColumn(remove, 1);
        remove.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        grid.Children.Add(remove);
        return new Border { MinHeight = SettingsLayout.CaptionRowHeight, Padding = SettingsLayout.DependentPadding, Child = grid };
    }

    /// <summary>The app's own Windows icon; a letter only until it loads, or when Windows has none (Travis, task 264).</summary>
    private static FrameworkElement Icon(string path, string name)
    {
        var letter = SettingsUi.Styled(new TextBlock
        {
            Text = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        }, "CaptionTextStyle");
        var monogram = new Border { Width = IconSize, Height = IconSize, CornerRadius = new CornerRadius(4), Child = letter };
        monogram.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
        return AppIcon.View(path, IconSize, monogram);
    }

    /// <summary>Add app: the apps open now, with their icons, then Browse… for one that isn't running (notes, wave 1 #8).</summary>
    internal Popup AddMenu(IReadOnlyList<(string Name, string Path)> running)
    {
        var listed = Apps;
        var stack = new StackPanel();
        var border = new Border { Child = stack, Padding = new Thickness(4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), MinWidth = 240 };
        border.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        var popup = new Popup { Child = border, PlacementTarget = _add, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var open = running.Where(a => !listed.Any(p => string.Equals(Path.GetFileName(p), Path.GetFileName(a.Path), StringComparison.OrdinalIgnoreCase))).ToList();
        var buttons = new List<Button>();
        if (open.Count > 0)
        {
            stack.Children.Add(SettingsUi.Styled(new TextBlock { Text = "Open apps", Margin = new Thickness(12, 6, 12, 4) }, "CaptionTextStyle"));
            foreach (var (name, path) in open)
                buttons.Add(Item(Icon(path, name), name, () => Save([.. Apps, path])));
            var divider = new Border { Height = 1, Margin = new Thickness(4, 4, 4, 4) };
            divider.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
            stack.Children.Add(divider);
        }
        var folder = new LucideIcon { Data = LucideIcons.Folder, Size = 16, Width = IconSize };
        folder.SetResourceReference(LucideIcon.BrushProperty, "TextPrimaryBrush");
        buttons.Add(Item(folder, "Browse…", Browse));
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
            var button = SettingsUi.Styled(new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Left }, "FlyoutRowButtonStyle");
            AutomationProperties.SetName(button, text);
            button.Click += (_, _) =>
            {
                popup.IsOpen = false;
                click();
            };
            stack.Children.Add(button);
            return button;
        }
    }

    private void Browse()
    {
        var dialog = new OpenFileDialog { Filter = "Apps (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true && !Apps.Contains(dialog.FileName, StringComparer.OrdinalIgnoreCase))
            Save([.. Apps, dialog.FileName]);
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

    /// <summary>The app's own name for itself, else its file name.</summary>
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
}
