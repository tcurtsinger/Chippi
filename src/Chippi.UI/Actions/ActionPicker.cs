// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Core.Actions;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.UI.Actions;

/// <summary>What the picker offers and reads. Set by its host; the lists are read each time the fields are drawn.</summary>
public sealed record ActionPickerSource
{
    /// <summary>The Start menu's apps: their parsing names and names.</summary>
    public Func<IReadOnlyList<(string Id, string Name)>> Apps { get; init; } = () => [];

    /// <summary>The Chippi commands an action may run (<see cref="ActionRunner.ChippiCommands"/>).</summary>
    public Func<IReadOnlyList<ChippiCommand>> Commands { get; init; } = () => [];

    /// <summary>Windows Settings pages: their ms-settings: addresses and names.</summary>
    public IReadOnlyList<(string Uri, string Name)> SettingsPages { get; init; } = [];

    /// <summary>Records a Key shortcut's keys; null offers no Record.</summary>
    public IKeyRecorder? Recorder { get; init; }

    /// <summary>An App or File action's own icon, once read; null shows its glyph.</summary>
    public Func<ChippiAction, ImageSource?> Icon { get; init; } = _ => null;
}

/// <summary>
/// The fields that pick an action (docs/phase-6/architecture.md section 3): Kind, what it acts on, Name and Glyph. The
/// Radial menu's slice editor shows it, and Shortcuts will. A host can add kinds of its own after the shared ones (the
/// Radial menu's Submenu), with rows of its own in place of a value, and rows after Glyph (Remove).
/// </summary>
public sealed class ActionPicker : ContentControl
{
    private static readonly string[] Shells = ["PowerShell", "Command Prompt"];

    private readonly ActionPickerSource _source;
    private ChippiAction _action = new();
    private int? _extra;
    private IDisposable? _recording;

    public ActionPicker(ActionPickerSource source)
    {
        _source = source;
        Focusable = false;
        Unloaded += (_, _) => StopRecording();
    }

    /// <summary>
    /// A change to the action: its new kind, value, shell, name or glyph. With a host's kind (its index in the extra kinds),
    /// only the name and the glyph mean anything; picking that kind raises it with the kind's name.
    /// </summary>
    public event Action<ChippiAction, int?>? Changed;

    /// <summary>The host's own kinds, after the shared ones in the Kind list; read when the fields are drawn.</summary>
    public IReadOnlyList<string> ExtraKinds { get; set; } = [];

    /// <summary>A host's kind's rows in place of a value, by its index.</summary>
    public Func<int, IEnumerable<UIElement>>? ExtraRows { get; set; }

    /// <summary>Rows after Glyph.</summary>
    public Func<IEnumerable<UIElement>>? Footer { get; set; }

    /// <summary>Shows the action, or a host's kind (<paramref name="extra"/>) with the action's name and glyph.</summary>
    public void Show(ChippiAction action, int? extra = null)
    {
        _action = action;
        _extra = extra;
        Draw();
    }

    public void StopRecording()
    {
        _recording?.Dispose();
        _recording = null;
    }

    /// <summary>The name the action gets from what it does: a host's kind's name, or <see cref="ActionCatalog.NameFor"/>.</summary>
    private string NameFor(ChippiAction action, int? extra) => extra is int i ? ExtraKinds[i] : ActionCatalog.NameFor(action, Known);

    /// <summary>An app's, a Chippi command's or a Settings page's name, by its value.</summary>
    private string? Known(string value)
    {
        foreach (var (id, name) in _source.Apps())
            if (string.Equals(id, value, StringComparison.OrdinalIgnoreCase))
                return name;
        return _source.Commands().FirstOrDefault(c => c.Key == value)?.Name
            ?? _source.SettingsPages.FirstOrDefault(p => p.Uri == value).Name;
    }

    private string GlyphOf(ChippiAction action) => _extra is not null
        ? action.Glyph ?? nameof(LucideIcons.LayoutGrid)
        : ActionCatalog.GlyphOf(action, key => _source.Commands().FirstOrDefault(c => c.Key == key)?.Icon);

