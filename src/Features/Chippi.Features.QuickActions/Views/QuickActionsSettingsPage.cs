// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using Chippi.UI.Controls;
using Microsoft.Win32;

namespace Chippi.Features.QuickActions.Views;

/// <summary>
/// Settings › Quick actions: Show in Actions controls gallery visibility, never the Windows setting itself.
/// Each row names its action and says Shown or Hidden. Keep open during Quit all lists the bulk-quit exceptions.
/// What this PC can't do is left out.
/// </summary>
public sealed class QuickActionsSettingsPage : UserControl
{
    private readonly QuickActionsModule _module;
    private readonly Card _shown = SettingsLayout.Card();
    private readonly Card _kept = SettingsLayout.Card();
    private readonly Dictionary<QuickActionId, (ToggleButton Switch, LucideIcon Glyph)> _rows = [];
    private string _offered = "", _keptShown = "";
    private bool _updating;

    public QuickActionsSettingsPage(QuickActionsModule module)
    {
        _module = module;
        Content = new StackPanel
        {
            Children =
            {
                SettingsLayout.Section("Show in Actions", first: true), _shown,
                SettingsLayout.Section("Keep open during Quit all"), _kept,
            },
        };
        Loaded += (_, _) =>
        {
            _module.Changed += Refresh;
            _ = _module.RefreshAsync();
            Refresh();
        };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Refresh);
            return;
        }
        var offered = _module.Offered;
        string key = string.Join(",", offered.Select(a => a.Key));
        if (key != _offered)
        {
            _offered = key;
            _rows.Clear();
            _shown.Children.Clear();
            foreach (var action in offered)
                _shown.Children.Add(PickRow(action));
        }
        _updating = true;
        try
        {
            foreach (var (id, row) in _rows)
            {
                bool shown = _module.Settings.Shows(id);
                row.Switch.IsChecked = shown;
                row.Switch.Content = shown ? "Shown" : "Hidden";
                AutomationProperties.SetItemStatus(row.Switch, shown ? "Shown" : "Hidden");
                row.Glyph.SetResourceReference(LucideIcon.BrushProperty, shown ? "AccentBrush" : "TextTertiaryBrush");
            }
        }
        finally { _updating = false; }
        RefreshKept();
    }

    private Border PickRow(QuickAction action)
    {
        var glyph = new LucideIcon { Data = action.Icon, Size = 16, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var toggle = new ToggleButton { VerticalAlignment = VerticalAlignment.Center };
        toggle.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
        AutomationProperties.SetName(toggle, $"Show {action.Name} in Actions");
        AutomationProperties.SetHelpText(toggle, "Changes the Actions gallery, not the Windows setting.");
        // Checked and Unchecked, not Click: UI Automation's Toggle raises no Click.
        toggle.Checked += (_, _) => { if (!_updating) _module.SetShown(action.Id, true); };
        toggle.Unchecked += (_, _) => { if (!_updating) _module.SetShown(action.Id, false); };
        _rows[action.Id] = (toggle, glyph);
        var name = Text(action.Name);
        DockPanel.SetDock(glyph, Dock.Left);
        DockPanel.SetDock(toggle, Dock.Right);
        return new Border { MinHeight = SettingsLayout.RowHeight, Padding = SettingsLayout.RowPadding, Child = new DockPanel { Children = { glyph, toggle, name } } };
    }

    private void RefreshKept()
    {
        var kept = _module.Settings.Kept;
        string key = string.Join("|", kept);
        if (key == _keptShown && _kept.Children.Count > 0)
            return;
        _keptShown = key;
        _kept.Children.Clear();
        foreach (var path in kept)
        {
            string name = AppName(path);
            var remove = Button("Remove");
            AutomationProperties.SetName(remove, $"Remove {name} from kept apps");
            remove.ToolTip = $"Allow {name} in Quit all";
            remove.Click += (_, _) => _module.Unkeep(path);
            _kept.Children.Add(Row(Mark(path, name), Text(name), remove));
        }
        var add = Button("Add app");
        add.Click += (_, _) => AddMenu(add).IsOpen = true;
        var count = Text(kept.Count switch { 0 => "None", 1 => "1 app", var n => $"{n} apps" });
        count.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        _kept.Children.Add(Row(null, count, add));
    }

    /// <summary>Add app: the open apps not kept yet, then Browse… for an .exe.</summary>
    private Popup AddMenu(FrameworkElement anchor)
    {
        var stack = new StackPanel();
        var border = new Border { Child = stack, Padding = new Thickness(4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        var popup = new Popup { Child = border, PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        void Item(string text, Action click)
        {
            var item = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Left, MinWidth = 160 };
            item.SetResourceReference(StyleProperty, "FlyoutRowButtonStyle");
            item.Click += (_, _) =>
            {
                popup.IsOpen = false;
                click();
            };
            stack.Children.Add(item);
        }
        foreach (var app in _module.State.Apps.Where(a => !_module.Settings.Keeps(a.Path)).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
            Item(app.Name, () => _module.Keep(app.Path));
        Item("Browse…", () =>
        {
            var dialog = new OpenFileDialog { Filter = "Apps (*.exe)|*.exe", CheckFileExists = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                _module.Keep(dialog.FileName);
        });
        border.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            popup.IsOpen = false;
            anchor.Focus();
            e.Handled = true;
        };
        popup.Opened += (_, _) => stack.Children.OfType<Button>().FirstOrDefault()?.Focus();
        return popup;
    }

    /// <summary>The app's own name for itself, else its file name.</summary>
    internal static string AppName(string path)
    {
        try
        {
            if (File.Exists(path) && System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description)
                return description.Trim();
        }
        catch (Exception)
        {
            // Unreadable version info.
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    private static UIElement Mark(string path, string name)
    {
        if (AppIcon.Load(path) is { } icon)
            return new System.Windows.Controls.Image { Source = icon, Width = 20, Height = 20, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var letters = Text(Ui.Initials(name), "CaptionTextStyle");
        letters.HorizontalAlignment = HorizontalAlignment.Center;
        var box = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 14, 0), Child = letters, VerticalAlignment = VerticalAlignment.Center };
        box.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
        return box;
    }

    private static Border Row(UIElement? lead, TextBlock label, Button end)
    {
        var dock = new DockPanel();
        DockPanel.SetDock(end, Dock.Right);
        dock.Children.Add(end);
        if (lead is not null)
        {
            DockPanel.SetDock(lead, Dock.Left);
            dock.Children.Add(lead);
        }
        dock.Children.Add(label);
        return new Border { MinHeight = SettingsLayout.RowHeight, Padding = SettingsLayout.RowPadding, Child = dock };
    }

    private static TextBlock Text(string text, string style = "BodyTextStyle")
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(StyleProperty, style);
        return block;
    }

    private static Button Button(string text)
    {
        var button = new Button { Content = text, VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        return button;
    }
}
