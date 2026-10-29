// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.UI.Controls;
using Microsoft.Win32;

namespace Chippi.Features.Scratchpad;

/// <summary>
/// Settings › Scratchpad (2026-10-28, brief F3 #10): the save status beside the title, the note tabs and +,
/// the note, then Edit / Preview, Export… and Delete note. It shares the island page's session, so an edit
/// in one shows in the other.
/// </summary>
public sealed class ScratchpadSettingsPage : UserControl
{
    private const double EditorHeight = 320;
    private readonly ScratchpadSession _session;
    private readonly Action<Window?, ScratchpadNote> _export;
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly SegmentedControl _tabs = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly IconButton _add = new() { Icon = LucideIcons.Plus, ToolTip = "New note" };
    private readonly TextBox _editor = new() { AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = EditorHeight, Padding = new Thickness(12, 10, 12, 10), BorderThickness = new Thickness(0) };
    private readonly ScrollViewer _preview = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = EditorHeight, Padding = new Thickness(12, 10, 12, 10), Visibility = Visibility.Collapsed };
    private readonly SegmentedControl _mode = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _delete;
    private readonly StackPanel _body = new();
    private bool _painting, _listening, _previewing, _exportFailed;
    private long _shownId;
    private string _tabsStamp = "";
    private Popup? _confirm;

    public ScratchpadSettingsPage(ScratchpadSession session, Action<Window?, ScratchpadNote>? export = null)
    {
        _session = session;
        _export = export ?? ExportFile;
        _status.SetResourceReference(StyleProperty, "BodyTextStyle");
        SettingsLayout.SetTitleAside(this, _status);
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        _tabs.SetResourceReference(StyleProperty, "SelectorBarStyle");
        _tabs.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "SelectorBarItemStyle");
        _tabs.DisplayMemberPath = nameof(SegmentItem.Label);
        AutomationProperties.SetName(_tabs, "Notes");
        _tabs.SelectionChanged += async (_, _) => { if (!_painting && _tabs.SelectedItem is SegmentItem { Value: long id }) await Run(_session.ChangeAsync(d => d.Select(id))); };
        AutomationProperties.SetName(_add, "New note");
        _add.Margin = new Thickness(4, 0, 0, 0);
        _add.Click += async (_, _) => { await Run(_session.ChangeAsync(d => d.Add())); _mode.SelectedIndex = 0; _editor.Focus(); };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Children = { _tabs, _add } };

        _editor.TextChanged += async (_, _) => { if (!_painting) { _exportFailed = false; await Run(_session.EditAsync(_editor.Text)); } };
        _editor.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        _editor.SetResourceReference(TextBoxBase.CaretBrushProperty, "TextPrimaryBrush");
        _editor.SetResourceReference(Control.FontFamilyProperty, "UiFont");
        _editor.FontSize = 14;
        _editor.Background = System.Windows.Media.Brushes.Transparent;
        AutomationProperties.SetName(_editor, "Note");
        var note = SettingsLayout.Card(new Grid { Children = { _editor, _preview } });

        _mode.Items.Add("Edit");
        _mode.Items.Add("Preview");
        _mode.SelectedIndex = 0;
        AutomationProperties.SetName(_mode, "View");
        _mode.SelectionChanged += (_, _) => SetPreview(_mode.SelectedIndex == 1);
        var exportButton = Button("Export…");
        exportButton.Click += async (_, _) => await ExportAsync();
        _delete = Button("Delete note", LucideIcons.Trash);
        _delete.Margin = new Thickness(8, 0, 0, 0);
        _delete.Click += (_, _) => ConfirmDelete().IsOpen = true;
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Children = { exportButton, _delete } };
        DockPanel.SetDock(commands, Dock.Right);
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0), Children = { commands, _mode } };

        _body.Children.Add(tabs);
        _body.Children.Add(note);
        _body.Children.Add(footer);
        Content = _body;
        Loaded += async (_, _) => { Subscribe(); await Run(_session.LoadAsync()); Paint(); };
        Unloaded += async (_, _) => { if (_confirm is not null) _confirm.IsOpen = false; Unsubscribe(); await Run(_session.FlushAsync()); };
        Paint();
    }

    private void Subscribe() { if (_listening) return; _listening = true; _session.Changed += Changed; }
    private void Unsubscribe() { if (!_listening) return; _listening = false; _session.Changed -= Changed; }
    private void Changed(ScratchpadState state) => Dispatcher.BeginInvoke(Paint);
    private static async Task Run(Task operation) { try { await operation; } catch (ObjectDisposedException) { } }

    private void Paint()
    {
        var state = _session.State;
        _painting = true;
        try
        {
            bool failed = _exportFailed || state.LoadFailed || state.SaveFailed;
            _status.Text = _exportFailed ? "Export failed" : state.LoadFailed ? "Load failed" : state.SaveFailed ? "Save failed" : state.Document is null ? "" : "Saved";
            _status.SetResourceReference(TextBlock.ForegroundProperty, failed ? "WarningTextBrush" : "TextSecondaryBrush");
            _body.Visibility = state.Document is null ? Visibility.Collapsed : Visibility.Visible;
            if (state.Document is not { } doc)
                return;
            // The other surface's edits show here, except under the person's own typing.
            if (_shownId != doc.SelectedId || (!_editor.IsKeyboardFocusWithin && _editor.Text != doc.Selected.Text))
            {
                _shownId = doc.SelectedId;
                _editor.Text = doc.Selected.Text;
                _editor.CaretIndex = _editor.Text.Length;
            }
            var stamp = doc.SelectedId + ":" + string.Join("|", doc.Notes.Select(n => n.Id + ":" + n.Name));
            if (_tabsStamp != stamp)
            {
                _tabsStamp = stamp;
                _tabs.Items.Clear();
                foreach (var each in doc.Notes)
                    _tabs.Items.Add(new SegmentItem { Label = each.Name, Value = each.Id });
                _tabs.SelectedItem = _tabs.Items.Cast<SegmentItem>().First(n => (long)n.Value! == doc.SelectedId);
            }
            _add.IsEnabled = doc.Notes.Count < ScratchpadDocument.MaximumNotes;
            if (_previewing)
                _preview.Content = ScratchpadMarkdown.Render(_editor.Text);
        }
        finally { _painting = false; }
    }

    private void SetPreview(bool preview)
    {
        _previewing = preview;
        if (preview)
        {
            _preview.Content = ScratchpadMarkdown.Render(_editor.Text);
            _ = Run(_session.FlushAsync());
        }
        _editor.Visibility = preview ? Visibility.Collapsed : Visibility.Visible;
        _preview.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Delete "<name>"? with Cancel and Delete, above the button. Created closed for windowless checks.</summary>
    internal Popup ConfirmDelete()
    {
        if (_confirm is not null)
            _confirm.IsOpen = false;
        var popup = _confirm = new Popup { PlacementTarget = _delete, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true };
        if (_session.State.Document is not { } doc)
            return popup;
        var cancel = Button("Cancel");
        var delete = Button("Delete");
        delete.SetResourceReference(StyleProperty, "CriticalButtonStyle");
        delete.Margin = new Thickness(8, 0, 0, 0);
        cancel.Click += (_, _) => popup.IsOpen = false;
        long selected = doc.SelectedId;
        delete.Click += async (_, _) =>
        {
            popup.IsOpen = false;
            await Run(_session.ChangeAsync(d => d.SelectedId == selected ? d.DeleteSelected() : d));
            _shownId = 0;
            Paint();
        };
        var question = new TextBlock { Text = $"Delete “{doc.Selected.Name}”?", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        question.SetResourceReference(StyleProperty, "BodyTextStyle");
        var border = new Border
        {
            Padding = new Thickness(16), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Width = 260,
            Child = new StackPanel { Children = { question, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, delete } } } },
        };
        border.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        popup.Child = border;
        popup.Opened += (_, _) => cancel.Focus();
        return popup;
    }

    public async Task ExportAsync()
    {
        await Run(_session.FlushAsync());
        if (_session.State.Document is not { } doc)
            return;
        try
        {
            _exportFailed = false;
            _export(Window.GetWindow(this), doc.Selected);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _exportFailed = true;
        }
        Paint();
    }

    private static void ExportFile(Window? owner, ScratchpadNote note)
    {
        var dialog = new SaveFileDialog { Filter = "Text (*.txt)|*.txt|Markdown (*.md)|*.md", DefaultExt = ".txt", AddExtension = true, FileName = "Note.txt" };
        if ((owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true)
            File.WriteAllText(dialog.FileName, note.Text);
    }

    private static Button Button(string label, System.Windows.Media.Geometry? glyph = null)
    {
        var button = new Button { Content = label };
        if (glyph is not null)
        {
            var icon = new LucideIcon { Data = glyph, Size = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(LucideIcon.BrushProperty, "TextPrimaryBrush");
            button.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } } };
        }
        button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        AutomationProperties.SetName(button, label);
        return button;
    }
}
