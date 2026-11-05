// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

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
using Chippi.UI.Controls;
using MouseButton = Chippi.Features.RadialMenu.Input.MouseButton;

namespace Chippi.Features.RadialMenu.Views;

/// <summary>
/// Settings › Radial menu (docs/phase-5.5/radial-menu.md): Opening, a switch per button and the shortcut; Wheel, the
/// wheel drawn at its real size beside the selected slice's fields. A submenu's Open steps into its wheel.
/// </summary>
public sealed class RadialMenuSettingsPage : UserControl
{
    private static readonly string[] Shells = ["PowerShell", "Command Prompt"];

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
    private IDisposable? _recording;

    public RadialMenuSettingsPage(RadialMenuModule module)
    {
        _module = module;
        _editor = new WheelEditor(module.ChippiColor) { Margin = new Thickness(0, 12, 0, 8), HorizontalAlignment = HorizontalAlignment.Center };
        _editor.Selected += i => Select(i);
        _editor.Swapped += Swap;
        _editor.Added += Add;
        _editor.WentBack += Up;
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
            StopRecording();
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
            IsMissing = _module.Runner?.IsMissing(s, known) == true,
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
        if (MicShortcut.From(e) is not { } shortcut)
            return;
        _capturingShortcut = false;
        _module.CaptureShortcut(false);
        _module.ChangeShortcut(shortcut);
        ShowShortcut();
    }

    // ---- The wheel's edits ----

    private void Select(int index)
    {
        StopRecording();
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

    /// <summary>A new value, kind or shell, with the name following it unless it was changed by hand.</summary>
    private void Retarget(Func<Slice, Slice> change) => Change(old =>
    {
        var next = change(old);
        bool named = old.Name.Length > 0 && old.Name != SliceCatalog.NameFor(old, Known);
        return named ? next : next with { Name = SliceCatalog.NameFor(next, Known) };
    });

    /// <summary>An app's or a Chippi command's name, by its value.</summary>
    private string? Known(string value) =>
        _module.Pc?.Apps.FirstOrDefault(a => string.Equals(a.ParsingName, value, StringComparison.OrdinalIgnoreCase))?.Name
        ?? _module.Runner?.ChippiCommands().FirstOrDefault(c => c.Key == value)?.Name;

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
            : new Slice { Kind = SliceKind.Media, Value = nameof(MediaAction.PlayPause), Name = SliceCatalog.NameOf(MediaAction.PlayPause) };
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
        StopRecording();
        _selected = _path[^1];
        _path.RemoveAt(_path.Count - 1);
        ShowWheel();
        ShowFields();
    }

