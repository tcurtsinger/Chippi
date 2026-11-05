// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Chippi.Core.Shortcuts;
using Chippi.UI.Shortcuts;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.RadialMenu.Input;
using Chippi.Features.RadialMenu.Slices;
using Chippi.Features.RadialMenu.Wheel;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Actions;
using Chippi.UI.Controls;
using MouseButton = Chippi.Features.RadialMenu.Input.MouseButton;

namespace Chippi.Features.RadialMenu.Views;

/// <summary>
/// Settings › Radial menu (docs/phase-5.5/radial-menu.md): Opening, a switch per button and the shortcut; Wheel, the
/// wheel drawn at its real size beside the selected slice's fields. A submenu's Open steps into its wheel.
/// </summary>
public sealed class RadialMenuSettingsPage : UserControl
{
    private readonly RadialMenuModule _module;
    private readonly ToggleButton _middle = Ui.Switch("Hold middle button"), _back = Ui.Switch("Hold Back button"), _forward = Ui.Switch("Hold Forward button");
    private readonly ToggleButton _overFullscreen = Ui.Switch("Open over fullscreen apps");
    private readonly ContentControl _shortcut = new() { VerticalAlignment = VerticalAlignment.Center, Focusable = false };
    private readonly WheelEditor _editor;
    private readonly Button _up = new() { Width = 32, Height = 32, Focusable = true };
    private readonly TextBlock _where = Ui.Text("Wheel");
    private readonly ContentControl _fields = new() { Focusable = false };
    private readonly List<int> _path = []; // the submenu slices stepped into, from the first wheel
    private int? _selected;
    private bool _updating, _capturingShortcut;
    private readonly ActionPicker _picker;

