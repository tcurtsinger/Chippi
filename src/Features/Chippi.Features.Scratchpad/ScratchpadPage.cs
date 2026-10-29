// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Microsoft.Win32;

namespace Chippi.Features.Scratchpad;

public sealed class ScratchpadPage : Grid, IDisposable
{
    private readonly ScratchpadSession _session;
    private readonly Func<IDisposable> _hold;
    private readonly Action<Window?, ScratchpadNote> _export;
    private readonly TextBlock _status = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _retry;
    private readonly DockPanel _body = new();
    private const double TabsWidth = IslandPage.BodyWidth - 28 - 52; // beside New note and the "+N" menu
    private readonly SegmentedControl _tabs = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly SegmentedControl _mode = new();
    private readonly TextBox _editor = new() { AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 12, Padding = new Thickness(8, 6, 8, 6), BorderThickness = new Thickness(0) };
    private readonly ScrollViewer _preview = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10, 8, 10, 8), Visibility = Visibility.Collapsed };
    private readonly Button _add = Icon(LucideIcons.Plus, "New note");
    private readonly TextBlock _overflowCount = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _overflow;
    private readonly Button _more = Icon(LucideIcons.Ellipsis, "More");
    private readonly Grid _surface = new();
    private readonly Grid _tabHost = new();
    private bool _painting, _disposed, _listening, _previewing, _exportFailed;
    private long _shownId;
    private string _tabsStamp = "";
    private Popup? _popup;
    private TextBox? _rename;
    private IDisposable? _renameHold;
    private long _renameId;

    public ScratchpadPage(ScratchpadSession session, Func<IDisposable> hold, Action<Window?, ScratchpadNote>? export = null)
    {
        _session = session; _hold = hold; _export = export ?? ExportFile;
        _overflow = Overflow(_overflowCount);
        Header = new DockPanel { Height = 28, Margin = new Thickness(0, 0, 0, 8) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        _retry = Button("Retry"); _retry.Margin = new Thickness(8, 0, 0, 0); _retry.SetResourceReference(StyleProperty, "IslandButtonStyle");
        _retry.Click += async (_, _) => await Run(_session.LoadAsync());
        right.Children.Add(_status); right.Children.Add(_retry);
        DockPanel.SetDock(right, Dock.Right); Header.Children.Add(right);
        Header.Children.Add(new TextBlock { Text = "Scratchpad", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        _tabs.SetResourceReference(StyleProperty, "SelectorBarStyle");
        var tabText = new FrameworkElementFactory(typeof(TextBlock));
        tabText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Label"));
        tabText.SetBinding(TextBlock.ToolTipProperty, new System.Windows.Data.Binding("Label"));
        tabText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        tabText.SetValue(TextBlock.MaxWidthProperty, 96.0);
        _tabs.ItemTemplate = new DataTemplate { VisualTree = tabText };
        var tabStyle = new Style(typeof(ListBoxItem), (Style)FindResource("SelectorBarItemStyle"));
        tabStyle.Setters.Add(new Setter(HeightProperty, 30.0));
        tabStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 0, 10, 0)));
        tabStyle.Setters.Add(new Setter(AutomationProperties.NameProperty, new System.Windows.Data.Binding("Label")));
        _tabs.ItemContainerStyle = tabStyle;
        AutomationProperties.SetName(_tabs, "Notes");
        _tabs.SelectionChanged += async (_, _) => { if (!_painting && _tabs.SelectedItem is SegmentItem { Value: long id }) await SelectAsync(id); };
        _tabs.PreviewMouseRightButtonUp += async (_, e) =>
        {
            e.Handled = true;
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(_tabs, source) is ListBoxItem { Content: SegmentItem { Value: long id } }) await SelectAsync(id);
            ShowMenu(_tabs);
        };
        // The tabs row (30); the note fills the body under it; Edit / Preview and More sit at the bottom edge.
        var row = new DockPanel { Height = 30, Margin = new Thickness(0, 0, 0, 8) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _add.SetResourceReference(MarginProperty, "IslandLastIconButtonMargin");
        actions.Children.Add(_overflow); actions.Children.Add(_add); DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions); _tabHost.Children.Add(_tabs); row.Children.Add(_tabHost);
        _overflow.Click += (_, _) => ShowNotes();
        _add.Click += async (_, _) => await NewAsync();
        _editor.TextChanged += async (_, _) => { if (!_painting) { _exportFailed = false; await Run(_session.EditAsync(_editor.Text)); } };
        _editor.GotKeyboardFocus += (_, _) => { if (_editor.SelectionLength == 0) _editor.CaretIndex = _editor.Text.Length; };
        _editor.SetResourceReference(TextBoxBase.CaretBrushProperty, "IslandTextBrush");
        _editor.SetResourceReference(TextBoxBase.SelectionBrushProperty, "IslandAccentBrush");
        _editor.SetResourceReference(Control.ForegroundProperty, "IslandTextBrush");
        _editor.Background = System.Windows.Media.Brushes.Transparent;
        TextBlock.SetLineHeight(_editor, 18);
        AutomationProperties.SetName(_editor, "Note");
        _surface.Children.Add(_editor); _surface.Children.Add(_preview);
        var frame = new Border { CornerRadius = new CornerRadius(8), Child = _surface };
        frame.SetResourceReference(Border.BackgroundProperty, "IslandPanelBrush");
        // Edit / Preview at the island's size: 22 items in the 2 padding and 1 border, 28 in all.
        var modeStyle = new Style(typeof(ListBoxItem), (Style)FindResource("SegmentItemStyle"));
        modeStyle.Setters.Add(new Setter(HeightProperty, 22.0));
        modeStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 0, 10, 0)));
        modeStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        _mode.ItemContainerStyle = modeStyle;
        _mode.Items.Add("Edit"); _mode.Items.Add("Preview"); _mode.SelectedIndex = 0;
        AutomationProperties.SetName(_mode, "View");
        _mode.SelectionChanged += (_, _) => SetPreview(_mode.SelectedIndex == 1);
        var footer = new DockPanel { Height = 28, Margin = new Thickness(0, 8, 0, 0) };
        _more.SetResourceReference(MarginProperty, "IslandLastIconButtonMargin");
        _mode.HorizontalAlignment = HorizontalAlignment.Left;
        DockPanel.SetDock(_more, Dock.Right); footer.Children.Add(_more); footer.Children.Add(_mode);
        _more.Click += (_, _) => ShowMenu(_more);
        DockPanel.SetDock(row, Dock.Top); DockPanel.SetDock(footer, Dock.Bottom);
        _body.Children.Add(row); _body.Children.Add(footer); _body.Children.Add(frame); Children.Add(_body);
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnKey), handledEventsToo: true);
        Loaded += async (_, _) => { Subscribe(); await Run(_session.LoadAsync()); Paint(); };
        Unloaded += async (_, _) => { CloseTransient(); Unsubscribe(); await Run(_session.FlushAsync()); };
        Subscribe(); Paint();
        _ = Run(_session.LoadAsync());
    }

    public DockPanel Header { get; }
    public UIElement InitialFocus => _session.State.LoadFailed ? _retry : _editor;
    private void Subscribe() { if (_listening || _disposed) return; _listening = true; _session.Changed += Changed; }
    private void Unsubscribe() { if (!_listening) return; _listening = false; _session.Changed -= Changed; }
    private void Changed(ScratchpadState state) => Dispatcher.BeginInvoke(() => { if (!_disposed) Paint(); });
    private async Task Run(Task operation) { try { await operation; } catch (ObjectDisposedException) { } }

    private void Paint()
    {
        var state = _session.State;
        _painting = true;
        try
        {
            _status.Text = _exportFailed ? "Export failed" : state.LoadFailed ? "Load failed" : state.SaveFailed ? "Save failed" : state.Document is null ? "" : "Saved";
            _status.SetResourceReference(TextBlock.ForegroundProperty, _exportFailed || state.LoadFailed || state.SaveFailed ? "WarningTextBrush" : "IslandTextTertiaryBrush");
            _retry.Visibility = state.LoadFailed ? Visibility.Visible : Visibility.Collapsed;
            _body.Visibility = state.Document is null ? Visibility.Collapsed : Visibility.Visible;
            if (state.Document is not { } doc) { if (state.LoadFailed && IsLoaded && Window.GetWindow(this)?.IsActive == true) _retry.Focus(); return; }
            bool firstDocument = _shownId == 0;
            if (_shownId != doc.SelectedId || (!_editor.IsKeyboardFocusWithin && _editor.Text != doc.Selected.Text)) // Settings' edits show here too
            {
                _shownId = doc.SelectedId; _editor.Text = doc.Selected.Text; _editor.CaretIndex = _editor.Text.Length;
            }
            var stamp = doc.SelectedId + ":" + string.Join("|", doc.Notes.Select(n => n.Id + ":" + n.Name));
            if (_tabsStamp != stamp)
            {
                _tabsStamp = stamp;
                _tabs.Items.Clear();
                double NoteWidth(ScratchpadNote note)
                {
                    var label = new TextBlock { Text = note.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = _tabs.FontFamily };
                    label.Measure(new Size(double.PositiveInfinity, 30));
                    return Math.Min(96, label.DesiredSize.Width) + 20;
                }
                var visible = new List<ScratchpadNote>();
                double width = 0;
                foreach (var note in doc.Notes)
                {
                    var next = NoteWidth(note);
                    if (visible.Count > 0 && width + next > TabsWidth) break;
                    visible.Add(note); width += next;
                }
                if (!visible.Any(n => n.Id == doc.SelectedId))
                {
                    visible[^1] = doc.Selected;
                    while (visible.Count > 1 && visible.Sum(NoteWidth) > TabsWidth) visible.RemoveAt(visible.Count - 2);
                }
                foreach (var note in visible) _tabs.Items.Add(new SegmentItem { Label = note.Name, Value = note.Id });
                _tabs.SelectedItem = _tabs.Items.Cast<SegmentItem>().First(n => (long)n.Value! == doc.SelectedId);
                _overflowCount.Text = "+" + (doc.Notes.Count - visible.Count);
                _overflow.Visibility = doc.Notes.Count > visible.Count ? Visibility.Visible : Visibility.Collapsed;
            }
            _add.IsEnabled = doc.Notes.Count < ScratchpadDocument.MaximumNotes;
            if (_previewing) _preview.Content = ScratchpadMarkdown.Render(_editor.Text);
            if (firstDocument && IsLoaded && Window.GetWindow(this)?.IsActive == true) _editor.Focus();
        }
        finally { _painting = false; }
    }

    private void SetPreview(bool preview)
    {
        _previewing = preview;
        if (preview) { _preview.Content = ScratchpadMarkdown.Render(_editor.Text); _ = Run(_session.FlushAsync()); }
        _editor.Visibility = preview ? Visibility.Collapsed : Visibility.Visible;
        _preview.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task SelectAsync(long id)
    {
        await Run(_session.ChangeAsync(d => d.Select(id))); Paint();
    }
    private async Task NewAsync()
    {
        if (_session.State.Document?.Notes.Count >= ScratchpadDocument.MaximumNotes) return;
        await Run(_session.ChangeAsync(d => d.Add())); Paint(); _mode.SelectedIndex = 0; _editor.Focus();
    }
    private void ShowNotes()
    {
        if (_session.State.Document is not { } doc) return;
        OpenMenu(CreateNotesMenu(doc));
    }
    private Popup CreateNotesMenu(ScratchpadDocument doc) => Menu(_overflow,
        [.. doc.Notes.Select(note => (note.Name + (note.Id == doc.SelectedId ? " \u2713" : ""),
            (Action)(() => _ = SelectAsync(note.Id))))]);

    private void ShowMenu(FrameworkElement anchor) => OpenMenu(CreateActionsMenu(anchor));

    private Popup CreateActionsMenu(FrameworkElement anchor) => Menu(anchor,
        ("Rename", BeginRename), ("Export\u2026", () => _ = ExportAsync()), ("Delete", () => ConfirmDelete(anchor)));

    private void OpenMenu(Popup menu)
    {
        if (_popup is not null) _popup.IsOpen = false;
        _popup = menu;
        menu.IsOpen = true;
    }

    // Same popup surface and row style as the Clipboard page's More menu.
    private static Popup Menu(FrameworkElement anchor, params (string Text, Action Click)[] items)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None };
        var list = new StackPanel { Margin = new Thickness(4) };
        foreach (var (text, click) in items)
        {
            var item = new Button { Content = text };
            item.SetResourceReference(StyleProperty, "IslandMenuItemStyle");
            item.Click += (_, _) => { popup.IsOpen = false; click(); };
            list.Children.Add(item);
        }
        var panel = new Border { MinWidth = 160, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = list };
        panel.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        popup.Child = panel;
        popup.Opened += (_, _) => list.Children.OfType<Button>().FirstOrDefault()?.Focus();
        return popup;
    }
    private void BeginRename()
    {
        if (_session.State.Document is not { } doc) return;
        EndRename(false);
        _renameId = doc.SelectedId;
        _renameHold = _hold();
        _rename = new TextBox { Text = doc.Selected.Name, MaxLength = 40, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_rename, "Name");
        _tabs.Visibility = Visibility.Hidden; _tabHost.Children.Add(_rename);
        _rename.LostKeyboardFocus += (_, _) => EndRename(true);
        _rename.Focus(); _rename.SelectAll();
    }
    private void EndRename(bool save)
    {
        if (_rename is not { } rename) return;
        _rename = null; _tabHost.Children.Remove(rename); _tabs.Visibility = Visibility.Visible;
        _renameHold?.Dispose(); _renameHold = null;
        if (save) { var id = _renameId; _ = Run(_session.ChangeAsync(d => d.Notes.Any(n => n.Id == id) ? d.Select(id).Rename(rename.Text).Select(d.SelectedId) : d)); }
    }
    private void ConfirmDelete(FrameworkElement anchor)
    {
        if (_session.State.Document is not { } doc) return;
        _popup?.SetCurrentValue(Popup.IsOpenProperty, false);
        var popup = _popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true };
        var cancel = Button("Cancel"); var delete = Button("Delete"); delete.SetResourceReference(StyleProperty, "CriticalButtonStyle");
        cancel.Click += (_, _) => popup.IsOpen = false;
        long selected = doc.SelectedId;
        delete.Click += async (_, _) => { popup.IsOpen = false; await Run(_session.ChangeAsync(d => d.SelectedId == selected ? d.DeleteSelected() : d)); _shownId = 0; Paint(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, delete } };
        var stack = new StackPanel { Children = { new TextBlock { Text = $"Delete \u201c{doc.Selected.Name}\u201d?", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }, buttons } };
        var border = new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(8), Width = 260, Child = stack };
        border.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush"); popup.Child = border;
        popup.Opened += (_, _) => cancel.Focus(); popup.IsOpen = true;
    }
    public async Task ExportAsync()
    {
        using var hold = _hold();
        await Run(_session.FlushAsync());
        if (_session.State.Document is not { } doc) return;
        try { _exportFailed = false; _export(Window.GetWindow(this), doc.Selected); Paint(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _exportFailed = true; Paint(); }
    }
    private static void ExportFile(Window? owner, ScratchpadNote note)
    {
        var dialog = new SaveFileDialog { Filter = "Text (*.txt)|*.txt|Markdown (*.md)|*.md", DefaultExt = ".txt", AddExtension = true, FileName = "Note.txt" };
        if ((owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true) File.WriteAllText(dialog.FileName, note.Text);
    }
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_rename is not null)
        {
            if (e.Key is Key.Escape or Key.Enter) { EndRename(e.Key == Key.Enter); e.Handled = true; }
            return;
        }
        if ((Keyboard.Modifiers == ModifierKeys.Control || Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) && e.Key == Key.Tab && _session.State.Document is { } doc)
        {
            var index = doc.Notes.ToList().FindIndex(n => n.Id == doc.SelectedId);
            var delta = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1;
            _ = SelectAsync(doc.Notes[(index + delta + doc.Notes.Count) % doc.Notes.Count].Id); e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) { _ = NewAsync(); e.Handled = true; }
        else if (e.Key == Key.F2) { BeginRename(); e.Handled = true; }
    }
    private void CloseTransient() { EndRename(true); if (_popup is not null) _popup.IsOpen = false; }
    public void Dispose() { _disposed = true; CloseTransient(); Unsubscribe(); }
    private static Button Button(string label)
    {
        var button = new Button { Content = label, MinHeight = 28, Padding = new Thickness(8, 2, 8, 2) };
        AutomationProperties.SetName(button, label); return button;
    }
    // The island's icon button: 28 × 24 with a 14 glyph.
    private static Button Icon(System.Windows.Media.Geometry icon, string label)
    {
        var glyph = new LucideIcon { Data = icon, Size = 14 };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextTertiaryBrush");
        var button = new Button { Content = glyph, ToolTip = label };
        button.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(button, label); return button;
    }

    // "+N ⌄": the notes that don't fit the tabs row.
    private static Button Overflow(TextBlock count)
    {
        var chevron = new LucideIcon { Data = LucideIcons.ChevronDown, Size = 12, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        chevron.SetResourceReference(LucideIcon.BrushProperty, "IslandTextTertiaryBrush");
        count.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        var button = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 6, 0), Children = { count, chevron } }, Width = double.NaN };
        button.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(button, "All notes"); return button;
    }
}
