// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.App.Shell;
using Chippi.Core.Actions;
using Chippi.Core.Input;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.Shortcuts;
using Chippi.UI.Actions;
using Chippi.UI.Controls;

namespace Chippi.App.Settings.Pages;

/// <summary>
/// Settings › Shortcuts (docs/phase-6/design/shortcuts.html): every shortcut in one list, Chippi's own from the shell's list
/// and the owner's bindings from the Shortcuts feature, with their keys in one column; then Add shortcut, the Caps Lock
/// combos and Mouse buttons. A shortcut that clashes is refused where it's recorded, with one line by Save.
/// </summary>
internal sealed class ShortcutsPage : UserControl
{
    private static readonly string[] GroupOrder = ["Chippi", "Window", "Media", "Text", "App", "Keys", "Command", "Selection", "Settings"];
    private const double KeysColumn = 300;

    private enum EditMode { New, Binding, Chippi }

    private sealed class Edit
    {
        public EditMode Mode;
        public long EntryId;
        public string EntryName = "";
        public string? BindingId;
        public ChippiAction Action = new();
        public BindingDoor Door;
        public KeyShortcut? Keys;
        public uint CapsKey;
        public MouseTrigger? Button;
        public bool Listening;
        public string? Error;
    }

    private sealed record Row(string Group, string Name, string? Icon, IReadOnlyList<string> Caps, bool Mouse, bool Dim, bool Unset, Action Change, Action? Remove, Action? Another = null);

    private readonly ShellContext _shell;
    private readonly ShortcutsModule? _module;
    private readonly KeyRecorder _recorder;
    private readonly Chippi.Features.CommandBar.Windows.WindowsPc _pc = new();
    private readonly StackPanel _page = new();
    private readonly Border _listHost = new();
    private readonly Border _editorHost = new();
    private readonly StackPanel _sections = new();
    private readonly StackPanel _confirmHost = new();
    private Border _inputRow = new();
    private TextBlock _message = new();
    private Button _save = new();
    private Edit? _edit;
    private IDisposable? _listening;
    private bool _building;

    public ShortcutsPage(ShellContext shell)
    {
        _shell = shell;
        _module = shell.ShortcutsFeature;
        _recorder = new KeyRecorder(() => shell.Input);
        _page.Children.Add(SettingsUi.Header("Shortcuts", ""));
        _page.Children.Add(_listHost);
        _editorHost.Margin = new Thickness(0, 8, 0, 0);
        _editorHost.Visibility = Visibility.Collapsed;
        _page.Children.Add(_editorHost);
        _page.Children.Add(_sections);
        Content = _page;
        void Changed() => Dispatcher.BeginInvoke(RebuildLists);
        void RegistryChanged(object? s, EventArgs e) => Dispatcher.BeginInvoke(RebuildAll);
        Loaded += (_, _) =>
        {
            shell.Shortcuts.Changed += Changed;
            shell.Registry.Changed += RegistryChanged;
            if (_module is not null) _module.Changed += Changed;
            _pc.RefreshApps();
            RebuildAll();
        };
        Unloaded += (_, _) =>
        {
            shell.Shortcuts.Changed -= Changed;
            shell.Registry.Changed -= RegistryChanged;
            if (_module is not null) _module.Changed -= Changed;
            StopListening();
        };
        RebuildAll();
    }

    private bool ModuleRuns => _module is not null && _shell.Registry.Features.Any(f => f.Id == ShortcutsModule.Id && f.IsRunning);

    private void RebuildAll()
    {
        RebuildLists();
        BuildSections();
    }

    // ---- the list ----

