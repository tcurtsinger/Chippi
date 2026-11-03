// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Chippi.UI.Controls;
using Microsoft.Win32;

namespace Chippi.Features.Scratchpad;

/// <summary>One view of the shared notes. Its navigation, caret and Edit/Preview mode are local.</summary>
internal sealed class ScratchpadWorkspace : DockPanel
{
    private readonly ScratchpadSession _session;
    private readonly ScratchpadSurface _surface;
    private readonly Func<IDisposable>? _hold;
    private readonly Action<Window?, ScratchpadNote> _export;
    private readonly ScratchpadEditor _editor;
    private readonly SegmentedControl _mode = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _retry;
    private readonly Button _actionAnchor;
    private Popup? _popup;
    private bool _listening, _disposed, _retrying, _loadFailureShown;
    private long _exportFailedId;
    private bool Widget => _surface == ScratchpadSurface.Widget;

    internal ScratchpadWorkspace(ScratchpadSession session, ScratchpadSurface surface,
        Func<IDisposable>? hold, Action<Window?, ScratchpadNote>? export)
    {
        _session = session;
        _surface = surface;
        _hold = hold;
        _export = export ?? ExportFile;
        if (Widget) _status.FontSize = 11;
        else _status.SetResourceReference(StyleProperty, "BodyTextStyle");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _retry = Button("Retry");
        _retry.Margin = new Thickness(8, 0, 0, 0);
        _retry.Click += async (_, _) => await RetryAsync();
        StatusArea = new StackPanel { Orientation = Orientation.Horizontal, Children = { _status, _retry } };

        Tabs = new ScratchpadTabs(Widget, hold) { Margin = new Thickness(0, 0, 0, 8) };
        Tabs.Selected += id => _ = SelectAsync(id);
        Tabs.AddRequested += () => _ = NewAsync();
        Tabs.RenameRequested += (id, name) => _ = ChangeAsync(doc => doc.Rename(id, name));
        Tabs.ContextRequested += (anchor, id) =>
        {
            _ = SelectAsync(id);
            Open(CreateActionsMenu(anchor, id));
        };
        _editor = new ScratchpadEditor(session, Widget);
        _editor.EditStarted += () => _exportFailedId = 0;
        FrameworkElement frame;
        if (Widget)
        {
            frame = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = _editor };
            frame.SetResourceReference(Border.BorderBrushProperty, "IslandCardBorderBrush");
            frame.SetResourceReference(Border.BackgroundProperty, "IslandPanelBrush");
            _mode.SetResourceReference(StyleProperty, "IslandSegmentedStyle");
        }
        else
        {
            _editor.Height = 320;
            frame = SettingsLayout.Card(_editor);
        }
        _mode.Items.Add("Edit");
        _mode.Items.Add("Preview");
        _mode.SelectedIndex = 0;
        AutomationProperties.SetName(_mode, "View");
        _mode.SelectionChanged += (_, _) =>
        {
            bool preview = _mode.SelectedIndex == 1;
            _editor.SetPreview(preview);
            if (preview) _ = Run(_session.FlushAsync());
        };
        var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(_mode);
        if (Widget)
        {
            footer.Height = 28;
            var glyph = new LucideIcon { Data = LucideIcons.Ellipsis, Size = 14 };
            glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextTertiaryBrush");
            _actionAnchor = new Button { Content = glyph, ToolTip = "More" };
            AutomationProperties.SetName(_actionAnchor, "More");
            _actionAnchor.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
            _actionAnchor.SetResourceReference(MarginProperty, "IslandLastIconButtonMargin");
            _actionAnchor.Click += (_, _) => Open(CreateActionsMenu(_actionAnchor, _editor.ShownId));
            Grid.SetColumn(_actionAnchor, 1);
            footer.Children.Add(_actionAnchor);
        }
        else
        {
            var rename = Button("Rename");
            var exportButton = Button("Export…");
            _actionAnchor = Button("Delete note");
            rename.Click += (_, _) => Tabs.BeginRename();
            exportButton.Click += async (_, _) => await ExportAsync();
            _actionAnchor.Click += (_, _) => Open(ConfirmDelete());
            exportButton.Margin = _actionAnchor.Margin = new Thickness(6, 0, 0, 0);
            var commands = new StackPanel { Orientation = Orientation.Horizontal, Children = { rename, exportButton, _actionAnchor } };
            Grid.SetColumn(commands, 1);
            footer.Children.Add(commands);
        }
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(frame, 1);
        Grid.SetRow(footer, 2);
        layout.Children.Add(Tabs);
        layout.Children.Add(frame);
        layout.Children.Add(footer);
        Children.Add(layout);
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnKey), handledEventsToo: true);
        // Listen to the host, since this body is collapsed while loading or after a load failure.
        Paint();
    }

    internal void Attach(FrameworkElement host)
    {
        host.Loaded += async (_, _) =>
        {
            Listen();
            Paint();
            if (_session.State.Document is null) await Run(_session.LoadAsync());
        };
        host.Unloaded += async (_, _) =>
        {
            CloseTransient();
            Unlisten();
            await Run(_session.FlushAsync());
        };
        if (Widget)
        {
            Listen();
            if (_session.State.Document is null) _ = Run(_session.LoadAsync());
        }
    }

    internal UIElement InitialFocus => _session.State.LoadFailed ? _retry : _editor.FocusTarget;
    internal StackPanel StatusArea { get; }
    internal ScratchpadTabs Tabs { get; }

    private void Listen()
    {
        if (_listening || _disposed) return;
        _session.Changed += Changed;
        _listening = true;
    }

    private void Unlisten()
    {
        if (!_listening) return;
        _session.Changed -= Changed;
        _listening = false;
    }

    private void Changed(ScratchpadState state)
    {
        // Two open views on this dispatcher see the same keystroke before either accepts another.
        if (Dispatcher.CheckAccess()) { if (_listening) Paint(); }
        else Dispatcher.BeginInvoke(() => { if (_listening) Paint(); });
    }

    private static async Task Run(Task operation)
    {
        try { await operation; }
        catch (ObjectDisposedException) { }
    }

    private void Paint()
    {
        if (_disposed) return;
        var doc = _session.State.Document;
        Visibility = doc is null ? Visibility.Collapsed : Visibility.Visible;
        if (doc is not null)
        {
            var note = doc.Current(_surface);
            bool first = _editor.ShownId == 0;
            _editor.Display(note);
            _editor.ForgetDeleted(doc);
            Tabs.Display(doc, note.Id);
            if (Widget && first && IsLoaded && Window.GetWindow(this)?.IsActive == true && !IsKeyboardFocusWithin)
                InitialFocus.Focus();
        }
        PaintStatus();
        bool loadFailed = _session.State.LoadFailed;
        if (Widget && loadFailed && !_loadFailureShown && IsLoaded && Window.GetWindow(this)?.IsActive == true)
            _retry.Focus();
        _loadFailureShown = loadFailed;
    }

    private void PaintStatus()
    {
        var state = _session.State;
        bool failed = state.SaveFailed || state.LoadFailed;
        bool exportFailed = _exportFailedId != 0 && _exportFailedId == _editor.ShownId;
        _status.Text = state.LoadFailed ? "Load failed" : state.SaveFailed ? "Save failed" : exportFailed ? "Export failed"
            : state.Document is null ? "" : state.HasChanges ? "" : "Saved";
        _status.SetResourceReference(TextBlock.ForegroundProperty, failed || exportFailed ? "WarningTextBrush"
            : Widget ? "IslandTextTertiaryBrush" : "TextSecondaryBrush");
        _retry.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        _retry.IsEnabled = !_retrying;
    }

    private async Task RetryAsync()
    {
        if (_retrying) return;
        bool restoreFocus = _retry.IsKeyboardFocused;
        _retrying = true;
        PaintStatus();
        await Run(_session.State.LoadFailed ? _session.LoadAsync() : _session.FlushAsync());
        _retrying = false;
        Paint();
        if (restoreFocus && IsLoaded && Window.GetWindow(this)?.IsActive == true)
            (_retry.Visibility == Visibility.Visible ? _retry : _editor.FocusTarget).Focus();
    }

    private async Task ChangeAsync(Func<ScratchpadDocument, ScratchpadDocument> change)
    {
        await Run(_session.ChangeAsync(change));
        Paint();
    }

    private async Task SelectAsync(long id)
    {
        if (_editor.ShownId == id) return;
        _exportFailedId = 0;
        await ChangeAsync(doc => doc.Select(_surface, id));
    }

    private async Task NewAsync()
    {
        if (_session.State.Document is not { } doc || doc.Notes.Count >= ScratchpadDocument.MaximumNotes) return;
        var save = ChangeAsync(d => d.Add(_surface));
        if (_session.State.Document?.Current(_surface).Id != doc.Current(_surface).Id)
        {
            _mode.SelectedIndex = 0;
            Paint();
            if (IsLoaded && Window.GetWindow(this)?.IsActive == true) _editor.FocusTarget.Focus();
        }
        await save;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Handled || (Keyboard.FocusedElement is TextBox box && AutomationProperties.GetName(box) == "Name")) return;
        var mods = Keyboard.Modifiers;
        if (e.Key == Key.Tab && (mods == ModifierKeys.Control || mods == (ModifierKeys.Control | ModifierKeys.Shift))
            && _session.State.Document is { } doc)
        {
            int index = doc.Notes.ToList().FindIndex(n => n.Id == _editor.ShownId);
            int delta = mods.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            _ = SelectAsync(doc.Notes[(index + delta + doc.Notes.Count) % doc.Notes.Count].Id);
        }
        else if (e.Key == Key.N && mods == ModifierKeys.Control) _ = NewAsync();
        else if (e.Key == Key.F2 && mods == ModifierKeys.None) Tabs.BeginRename();
        else return;
        e.Handled = true;
    }

    internal Popup CreateActionsMenu(FrameworkElement anchor, long id)
    {
        var popup = PopupAt(anchor, PlacementMode.Bottom);
        var list = new StackPanel { Margin = new Thickness(4) };
        void Add(string label, Action click)
        {
            var item = new Button { Content = label };
            item.SetResourceReference(StyleProperty, Widget ? "IslandMenuItemStyle" : "SecondaryButtonStyle");
            item.Click += (_, _) => { popup.IsOpen = false; click(); };
            list.Children.Add(item);
        }
        Add("Rename", () => { if (_editor.ShownId == id) Tabs.BeginRename(); });
        Add("Export…", () => _ = ExportAsync(id));
        Add("Delete", () => Open(ConfirmDelete(anchor, id)));
        var panel = new Border { MinWidth = 160, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = list };
        panel.SetResourceReference(Border.BackgroundProperty, Widget ? "IslandBrush" : "FlyoutBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, Widget ? "IslandBorderBrush" : "BorderBrush");
        popup.Child = panel;
        popup.Opened += (_, _) => list.Children[0].Focus();
        list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Up or Key.Down) || Keyboard.FocusedElement is not Button focused) return;
            int count = list.Children.Count;
            list.Children[(list.Children.IndexOf(focused) + (e.Key == Key.Up ? -1 : 1) + count) % count].Focus();
            e.Handled = true;
        };
        return popup;
    }

    internal Popup ConfirmDelete() => ConfirmDelete(_actionAnchor, _editor.ShownId);

    private Popup ConfirmDelete(FrameworkElement anchor, long id)
    {
        var popup = PopupAt(anchor, PlacementMode.Top);
        if (_session.State.Document?.Notes.FirstOrDefault(n => n.Id == id) is not { } note) return popup;
        var cancel = Button("Cancel");
        var delete = Button("Delete");
        delete.SetResourceReference(StyleProperty, "CriticalButtonStyle");
        delete.Margin = new Thickness(8, 0, 0, 0);
        cancel.Click += (_, _) => CloseToAnchor(popup, anchor);
        delete.Click += async (_, _) => { popup.IsOpen = false; await ChangeAsync(d => d.Delete(id)); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, delete } };
        var question = new TextBlock { Text = $"Delete \"{note.Name}\"?", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        question.SetResourceReference(TextBlock.ForegroundProperty, Widget ? "IslandTextBrush" : "TextPrimaryBrush");
        var frame = new Border { Padding = new Thickness(16), Width = 260, CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), Child = new StackPanel { Children = { question, buttons } } };
        frame.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        popup.Child = frame;
        popup.Opened += (_, _) => cancel.Focus();
        return popup;
    }

    private Popup PopupAt(FrameworkElement anchor, PlacementMode placement)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = placement, StaysOpen = false, AllowsTransparency = true };
        IDisposable? hold = null;
        popup.Opened += (_, _) =>
        {
            hold = _hold?.Invoke();
            KeyboardNavigation.SetTabNavigation(popup.Child, KeyboardNavigationMode.Cycle);
            popup.Child.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                CloseToAnchor(popup, anchor);
            };
        };
        popup.Closed += (_, _) => hold?.Dispose();
        return popup;
    }

    private static void CloseToAnchor(Popup popup, FrameworkElement anchor)
    {
        popup.IsOpen = false;
        if (anchor.IsLoaded && Window.GetWindow(anchor)?.IsActive == true) anchor.Focus();
    }

    private void Open(Popup popup)
    {
        if (_popup is not null) _popup.IsOpen = false;
        _popup = popup;
        if (popup.Child is not null) popup.IsOpen = true;
    }

    internal bool HandleEscape()
    {
        if (Tabs.HandleEscape()) return true;
        if (_popup?.IsOpen != true) return false;
        CloseToAnchor(_popup, (FrameworkElement)_popup.PlacementTarget);
        return true;
    }

    internal Task ExportAsync() => ExportAsync(_editor.ShownId);

    private async Task ExportAsync(long id)
    {
        if (_session.State.Document?.Notes.FirstOrDefault(n => n.Id == id) is not { } note) return;
        if (id == _editor.ShownId) note = note with { Text = _editor.Text };
        using var hold = _hold?.Invoke();
        await Run(_session.FlushAsync());
        if (_disposed) return;
        try { _exportFailedId = 0; _export(Window.GetWindow(this), note); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _exportFailedId = id; }
        PaintStatus();
    }

    internal static string ExportName(string name)
    {
        string stem = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.', ' ');
        if (stem.Length == 0) stem = "Note";
        var first = stem.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (first is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (first.Length == 4 && (first.StartsWith("COM", StringComparison.Ordinal) || first.StartsWith("LPT", StringComparison.Ordinal))
             && "123456789¹²³".Contains(first[3]))) stem = "_" + stem;
        return stem + ".txt";
    }

    private static void ExportFile(Window? owner, ScratchpadNote note)
    {
        var dialog = new SaveFileDialog { Filter = "Text (*.txt)|*.txt|Markdown (*.md)|*.md", DefaultExt = ".txt",
            AddExtension = true, FileName = ExportName(note.Name) };
        if ((owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true) File.WriteAllText(dialog.FileName, note.Text);
    }

    private Button Button(string label)
    {
        var button = new Button { Content = label };
        button.SetResourceReference(StyleProperty, Widget ? "IslandButtonStyle" : "SecondaryButtonStyle");
        AutomationProperties.SetName(button, label);
        return button;
    }

    private void CloseTransient()
    {
        Tabs.EndRename(true);
        if (_popup is not null) _popup.IsOpen = false;
    }
    internal void Dispose()
    {
        CloseTransient();
        _disposed = true;
        Unlisten();
    }
}
