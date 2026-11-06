// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
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
/// Settings › Shortcuts (designs/settings-shortcuts/v2.html): every shortcut in one list, the features' own under the
/// sidebar's groups (System, Windows, Capture…) and the owner's bindings from the Shortcuts feature under Your shortcuts,
/// with their keys in one column; then Add shortcut, the Caps Lock combos and Mouse buttons. A shortcut that clashes is
/// refused where it's recorded, with one line by Save.
/// </summary>
internal sealed class ShortcutsPage : UserControl
{
    /// <summary>Shortcuts from no installed feature, after the feature groups.</summary>
    private const string ChippiGroup = "Chippi";
    private const string YourGroup = "Your shortcuts";
    private const double KeysColumn = 300;
    private const double OptionsColumn = 44;
    /// <summary>The editor's fields span the keys' and options' columns, so they start where the keycaps do.</summary>
    private const double FieldWidth = KeysColumn + OptionsColumn;

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

        /// <summary>The row it edits, which it takes the place of; null for a new shortcut, edited under the list.</summary>
        public string? RowKey => Mode switch
        {
            EditMode.Chippi => ChippiKey(EntryId),
            EditMode.Binding => BindingKey(BindingId),
            _ => null,
        };
    }

    /// <param name="Feature">The second line: its feature's name, when that feature offers more than one shortcut.</param>
    private sealed record Row(string Key, string Group, string Name, string? Feature, string? Icon, IReadOnlyList<string> Caps, bool Mouse, bool Dim, bool Unset, Action Change, Action? Remove, Action? Another = null);

    private static string ChippiKey(long id) => "chippi:" + id;
    private static string BindingKey(string? id) => "binding:" + id;

    private readonly ShellContext _shell;
    private readonly ShortcutsModule? _module;
    private readonly Chippi.Features.CommandBar.Windows.WindowsPc _pc = new();
    private readonly StackPanel _page = new();
    private readonly Border _listHost = new();
    private StackPanel? _editorView;
    private TextBlock _title = new();
    private LucideIcon _titleIcon = new() { Size = 18, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
    private StackPanel _messagePanel = new();
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
        Recorder = new KeyRecorder(() => shell.Input);
        _page.Children.Add(SettingsUi.Header("Shortcuts", ""));
        _page.Children.Add(_listHost);
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

    /// <summary>Records keys for the editor and the Other… picker; tests stand in for the input hub's.</summary>
    internal IKeyRecorder Recorder { get; set; }

    private bool ModuleRuns => _module is not null && _shell.Registry.Features.Any(f => f.Id == ShortcutsModule.Id && f.IsRunning);

    private void RebuildAll()
    {
        RebuildLists();
        BuildSections();
    }

    // ---- the list ----

    /// <summary>
    /// The rows in the list's order: the features' groups in the sidebar's order, features by name within a group and
    /// each feature's shortcuts in the order it added them; then any from no installed feature, then Your shortcuts.
    /// </summary>
    private IEnumerable<Row> Rows()
    {
        var ordered = _shell.Shortcuts.List.Where(e => e.Editable)
            .Select(e => (Entry: e, Feature: FeatureOf(e)))
            .OrderBy(x => x.Feature is { } f ? (int)f.Group : int.MaxValue)
            .ThenBy(x => x.Feature?.Name ?? "", StringComparer.CurrentCultureIgnoreCase);
        foreach (var (e, feature) in ordered)
        {
            var entry = e;
            string group = feature?.Group.DisplayName() ?? ChippiGroup;
            yield return new Row(ChippiKey(entry.Id), group, entry.Name, FeatureLine(entry), entry.Icon, entry.Shortcut is { } k ? Keycaps.CapsOf(k) : [], false, !entry.Held, entry.Shortcut is null,
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
                BindingDoor.Key => [.. Keycaps.CapsOf(binding.Keys!)],
                BindingDoor.Caps => [CombosKey(_module.Settings.CapsHeldKey), KeyCombo.NameOf(binding.CapsKey)],
                _ => [MouseName(binding.Button)],
            };
            yield return new Row(BindingKey(binding.Id), YourGroup, binding.Label, null, ActionCatalog.GlyphOf(binding.Action), caps, binding.Door == BindingDoor.Mouse, !status.Acts, false,
                () => StartEdit(new Edit
                {
                    Mode = EditMode.Binding, BindingId = binding.Id, Action = binding.Action, Door = binding.Door, Keys = binding.Keys, CapsKey = binding.CapsKey,
                    Button = binding.Door == BindingDoor.Mouse ? binding.Button : null,
                }),
                () => _module.Remove(binding.Id),
                () => StartEdit(new Edit { Mode = EditMode.New, Action = binding.Action, Door = BindingDoor.Key }));
        }
    }

    private FeatureDescriptor? FeatureOf(ShortcutEntry entry) => _shell.Registry.Features.FirstOrDefault(f => f.Id == entry.Owner)?.Descriptor;

    /// <summary>The row's second line: its feature's name, when that feature offers more than one shortcut.</summary>
    private string? FeatureLine(ShortcutEntry entry) =>
        FeatureOf(entry) is { } feature && feature.Name != entry.Name && _shell.Shortcuts.List.Count(e => e.Editable && e.Owner == entry.Owner) > 1 ? feature.Name : null;

    /// <summary>A row's name over its feature's, in the caption style; the name alone without one.</summary>
    private static FrameworkElement Named(TextBlock name, string? feature)
    {
        if (feature is null)
            return name;
        var caption = SettingsUi.Styled(new TextBlock { Text = feature, TextTrimming = TextTrimming.CharacterEllipsis }, "CaptionTextStyle");
        return new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, caption } };
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
        if (_editorView?.Parent is Panel old)
            old.Children.Remove(_editorView);
        var card = new Card();
        if (rows.Count == 0)
            card.Children.Add(SettingsLayout.Empty("No shortcuts yet. Add the first one."));
        foreach (var group in rows.GroupBy(r => r.Group))
        {
            var heading = SettingsUi.Styled(new TextBlock { Text = group.Key, VerticalAlignment = VerticalAlignment.Center }, "CaptionTextStyle");
            card.Children.Add(new Border { MinHeight = 36, Padding = new Thickness(16, 8, 16, 0), Child = heading });
            // Record and Change edit in the row's place; a new shortcut, or one whose row has gone, under the list.
            foreach (var row in group)
                card.Children.Add(_editorView is not null && row.Key == _edit?.RowKey ? _editorView : RowView(row));
        }
        if (_editorView is not null)
        {
            if (_editorView.Parent is null)
                card.Children.Add(_editorView);
        }
        else if (ModuleRuns)
        {
            var add = SettingsUi.Button("Add shortcut");
            AutomationProperties.SetName(add, "Add shortcut");
            add.Click += (_, _) => StartEdit(new Edit { Mode = EditMode.New, Door = BindingDoor.Key, Action = new ChippiAction() });
            string count = rows.Count == 1 ? "1 shortcut" : $"{rows.Count} shortcuts";
            card.Children.Add(SettingsLayout.Row(count, add));
        }
        _listHost.Child = card;
    }

    private FrameworkElement RowView(Row row)
    {
        var grid = new Grid { MinHeight = SettingsLayout.RowHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(KeysColumn) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(OptionsColumn) });
        var icon = new LucideIcon { Data = LucideIcons.Get(row.Icon), Size = 18, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(LucideIcon.BrushProperty, "TextPrimaryBrush");
        var name = Named(SettingsUi.Styled(new TextBlock { Text = row.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, "BodyTextStyle"), row.Feature);
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
            keys = Keycaps.Of(row.Caps, row.Mouse, row.Dim);
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

    // ---- the editor ----

    private void StartEdit(Edit edit, bool listen = true)
    {
        StopListening();
        _edit = edit;
        BuildEditor();
        if (listen)
            Listen();
        ShowEditor();
    }

    /// <summary>
    /// A feature's Edit link (<c>shortcuts/&lt;module id&gt;/&lt;name&gt;</c> past the page): the editor open in that
    /// shortcut's row, recording as Change does. The list alone when the feature lists no such shortcut.
    /// </summary>
    internal void OpenEditor(string ownerAndName, bool listen = true)
    {
        int slash = ownerAndName.IndexOf('/');
        string owner = slash < 0 ? ownerAndName : ownerAndName[..slash];
        string? name = slash < 0 ? null : ownerAndName[(slash + 1)..];
        var owned = _shell.Shortcuts.List.Where(e => e.Editable && e.Owner == owner).ToList();
        if ((owned.FirstOrDefault(e => e.Name == name) ?? (owned.Count == 1 ? owned[0] : null)) is not { } entry)
        {
            Chippi.Core.Diagnostics.Log.Warn($"Shortcuts: no shortcut '{name}' from {owner} to edit");
            return;
        }
        StartEdit(new Edit { Mode = EditMode.Chippi, EntryId = entry.Id, EntryName = entry.Name, Door = BindingDoor.Key, Keys = entry.Shortcut }, listen);
    }

    /// <summary>Once laid out: the editor scrolled into view and, editing a row, the keyboard on its keys.</summary>
    private void ShowEditor()
    {
        var view = _editorView;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (view is null || !ReferenceEquals(view, _editorView) || !view.IsVisible)
                return;
            view.BringIntoView();
            if (_edit is { Mode: not EditMode.New })
                _inputRow.Child?.Focus();
        });
    }

    private void CloseEditor()
    {
        StopListening();
        _edit = null;
        _editorView = null;
        RebuildLists();
    }

    private void StopListening()
    {
        _listening?.Dispose();
        _listening = null;
        if (_edit is not null) _edit.Listening = false;
    }

    /// <summary>The editor's rows, inside the list's card: the action, the input, then the line by Save and the buttons.</summary>
    private void BuildEditor()
    {
        var edit = _edit!;
        _building = true;
        var view = new StackPanel();
        view.SetResourceReference(Panel.BackgroundProperty, "HoverBrush");
        _titleIcon = new LucideIcon { Size = 18, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _titleIcon.SetResourceReference(LucideIcon.BrushProperty, "TextPrimaryBrush");
        _title = SettingsUi.Styled(new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, "BodyTextStyle");
        if (edit.Mode != EditMode.Chippi)
        {
            var actionBox = new StackPanel();
            var otherHost = new Border { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
            var dropdown = new ActionDropdown(Items, chosen =>
            {
                edit.Action = chosen;
                otherHost.Visibility = Visibility.Collapsed;
                Titled(edit);
                RefreshSave();
            }, () =>
            {
                edit.Action = new ChippiAction();
                ShowOther(otherHost, edit);
                Titled(edit);
                RefreshSave();
            }, FieldWidth);
            if (OtherKind(edit.Action))
            {
                dropdown.Show(null, "Other…");
                ShowOther(otherHost, edit);
            }
            else if (edit.Action.Value.Length > 0)
                dropdown.Show(new ActionDropdown.Item("", edit.Action.Name.Length > 0 ? edit.Action.Name : ActionCatalog.NameFor(edit.Action), ActionCatalog.GlyphOf(edit.Action), edit.Action), null);
            actionBox.Children.Add(dropdown);
            actionBox.Children.Add(otherHost);
            Titled(edit);
            view.Children.Add(EditorRow(_titleIcon, _title, actionBox));

            var segments = SettingsUi.Segments("Input", new[] { ("Keys", BindingDoor.Key), ("Caps Lock", BindingDoor.Caps), ("Mouse button", BindingDoor.Mouse) }, edit.Door);
            segments.SelectionChanged += (_, _) =>
            {
                if (_building || segments.SelectedItem is not SegmentItem { Value: BindingDoor door } || door == edit.Door) return;
                edit.Door = door;
                edit.Error = null;
                Listen();
            };
            segments.Margin = new Thickness(0, 0, 12, 0);
            _inputRow = new Border();
            var label = SettingsUi.Styled(new TextBlock { Text = "Input", VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle");
            view.Children.Add(EditorRow(null, label, _inputRow, segments));
        }
        else
        {
            var entry = _shell.Shortcuts.List.FirstOrDefault(e => e.Id == edit.EntryId);
            _title.Text = edit.EntryName;
            _titleIcon.Data = LucideIcons.Get(entry?.Icon);
            _titleIcon.Visibility = Visibility.Visible;
            _inputRow = new Border();
            view.Children.Add(EditorRow(_titleIcon, Named(_title, entry is null ? null : FeatureLine(entry)), _inputRow));
        }

        _messagePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var glyph = new LucideIcon { Data = LucideIcons.TriangleAlert, Size = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "WarningTextBrush");
        _message = SettingsUi.Styled(new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }, "BodyTextStyle");
        _message.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Polite);
        _messagePanel.Children.Add(glyph);
        _messagePanel.Children.Add(_message);
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
        footer.Children.Add(_messagePanel);
        view.Children.Add(footer);

        _editorView = view;
        _building = false;
        RebuildLists();
        RebuildEditorInput();
    }

    /// <summary>The row's title: "New shortcut" until an action is chosen, then its icon and name.</summary>
    private void Titled(Edit edit)
    {
        if (edit.Action.Value.Length == 0)
        {
            _title.Text = edit.Mode == EditMode.New ? "New shortcut" : "Action";
            _titleIcon.Data = LucideIcons.Plus;
            _titleIcon.Visibility = edit.Mode == EditMode.New ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        _title.Text = edit.Action.Name.Length > 0 ? edit.Action.Name : ActionCatalog.NameFor(edit.Action);
        _titleIcon.Data = LucideIcons.Get(ActionCatalog.GlyphOf(edit.Action));
        _titleIcon.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// A row in the editor, on the list rows' columns: an optional icon, the title (and a switcher at its right), and the
    /// control from the keys' column's left edge to the options column's right edge.
    /// </summary>
    private static FrameworkElement EditorRow(FrameworkElement? icon, FrameworkElement title, FrameworkElement control, FrameworkElement? middle = null)
    {
        var grid = new Grid { MinHeight = SettingsLayout.RowHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(KeysColumn) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(OptionsColumn) });
        if (icon is not null) { icon.HorizontalAlignment = HorizontalAlignment.Left; grid.Children.Add(icon); }
        var left = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        if (middle is not null) { DockPanel.SetDock(middle, Dock.Right); middle.VerticalAlignment = VerticalAlignment.Center; left.Children.Add(middle); }
        left.Children.Add(title);
        Grid.SetColumn(left, 1);
        grid.Children.Add(left);
        Grid.SetColumn(control, 2);
        Grid.SetColumnSpan(control, 2);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
        return new Border { Padding = SettingsLayout.RowPadding, Child = grid };
    }

#if DEBUG
    /// <summary>
    /// Debug snapshots: shows the editor in a state without recording, so a shot never takes the keyboard.
    /// "new", "listening", "caps", "ready", "refused"; null closes it.
    /// </summary>
    internal void ShowEditorForShot(string? state)
    {
        StopListening();
        if (state is null) { CloseEditor(); return; }
        var keepOnTop = new ChippiAction { Kind = ActionKind.Window, Value = nameof(WindowAction.KeepOnTop), Name = "Keep on top" };
        _edit = state switch
        {
            "new" => new Edit { Mode = EditMode.New, Door = BindingDoor.Key },
            "listening" => new Edit { Mode = EditMode.New, Door = BindingDoor.Key, Action = keepOnTop, Listening = true },
            "caps" => new Edit { Mode = EditMode.New, Door = BindingDoor.Caps, Action = keepOnTop, Listening = true },
            "ready" => new Edit { Mode = EditMode.New, Door = BindingDoor.Caps, Action = keepOnTop, CapsKey = 0x31 },
            _ => new Edit { Mode = EditMode.New, Door = BindingDoor.Key, Action = keepOnTop, Keys = new KeyShortcut(ShortcutModifiers.Windows, 0x59), Error = "Another app uses this" },
        };
        BuildEditor();
    }
#endif

    private static bool OtherKind(ChippiAction a) => a.Value.Length > 0 && a.Kind is ActionKind.App or ActionKind.File or ActionKind.Link or ActionKind.Keys or ActionKind.Text or ActionKind.Command;

    private void ShowOther(Border host, Edit edit)
    {
        var picker = new ActionPicker(new ActionPickerSource
        {
            Apps = () => [.. _pc.Apps.Select(a => (a.ParsingName, a.Name))],
            Commands = () => [.. _shell.Registry.Commands().Where(c => c.Kind != ChippiCommandKind.Paste)],
            SettingsPages = [.. SettingsPages.All.Select(p => (p.Uri, p.Name))],
            Recorder = Recorder,
        });
        picker.Show(edit.Action);
        picker.Changed += (action, _) => { edit.Action = action; RefreshSave(); };
        host.Child = picker;
        host.Visibility = Visibility.Visible;
    }

    /// <summary>The dropdown's actions, grouped: Window, Media, Selected text, Chippi and Settings page.</summary>
    private IReadOnlyList<ActionDropdown.Item> Items()
    {
        var items = new List<ActionDropdown.Item>();
        foreach (var (action, name, glyph) in ActionCatalog.Window)
            items.Add(new("Window", name, glyph, new ChippiAction { Kind = ActionKind.Window, Value = action.ToString(), Name = name }));
        foreach (var (action, name, glyph) in ActionCatalog.Media)
            items.Add(new("Media", name, glyph, new ChippiAction { Kind = ActionKind.Media, Value = action.ToString(), Name = name }));
        foreach (var (action, name, glyph) in ActionCatalog.Selection)
            items.Add(new("Selected text", name, glyph, new ChippiAction { Kind = ActionKind.Selection, Value = action.ToString(), Name = name }));
        foreach (var c in _shell.Registry.Commands().Where(c => c.Kind != ChippiCommandKind.Paste))
            items.Add(new("Chippi", c.Name, c.Icon, new ChippiAction { Kind = ActionKind.Chippi, Value = c.Key, Name = c.Name }));
        foreach (var p in SettingsPages.All)
            items.Add(new("Settings page", p.Name, "Settings", new ChippiAction { Kind = ActionKind.SettingsPage, Value = p.Uri, Name = p.Name }));
        return items;
    }

    private void RebuildEditorInput()
    {
        var edit = _edit;
        if (edit is null) return;
        // A refusal's border is 2px; the padding takes the extra pixel, so the keys don't move.
        bool refused = edit.Error is not null;
        var field = new Border { Width = FieldWidth, MinHeight = 32, Padding = refused ? new Thickness(9, 3, 9, 3) : new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(refused ? 2 : 1), HorizontalAlignment = HorizontalAlignment.Stretch };
        field.SetResourceReference(Border.BorderBrushProperty, edit.Error is not null ? "WarningTextBrush" : edit.Listening ? "AccentBrush" : "BorderStrongBrush");
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        IReadOnlyList<string> caps = edit.Door switch
        {
            BindingDoor.Key => edit.Keys is { } k ? Keycaps.CapsOf(k) : [],
            BindingDoor.Caps => edit.CapsKey != 0 ? [CombosKey(_module?.Settings.CapsHeldKey ?? 0x14), KeyCombo.NameOf(edit.CapsKey)] : [],
            _ => edit.Button is { } b ? [MouseName(b)] : [],
        };
        if (caps.Count > 0)
            content.Children.Add(Keycaps.Of(caps, edit.Door == BindingDoor.Mouse));
        else
        {
            // Caps Lock: the held key's cap, then the prompt for the key pressed with it.
            if (edit.Door == BindingDoor.Caps)
            {
                var held = Keycaps.Of([CombosKey(_module?.Settings.CapsHeldKey ?? 0x14)]);
                held.Margin = new Thickness(0, 0, 4, 0);
                content.Children.Add(held);
                var plus = SettingsUi.Styled(new TextBlock { Text = "+", Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle");
                plus.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
                content.Children.Add(plus);
            }
            string prompt = edit.Door switch
            {
                BindingDoor.Key => "Press a shortcut",
                BindingDoor.Caps => "Press a key",
                _ => "Press a mouse button or tilt the wheel",
            };
            var hint = SettingsUi.Styled(new TextBlock { Text = prompt, VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle");
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            content.Children.Add(hint);
        }
        field.Child = content;
        // A button, so the keyboard and UI Automation can start recording again too.
        var press = SettingsUi.Styled(new Button { Content = field, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }, "SubtleButtonStyle");
        press.Click += (_, _) => Listen();
        AutomationProperties.SetName(press, "Shortcut");
        _inputRow.Child = press;
        RefreshSave();
    }

    private static bool Complete(Edit e) => (e.Mode == EditMode.Chippi || e.Action.Value.Length > 0) && e.Door switch
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
        _messagePanel.Visibility = edit.Error is null ? Visibility.Collapsed : Visibility.Visible;
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
                : Recorder.Record(combo => Recorded(edit, combo, null));
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
            {
                // The held key (and a modifier) isn't the combo's key: keep listening for the one pressed with it.
                if (c.Key == (_module?.Settings.CapsHeldKey ?? 0x14) || KeyCombo.ModifierOf(c.Key) != KeyModifiers.None)
                {
                    Listen();
                    return;
                }
                edit.CapsKey = c.Key;
            }
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
            bool reverting = false;
            // A UIA Toggle raises Checked and Unchecked, not Click.
            void Changed(object sender, RoutedEventArgs e)
            {
                if (reverting) return;
                var next = box.IsChecked == true ? _module.Settings.CapsModifiers | captured : _module.Settings.CapsModifiers & ~captured;
                if (captured == KeyModifiers.Win && box.IsChecked == true)
                {
                    reverting = true;
                    box.IsChecked = false;
                    reverting = false;
                    ConfirmWin(() => Caps(modifiers: next));
                    return;
                }
                Caps(modifiers: next);
            }
            box.Checked += Changed;
            box.Unchecked += Changed;
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
        ((SettingRow)combos.Children[0]).Icon = LucideIcons.ArrowUp;
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
        var mouseRow = SettingsLayout.Row("Mouse buttons", mouse, _module.MouseError);
        mouseRow.Icon = LucideIcons.Mouse;
        _sections.Children.Add(SettingsLayout.Card(mouseRow));
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
