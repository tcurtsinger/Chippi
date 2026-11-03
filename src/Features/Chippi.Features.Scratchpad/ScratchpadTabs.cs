// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.Scratchpad;

/// <summary>The active note, picker and New note always fit before neighboring tabs are admitted.</summary>
internal sealed class ScratchpadTabs : Grid
{
    private readonly bool _widget;
    private readonly Func<IDisposable>? _hold;
    private readonly SegmentedControl _tabs = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _more;
    private readonly Button _add;
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center };
    private (long Id, string Name)[] _notes = [];
    private long _selectedId;
    private double _width;
    private bool _updating;
    private TextBox? _rename;
    private IDisposable? _renameHold;
    private long _renameId;
    private Popup? _picker;

    public event Action<long>? Selected;
    public event Action? AddRequested;
    public event Action<long, string>? RenameRequested;
    public event Action<FrameworkElement, long>? ContextRequested;

    public ScratchpadTabs(bool widget, Func<IDisposable>? hold = null)
    {
        _widget = widget;
        _hold = hold;
        Height = widget ? 30 : 40;
        _width = IslandPage.BodyWidth; // initial layout until the host measures the strip
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Chippi.Features.Scratchpad;component/ScratchpadStyles.xaml")
        });
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (widget)
        {
            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
            line.SetResourceReference(Border.BackgroundProperty, "IslandHairBrush");
            SetColumnSpan(line, 3);
            Children.Add(line);
        }

        _tabs.SetResourceReference(StyleProperty, "SelectorBarStyle");
        _tabs.SetResourceReference(ItemsControl.ItemContainerStyleProperty, widget ? "ScratchpadTabStyle" : "SelectorBarItemStyle");
        _tabs.SetResourceReference(Control.FontFamilyProperty, "UiFont");
        _tabs.FontSize = widget ? 12 : 14;
        AutomationProperties.SetName(_tabs, "Notes");
        _tabs.SelectionChanged += (_, _) =>
        {
            if (!_updating && _tabs.SelectedItem is ListBoxItem { Tag: long id } && id != _selectedId)
                Selected?.Invoke(id);
        };
        _tabs.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (_rename is null && e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(_tabs, source) is ListBoxItem { Tag: long id } tab)
            {
                ContextRequested?.Invoke(tab, id);
                e.Handled = true;
            }
        };
        _tabs.PreviewKeyDown += OnTabKey;
        Children.Add(_tabs);

        var chevron = Glyph(LucideIcons.ChevronDown, 12);
        chevron.Margin = new Thickness(3, 0, 0, 0);
        _count.FontSize = widget ? 12 : 13;
        _more = ActionButton("All notes", new StackPanel { Orientation = Orientation.Horizontal, Children = { _count, chevron } }, widget ? 44 : 50);
        _more.Visibility = Visibility.Collapsed;
        _more.Click += (_, _) =>
        {
            _picker = BuildNotesMenu();
            _picker.IsOpen = true;
        };
        SetColumn(_more, 1);
        Children.Add(_more);
        _add = ActionButton("New note", Glyph(LucideIcons.Plus, widget ? 14 : 16), widget ? 28 : 32);
        if (widget) _add.SetResourceReference(MarginProperty, "IslandLastIconButtonMargin");
        ToolTipService.SetShowOnDisabled(_add, true);
        _add.Click += (_, _) => AddRequested?.Invoke();
        SetColumn(_add, 2);
        Children.Add(_add);
        Unloaded += (_, _) =>
        {
            if (_picker is not null) _picker.IsOpen = false;
            EndRename(true);
        };
    }

    private LucideIcon Glyph(System.Windows.Media.Geometry icon, double size)
    {
        var glyph = new LucideIcon { Data = icon, Size = size, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, _widget ? "IslandTextSecondaryBrush" : "TextSecondaryBrush");
        return glyph;
    }

    private Button ActionButton(string name, UIElement content, double width)
    {
        var button = new Button
        {
            Content = content, Width = width, Height = _widget ? 26 : 32, Padding = new Thickness(0),
            MinWidth = 0, VerticalAlignment = VerticalAlignment.Center, ToolTip = name
        };
        button.SetResourceReference(StyleProperty, _widget ? "IslandIconButtonStyle" : "SubtleButtonStyle");
        AutomationProperties.SetName(button, name);
        return button;
    }

    public void Display(ScratchpadDocument document, long selectedId)
    {
        var notes = document.Notes.Select(n => (n.Id, n.Name)).ToArray();
        bool titlesChanged = !_notes.SequenceEqual(notes);
        if (_selectedId == selectedId && !titlesChanged) return;
        if (titlesChanged && _picker?.IsOpen == true)
        {
            bool focused = _picker.Child.IsKeyboardFocusWithin;
            _picker.IsOpen = false;
            if (focused) _more.Focus();
        }
        if (_rename is not null && selectedId != _renameId)
            EndRename(notes.Any(n => n.Id == _renameId));
        _notes = notes;
        _selectedId = selectedId;
        _add.IsEnabled = notes.Length < ScratchpadDocument.MaximumNotes;
        _add.ToolTip = _add.IsEnabled ? "New note" : "12-note limit";
        LayoutTabs();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (double.IsFinite(constraint.Width) && constraint.Width != _width)
        {
            _width = constraint.Width;
            LayoutTabs();
        }
        return base.MeasureOverride(constraint);
    }

    private void LayoutTabs(bool restoreFocus = true)
    {
        if (_notes.Length == 0) return;
        bool focused = restoreFocus && _tabs.IsKeyboardFocusWithin && _rename is null;
        _updating = true;
        try
        {
            var padding = _widget ? 20d : 24d;
            var available = Math.Max(0, _width - _add.Width);
            var natural = _notes.ToDictionary(n => n.Id, n => Math.Ceiling(TextWidth(n.Name, n.Id == _selectedId)) + padding + 1);
            double WidthOf((long Id, string Name) note)
            {
                bool active = note.Id == _selectedId;
                double cap = _widget ? (active ? 204 : 114) : (active ? 264 : 154);
                double width = Math.Min(cap, natural[note.Id]);
                return _rename is not null && active ? Math.Max(width, 132) : width;
            }
            var widths = _notes.ToDictionary(n => n.Id, WidthOf);
            bool overflow = widths.Values.Sum() > available;
            if (overflow) available = Math.Max(0, available - _more.Width);
            widths[_selectedId] = Math.Min(widths[_selectedId], available);
            var remaining = available - widths[_selectedId];
            var visible = new HashSet<long> { _selectedId };
            foreach (var note in _notes)
            {
                if (note.Id == _selectedId) continue;
                if (widths[note.Id] > remaining) break;
                visible.Add(note.Id);
                remaining -= widths[note.Id];
            }
            // Spend the gap before the picker on complete titles, starting with the active note.
            foreach (var note in _notes.Where(n => visible.Contains(n.Id)).OrderByDescending(n => n.Id == _selectedId))
            {
                double extra = Math.Min(remaining, Math.Max(0, natural[note.Id] - widths[note.Id]));
                widths[note.Id] += extra;
                remaining -= extra;
            }
            _more.Visibility = visible.Count < _notes.Length ? Visibility.Visible : Visibility.Collapsed;
            _count.Text = "+" + (_notes.Length - visible.Count);
            foreach (var tab in _tabs.Items.Cast<ListBoxItem>().Where(t => !visible.Contains((long)t.Tag)).ToArray())
                _tabs.Items.Remove(tab);

            int index = 0;
            foreach (var note in _notes.Where(n => visible.Contains(n.Id)))
            {
                var tab = _tabs.Items.Cast<ListBoxItem>().FirstOrDefault(t => (long)t.Tag == note.Id);
                if (tab is null)
                {
                    tab = new ListBoxItem { Tag = note.Id };
                    _tabs.Items.Insert(index, tab);
                }
                tab.Width = widths[note.Id];
                tab.ToolTip = note.Name;
                AutomationProperties.SetName(tab, note.Name);
                if (_rename is not null && note.Id == _renameId)
                {
                    _rename.Width = Math.Max(0, tab.Width - padding);
                    tab.Content = _rename;
                }
                else
                    tab.Content = FitTitle(note.Name, Math.Max(0, tab.Width - padding), note.Id == _selectedId);
                if (note.Id == _selectedId) _tabs.SelectedItem = tab;
                index++;
            }
        }
        finally { _updating = false; }
        if (focused) FocusSelected();
    }

    private double TextWidth(string text, bool active)
    {
        var label = new TextBlock
        {
            Text = text, FontFamily = _tabs.FontFamily, FontSize = _tabs.FontSize,
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal
        };
        label.Measure(new Size(double.PositiveInfinity, Height));
        return label.DesiredSize.Width;
    }

    // Keep both ends of a long title. Related notes often differ only in their last word.
    private string FitTitle(string name, double available, bool active)
    {
        if (TextWidth(name, active) <= available) return name;
        // A grouped name often reads “Project review · kickoff”. Keep its distinguishing word whole.
        int separator = name.LastIndexOf(" · ", StringComparison.Ordinal);
        if (separator > 0)
        {
            string suffix = name[(separator + 3)..];
            string firstWord = name[..separator].Split(' ', 2)[0];
            string grouped = firstWord + "… " + suffix;
            if (TextWidth(grouped, active) <= available) return grouped;
            if (TextWidth("… " + suffix, active) <= available) return "… " + suffix;
        }
        var elements = StringInfo.ParseCombiningCharacters(name);
        int low = 0, high = elements.Length - 1;
        string best = "…";
        while (low <= high)
        {
            int keep = (low + high) / 2;
            int leading = keep / 3, trailing = keep - leading;
            string candidate = name[..elements[leading]].TrimEnd() + "…" +
                (trailing == 0 ? "" : name[elements[^trailing]..].TrimStart());
            if (TextWidth(candidate, active) <= available) { best = candidate; low = keep + 1; }
            else high = keep - 1;
        }
        return best;
    }

    private void OnTabKey(object sender, KeyEventArgs e)
    {
        if (_rename is not null) return;
        if ((e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift)) &&
            _tabs.SelectedItem is ListBoxItem tab)
        {
            ContextRequested?.Invoke(tab, _selectedId);
            e.Handled = true;
        }
        if (Keyboard.Modifiers != ModifierKeys.None || _notes.Length == 0) return;
        int index = Array.FindIndex(_notes, n => n.Id == _selectedId);
        switch (e.Key)
        {
            case Key.Left: index = (index + _notes.Length - 1) % _notes.Length; break;
            case Key.Right: index = (index + 1) % _notes.Length; break;
            case Key.Home: index = 0; break;
            case Key.End: index = _notes.Length - 1; break;
            default: return;
        }
        Selected?.Invoke(_notes[index].Id);
        e.Handled = true;
    }

    private void FocusSelected()
    {
        if (_tabs.SelectedItem is ListBoxItem tab) tab.Focus();
    }

    public void BeginRename()
    {
        if (_rename is not null || _tabs.SelectedItem is not ListBoxItem tab) return;
        var previousFocus = Keyboard.FocusedElement;
        _renameId = (long)tab.Tag;
        _renameHold = _hold?.Invoke();
        var rename = _rename = new TextBox
        {
            Text = _notes.Single(n => n.Id == _renameId).Name, MaxLength = ScratchpadDocument.MaximumNameLength,
            FontSize = _tabs.FontSize, FontFamily = _tabs.FontFamily, VerticalContentAlignment = VerticalAlignment.Center
        };
        rename.SetResourceReference(StyleProperty, "ScratchpadNameStyle");
        rename.SetResourceReference(Control.ForegroundProperty, _widget ? "IslandTextBrush" : "TextPrimaryBrush");
        rename.SetResourceReference(Control.BackgroundProperty, _widget ? "IslandPanelBrush" : "InputFocusBrush");
        rename.SetResourceReference(Control.BorderBrushProperty, _widget ? "IslandAccentBrush" : "AccentBrush");
        rename.SetResourceReference(TextBoxBase.CaretBrushProperty, _widget ? "IslandTextBrush" : "TextPrimaryBrush");
        rename.SetResourceReference(TextBoxBase.SelectionBrushProperty, _widget ? "IslandAccentBrush" : "AccentBrush");
        AutomationProperties.SetName(rename, "Name");
        rename.LostKeyboardFocus += (_, _) => EndRename(true);
        rename.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;
            EndRename(e.Key == Key.Enter);
            FocusSelected();
            e.Handled = true;
        };
        LayoutTabs();
        rename.SelectAll();
        if (!IsLoaded) return;
        // A popup's closing HWND returns focus to its host after the action fires.
        // Let the tab realize the field and the popup finish that handoff before taking focus.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_rename != rename) return;
            var host = Window.GetWindow(this);
            var focused = Keyboard.FocusedElement;
            if (host?.IsActive == true && rename.IsVisible &&
                (focused is null || focused == host || focused == previousFocus || focused == tab || focused == rename))
            {
                rename.Focus();
                rename.SelectAll();
            }
            else EndRename(false);
        });
    }

    public void EndRename(bool save)
    {
        if (_rename is not { } rename) return;
        _rename = null;
        _renameHold?.Dispose();
        _renameHold = null;
        var id = _renameId;
        LayoutTabs(restoreFocus: false);
        if (save && !string.IsNullOrWhiteSpace(rename.Text)) RenameRequested?.Invoke(id, rename.Text.Trim());
    }

    public bool HandleEscape()
    {
        if (_rename is not null) { EndRename(false); FocusSelected(); return true; }
        if (_picker?.IsOpen == true) { _picker.IsOpen = false; _more.Focus(); return true; }
        return false;
    }

    public Popup BuildNotesMenu()
    {
        var stack = new StackPanel();
        var scroll = new ScrollViewer { Content = stack, Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 384 };
        var surface = new Border
        {
            Child = scroll, MinWidth = 184, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(4)
        };
        KeyboardNavigation.SetTabNavigation(surface, KeyboardNavigationMode.Cycle);
        surface.SetResourceReference(Border.BackgroundProperty, _widget ? "IslandBrush" : "FlyoutBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, _widget ? "IslandBorderBrush" : "BorderBrush");
        surface.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        var popup = new Popup { Child = surface, PlacementTarget = _more, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        Button? current = null;
        foreach (var note in _notes)
        {
            bool active = note.Id == _selectedId;
            var label = new DockPanel();
            var check = Glyph(LucideIcons.Check, 14);
            check.SetResourceReference(LucideIcon.BrushProperty, _widget ? "IslandAccentBrush" : "AccentBrush");
            check.Margin = new Thickness(12, 0, 0, 0);
            check.Visibility = active ? Visibility.Visible : Visibility.Hidden;
            DockPanel.SetDock(check, Dock.Right);
            label.Children.Add(check);
            label.Children.Add(new TextBlock { Text = note.Name, VerticalAlignment = VerticalAlignment.Center });
            var row = new Button { Content = label, Focusable = true, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            row.SetResourceReference(StyleProperty, _widget ? "IslandMenuItemStyle" : "FlyoutRowButtonStyle");
            row.SetResourceReference(FocusVisualStyleProperty, _widget ? "IslandFocusVisual" : "FocusRing4");
            AutomationProperties.SetName(row, note.Name);
            if (active) { current = row; AutomationProperties.SetItemStatus(row, "Selected"); }
            row.Click += (_, _) =>
            {
                bool returnFocus = popup.IsOpen && surface.IsKeyboardFocusWithin;
                popup.IsOpen = false;
                if (returnFocus) _more.Focus();
                Selected?.Invoke(note.Id);
            };
            stack.Children.Add(row);
        }
        surface.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { popup.IsOpen = false; _more.Focus(); e.Handled = true; }
            if (e.Key is not (Key.Up or Key.Down or Key.Home or Key.End)) return;
            var rows = stack.Children.OfType<Button>().ToList();
            if (rows.Count == 0) return;
            int index = e.Key switch
            {
                Key.Home => 0,
                Key.End => rows.Count - 1,
                _ => (rows.FindIndex(r => r.IsKeyboardFocusWithin) + (e.Key == Key.Up ? rows.Count - 1 : 1)) % rows.Count
            };
            rows[index].Focus();
            rows[index].BringIntoView();
            e.Handled = true;
        };
        IDisposable? hold = null;
        popup.Opened += (_, _) =>
        {
            hold = _hold?.Invoke();
            current?.Focus();
            current?.BringIntoView();
        };
        popup.Closed += (_, _) => { hold?.Dispose(); hold = null; };
        return popup;
    }
}
