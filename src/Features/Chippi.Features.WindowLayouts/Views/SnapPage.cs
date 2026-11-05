// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Core.Diagnostics;
using Chippi.UI.Controls;

namespace Chippi.Features.WindowLayouts.Views;

/// <summary>
/// Settings › Snap (docs/phase-6/design/wave-3.html, boards snap and sheet-snap-editor): Shift+drag zones with a layout per
/// display and its Excluded apps, the Layouts list in the Snap menu's order, and the gaps. A layout's editor opens in its
/// row's place, or above the list's last row for a new one: its name, the zones on a canvas at the main display's shape, and
/// the selected zone's name, splits, size and Remove.
/// </summary>
internal sealed class SnapPage : UserControl
{
    /// <summary>The Snap menu shows this many layouts, the first in the list (notes.md, wave 3).</summary>
    public const int InMenu = 6;
    public const string SavingFailed = "Saving failed", NameTakenMessage = "Another layout already uses this name";
    private const double FieldWidth = 340, SideWidth = 280, Dim = 0.45;

    /// <summary>A layout being edited: a copy, so Cancel leaves the saved one as it was.</summary>
    internal sealed class Draft
    {
        public string? Id;
        public string Name = "";
        public ZoneNode Root = new() { Name = "Zone 1" };
        public string Selected = "";
        public string ZoneName = "";
    }

    private readonly WindowLayoutsModule _module;
    private readonly StackPanel _page = new();
    private readonly Border _shiftHost = new();
    private readonly ExcludedAppsCard _excluded;
    private readonly Card _layouts = new();
    private readonly Border _gapsHost = new();
    private readonly TextBlock _failed;
    private Draft? _draft;
    private FrameworkElement? _editor;
    private ZoneCanvas? _canvas;
    private string? _focusHandle;
    private bool _refreshing;