    // ---- The selected slice's fields ----

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
        var rows = new Card();
        rows.SetResourceReference(StyleProperty, "CardRowsStyle");
        var kinds = SliceCatalog.Kinds.Where(k => k.Kind != SliceKind.Submenu || _path.Count < RadialMenuSettings.MaxDepth - 1).ToList();
        var kind = Ui.Combo("Kind", kinds.Select(k => k.Name));
        kind.SelectedIndex = kinds.FindIndex(k => k.Kind == slice.Kind);
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedIndex >= 0 && kinds[kind.SelectedIndex].Kind != slice.Kind)
                SetKind(kinds[kind.SelectedIndex].Kind);
        };
        rows.Children.Add(SettingsLayout.Row("Kind", kind));
        foreach (var row in ValueRows(slice))
            rows.Children.Add(row);

        var name = Ui.Field("Name");
        name.Text = slice.Name;
        Commit(name, text => Change(s => s with { Name = text.Trim().Length > 0 ? text.Trim() : SliceCatalog.NameFor(s, Known) }));
        rows.Children.Add(SettingsLayout.Row("Name", name));

        var glyph = new StackPanel { Orientation = Orientation.Horizontal };
        glyph.Children.Add(Ui.Swatch(SliceCatalog.GlyphOf(slice, key => _module.Runner?.ChippiCommands().FirstOrDefault(c => c.Key == key)?.Icon), _module.IconOf(slice)));
        var change = Ui.Button("Change", () => { });
        change.Margin = new Thickness(12, 0, 0, 0);
        change.Click += (_, _) => PickGlyph(change, slice);
        AutomationProperties.SetName(change, "Change glyph");
        glyph.Children.Add(change);
        rows.Children.Add(SettingsLayout.Row("Glyph", glyph));

        var remove = Ui.Button("Remove", Remove);
        AutomationProperties.SetName(remove, $"Remove {WheelState.NameOf(slice)}");
        rows.Children.Add(new Border { Padding = SettingsLayout.RowPadding, MinHeight = SettingsLayout.RowHeight, Child = new Grid { Children = { remove } } });
        remove.HorizontalAlignment = HorizontalAlignment.Right;
        _fields.Content = rows;
    }

    /// <summary>What a kind needs: the app, the path, the address, the keys, the text, the command and its shell, an action, a page.</summary>
    private IEnumerable<UIElement> ValueRows(Slice slice)
    {
        switch (slice.Kind)
        {
            case SliceKind.App:
                var apps = _module.Pc?.Apps ?? [];
                var names = apps.Select(a => a.Name).ToList();
                bool program = Path.IsPathFullyQualified(slice.Value);
                if (program)
                    names.Insert(0, Path.GetFileNameWithoutExtension(slice.Value));
                var app = Ui.Combo("App", names, 160);
                app.SelectedIndex = program ? 0 : apps.ToList().FindIndex(a => string.Equals(a.ParsingName, slice.Value, StringComparison.OrdinalIgnoreCase));
                app.SelectionChanged += (_, _) =>
                {
                    int i = app.SelectedIndex - (program ? 1 : 0);
                    if (i >= 0 && i < apps.Count)
                        Retarget(s => s with { Value = apps[i].ParsingName });
                };
                yield return SettingsLayout.Row("App", Pair(app, Ui.Button("Browse", BrowseProgram)));
                break;
            case SliceKind.File:
                var path = Ui.Field("Path", 160);
                path.Text = slice.Value;
                Commit(path, v => Retarget(s => s with { Value = v.Trim().Trim('"') }));
                var browse = Ui.Button("Browse", () => { });
                browse.Click += (_, _) => BrowseFile(browse);
                yield return SettingsLayout.Row("Path", Pair(path, browse));
                break;
            case SliceKind.Link:
                var address = Ui.Field("Address");
                address.Text = slice.Value;
                Commit(address, v => Retarget(s => s with { Value = v.Trim() }));
                yield return SettingsLayout.Row("Address", address);
                break;
            case SliceKind.Keys:
                yield return SettingsLayout.Row("Keys", KeysField(slice));
                break;
            case SliceKind.Text:
                var text = Ui.Field("Text");
                text.Text = slice.Value;
                text.AcceptsReturn = true;
                text.AcceptsTab = false;
                text.Height = 64;
                text.TextWrapping = TextWrapping.Wrap;
                text.VerticalContentAlignment = VerticalAlignment.Top;
                text.Padding = new Thickness(0, 4, 0, 4);
                Commit(text, v => Retarget(s => s with { Value = v }), enter: false);
                var row = SettingsLayout.Row("Text", text);
                row.VerticalContentAlignment = VerticalAlignment.Top;
                yield return row;
                break;
            case SliceKind.Command:
                var shell = Ui.Combo("Shell", Shells);
                shell.SelectedIndex = (int)slice.Shell;
                shell.SelectionChanged += (_, _) =>
                {
                    if (shell.SelectedIndex >= 0 && (CommandShell)shell.SelectedIndex != slice.Shell)
                        Change(s => s with { Shell = (CommandShell)shell.SelectedIndex });
                };
                yield return SettingsLayout.Row("Shell", shell);
                var line = Ui.Field("Command", code: true);
                line.Text = slice.Value;
                Commit(line, v => Retarget(s => s with { Value = v.Trim() }));
                yield return SettingsLayout.Row("Command", line);
                break;
            case SliceKind.Chippi:
                var commands = _module.Runner?.ChippiCommands() ?? [];
                var chippi = Ui.Combo("Action", commands.Select(c => c.Name));
                chippi.SelectedIndex = commands.ToList().FindIndex(c => c.Key == slice.Value);
                chippi.SelectionChanged += (_, _) =>
                {
                    if (chippi.SelectedIndex >= 0)
                        Retarget(s => s with { Value = commands[chippi.SelectedIndex].Key });
                };
                yield return SettingsLayout.Row("Action", chippi);
                break;
            case SliceKind.Media:
                yield return SettingsLayout.Row("Action", Actions(slice, SliceCatalog.Media.Select(m => (m.Action.ToString(), m.Name)).ToList()));
                break;
            case SliceKind.Window:
                yield return SettingsLayout.Row("Action", Actions(slice, SliceCatalog.Window.Select(m => (m.Action.ToString(), m.Name)).ToList()));
                break;
            case SliceKind.Selection:
                yield return SettingsLayout.Row("Action", Actions(slice, SliceCatalog.Selection.Select(m => (m.Action.ToString(), m.Name)).ToList()));
                break;
            case SliceKind.SettingsPage:
                yield return SettingsLayout.Row("Page", Actions(slice, SettingsPages.All.Select(p => (p.Uri, p.Name)).ToList(), "Page"));
                break;
            case SliceKind.Submenu:
                int count = slice.Slices.Count;
                var open = Ui.Button("Open", Open);
                open.Margin = new Thickness(12, 0, 0, 0);
                AutomationProperties.SetName(open, $"Open {WheelState.NameOf(slice)}");
                yield return SettingsLayout.Row("Slices", Pair(Ui.Text(count == 1 ? "1 slice" : $"{count} slices", "TextSecondaryBrush"), open));
                break;
        }
    }

    private ComboBox Actions(Slice slice, IReadOnlyList<(string Value, string Name)> actions, string label = "Action")
    {
        var combo = Ui.Combo(label, actions.Select(a => a.Name));
        combo.SelectedIndex = actions.ToList().FindIndex(a => a.Value == slice.Value);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && actions[combo.SelectedIndex].Value != slice.Value)
                Retarget(s => s with { Value = actions[combo.SelectedIndex].Value });
        };
        return combo;
    }

    /// <summary>A new kind starts with its first action, or nothing to fill in; a submenu starts empty.</summary>
    private void SetKind(SliceKind kind)
    {
        StopRecording();
        string value = kind switch
        {
            SliceKind.Media => nameof(MediaAction.PlayPause),
            SliceKind.Window => nameof(WindowAction.SnapLeft),
            SliceKind.Selection => nameof(SelectionAction.Search),
            SliceKind.SettingsPage => SettingsPages.All[0].Uri,
            SliceKind.Chippi => _module.Runner?.ChippiCommands().FirstOrDefault()?.Key ?? "",
            SliceKind.App => _module.Pc?.Apps.FirstOrDefault()?.ParsingName ?? "",
            _ => "",
        };
        Change(old =>
        {
            var next = new Slice { Kind = kind, Value = value, Shell = old.Shell };
            return next with { Name = SliceCatalog.NameFor(next, Known) };
        });
    }

    private UIElement KeysField(Slice slice)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (_recording is not null)
        {
            row.Children.Add(Ui.Text("Press keys", "TextSecondaryBrush"));
            var cancel = Ui.Button("Cancel", () =>
            {
                StopRecording();
                ShowFields();
            });
            cancel.Margin = new Thickness(12, 0, 0, 0);
            row.Children.Add(cancel);
            return row;
        }
        if (KeyCombo.Parse(slice.Value) is { } keys)
            row.Children.Add(Ui.Caps(keys.Caps));
        else
            row.Children.Add(Ui.Text("Not set", "TextSecondaryBrush"));
        var record = Ui.Button("Record", () => { });
        record.Margin = new Thickness(12, 0, 0, 0);
        AutomationProperties.SetName(record, "Record keys");
        record.Click += (_, _) => Record();
        row.Children.Add(record);
        return row;
    }

    private void Record()
    {
        try
        {
            _recording = _module.Recorder.Record(keys =>
            {
                // Recorded, the recorder lets go of the keyboard itself once the keys are up.
                _recording = null;
                if (keys is { } recorded)
                    Retarget(s => s with { Value = recorded.ToString() });
                else
                    ShowFields();
            });
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Warn("Radial menu: recording keys failed", ex);
            _recording = null;
        }
        ShowFields();
    }

    private void StopRecording()
    {
        _recording?.Dispose();
        _recording = null;
    }

    private void BrowseProgram()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Programs (*.exe, *.lnk, *.bat, *.cmd)|*.exe;*.lnk;*.bat;*.cmd|All files|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            Retarget(s => s with { Value = dialog.FileName });
    }

    private void BrowseFile(Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        var file = new MenuItem { Header = "File…" };
        file.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { CheckFileExists = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                Retarget(s => s with { Value = dialog.FileName });
        };
        var folder = new MenuItem { Header = "Folder…" };
        folder.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = false };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                Retarget(s => s with { Value = dialog.FolderName });
        };
        menu.Items.Add(file);
        menu.Items.Add(folder);
        menu.IsOpen = true;
    }

    private void PickGlyph(Button anchor, Slice slice)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var grid = new UniformGrid { Columns = 8 };
        foreach (string name in SliceCatalog.Glyphs)
        {
            var button = new Button { Content = Ui.Swatch(LucideIcons.Get(name)), Margin = new Thickness(2), Padding = new Thickness(0), Width = 36, Height = 36 };
            button.SetResourceReference(StyleProperty, "SubtleButtonStyle");
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) =>
            {
                popup.IsOpen = false;
                Change(s => s with { Glyph = name });
            };
            grid.Children.Add(button);
        }
        var reset = Ui.Button(slice.Kind is SliceKind.App or SliceKind.File ? "Use its icon" : "Use the kind's", () =>
        {
            popup.IsOpen = false;
            Change(s => s with { Glyph = null });
        });
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(2, 8, 2, 2);
        var panel = new Border
        {
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { grid, reset } },
        };
        panel.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        popup.Child = panel;
        popup.IsOpen = true;
    }

    private static StackPanel Pair(UIElement first, UIElement second)
    {
        if (second is FrameworkElement element && element.Margin == default)
            element.Margin = new Thickness(8, 0, 0, 0);
        return new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { first, second } };
    }

    /// <summary>Saves a field's text when it loses the keyboard, or on Enter for a one-line field.</summary>
    private static void Commit(TextBox box, Action<string> save, bool enter = true)
    {
        string was = box.Text;
        void Done()
        {
            if (box.Text == was)
                return;
            was = box.Text;
            save(box.Text);
        }
        box.LostKeyboardFocus += (_, _) => Done();
        // Enter lets go of the field, which saves it, so the fields show what it changed (the name following it).
        if (enter)
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    Keyboard.ClearFocus();
                }
            };
    }
}
