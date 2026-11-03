// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Chippi.Features.Scratchpad;

/// <summary>The editor belongs to one view. A queued edit belongs to the note on screen at the keystroke.</summary>
internal sealed class ScratchpadEditor : Grid
{
    private readonly ScratchpadSession _session;
    private readonly TextBox _field;
    private readonly ScrollViewer _preview;
    private readonly Dictionary<long, (int Start, int Length, double Offset)> _positions = [];
    private string? _rendered;
    private bool _painting;

    internal ScratchpadEditor(ScratchpadSession session, bool widget)
    {
        _session = session;
        var pad = widget ? new Thickness(11, 9, 11, 9) : new Thickness(12, 10, 12, 10);
        _field = new TextBox
        {
            AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Padding = pad, FontSize = widget ? 13 : 14,
        };
        _field.SetResourceReference(Control.ForegroundProperty, widget ? "IslandTextBrush" : "TextPrimaryBrush");
        _field.SetResourceReference(TextBoxBase.CaretBrushProperty, widget ? "IslandTextBrush" : "TextPrimaryBrush");
        _field.SetResourceReference(TextBoxBase.SelectionBrushProperty, widget ? "IslandAccentBrush" : "AccentBrush");
        _field.SetResourceReference(Control.FontFamilyProperty, "UiFont");
        TextBlock.SetLineHeight(_field, widget ? 20 : 21);
        AutomationProperties.SetName(_field, "Note");
        _field.TextChanged += async (_, _) => await OnEditAsync();
        _preview = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = pad,
            Focusable = true, Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetName(_preview, "Note preview");
        Children.Add(_field);
        Children.Add(_preview);
    }

    internal event Action? EditStarted;
    internal long ShownId { get; private set; }
    internal UIElement FocusTarget => _preview.Visibility == Visibility.Visible ? _preview : _field;
    internal string Text => _field.Text;

    private async Task OnEditAsync()
    {
        if (_painting || ShownId == 0) return;
        long id = ShownId;
        string text = _field.Text;
        EditStarted?.Invoke();
        try { await _session.EditAsync(id, text); }
        catch (ObjectDisposedException) { }
    }

    internal void Display(ScratchpadNote note)
    {
        bool switching = ShownId != note.Id;
        string text = note.Text;
        if (switching || _field.Text != text)
        {
            var position = (_field.SelectionStart, _field.SelectionLength, _field.VerticalOffset);
            if (switching)
            {
                if (ShownId != 0) _positions[ShownId] = position;
                position = _positions.GetValueOrDefault(note.Id, (text.Length, 0, 0d));
            }
            _painting = true;
            try
            {
                ShownId = note.Id;
                // Reset history between notes; Ctrl+Z must never bring another note into this one.
                _field.IsUndoEnabled = false;
                _field.Text = text;
                int start = Math.Min(position.Item1, _field.Text.Length);
                _field.Select(start, Math.Min(position.Item2, _field.Text.Length - start));
                if (switching && !_positions.ContainsKey(note.Id)) _field.ScrollToEnd();
                else _field.ScrollToVerticalOffset(position.Item3);
            }
            finally { _field.IsUndoEnabled = true; _painting = false; }
            if (switching) _preview.ScrollToTop();
        }
        if (_preview.Visibility == Visibility.Visible) RenderPreview();
    }

    internal void ForgetDeleted(ScratchpadDocument doc)
    {
        foreach (long id in _positions.Keys.Where(id => !doc.Notes.Any(note => note.Id == id)).ToArray())
            _positions.Remove(id);
    }

    internal void SetPreview(bool preview)
    {
        bool moveFocus = IsKeyboardFocusWithin;
        if (preview) RenderPreview();
        _field.Visibility = preview ? Visibility.Collapsed : Visibility.Visible;
        _preview.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        if (moveFocus) FocusTarget.Focus();
    }

    private void RenderPreview()
    {
        if (_rendered == _field.Text) return;
        _rendered = _field.Text;
        _preview.Content = ScratchpadMarkdown.Render(_rendered);
    }
}
