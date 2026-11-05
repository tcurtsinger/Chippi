// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using Chippi.Core.Actions;
using Chippi.UI.Controls;

namespace Chippi.App.Settings.Pages;

/// <summary>
/// "Choose an action" (docs/phase-6/design/shortcuts.html): a button that opens a searchable list of the actions grouped as
/// Window, Media, Selected text, Chippi and Settings page, with Other… for the kinds that take a value (an app, a file, keys, text).
/// </summary>
internal sealed class ActionDropdown : Button
{
    internal sealed record Item(string Group, string Name, string Glyph, ChippiAction Action);

    private readonly Func<IReadOnlyList<Item>> _items;
    private readonly Action<ChippiAction> _chosen;
    private readonly Action _other;
    private readonly TextBlock _label = new();
    private readonly LucideIcon _icon = new() { Size = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private Popup? _popup;

    public ActionDropdown(Func<IReadOnlyList<Item>> items, Action<ChippiAction> chosen, Action other)
    {
        _items = items;
        _chosen = chosen;
        _other = other;
        SettingsUi.Styled(this, "SecondaryButtonStyle");
        Width = 300;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(this, "Choose an action");
        _icon.SetResourceReference(LucideIcon.BrushProperty, "TextPrimaryBrush");
        var chevron = new LucideIcon { Data = LucideIcons.ChevronDown, Size = 14, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        chevron.SetResourceReference(LucideIcon.BrushProperty, "TextSecondaryBrush");
        var dock = new DockPanel { LastChildFill = true, Width = 268, HorizontalAlignment = HorizontalAlignment.Left };
        DockPanel.SetDock(chevron, Dock.Right);
        dock.Children.Add(chevron);
        DockPanel.SetDock(_icon, Dock.Left);
        dock.Children.Add(_icon);
        dock.Children.Add(_label);
        Content = dock;
        Click += (_, _) => Open();
        Show(null, null);
    }

    /// <summary>The action chosen, or null for "Choose an action"; <paramref name="text"/> overrides its name ("Other…").</summary>
    public void Show(Item? item, string? text)
    {
        _label.Text = text ?? item?.Name ?? "Choose an action";
        _icon.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        if (item is not null)
            _icon.Data = LucideIcons.Get(item.Glyph);
        _label.SetResourceReference(TextBlock.ForegroundProperty, item is null && text is null ? "TextTertiaryBrush" : "TextPrimaryBrush");
    }

    private void Open()
    {
        var search = new SearchBox { Placeholder = "Search", Margin = new Thickness(0, 0, 0, 6) };
        AutomationProperties.SetName(search, "Search actions");
        var list = new StackPanel();
        var scroll = new ScrollViewer { Content = list, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel { Width = Math.Max(ActualWidth, 300) };
        stack.Children.Add(search);
        stack.Children.Add(scroll);
        var border = new Border { Child = stack, Padding = new Thickness(6), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        var popup = new Popup { Child = border, PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        _popup = popup;
        var all = _items();
        var buttons = new List<Button>();

        void Fill()
        {
            list.Children.Clear();
            buttons.Clear();
            string text = search.Text.Trim();
            foreach (var group in all.Where(i => text.Length == 0 || i.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)).GroupBy(i => i.Group))
            {
                list.Children.Add(SettingsUi.Styled(new TextBlock { Text = group.Key, Margin = new Thickness(10, 8, 10, 4) }, "CaptionTextStyle"));
                foreach (var item in group)
                    buttons.Add(Row(item.Name, item.Glyph, () => { _chosen(item.Action); Show(item, null); }));
            }
            if (text.Length == 0)
            {
                list.Children.Add(new Border { Height = 1, Margin = new Thickness(4, 6, 4, 6), Background = (System.Windows.Media.Brush)FindResource("BorderBrush") });
                buttons.Add(Row("Other…", nameof(LucideIcons.Plus), () => { _other(); Show(null, "Other…"); }));
            }
            else if (buttons.Count == 0)
                list.Children.Add(SettingsUi.Styled(new TextBlock { Text = "No actions found", Margin = new Thickness(10, 8, 10, 8) }, "CaptionTextStyle"));
        }

        Button Row(string name, string glyph, Action click)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new LucideIcon { Data = LucideIcons.Get(glyph), Size = 16, Width = 20 };
            icon.SetResourceReference(LucideIcon.BrushProperty, "TextPrimaryBrush");
            content.Children.Add(icon);
            content.Children.Add(new TextBlock { Text = name, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var button = SettingsUi.Styled(new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Left }, "FlyoutRowButtonStyle");
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) => { popup.IsOpen = false; click(); };
            list.Children.Add(button);
            return button;
        }

        search.TextChanged += (_, _) => Fill();
        border.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { popup.IsOpen = false; Focus(); e.Handled = true; }
            else if (e.Key == Key.Enter && search.IsKeyboardFocused && buttons.Count > 0) { buttons[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); e.Handled = true; }
            else if (e.Key is Key.Up or Key.Down && buttons.Count > 0)
            {
                int index = buttons.FindIndex(b => b.IsKeyboardFocusWithin);
                int next = index < 0 ? (e.Key == Key.Down ? 0 : buttons.Count - 1) : (index + (e.Key == Key.Down ? 1 : buttons.Count - 1)) % buttons.Count;
                buttons[next].Focus();
                e.Handled = true;
            }
        };
        popup.Opened += (_, _) => search.Focus();
        Fill();
        popup.IsOpen = true;
    }

    /// <summary>Tests open the list without a click.</summary>
    internal void OpenList() => Open();
}