    private void Raise(ChippiAction next, int? extra = null) => Changed?.Invoke(next, extra ?? _extra);

    /// <summary>A new value, kind or shell, with the name following it unless it was changed by hand.</summary>
    private void Retarget(Func<ChippiAction, ChippiAction> change)
    {
        var next = change(_action);
        bool named = _action.Name.Length > 0 && _action.Name != NameFor(_action, _extra);
        Raise(named ? next : next with { Name = NameFor(next, _extra) });
    }

    private void Draw()
    {
        var action = _action;
        var rows = new Card();
        rows.SetResourceReference(StyleProperty, "CardRowsStyle");
        var kinds = ActionCatalog.Kinds.Select(k => k.Name).Concat(ExtraKinds).ToList();
        var kind = PickerUi.Combo("Kind", kinds);
        int shown = _extra is int e ? ActionCatalog.Kinds.Count + e : ActionCatalog.Kinds.ToList().FindIndex(k => k.Kind == action.Kind);
        kind.SelectedIndex = shown;
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedIndex >= 0 && kind.SelectedIndex != shown)
                SetKind(kind.SelectedIndex);
        };
        rows.Children.Add(SettingsLayout.Row("Kind", kind));
        foreach (var row in _extra is int extra ? ExtraRows?.Invoke(extra) ?? [] : ValueRows(action))
            rows.Children.Add(row);

        var name = PickerUi.Field("Name");
        name.Text = action.Name;
        Commit(name, text => Raise(action with { Name = text.Trim().Length > 0 ? text.Trim() : NameFor(action, _extra) }));
        rows.Children.Add(SettingsLayout.Row("Name", name));

        var glyph = new StackPanel { Orientation = Orientation.Horizontal };
        glyph.Children.Add(PickerUi.Swatch(LucideIcons.Get(GlyphOf(action)), _extra is null ? _source.Icon(action) : null));
        var change = PickerUi.Button("Change", () => { });
        change.Margin = new Thickness(12, 0, 0, 0);
        change.Click += (_, _) => PickGlyph(change, action);
        AutomationProperties.SetName(change, "Change glyph");
        glyph.Children.Add(change);
        rows.Children.Add(SettingsLayout.Row("Glyph", glyph));

        foreach (var row in Footer?.Invoke() ?? [])
            rows.Children.Add(row);
        Content = rows;
    }

    /// <summary>What a kind needs: the app, the path, the address, the keys, the text, the command and its shell, an action, a page.</summary>
    private IEnumerable<UIElement> ValueRows(ChippiAction action)
    {
        switch (action.Kind)
        {
            case ActionKind.App:
                var apps = _source.Apps();
                var names = apps.Select(a => a.Name).ToList();
                bool program = Path.IsPathFullyQualified(action.Value);
                if (program)
                    names.Insert(0, Path.GetFileNameWithoutExtension(action.Value));
                var app = PickerUi.Combo("App", names, 160);
                app.SelectedIndex = program ? 0 : apps.ToList().FindIndex(a => string.Equals(a.Id, action.Value, StringComparison.OrdinalIgnoreCase));
                app.SelectionChanged += (_, _) =>
                {
                    int i = app.SelectedIndex - (program ? 1 : 0);
                    if (i >= 0 && i < apps.Count)
                        Retarget(a => a with { Value = apps[i].Id });
                };
                yield return SettingsLayout.Row("App", Pair(app, PickerUi.Button("Browse", BrowseProgram)));
                break;
            case ActionKind.File:
                var path = PickerUi.Field("Path", 160);
                path.Text = action.Value;
                Commit(path, v => Retarget(a => a with { Value = v.Trim().Trim('"') }));
                var browse = PickerUi.Button("Browse", () => { });
                browse.Click += (_, _) => BrowseFile(browse);
                yield return SettingsLayout.Row("Path", Pair(path, browse));
                break;
            case ActionKind.Link:
                var address = PickerUi.Field("Address");
                address.Text = action.Value;
                Commit(address, v => Retarget(a => a with { Value = v.Trim() }));
                yield return SettingsLayout.Row("Address", address);
                break;
            case ActionKind.Keys:
                yield return SettingsLayout.Row("Keys", KeysField(action));
                break;
            case ActionKind.Text:
                var text = PickerUi.Field("Text");
                text.Text = action.Value;
                text.AcceptsReturn = true;
                text.AcceptsTab = false;
                text.Height = 64;
                text.TextWrapping = TextWrapping.Wrap;
                text.VerticalContentAlignment = VerticalAlignment.Top;
                text.Padding = new Thickness(0, 4, 0, 4);
                Commit(text, v => Retarget(a => a with { Value = v }), enter: false);
                var row = SettingsLayout.Row("Text", text);
                row.VerticalContentAlignment = VerticalAlignment.Top;
                yield return row;
                break;
            case ActionKind.Command:
                var shell = PickerUi.Combo("Shell", Shells);
                shell.SelectedIndex = (int)action.Shell;
                shell.SelectionChanged += (_, _) =>
                {
                    if (shell.SelectedIndex >= 0 && (CommandShell)shell.SelectedIndex != action.Shell)
                        Raise(action with { Shell = (CommandShell)shell.SelectedIndex });
                };
                yield return SettingsLayout.Row("Shell", shell);
                var line = PickerUi.Field("Command", code: true);
                line.Text = action.Value;
                Commit(line, v => Retarget(a => a with { Value = v.Trim() }));
                yield return SettingsLayout.Row("Command", line);
                break;
            case ActionKind.Chippi:
                var commands = _source.Commands();
                var chippi = PickerUi.Combo("Action", commands.Select(c => c.Name));
                chippi.SelectedIndex = commands.ToList().FindIndex(c => c.Key == action.Value);
                chippi.SelectionChanged += (_, _) =>
                {
                    if (chippi.SelectedIndex >= 0)
                        Retarget(a => a with { Value = commands[chippi.SelectedIndex].Key });
                };
                yield return SettingsLayout.Row("Action", chippi);
                break;
            case ActionKind.Media:
                yield return SettingsLayout.Row("Action", Actions(action, [.. ActionCatalog.Media.Select(m => (m.Action.ToString(), m.Name))]));
                break;
            case ActionKind.Window:
                yield return SettingsLayout.Row("Action", WindowActions(action));
                break;
            case ActionKind.Selection:
                yield return SettingsLayout.Row("Action", Actions(action, [.. ActionCatalog.Selection.Select(m => (m.Action.ToString(), m.Name))]));
                break;
            case ActionKind.SettingsPage:
                yield return SettingsLayout.Row("Page", Actions(action, _source.SettingsPages, "Page"));
                break;
        }
    }

    private ComboBox WindowActions(ChippiAction action)
    {
        var options = ActionCatalog.Window.Select(w => (Action: new ChippiAction { Kind = ActionKind.Window, Value = w.Action.ToString() }, w.Name))
            .Concat(_source.Commands().Where(c => c.WindowAction?.Value == nameof(WindowAction.Zone))
                .Select(c => (Action: c.WindowAction!, Name: c.Name))).ToList();
        var combo = PickerUi.Combo("Action", options.Select(o => o.Name));
        combo.SelectedIndex = options.FindIndex(o => o.Action.Value == action.Value
            && o.Action.LayoutId == action.LayoutId && o.Action.ZoneId == action.ZoneId);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0) return;
            var next = options[combo.SelectedIndex].Action;
            Retarget(a => a with { Value = next.Value, LayoutId = next.LayoutId, ZoneId = next.ZoneId });
        };
        return combo;
    }

    private ComboBox Actions(ChippiAction action, IReadOnlyList<(string Value, string Name)> actions, string label = "Action")
    {
        var combo = PickerUi.Combo(label, actions.Select(a => a.Name));
        combo.SelectedIndex = actions.ToList().FindIndex(a => a.Value == action.Value);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && actions[combo.SelectedIndex].Value != action.Value)
                Retarget(a => a with { Value = actions[combo.SelectedIndex].Value });
        };
        return combo;
    }

    /// <summary>
    /// A new kind, by its place in the Kind list, starts with its first action, or nothing to fill in. A host's kind
    /// starts with its name.
    /// </summary>
    private void SetKind(int index)
    {
        StopRecording();
        if (index >= ActionCatalog.Kinds.Count)
        {
            int extra = index - ActionCatalog.Kinds.Count;
            Raise(new ChippiAction { Name = ExtraKinds[extra], Shell = _action.Shell }, extra);
            return;
        }
        var kind = ActionCatalog.Kinds[index].Kind;
        string value = kind switch
        {
            ActionKind.Media => nameof(MediaAction.PlayPause),
            ActionKind.Window => nameof(WindowAction.SnapLeft),
            ActionKind.Selection => nameof(SelectionAction.Search),
            ActionKind.SettingsPage => _source.SettingsPages.FirstOrDefault().Uri ?? "",
            ActionKind.Chippi => _source.Commands().FirstOrDefault()?.Key ?? "",
            ActionKind.App => _source.Apps().FirstOrDefault().Id ?? "",
            _ => "",
        };
        var next = new ChippiAction { Kind = kind, Value = value, Shell = _action.Shell };
        Changed?.Invoke(next with { Name = NameFor(next, null) }, null);
    }

    private UIElement KeysField(ChippiAction action)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (_recording is not null)
        {
            row.Children.Add(PickerUi.Text("Press keys", "TextSecondaryBrush"));
            var cancel = PickerUi.Button("Cancel", () =>
            {
                StopRecording();
                Draw();
            });
            cancel.Margin = new Thickness(12, 0, 0, 0);
            row.Children.Add(cancel);
            return row;
        }
        if (KeyCombo.Parse(action.Value) is { } keys)
            row.Children.Add(PickerUi.Caps(keys.Caps));
        else
            row.Children.Add(PickerUi.Text("Not set", "TextSecondaryBrush"));
        if (_source.Recorder is null)
            return row;
        var record = PickerUi.Button("Record", () => { });
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
            _recording = _source.Recorder!.Record(keys =>
            {
                // Recorded, the recorder lets go of the keyboard itself once the keys are up.
                _recording = null;
                if (keys is { } recorded)
                    Retarget(a => a with { Value = recorded.ToString() });
                else
                    Draw();
            });
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Warn("Action picker: recording keys failed", ex);
            _recording = null;
        }
        Draw();
    }

    private void BrowseProgram()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Programs (*.exe, *.lnk, *.bat, *.cmd)|*.exe;*.lnk;*.bat;*.cmd|All files|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            Retarget(a => a with { Value = dialog.FileName });
    }

    private void BrowseFile(Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        var file = new MenuItem { Header = "File…" };
        file.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { CheckFileExists = true };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                Retarget(a => a with { Value = dialog.FileName });
        };
        var folder = new MenuItem { Header = "Folder…" };
        folder.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = false };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                Retarget(a => a with { Value = dialog.FolderName });
        };
        menu.Items.Add(file);
        menu.Items.Add(folder);
        menu.IsOpen = true;
    }

    private void PickGlyph(Button anchor, ChippiAction action)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var grid = new UniformGrid { Columns = 8 };
        foreach (string name in ActionCatalog.Glyphs)
        {
            var button = new Button { Content = PickerUi.Swatch(LucideIcons.Get(name)), Margin = new Thickness(2), Padding = new Thickness(0), Width = 36, Height = 36 };
            button.SetResourceReference(StyleProperty, "SubtleButtonStyle");
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) =>
            {
                popup.IsOpen = false;
                Raise(action with { Glyph = name });
            };
            grid.Children.Add(button);
        }
        var reset = PickerUi.Button(_extra is null && action.Kind is ActionKind.App or ActionKind.File ? "Use its icon" : "Use the kind's", () =>
        {
            popup.IsOpen = false;
            Raise(action with { Glyph = null });
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