    private IEnumerable<Row> Rows()
    {
        foreach (var e in _shell.Shortcuts.List.Where(e => e.Editable))
        {
            var entry = e;
            yield return new Row("Chippi", entry.Name, entry.Icon, entry.Shortcut is { } k ? CapsOf(k) : [], false, !entry.Held, entry.Shortcut is null,
                () => StartEdit(new Edit { Mode = EditMode.Chippi, EntryId = entry.Id, EntryName = entry.Name, Door = BindingDoor.Key, Keys = entry.Shortcut }),
                entry.Shortcut is null ? null : () => _shell.Shortcuts.Remove(entry.Id));
        }
        if (_module is null || !ModuleRuns)
            yield break;
        foreach (var b in _module.Settings.Bindings)
        {
            var binding = b;
            var status = _module.StatusOf(binding.Id);
            string[] caps = binding.Door switch
            {
                BindingDoor.Key => [.. CapsOf(binding.Keys!)],
                BindingDoor.Caps => [CombosKey(_module.Settings.CapsHeldKey), KeyCombo.NameOf(binding.CapsKey)],
                _ => [MouseName(binding.Button)],
            };
            yield return new Row(GroupOf(binding.Action.Kind), binding.Label, ActionCatalog.GlyphOf(binding.Action), caps, binding.Door == BindingDoor.Mouse, !status.Acts, false,
                () => StartEdit(new Edit
                {
                    Mode = EditMode.Binding, BindingId = binding.Id, Action = binding.Action, Door = binding.Door, Keys = binding.Keys, CapsKey = binding.CapsKey,
                    Button = binding.Door == BindingDoor.Mouse ? binding.Button : null,
                }),
                () => _module.Remove(binding.Id),
                () => StartEdit(new Edit { Mode = EditMode.New, Action = binding.Action, Door = BindingDoor.Key }));
        }
    }

    private static string GroupOf(ActionKind kind) => kind switch
    {
        ActionKind.App or ActionKind.File or ActionKind.Link => "App",
        ActionKind.SettingsPage => "Settings",
        _ => kind.ToString(),
    };

    private static IReadOnlyList<string> CapsOf(KeyShortcut k)
    {
        var mods = KeyModifiers.None;
        if (k.Modifiers.HasFlag(ShortcutModifiers.Control)) mods |= KeyModifiers.Ctrl;
        if (k.Modifiers.HasFlag(ShortcutModifiers.Alt)) mods |= KeyModifiers.Alt;
        if (k.Modifiers.HasFlag(ShortcutModifiers.Shift)) mods |= KeyModifiers.Shift;
        if (k.Modifiers.HasFlag(ShortcutModifiers.Windows)) mods |= KeyModifiers.Win;
        return new KeyCombo(mods, k.Key).Caps;
    }

    private static string CombosKey(uint vk) => vk switch { 0x14 => "Caps Lock", 0xA3 => "Right Ctrl", 0xA1 => "Right Shift", 0x5C => "Right Win", _ => KeyCombo.NameOf(vk) };

    private static string MouseName(MouseTrigger t) => t switch
    {
        MouseTrigger.Middle => "Middle button",
        MouseTrigger.Back => "Back button",
        MouseTrigger.Forward => "Forward button",
        MouseTrigger.WheelLeft => "Wheel left",
        _ => "Wheel right",
    };

    private void RebuildLists()
    {
        var rows = Rows().ToList();
        var card = new Card();
        if (rows.Count == 0)
            card.Children.Add(SettingsLayout.Empty("No shortcuts yet. Add the first one."));
        foreach (var group in rows.GroupBy(r => r.Group).OrderBy(g => Math.Max(0, Array.IndexOf(GroupOrder, g.Key))))
        {
            var heading = SettingsUi.Styled(new TextBlock { Text = group.Key, VerticalAlignment = VerticalAlignment.Center }, "CaptionTextStyle");
            card.Children.Add(new Border { MinHeight = 36, Padding = new Thickness(16, 8, 16, 0), Child = heading });
            foreach (var row in group)
                card.Children.Add(RowView(row));
        }
        if (ModuleRuns)
        {
            var add = SettingsUi.Button("Add shortcut");
            AutomationProperties.SetName(add, "Add shortcut");
            add.Click += (_, _) => StartEdit(new Edit { Mode = EditMode.New, Door = BindingDoor.Key, Action = FirstAction() });
            string count = rows.Count == 1 ? "1 shortcut" : $"{rows.Count} shortcuts";
            card.Children.Add(SettingsLayout.Row(count, add));
        }
        _listHost.Child = card;
    }