    public RadialMenuSettingsPage(RadialMenuModule module)
    {
        _module = module;
        _editor = new WheelEditor(module.ChippiColor) { Margin = new Thickness(0, 12, 0, 8), HorizontalAlignment = HorizontalAlignment.Center };
        _editor.Selected += i => Select(i);
        _editor.Swapped += Swap;
        _editor.Added += Add;
        _editor.WentBack += Up;
        _picker = new ActionPicker(new ActionPickerSource
        {
            Apps = () => [.. (module.Pc?.Apps ?? []).Select(a => (a.ParsingName, a.Name))],
            Commands = () => module.Runner?.ChippiCommands() ?? [],
            SettingsPages = [.. SettingsPages.All.Select(p => (p.Uri, p.Name))],
            Recorder = module.Recorder,
            Icon = action => module.IconOf(Slice.From(action)),
        })
        {
            ExtraRows = _ => SubmenuRows(),
            Footer = RemoveRow,
        };
        _picker.Changed += Picked;
        Hook(_middle, MouseButton.Middle);
        Hook(_back, MouseButton.Back);
        Hook(_forward, MouseButton.Forward);
        _overFullscreen.Checked += (_, _) => { if (!_updating) _module.SetOpenOverFullscreen(true); };
        _overFullscreen.Unchecked += (_, _) => { if (!_updating) _module.SetOpenOverFullscreen(false); };
        // Last: it applies to every trigger above it (docs/phase-6/design/notes.md, wave 1 #9).
        var opening = SettingsLayout.Card(
            SettingsLayout.Row("Hold middle button", _middle),
            SettingsLayout.Row("Hold Back button", _back),
            SettingsLayout.Row("Hold Forward button", _forward),
            SettingsLayout.Row("Shortcut", _shortcut),
            SettingsLayout.Row("Open over fullscreen apps", _overFullscreen));

        var chevron = new LucideIcon { Data = LucideIcons.ChevronLeft, Size = 16 };
        chevron.SetResourceReference(LucideIcon.BrushProperty, "TextPrimaryBrush");
        _up.Content = chevron;
        _up.SetResourceReference(StyleProperty, "SubtleButtonStyle");
        AutomationProperties.SetName(_up, "Back");
        _up.Click += (_, _) => Up();
        var where = new StackPanel { Orientation = Orientation.Horizontal, Height = 32, Children = { _up, _where } };
        _where.Margin = new Thickness(4, 0, 0, 0);
        var left = new StackPanel { Width = 320, Margin = new Thickness(16, 12, 16, 8), Children = { where, _editor } };
        var divider = new Border { BorderThickness = new Thickness(1, 0, 0, 0), Child = _fields, MinHeight = 332 };
        divider.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var wheel = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(352) }, new ColumnDefinition() } };
        Grid.SetColumn(divider, 1);
        wheel.Children.Add(left);
        wheel.Children.Add(divider);

        Content = new StackPanel
        {
            Children =
            {
                SettingsLayout.Section("Opening", first: true), opening,
                SettingsLayout.Section("Wheel"), SettingsLayout.Card(wheel),
            },
        };
        PreviewKeyDown += CaptureShortcut;
        Loaded += (_, _) =>
        {
            _module.Changed += Refresh;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _module.Changed -= Refresh;
            _picker.StopRecording();
            CancelShortcut();
        };
        Refresh();
    }

    private void Hook(ToggleButton toggle, MouseButton button)
    {
        // Checked and Unchecked, not Click: UI Automation's Toggle raises no Click (AGENTS.md).
        toggle.Checked += (_, _) => { if (!_updating) _module.SetHold(button, true); };
        toggle.Unchecked += (_, _) => { if (!_updating) _module.SetHold(button, false); };
    }

    // ---- What's shown ----

    /// <summary>The slices of the wheel being edited.</summary>
    private List<Slice> Here => ListAt(_module.Settings.Slices, _path);

    private static List<Slice> ListAt(List<Slice> root, IReadOnlyList<int> path)
    {
        var list = root;
        foreach (int i in path)
            list = i < list.Count && list[i].Kind == SliceKind.Submenu ? list[i].Slices : [];
        return list;
    }

    private int Max => _path.Count == 0 ? RadialMenuSettings.MaxSlices : RadialMenuSettings.MaxSlices - 1;

    private Slice? Current => _selected is int i && i < Here.Count ? Here[i] : null;

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Refresh);
            return;
        }
        var settings = _module.Settings;
        _updating = true;
        try
        {
            _middle.IsChecked = settings.HoldMiddle;
            _back.IsChecked = settings.HoldBack;
            _forward.IsChecked = settings.HoldForward;
            _overFullscreen.IsChecked = settings.OpenOverFullscreen;
        }
        finally { _updating = false; }
        if (!_capturingShortcut)
            ShowShortcut();
        // A path or selection the settings no longer hold (edited elsewhere) falls back.
        while (_path.Count > 0 && ListAt(settings.Slices, ParentOf(_path)) is var parent && (_path[^1] >= parent.Count || parent[_path[^1]].Kind != SliceKind.Submenu))
            _path.RemoveAt(_path.Count - 1);
        if (_selected is int s && s >= Here.Count)
            _selected = Here.Count > 0 ? Here.Count - 1 : null;
        // A wheel with slices always has one selected, so its fields show.
        _selected ??= Here.Count > 0 ? 0 : null;
        ShowWheel();
        // Typing in a field (the apps read meanwhile): leave the fields be, so the text and the caret stay.
        if (!(Keyboard.FocusedElement is TextBox box && _fields.IsAncestorOf(box)))
            ShowFields();
    }

    private static List<int> ParentOf(IReadOnlyList<int> path) => [.. path.Take(path.Count - 1)];

    private void ShowWheel()
    {
        var slices = Here;
        var known = _module.Runner?.ChippiCommands() ?? [];
        var slots = slices.Select(s => new WheelSlot(SliceCatalog.GlyphOf(s, key => known.FirstOrDefault(c => c.Key == key)?.Icon), WheelState.NameOf(s))
        {
            Image = _module.IconOf(s),
            IsSubmenu = s.Kind == SliceKind.Submenu,
            IsMissing = _module.IsMissing(s, known),
        }).ToList();
        string hub = "";
        var tone = HubTone.Name;
        if (slices.Count == 0)
            (hub, tone) = ("No slices", HubTone.Quiet);
        else if (Current is { } current)
            (hub, tone) = slots[_selected!.Value].IsMissing ? ("Not found", HubTone.Caution) : (WheelState.NameOf(current), HubTone.Name);
        _editor.Show(slots, BackOf(_path), Max, _selected, hub, tone);
        _up.Visibility = _path.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var names = new List<string> { "Wheel" };
        var list = _module.Settings.Slices;
        foreach (int i in _path)
        {
            names.Add(WheelState.NameOf(list[i]));
            list = list[i].Slices;
        }
        _where.Text = string.Join(" › ", names);
    }

    /// <summary>
    /// A submenu's Back points back the way its slice points out of the editor's wheel before (whose places count its own
    /// Back and +); null for the first wheel.
    /// </summary>
    private double? BackOf(IReadOnlyList<int> path)
    {
        if (path.Count == 0)
            return null;
        var parentPath = ParentOf(path);
        var parent = ListAt(_module.Settings.Slices, parentPath);
        double? parentBack = BackOf(parentPath);
        int lead = parentBack is null ? 0 : 1;
        int max = parentPath.Count == 0 ? RadialMenuSettings.MaxSlices : RadialMenuSettings.MaxSlices - 1;
        int places = lead + parent.Count + (parent.Count < max ? 1 : 0);
        return WheelGeometry.Normalize((parentBack ?? 0) + (path[^1] + lead) * 360.0 / places + 180);
    }

    // ---- Opening's shortcut row: "Not set" with Record, or its keys with Remove ----

    private void ShowShortcut()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var shortcut = _module.Settings.Shortcut;
        if (_module.ShortcutError is { } error)
            row.Children.Add(Ui.Text(error, "WarningTextBrush"));
        else if (shortcut is null)
            row.Children.Add(Ui.Text("Not set", "TextSecondaryBrush"));
        else
            row.Children.Add(Ui.Caps(shortcut.ToString().Split('+')));
        var button = shortcut is null ? Ui.Button("Record", BeginShortcut) : Ui.Button("Remove", _module.RemoveShortcut);
        button.Margin = new Thickness(12, 0, 0, 0);
        AutomationProperties.SetName(button, shortcut is null ? "Record shortcut" : "Remove shortcut");
        row.Children.Add(button);
        _shortcut.Content = row;
    }

    private void BeginShortcut()
    {
        _capturingShortcut = true;
        _module.CaptureShortcut(true);
        var cancel = Ui.Button("Cancel", CancelShortcut);
        cancel.Margin = new Thickness(12, 0, 0, 0);
        _shortcut.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { Ui.Text("Press keys", "TextSecondaryBrush"), cancel } };
        Focusable = true;
        Focus();
    }

    private void CancelShortcut()
    {
        if (!_capturingShortcut)
            return;
        _capturingShortcut = false;
        _module.CaptureShortcut(false);
        ShowShortcut();
    }

    private void CaptureShortcut(object sender, KeyEventArgs e)
    {
        if (!_capturingShortcut)
            return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            CancelShortcut();
            return;
        }
        if (ShortcutKeys.From(e) is not { } shortcut)
            return;
        _capturingShortcut = false;
        _module.CaptureShortcut(false);
        _module.ChangeShortcut(shortcut);
        ShowShortcut();
    }

    // ---- The wheel's edits ----

    private void Select(int index)
    {
        _picker.StopRecording();
        _selected = index;
        ShowWheel();
        ShowFields();
    }

    private void Save(Func<List<Slice>, List<Slice>> change) => _module.SetSlices(Replace(_module.Settings.Slices, _path, change));

    private static List<Slice> Replace(List<Slice> list, IReadOnlyList<int> path, Func<List<Slice>, List<Slice>> change)
    {
        if (path.Count == 0)
            return change(list);
        var copy = list.ToList();
        copy[path[0]] = copy[path[0]] with { Slices = Replace(copy[path[0]].Slices, path.Skip(1).ToList(), change) };
        return copy;
    }

    private void Change(Func<Slice, Slice> change)
    {
        if (_selected is not int index || index >= Here.Count)
            return;
        Save(list =>
        {
            var copy = list.ToList();
            copy[index] = change(copy[index]);
            return copy;
        });
    }

    private void Swap(int from, int to)
    {
        _selected = to;
        Save(list =>
        {
            var copy = list.ToList();
            (copy[from], copy[to]) = (copy[to], copy[from]);
            return copy;
        });
    }

    /// <summary>+: File Explorer, as the mockup starts one, ready to change.</summary>
    private void Add()
    {
        if (Here.Count >= Max)
            return;
        var explorer = _module.Pc?.Apps.FirstOrDefault(a => a.ParsingName.Equals("Microsoft.Windows.Explorer", StringComparison.OrdinalIgnoreCase));
        var slice = explorer is not null
            ? new Slice { Kind = SliceKind.App, Value = explorer.ParsingName, Name = explorer.Name }
            : new Slice { Kind = SliceKind.Media, Value = nameof(MediaAction.PlayPause), Name = ActionCatalog.NameOf(MediaAction.PlayPause) };
        _selected = Here.Count;
        Save(list => [.. list, slice]);
    }

    private void Remove()
    {
        if (_selected is not int index || index >= Here.Count)
            return;
        int left = Here.Count - 1;
        _selected = left == 0 ? null : Math.Min(index, left - 1);
        Save(list => [.. list.Where((_, i) => i != index)]);
    }

    private void Open()
    {
        if (Current is not { Kind: SliceKind.Submenu } || _path.Count >= RadialMenuSettings.MaxDepth - 1)
            return;
        _path.Add(_selected!.Value);
        _selected = Here.Count > 0 ? 0 : null;
        ShowWheel();
        ShowFields();
    }

    private void Up()
    {
        if (_path.Count == 0)
            return;
        _picker.StopRecording();
        _selected = _path[^1];
        _path.RemoveAt(_path.Count - 1);
        ShowWheel();
        ShowFields();
    }

    // ---- The selected slice's fields: the shared action picker, with Submenu and Remove ----

    private void ShowFields()
    {
        if (Current is not { } slice)
        {
            var empty = Ui.Text("No slices", "TextSecondaryBrush");
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.VerticalAlignment = VerticalAlignment.Center;
            _fields.Content = new Grid { Children = { empty } };
            return;
        }
        _picker.ExtraKinds = _path.Count < RadialMenuSettings.MaxDepth - 1 ? [SliceCatalog.SubmenuName] : [];
        _picker.Show(slice.ToAction(), slice.Kind == SliceKind.Submenu ? 0 : null);
        _fields.Content = _picker;
    }

    /// <summary>A picked action replaces the slice; a submenu kept takes only its name and glyph, and a new one starts empty.</summary>
    private void Picked(ChippiAction action, int? submenu) => Change(slice => submenu is null
        ? Slice.From(action)
        : slice.Kind == SliceKind.Submenu
            ? slice with { Name = action.Name, Glyph = action.Glyph }
            : new Slice { Kind = SliceKind.Submenu, Name = action.Name, Shell = slice.Shell });

    /// <summary>A submenu's Slices row, in place of a value.</summary>
    private IEnumerable<UIElement> SubmenuRows()
    {
        if (Current is not { } slice)
            yield break;
        int count = slice.Slices.Count;
        var open = Ui.Button("Open", Open);
        open.Margin = new Thickness(12, 0, 0, 0);
        AutomationProperties.SetName(open, $"Open {WheelState.NameOf(slice)}");
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Ui.Text(count == 1 ? "1 slice" : $"{count} slices", "TextSecondaryBrush"));
        row.Children.Add(open);
        yield return SettingsLayout.Row("Slices", row);
    }

    private IEnumerable<UIElement> RemoveRow()
    {
        if (Current is not { } slice)
            yield break;
        var remove = Ui.Button("Remove", Remove);
        AutomationProperties.SetName(remove, $"Remove {WheelState.NameOf(slice)}");
        yield return new Border { Padding = SettingsLayout.RowPadding, MinHeight = SettingsLayout.RowHeight, Child = new Grid { Children = { remove } } };
        remove.HorizontalAlignment = HorizontalAlignment.Right;
    }
}
