// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using Chippi.Core.Diagnostics;
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
    private readonly Button _add = Button("Add app");
    private readonly TextBlock _keptError = Error();
    private Popup? _addPopup;
    private string _offered = "", _keptShown = "";
    private bool _updating, _loaded;

    public QuickActionsSettingsPage(QuickActionsModule module)
    {
        _module = module;
        _add.Click += (_, _) =>
        {
            CloseAddMenu();
            _addPopup = AddMenu(_add);
            _addPopup.IsOpen = true;
        };
        Content = new StackPanel
        {
            Children =
            {
                SettingsLayout.Section("Show in Actions", first: true), _shown,
                SettingsLayout.Section("Keep open during Quit all"), _kept, _keptError,
            },
        };
        Loaded += (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            _module.Changed += OnChanged;
            _ = _module.RefreshAsync();
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _loaded = false;
            _module.Changed -= OnChanged;
            CloseAddMenu();
        };
        Refresh();
    }

    private void OnChanged()
    {
        if (!_loaded) return;
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.BeginInvoke(() => { if (_loaded) Refresh(); });
    }

    private void Refresh()
    {
        var offered = _module.Offered;
        string key = string.Join(",", offered.Select(a => a.Key));
        if (key != _offered)
        {
            QuickActionId? focused = _rows.Where(pair => pair.Value.Switch.IsKeyboardFocusWithin).Select(pair => (QuickActionId?)pair.Key).FirstOrDefault();
            _offered = key;
            _rows.Clear();
            _shown.Children.Clear();
            foreach (var action in offered)
                _shown.Children.Add(PickRow(action));
            if (offered.Count == 0) _shown.Children.Add(SettingsLayout.Empty("No actions available"));
            if (focused is { } id)
                (_rows.GetValueOrDefault(id).Switch ?? _rows.Values.FirstOrDefault().Switch)?.Focus();
        }
        if (_shown.Children.Count == 0) _shown.Children.Add(SettingsLayout.Empty("No actions available"));
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
        _rows[action.Id] = (toggle, glyph);
        var error = Error();
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Text(action.Name), error } };
        DockPanel.SetDock(glyph, Dock.Left);
        DockPanel.SetDock(toggle, Dock.Right);
        var row = new Border { MinHeight = SettingsLayout.RowHeight, Padding = SettingsLayout.RowPadding, Child = new DockPanel { Children = { glyph, toggle, label } } };
        void SetShown(bool shown)
        {
            if (_updating) return;
            Change(() => _module.SetShown(action.Id, shown), error);
            row.MinHeight = error.Visibility == Visibility.Visible ? SettingsLayout.CaptionRowHeight : SettingsLayout.RowHeight;
        }
        // Checked and Unchecked, not Click: UI Automation's Toggle raises no Click.
        toggle.Checked += (_, _) => SetShown(true);
        toggle.Unchecked += (_, _) => SetShown(false);
        return row;
    }

    private void Change(Action edit, TextBlock error)
    {
        try
        {
            edit();
            error.Text = "";
            error.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Warn("Quick actions: saving settings failed", ex);
            error.Text = "Save failed";
            error.Visibility = Visibility.Visible;
            Refresh(); // Revert a checked switch to the value that was actually saved.
        }
    }

    private static TextBlock Error()
    {
        var error = Text("", "CaptionTextStyle");
        error.Visibility = Visibility.Collapsed;
        error.Margin = new Thickness(0, 4, 0, 0);
        error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        return error;
    }

    private void RefreshKept()
    {
        var kept = _module.Settings.Kept;
        string key = string.Join("|", kept);
        if (key == _keptShown && _kept.Children.Count > 0)
            return;
        int focused = _kept.Children.OfType<Border>().Select((row, index) => (row, index)).Where(part => part.row.IsKeyboardFocusWithin).Select(part => part.index).DefaultIfEmpty(-1).First();
        _keptShown = key;
        if (_add.Parent is Panel parent) parent.Children.Remove(_add);
        _kept.Children.Clear();
        foreach (var path in kept)
        {
            string name = AppName(path);
            var remove = Button("Remove");
            AutomationProperties.SetName(remove, $"Remove {name} from kept apps");
            remove.ToolTip = $"Allow {name} in Quit all";
            remove.Click += (_, _) => Change(() => _module.Unkeep(path), _keptError);
            _kept.Children.Add(Row(Mark(path, name), Text(name), remove));
        }
        var count = Text(kept.Count switch { 0 => "None", 1 => "1 app", var n => $"{n} apps" });
        count.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        _kept.Children.Add(Row(null, count, _add));
        if (focused >= 0 && _loaded)
        {
            var row = (Border)_kept.Children[Math.Min(focused, _kept.Children.Count - 1)];
            ((DockPanel)row.Child).Children.OfType<Button>().FirstOrDefault()?.Focus();
        }
    }

    private void CloseAddMenu()
    {
        if (_addPopup is null) return;
        _addPopup.IsOpen = false;
        _addPopup = null;
    }

    /// <summary>Add app: the open apps not kept yet, then Browse… for an .exe.</summary>
    internal Popup AddMenu(FrameworkElement anchor)
    {
        var stack = new StackPanel();
        KeyboardNavigation.SetTabNavigation(stack, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(stack, KeyboardNavigationMode.Cycle);
        var scroll = new ScrollViewer { Content = stack, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
        var border = new Border { Child = scroll, MinWidth = 200, MaxWidth = 320, Padding = new Thickness(4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        var popup = new Popup { Child = border, PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        void Item(string text, Action click)
        {
            var label = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 280 };
            var item = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = text };
            AutomationProperties.SetName(item, text);
            item.SetResourceReference(StyleProperty, "FlyoutRowButtonStyle");
            item.Click += (_, _) =>
            {
                popup.IsOpen = false;
                click();
                if (_loaded) anchor.Focus();
            };
            stack.Children.Add(item);
        }
        foreach (var app in _module.State.Apps.Where(a => !_module.Settings.Keeps(a.Path))
            .DistinctBy(a => Path.GetFileName(a.Path), StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
            Item(string.IsNullOrWhiteSpace(app.Name) ? AppName(app.Path) : OneLine(app.Name), () => Change(() => _module.Keep(app.Path), _keptError));
        Item("Browse…", () =>
        {
            var dialog = new OpenFileDialog { Filter = "Apps (*.exe)|*.exe", CheckFileExists = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                Change(() => _module.Keep(dialog.FileName), _keptError);
        });
        border.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            popup.IsOpen = false;
            if (_loaded) anchor.Focus();
            e.Handled = true;
        };
        popup.Opened += (_, _) => stack.Children.OfType<Button>().FirstOrDefault()?.Focus();
        popup.Closed += (_, _) => { if (ReferenceEquals(_addPopup, popup)) _addPopup = null; };
        return popup;
    }

    /// <summary>The app's own name for itself, else its file name.</summary>
    internal static string AppName(string path)
    {
        try
        {
            if (File.Exists(path) && System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription is { } description && !string.IsNullOrWhiteSpace(description))
                return OneLine(description);
        }
        catch (Exception)
        {
            // Unreadable version info.
        }
        string fallback = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(fallback) ? "App" : OneLine(fallback);
    }

    private static string OneLine(string value) => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = text };
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