    private static ChippiAction FirstAction()
    {
        var first = ActionCatalog.Window[0];
        return new ChippiAction { Kind = ActionKind.Window, Value = first.Action.ToString(), Name = first.Name };
    }

    private FrameworkElement RowView(Row row)
    {
        var grid = new Grid { MinHeight = SettingsLayout.RowHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(KeysColumn) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        var icon = new LucideIcon { Data = LucideIcons.Get(row.Icon), Size = 18, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(LucideIcon.BrushProperty, "TextPrimaryBrush");
        var name = SettingsUi.Styled(new TextBlock { Text = row.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, "BodyTextStyle");
        FrameworkElement keys;
        if (row.Unset)
        {
            var record = SettingsUi.Button("Record");
            record.Margin = new Thickness(0);
            record.HorizontalAlignment = HorizontalAlignment.Left;
            AutomationProperties.SetName(record, $"Record {row.Name}");
            record.Click += (_, _) => row.Change();
            keys = record;
        }
        else
            keys = Keycaps(row.Caps, row.Mouse, row.Dim);
        keys.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        Grid.SetColumn(keys, 2);
        grid.Children.Add(icon);
        grid.Children.Add(name);
        grid.Children.Add(keys);
        if (!row.Unset)
        {
            var more = new IconButton { Icon = LucideIcons.Ellipsis, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            AutomationProperties.SetName(more, $"{row.Name} options");
            var menu = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Bottom };
            var change = new MenuItem { Header = "Change" };
            change.Click += (_, _) => row.Change();
            menu.Items.Add(change);
            if (row.Another is { } another)
            {
                var item = new MenuItem { Header = "Add another" };
                item.Click += (_, _) => another();
                menu.Items.Add(item);
            }
            if (row.Remove is { } remove)
            {
                var item = new MenuItem { Header = "Remove" };
                item.Click += (_, _) => remove();
                menu.Items.Add(item);
            }
            more.Click += (_, _) => menu.IsOpen = true;
            Grid.SetColumn(more, 3);
            grid.Children.Add(more);
        }
        return new Border { Padding = SettingsLayout.RowPadding, Child = grid };
    }

    /// <summary>Keys as caps, from the column's left edge; dimmed when they aren't working.</summary>
    private static FrameworkElement Keycaps(IReadOnlyList<string> caps, bool mouse, bool dim)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Opacity = dim ? 0.45 : 1, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (string text in caps)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (mouse)
            {
                var m = new LucideIcon { Data = LucideIcons.Mouse, Size = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                m.SetResourceReference(LucideIcon.BrushProperty, "TextPrimaryBrush");
                content.Children.Add(m);
            }
            var label = SettingsUi.Styled(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }, "CaptionTextStyle");
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            content.Children.Add(label);
            var cap = new Border { Child = content, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
            cap.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
            panel.Children.Add(cap);
        }
        return panel;
    }

    // ---- the editor ----

    private void StartEdit(Edit edit)
    {
        StopListening();
        _edit = edit;
        BuildEditor();
        Listen();
    }

    private void CloseEditor()
    {
        StopListening();
        _edit = null;
        _editorHost.Child = null;
        _editorHost.Visibility = Visibility.Collapsed;
    }

    private void StopListening()
    {
        _listening?.Dispose();
        _listening = null;
        if (_edit is not null) _edit.Listening = false;
    }

    private void BuildEditor()
    {
        var edit = _edit!;
        _building = true;
        var card = new Card();
        if (edit.Mode != EditMode.Chippi)
        {
            var picker = new ActionPicker(new ActionPickerSource
            {
                Apps = () => [.. _pc.Apps.Select(a => (a.ParsingName, a.Name))],
                Commands = () => [.. _shell.Registry.Commands().Where(c => c.Kind != ChippiCommandKind.Paste)],
                SettingsPages = [.. SettingsPages.All.Select(p => (p.Uri, p.Name))],
                Recorder = _recorder,
            });
            picker.Show(edit.Action);
            picker.Changed += (action, _) => { edit.Action = action; RefreshSave(); };
            card.Children.Add(SettingsLayout.Row(edit.Mode == EditMode.New ? "New shortcut" : "Action", picker));

            var segments = SettingsUi.Segments("Input", new[] { ("Keys", BindingDoor.Key), ("Caps Lock", BindingDoor.Caps), ("Mouse button", BindingDoor.Mouse) }, edit.Door);
            segments.SelectionChanged += (_, _) =>
            {
                if (_building || segments.SelectedItem is not SegmentItem { Value: BindingDoor door } || door == edit.Door) return;
                edit.Door = door;
                edit.Error = null;
                Listen();
            };
            _inputRow = new Border();
            var dock = new DockPanel();
            DockPanel.SetDock(segments, Dock.Left);
            segments.Margin = new Thickness(0, 0, 12, 0);
            dock.Children.Add(segments);
            dock.Children.Add(_inputRow);
            card.Children.Add(new Border { Padding = SettingsLayout.RowPadding, Child = dock });
        }
        else
        {
            card.Children.Add(SettingsLayout.Row(edit.EntryName));
            _inputRow = new Border { Margin = new Thickness(16, 8, 16, 8) };
            card.Children.Add(_inputRow);
        }

        _message = SettingsUi.Styled(new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }, "CaptionTextStyle");
        _message.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Polite);
        _save = SettingsUi.Button("Save", primary: true);
        var cancel = SettingsUi.Button("Cancel");
        _save.Click += (_, _) => Save();
        cancel.Click += (_, _) => CloseEditor();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_save);
        buttons.Children.Add(cancel);
        var footer = new DockPanel { Margin = SettingsLayout.RowPadding };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_message);
        card.Children.Add(footer);

        _editorHost.Child = card;
        _editorHost.Visibility = Visibility.Visible;
        _building = false;
        RebuildEditorInput();
    }

    private void RebuildEditorInput()
    {
        var edit = _edit;
        if (edit is null) return;
        var field = new Border { MinHeight = 32, Padding = new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Stretch };
        field.SetResourceReference(Border.BorderBrushProperty, edit.Error is not null ? "WarningTextBrush" : edit.Listening ? "AccentBrush" : "BorderStrongBrush");
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        IReadOnlyList<string> caps = edit.Door switch
        {
            BindingDoor.Key => edit.Keys is { } k ? CapsOf(k) : [],
            BindingDoor.Caps => edit.CapsKey != 0 ? [CombosKey(_module?.Settings.CapsHeldKey ?? 0x14), KeyCombo.NameOf(edit.CapsKey)] : [],
            _ => edit.Button is { } b ? [MouseName(b)] : [],
        };
        if (caps.Count > 0)
            content.Children.Add(Keycaps(caps, edit.Door == BindingDoor.Mouse, false));
        else
        {
            string prompt = edit.Door switch
            {
                BindingDoor.Key => "Press a shortcut",
                BindingDoor.Caps => "Hold Caps Lock and press a key",
                _ => "Press a mouse button or tilt the wheel",
            };
            var hint = SettingsUi.Styled(new TextBlock { Text = prompt }, "BodyTextStyle");
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            content.Children.Add(hint);
        }
        field.Child = content;
        field.MouseLeftButtonUp += (_, _) => Listen();
        AutomationProperties.SetName(field, "Shortcut");
        _inputRow.Child = field;
        RefreshSave();
    }

    private static bool Complete(Edit e) => e.Door switch
    {
        BindingDoor.Key => e.Keys is not null,
        BindingDoor.Caps => e.CapsKey != 0,
        _ => e.Button is not null,
    };

    private void RefreshSave()
    {
        if (_edit is not { } edit) return;
        _save.IsEnabled = Complete(edit) && edit.Error is null;
        _message.Text = edit.Error ?? "";
    }

    private void Listen()
    {
        if (_edit is not { } edit) return;
        _listening?.Dispose();
        _listening = null;
        edit.Listening = true;
        try
        {
            _listening = edit.Door == BindingDoor.Mouse
                ? new MouseButtonRecorder(_shell.Input, t => Dispatcher.BeginInvoke(() => Recorded(edit, null, t)))
                : _recorder.Record(combo => Recorded(edit, combo, null));
        }
        catch (Exception ex)
        {
            Chippi.Core.Diagnostics.Log.Warn("Shortcuts: recording failed", ex);
            edit.Listening = false;
            edit.Error = "Recording failed";
        }
        RebuildEditorInput();
    }

    private void Recorded(Edit edit, KeyCombo? combo, MouseTrigger? button)
    {
        if (!ReferenceEquals(edit, _edit)) return;
        _listening?.Dispose();
        _listening = null;
        edit.Listening = false;
        if (button is { } b)
            edit.Button = b;
        else if (combo is { } c)
        {
            if (edit.Door == BindingDoor.Caps)
                edit.CapsKey = c.Key;
            else
            {
                var mods = (c.Modifiers.HasFlag(KeyModifiers.Ctrl) ? ShortcutModifiers.Control : 0) | (c.Modifiers.HasFlag(KeyModifiers.Alt) ? ShortcutModifiers.Alt : 0)
                    | (c.Modifiers.HasFlag(KeyModifiers.Shift) ? ShortcutModifiers.Shift : 0) | (c.Modifiers.HasFlag(KeyModifiers.Win) ? ShortcutModifiers.Windows : 0);
                edit.Keys = new KeyShortcut(mods, c.Key);
            }
        }
        edit.Error = Complete(edit) ? Refusal(edit) : null;
        RebuildEditorInput();
    }

    private static ShortcutBinding Draft(Edit e) => new()
    {
        Id = e.BindingId ?? Guid.NewGuid().ToString("N"),
        Door = e.Door,
        Action = e.Action,
        Keys = e.Door == BindingDoor.Key ? e.Keys : null,
        CapsKey = e.Door == BindingDoor.Caps ? e.CapsKey : 0,
        Button = e.Button ?? MouseTrigger.Middle,
    };

    /// <summary>Why these keys can't be saved, in the words by Save; null when they can.</summary>
    private string? Refusal(Edit e) => e.Mode == EditMode.Chippi
        ? _shell.Shortcuts.Check(e.Keys!, e.EntryId)
        : _module?.Check(Draft(e), e.BindingId);

    private void Save()
    {
        if (_edit is not { } edit || !Complete(edit)) return;
        string? error;
        if (edit.Mode == EditMode.Chippi)
            error = _shell.Shortcuts.Change(edit.EntryId, edit.Keys!);
        else if (_module is null)
            error = "Save failed";
        else
            error = (edit.Mode == EditMode.New ? _module.Add(Draft(edit)) : _module.Change(Draft(edit))).Error;
        if (error is not null)
        {
            edit.Error = error;
            RebuildEditorInput();
            return;
        }
        CloseEditor();
    }

    // ---- Caps Lock and Mouse buttons ----

    private void BuildSections()
    {
        _sections.Children.Clear();
        if (_module is null || !ModuleRuns)
            return;
        var s = _module.Settings;
        _sections.Children.Add(SettingsUi.Section("Caps Lock"));
        var capsSwitch = SettingsUi.Switch(s.CapsEnabled);
        AutomationProperties.SetName(capsSwitch, "Caps Lock combos");
        SettingsUi.OnSwitched(capsSwitch, on => Caps(on: on));
        var held = SettingsUi.Styled(new ComboBox { MinWidth = 180 }, "HopperComboBoxStyle");
        foreach (uint vk in new uint[] { 0x14, 0xA3, 0xA1, 0x5C })
            held.Items.Add(new ComboBoxItem { Content = CombosKey(vk), Tag = vk });
        held.SelectedItem = held.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (uint)i.Tag == s.CapsHeldKey);
        AutomationProperties.SetName(held, "Key to hold");
        held.SelectionChanged += (_, _) => { if (held.SelectedItem is ComboBoxItem { Tag: uint vk } && vk != s.CapsHeldKey) Caps(heldKey: vk); };
        var acts = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, flag) in new[] { ("Ctrl", KeyModifiers.Ctrl), ("Alt", KeyModifiers.Alt), ("Shift", KeyModifiers.Shift), ("Win", KeyModifiers.Win) })
        {
            var box = SettingsUi.Styled(new ToggleButton { Content = label, IsChecked = s.CapsModifiers.HasFlag(flag) }, "ChipToggleStyle");
            AutomationProperties.SetName(box, label);
            var captured = flag;
            box.Click += (_, _) =>
            {
                var next = box.IsChecked == true ? _module.Settings.CapsModifiers | captured : _module.Settings.CapsModifiers & ~captured;
                if (captured == KeyModifiers.Win && box.IsChecked == true)
                {
                    box.IsChecked = false;
                    ConfirmWin(() => Caps(modifiers: next));
                    return;
                }
                Caps(modifiers: next);
            };
            acts.Children.Add(box);
        }
        var tap = SettingsUi.Styled(new ComboBox { MinWidth = 180 }, "HopperComboBoxStyle");
        foreach (var (label, value) in new[] { ("Nothing", CapsTap.Nothing), ("Turn capitals on and off", CapsTap.Capitals), ("Press Escape", CapsTap.Escape), ("Switch input language", CapsTap.InputLanguage) })
            tap.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        tap.SelectedItem = tap.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (CapsTap)i.Tag == s.CapsTap);
        AutomationProperties.SetName(tap, "Tap on its own");
        tap.SelectionChanged += (_, _) => { if (tap.SelectedItem is ComboBoxItem { Tag: CapsTap t } && t != s.CapsTap) Caps(tapAction: t); };
        var combos = new Card();
        combos.Children.Add(SettingsLayout.Row("Caps Lock combos", capsSwitch, _module.CapsError));
        combos.Children.Add(Dependent("Key to hold", held));
        combos.Children.Add(Dependent("Acts as", acts));
        combos.Children.Add(Dependent("Tap on its own", tap));
        _sections.Children.Add(combos);
        _confirmHost.Margin = new Thickness(0, 8, 0, 0);
        _sections.Children.Add(_confirmHost);
        _sections.Children.Add(new ExcludedAppsCard(() => _module.Settings.CapsIgnoredApps, apps => Caps(ignored: apps)) { Margin = new Thickness(0, 8, 0, 0) });

        _sections.Children.Add(SettingsUi.Section("Mouse buttons"));
        var mouse = SettingsUi.Switch(s.MouseEnabled);
        AutomationProperties.SetName(mouse, "Mouse buttons");
        SettingsUi.OnSwitched(mouse, on => _module.SetMouseEnabled(on));
        _sections.Children.Add(SettingsLayout.Card(SettingsLayout.Row("Mouse buttons", mouse, _module.MouseError)));
        _sections.Children.Add(new ExcludedAppsCard(() => _module.Settings.MouseIgnoredApps, apps => _module.SetMouseIgnoredApps(apps)) { Margin = new Thickness(0, 8, 0, 0) });
    }

    private static FrameworkElement Dependent(string title, FrameworkElement control)
    {
        var grid = new Grid { MinHeight = SettingsLayout.RowHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = SettingsUi.Styled(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle");
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(label);
        grid.Children.Add(control);
        return new Border { Padding = SettingsLayout.DependentPadding, Child = grid };
    }

    private void ConfirmWin(Action add)
    {
        _confirmHost.Children.Clear();
        var addButton = SettingsUi.Button("Add", primary: true);
        var cancel = SettingsUi.Button("Cancel");
        cancel.Click += (_, _) => _confirmHost.Children.Clear();
        addButton.Click += (_, _) => { _confirmHost.Children.Clear(); add(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancel, addButton } };
        _confirmHost.Children.Add(SettingsLayout.Card(SettingsLayout.Row("Add Win to Caps Lock?", buttons)));
    }

    private void Caps(bool? on = null, uint? heldKey = null, KeyModifiers? modifiers = null, CapsTap? tapAction = null, IEnumerable<string>? ignored = null)
    {
        var s = _module!.Settings;
        try
        {
            _module.ConfigureCaps(on ?? s.CapsEnabled, heldKey ?? s.CapsHeldKey, modifiers ?? s.CapsModifiers, tapAction ?? s.CapsTap, ignored ?? s.CapsIgnoredApps);
        }
        catch (Exception ex)
        {
            Chippi.Core.Diagnostics.Log.Warn("Shortcuts: saving Caps Lock settings failed", ex);
        }
        BuildSections();
    }
}