    public SnapPage(WindowLayoutsModule module)
    {
        _module = module;
        _excluded = new ExcludedAppsCard(() => Settings.ExcludedApps, apps => Save(s => s.Configure(Settings.ShiftDragEnabled, Settings.WindowGap, Settings.ScreenEdgeGap, apps)))
        { Margin = new Thickness(0, 8, 0, 0) };
        _failed = Styled(new TextBlock { Text = SavingFailed, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) }, "CaptionTextStyle");
        _failed.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_failed, AutomationLiveSetting.Polite);
        _page.Children.Add(SettingsLayout.Section("Shift+drag", first: true));
        _page.Children.Add(_shiftHost);
        _page.Children.Add(_excluded);
        _page.Children.Add(SettingsLayout.Section("Layouts"));
        _page.Children.Add(_layouts);
        _page.Children.Add(SettingsLayout.Section("Gaps"));
        _page.Children.Add(_gapsHost);
        _page.Children.Add(_failed);
        Content = _page;
        void Changed() => Dispatcher.BeginInvoke(Refresh);
        Loaded += (_, _) => { _module.Changed += Changed; _module.DisplaysChanged += Changed; Refresh(); };
        Unloaded += (_, _) => { _module.Changed -= Changed; _module.DisplaysChanged -= Changed; };
        Refresh();
    }

    private SnapLayoutStore? Store => _module.Layouts;

    private WindowLayoutsSettings Settings => Store?.Settings ?? new WindowLayoutsSettings();

    /// <summary>The displays, the main one first.</summary>
    private IReadOnlyList<SnapDisplay> Displays => [.. _module.Displays.OrderByDescending(d => d.IsMain)];

    private bool Save(Action<SnapLayoutStore> change)
    {
        if (Store is not { } store)
            return false;
        try
        {
            change(store);
            _failed.Visibility = Visibility.Collapsed;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warn("Snap: saving failed", ex);
            _failed.Visibility = Visibility.Visible;
            return false;
        }
    }

    private void Refresh()
    {
        if (_canvas?.Dragging == true)
            return;
        _refreshing = true;
        _shiftHost.Child = ShiftCard();
        _gapsHost.Child = GapsCard();
        _excluded.Render();
        _refreshing = false;
        RenderLayouts();
    }

    // ---- Shift+drag ----

    private Card ShiftCard()
    {
        var s = Settings;
        var card = new Card();
        var toggle = Switch(s.ShiftDragEnabled);
        OnSwitched(toggle, on => { if (!Save(x => x.Configure(on, Settings.WindowGap, Settings.ScreenEdgeGap, Settings.ExcludedApps))) Refresh(); });
        card.Children.Add(new SettingRow { Header = "Shift+drag zones", Icon = LucideIcons.LayoutPanelLeft, Content = toggle });
        foreach (var display in Displays)
        {
            var combo = SettingsLayout.Combo(s.Layouts.Select(l => (object)l.Name));
            combo.Width = 200;
            AutomationProperties.SetName(combo, $"Layout on {display.Name}");
            string current = Store?.ForDisplay(display.Id).Id ?? "halves";
            combo.SelectedIndex = Math.Max(0, s.Layouts.FindIndex(l => l.Id == current));
            combo.SelectionChanged += (_, _) =>
            {
                if (_refreshing || combo.SelectedIndex < 0) return;
                string id = Settings.Layouts[combo.SelectedIndex].Id;
                Save(x => x.AssignDisplay(display.Id, id));
            };
            string size = $"{display.Bounds.Width} × {display.Bounds.Height}";
            var name = Styled(new TextBlock { Text = display.Name, TextTrimming = TextTrimming.CharacterEllipsis }, "BodyTextStyle");
            var caption = Styled(new TextBlock { Text = display.IsMain ? $"Main display · {size}" : size }, "CaptionTextStyle");
            caption.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var label = new StackPanel { Children = { name, caption }, Opacity = s.ShiftDragEnabled ? 1 : Dim };
            card.Children.Add(Line(label, combo, SettingsLayout.DependentPadding, SettingsLayout.CaptionRowHeight));
        }
        return card;
    }

    // ---- Layouts ----

    private void RenderLayouts()
    {
        if (_editor?.Parent is Panel old)
            old.Children.Remove(_editor);
        _layouts.Children.Clear();
        var layouts = Settings.Layouts;
        for (int i = 0; i < layouts.Count; i++)
            _layouts.Children.Add(_draft?.Id == layouts[i].Id ? _editor! : RowView(layouts[i], i, layouts.Count));
        if (_draft is { Id: null })
            _layouts.Children.Add(_editor!);
        var add = SettingsLayout.AddButton("layout", () => StartEdit(new Draft()));
        var count = Styled(new TextBlock { Text = layouts.Count == 1 ? "1 layout" : $"{layouts.Count} layouts", VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle");
        count.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _layouts.Children.Add(Line(count, add, SettingsLayout.RowPadding, 64));
        if (_focusHandle is { } focus)
        {
            _focusHandle = null;
            Dispatcher.BeginInvoke(() => Descendants<DragHandle>(_layouts).FirstOrDefault(h => (string?)h.Tag == focus)?.Focus());
        }
    }

    /// <summary>A built-in layout is "Built in"; one of the owner's says its zones. Past the sixth, it isn't in the Snap menu.</summary>
    internal static string Caption(SnapLayout layout, int index)
    {
        int zones = layout.Root.Flatten().Zones.Count;
        string what = layout.BuiltIn ? "Built in" : zones == 1 ? "1 zone" : $"{zones} zones";
        return index >= InMenu ? what + " · not in the Snap menu" : what;
    }

    private FrameworkElement RowView(SnapLayout layout, int index, int count)
    {
        var handle = new DragHandle { Tag = layout.Id, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        AutomationProperties.SetName(handle, $"Move {layout.Name}");
        AutomationProperties.SetHelpText(handle, $"Position {index + 1} of {count}");
        handle.MoveRequested += (_, step) => Move(layout.Id, index + step);
        double dragged = 0;
        handle.DragStarted += (_, _) => dragged = 0;
        handle.DragDelta += (_, e) => dragged += e.VerticalChange;
        handle.DragCompleted += (_, _) =>
        {
            int to = index + (int)Math.Round(dragged / (SettingsLayout.CaptionRowHeight + 1));
            if (to != index) Move(layout.Id, to);
        };
        var picture = new LayoutPicture(layout.Root) { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = Styled(new TextBlock { Text = layout.Name, TextTrimming = TextTrimming.CharacterEllipsis }, "BodyTextStyle");
        var caption = Styled(new TextBlock { Text = Caption(layout, index), TextTrimming = TextTrimming.CharacterEllipsis }, "CaptionTextStyle");
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, caption } };
        var more = new IconButton { Icon = LucideIcons.Ellipsis, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(more, $"{layout.Name} options");
        more.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Bottom };
            if (!layout.BuiltIn)
                menu.Items.Add(Item("Edit", () => StartEdit(From(layout))));
            menu.Items.Add(Item("Duplicate", () => Save(s => s.Duplicate(layout.Id))));
            if (!layout.BuiltIn)
                menu.Items.Add(Item("Delete", () => Confirm(more, () => Delete(layout.Id))));
            menu.IsOpen = true;
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(picture, 1);
        Grid.SetColumn(text, 2);
        Grid.SetColumn(more, 3);
        grid.Children.Add(handle);
        grid.Children.Add(picture);
        grid.Children.Add(text);
        grid.Children.Add(more);
        var row = new Border { MinHeight = SettingsLayout.CaptionRowHeight, Padding = new Thickness(12, 8, 16, 8), Child = grid, Background = Brushes.Transparent };
        if (!layout.BuiltIn)
            row.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && Inside<ButtonBase>(source) || e.OriginalSource is DependencyObject s2 && Inside<Thumb>(s2))
                    return;
                StartEdit(From(layout));
            };
        return row;

        static MenuItem Item(string header, Action click)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => click();
            return item;
        }
    }

    private void Move(string id, int to)
    {
        int last = Settings.Layouts.Count - 1;
        to = Math.Clamp(to, 0, last);
        _focusHandle = id;
        Save(s => s.Move(id, to));
    }

    private void Delete(string id)
    {
        if (!Save(s => s.Delete(id)))
            return;
        if (_draft?.Id == id)
            CloseEditor();
    }

    // ---- the editor ----

    internal static Draft From(SnapLayout layout)
    {
        var first = layout.Root.Flatten().Zones[0];
        return new() { Id = layout.Id, Name = layout.Name, Root = layout.Root, Selected = first.Id, ZoneName = first.Name };
    }

    internal void StartEdit(Draft draft)
    {
        if (draft.Selected.Length == 0)
        {
            var first = draft.Root.Flatten().Zones[0];
            draft.Selected = first.Id;
            draft.ZoneName = first.Name;
        }
        _draft = draft;
        _editor = BuildEditor(draft);
        RenderLayouts();
    }

    private void CloseEditor()
    {
        _draft = null;
        _editor = null;
        _canvas = null;
        RenderLayouts();
    }

    /// <summary>The display the canvas takes its shape from, and the size readout is for: the main one.</summary>
    private SnapDisplay? MainDisplay => Displays.FirstOrDefault();

    private FrameworkElement BuildEditor(Draft draft)
    {
        var view = new StackPanel();
        view.SetResourceReference(Panel.BackgroundProperty, "HoverBrush");

        var icon = Glyph(draft.Id is null ? LucideIcons.Plus : LucideIcons.Pencil, "TextPrimaryBrush");
        var title = Styled(new TextBlock { Text = draft.Id is null ? "New layout" : "Edit layout", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) }, "BodyTextStyle");
        Button? delete = null;
        if (draft.Id is { } id)
        {
            delete = Button("Delete");
            delete.Margin = new Thickness(0);
            delete.Click += (_, _) => Confirm(delete, () => Delete(id));
        }
        view.Children.Add(Line(new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, title } }, delete, SettingsLayout.RowPadding));

        var name = Field(draft.Name, "Name");
        name.Width = FieldWidth;
        name.MaxLength = 80;
        var nameFrame = new Border { CornerRadius = new CornerRadius(5), Child = name };
        var nameLabel = Styled(new TextBlock { Text = "Name", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(36, 0, 0, 0) }, "BodyTextStyle");
        view.Children.Add(Line(nameLabel, nameFrame, SettingsLayout.RowPadding));

        // The canvas at the main display's shape, beside the selected zone's settings.
        var display = MainDisplay;
        double aspect = display is { } d && d.Bounds.Height > 0 ? (double)d.Bounds.Width / d.Bounds.Height : 16.0 / 9;
        var canvas = new ZoneCanvas();
        _canvas = canvas;
        AutomationProperties.SetName(canvas, "Zones");
        var canvasFrame = new Border { Padding = new Thickness(6), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = canvas, VerticalAlignment = VerticalAlignment.Top };
        canvasFrame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        canvasFrame.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");

        var zoneName = Field(draft.ZoneName, "Zone name");
        zoneName.MaxLength = 80;
        var sideBySide = Button("Side by side", icon: LucideIcons.Columns);
        sideBySide.Margin = new Thickness(0);
        var stacked = Button("Stacked", icon: LucideIcons.Rows);
        var size = Styled(new TextBlock { TextWrapping = TextWrapping.Wrap }, "CaptionTextStyle");
        size.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var remove = Button("Remove");
        remove.Margin = new Thickness(0);
        remove.HorizontalAlignment = HorizontalAlignment.Left;
        var side = new StackPanel { Width = SideWidth, Margin = new Thickness(20, 0, 0, 0) };
        side.Children.Add(Label("Zone name"));
        side.Children.Add(zoneName);
        side.Children.Add(Label("Split", top: 16));
        side.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { sideBySide, stacked } });
        if (display is not null)
        {
            side.Children.Add(Label("Size", top: 16));
            side.Children.Add(size);
        }
        side.Children.Add(new Border { Height = 16 });
        side.Children.Add(remove);
        var body = new Grid { Margin = new Thickness(16, 8, 16, 12) };
        body.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 240 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(side, 1);
        body.Children.Add(canvasFrame);
        body.Children.Add(side);
        var bodyBorder = new Border { Child = body };
        view.Children.Add(bodyBorder);

        var message = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var save = Button("Save", primary: true);
        save.Margin = new Thickness(0);
        var cancel = Button("Cancel");
        cancel.Click += (_, _) => CloseEditor();
        var footer = new DockPanel { Margin = SettingsLayout.RowPadding };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { save, cancel } };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(message);
        view.Children.Add(footer);
        view.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && e.OriginalSource is not ComboBox { IsDropDownOpen: true })
            {
                CloseEditor();
                e.Handled = true;
            }
        };

        void Draw()
        {
            canvas.Show(draft.Root, draft.Selected, aspect);
            var zones = draft.Root.Flatten().Zones;
            remove.IsEnabled = zones.Count > 1;
            if (display is not null && zones.FirstOrDefault(z => z.Id == draft.Selected) is { } zone)
            {
                var r = SnapGeometry.PlaceWithGaps(display.WorkArea, zone, Settings.WindowGap, Settings.ScreenEdgeGap);
                size.Text = $"{r.Width} × {r.Height} on {display.Name}";
            }
        }
        void Validate()
        {
            string trimmed = draft.Name.Trim();
            string? error = trimmed.Length > 0 && Store?.NameTaken(trimmed, draft.Id) == true ? NameTakenMessage : null;
            nameFrame.BorderThickness = new Thickness(error is null ? 0 : 2);
            nameFrame.Margin = new Thickness(error is null ? 0 : -2);
            nameFrame.SetResourceReference(Border.BorderBrushProperty, "WarningTextBrush");
            Show(message, error);
            save.IsEnabled = error is null && trimmed.Length > 0 && draft.ZoneName.Trim().Length > 0;
        }
        void Select(string zoneId)
        {
            draft.Selected = zoneId;
            draft.ZoneName = draft.Root.Flatten().Zones.First(z => z.Id == zoneId).Name;
            zoneName.Text = draft.ZoneName;
            Draw();
            Validate();
        }
        name.TextChanged += (_, _) => { draft.Name = name.Text; Validate(); };
        zoneName.TextChanged += (_, _) =>
        {
            draft.ZoneName = zoneName.Text;
            if (zoneName.Text.Trim().Length > 0 && draft.Root.Flatten().Zones.Any(z => z.Id == draft.Selected))
            {
                draft.Root = draft.Root.Rename(draft.Selected, zoneName.Text);
                Draw();
            }
            Validate();
        };
        void Split(SplitDirection direction)
        {
            var before = draft.Root.Flatten().Zones.Select(z => z.Id).ToHashSet();
            var next = draft.Root.Divide(draft.Selected, direction);
            var zones = next.Flatten().Zones;
            var added = zones.First(z => !before.Contains(z.Id));
            draft.Root = next.Rename(added.Id, NextName(zones));
            Select(added.Id);
        }
        sideBySide.Click += (_, _) => Split(SplitDirection.SideBySide);
        stacked.Click += (_, _) => Split(SplitDirection.Stacked);
        remove.Click += (_, _) =>
        {
            if (draft.Root.Flatten().Zones.Count < 2) return;
            draft.Root = draft.Root.Remove(draft.Selected);
            Select(draft.Root.Flatten().Zones[0].Id);
        };
        canvas.ZoneSelected += Select;
        canvas.DividerMoved += (dividerId, ratio) =>
        {
            draft.Root = draft.Root.Resize(dividerId, ratio);
            if (canvas.Dragging)
                canvas.Resize(draft.Root);
            else
                Draw();
            if (display is not null && draft.Root.Flatten().Zones.FirstOrDefault(z => z.Id == draft.Selected) is { } zone)
            {
                var r = SnapGeometry.PlaceWithGaps(display.WorkArea, zone, Settings.WindowGap, Settings.ScreenEdgeGap);
                size.Text = $"{r.Width} × {r.Height} on {display.Name}";
            }
        };
        save.Click += (_, _) =>
        {
            Validate();
            if (!save.IsEnabled) return;
            var layout = new SnapLayout(draft.Id ?? Guid.NewGuid().ToString("N"), draft.Name.Trim(), draft.Root);
            // Written first: when it fails, the draft stays open with the reason by Save.
            if (!Save(s => s.Save(layout)))
            {
                _failed.Visibility = Visibility.Collapsed;
                Show(message, SavingFailed);
                return;
            }
            CloseEditor();
        };
        Draw();
        Validate();
        return view;
    }

    /// <summary>"Zone 2", "Zone 3": the first number no zone of this layout has.</summary>
    internal static string NextName(IReadOnlyList<SnapZone> zones)
    {
        for (int n = zones.Count; ; n++)
            if (!zones.Any(z => string.Equals(z.Name, $"Zone {n}", StringComparison.CurrentCultureIgnoreCase)))
                return $"Zone {n}";
    }

    private static void Show(StackPanel message, string? text)
    {
        message.Children.Clear();
        message.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        if (text is null) return;
        var glyph = Glyph(LucideIcons.TriangleAlert, "WarningTextBrush");
        glyph.Margin = new Thickness(0, 0, 8, 0);
        var label = Styled(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }, "BodyTextStyle");
        label.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(label, AutomationLiveSetting.Polite);
        message.Children.Add(glyph);
        message.Children.Add(label);
    }

    // ---- Gaps ----

    private Card GapsCard()
    {
        var s = Settings;
        return SettingsLayout.Card(
            GapRow("Window gap", s.WindowGap, v => Save(x => x.Configure(Settings.ShiftDragEnabled, v, Settings.ScreenEdgeGap, Settings.ExcludedApps))),
            GapRow("Screen edge gap", s.ScreenEdgeGap, v => Save(x => x.Configure(Settings.ShiftDragEnabled, Settings.WindowGap, v, Settings.ExcludedApps))));
    }

    private Border GapRow(string title, int value, Func<int, bool> changed)
    {
        var slider = Styled(new Slider { Minimum = 0, Maximum = 24, TickFrequency = 2, IsSnapToTickEnabled = true, SmallChange = 2, LargeChange = 4, Value = value, Width = 180, VerticalAlignment = VerticalAlignment.Center }, "HopperSliderStyle");
        AutomationProperties.SetName(slider, title);
        var readout = Styled(new TextBlock { Text = $"{value} px", Width = 48, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center }, "NumberTextStyle");
        slider.ValueChanged += (_, e) =>
        {
            readout.Text = $"{(int)e.NewValue} px";
            if (!_refreshing) changed((int)e.NewValue);
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { slider, readout } };
        var label = Styled(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle");
        return Line(label, right, SettingsLayout.RowPadding);
    }

    // ---- shared pieces ----

    /// <summary>"Delete this layout?" under the button, with Cancel and Delete (Critical).</summary>
    private static void Confirm(FrameworkElement anchor, Action confirmed)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None };
        popup.Child = ConfirmPanel(() => { popup.IsOpen = false; anchor.Focus(); }, () => { popup.IsOpen = false; confirmed(); }, out var first);
        popup.Opened += (_, _) => first.Focus();
        popup.IsOpen = true;
    }

    internal static Border ConfirmPanel(Action cancelled, Action confirmed, out Button first)
    {
        var cancel = Button("Cancel");
        cancel.Margin = new Thickness(0);
        cancel.Click += (_, _) => cancelled();
        var act = Styled(new Button { Content = "Delete", Margin = new Thickness(8, 0, 0, 0) }, "CriticalButtonStyle");
        act.Click += (_, _) => confirmed();
        var question = Styled(new TextBlock { Text = "Delete this layout?", FontWeight = FontWeights.SemiBold }, "BodyTextStyle");
        var body = new StackPanel { Margin = new Thickness(16), Children = { question, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { cancel, act } } } };
        KeyboardNavigation.SetTabNavigation(body, KeyboardNavigationMode.Cycle);
        body.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            cancelled();
            e.Handled = true;
        };
        first = cancel;
        var panel = new Border { MinWidth = 260, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = body };
        panel.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        panel.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        return panel;
    }

    private static TextBlock Label(string text, double top = 0)
    {
        var label = Styled(new TextBlock { Text = text, Margin = new Thickness(0, top, 0, 6) }, "BodyTextStyle");
        return label;
    }

    private static Border Line(FrameworkElement left, FrameworkElement? right, Thickness padding, double minHeight = SettingsLayout.RowHeight)
    {
        var dock = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        if (right is not null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);
        }
        left.VerticalAlignment = VerticalAlignment.Center;
        dock.Children.Add(left);
        return new Border { MinHeight = minHeight, Padding = padding, Child = dock };
    }

    private static TextBox Field(string text, string placeholder)
    {
        var box = Styled(new TextBox { Text = text, Tag = placeholder, VerticalContentAlignment = VerticalAlignment.Center }, "TextFieldStyle");
        AutomationProperties.SetName(box, placeholder);
        return box;
    }

    private static ToggleButton Switch(bool on) =>
        Styled(new ToggleButton { IsChecked = on, Content = on ? "On" : "Off", VerticalAlignment = VerticalAlignment.Center }, "SwitchWithLabelStyle");

    /// <summary>A UI Automation toggle raises Checked and Unchecked, not Click.</summary>
    private static void OnSwitched(ToggleButton toggle, Action<bool> changed)
    {
        void Handler(object sender, RoutedEventArgs e)
        {
            toggle.Content = toggle.IsChecked == true ? "On" : "Off";
            changed(toggle.IsChecked == true);
        }
        toggle.Checked += Handler;
        toggle.Unchecked += Handler;
    }

    private static Button Button(string text, bool primary = false, Geometry? icon = null)
    {
        object content = text;
        if (icon is not null)
        {
            var glyph = Glyph(icon, "TextPrimaryBrush");
            glyph.Margin = new Thickness(0, 0, 8, 0);
            content = new StackPanel { Orientation = Orientation.Horizontal, Children = { glyph, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } } };
        }
        var button = Styled(new Button { Content = content, Margin = new Thickness(8, 0, 0, 0) }, primary ? "PrimaryButtonStyle" : "SecondaryButtonStyle");
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static LucideIcon Glyph(Geometry data, string brush)
    {
        var icon = new LucideIcon { Data = data, Size = 16, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(LucideIcon.BrushProperty, brush);
        return icon;
    }

    private static bool Inside<T>(DependencyObject source) where T : DependencyObject
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node))
            if (node is T) return true;
        return false;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T item) yield return item;
            foreach (var found in Descendants<T>(child)) yield return found;
        }
    }

    internal static T Styled<T>(T element, string style) where T : FrameworkElement
    {
        element.SetResourceReference(StyleProperty, style);
        return element;
    }
}
