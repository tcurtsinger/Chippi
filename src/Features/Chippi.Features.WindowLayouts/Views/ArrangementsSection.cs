// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Chippi.Core.Actions;
using Chippi.Core.Diagnostics;
using Chippi.Core.Shortcuts;
using Chippi.UI.Controls;
using static Chippi.Features.WindowLayouts.Views.SnapPage;

namespace Chippi.Features.WindowLayouts.Views;

/// <summary>
/// Settings › Snap › Arrangements (docs/phase-6/design/arrangements.html, boards arrangements, arrangements-review and
/// sheet-arrangements-states): the saved arrangements by name, each with its apps' icons, its shortcut, Launch and a menu
/// with Edit and Delete. New arrangement captures what's open at once and opens the review under the list; Edit opens
/// it in the row's place. The review has the name, a preview of each display, a row per window grouped by display
/// with its launch details, the windows that weren't captured, and refusals on the Save row.
/// </summary>
internal sealed class ArrangementsSection
{
    public const string NameTaken = "Another arrangement already uses this name", NoWindows = "No windows to save",
        CapturingFailed = "Capturing failed", SavingFailed = "Saving failed";
    private const double FieldWidth = 340, IconsWidth = 104;

    /// <summary>An arrangement under review: a copy, so Cancel leaves the saved one as it was.</summary>
    internal sealed class Draft
    {
        public SavedArrangement? Saved;
        public string Name = "";
        public List<ArrangementRow> Rows = [];
        public IReadOnlyList<string> Skipped = [];
        /// <summary>Rows Recapture added, rows whose launch details it kept, and rows whose details are open.</summary>
        public HashSet<string> Fresh = [], Kept = [], Open = [];
        public string? Recaptured;
        public bool Capturing;
        public string? CaptureError;
        public string? Id => Saved?.Id;
    }

    private readonly WindowLayoutsModule _module;
    private Draft? _draft;
    private FrameworkElement? _editor;
    private Action? _drawEditor;
    private bool _renderQueued;

    public ArrangementsSection(WindowLayoutsModule module)
    {
        _module = module;
        Listen(true);
        View.Loaded += (_, _) => { Listen(true); Render(); };
        View.Unloaded += (_, _) => Listen(false);
    }

    private bool _listening;

    /// <summary>Follows the run from the start, before the page is first shown, and stops once it's closed.</summary>
    private void Listen(bool on)
    {
        if (_listening == on) return;
        _listening = on;
        if (on) _module.ArrangementsChanged += Queue;
        else _module.ArrangementsChanged -= Queue;
    }

    public Card View { get; } = new();

    /// <summary>The open review, for tests and shots.</summary>
    internal Draft? Current => _draft;

    private SnapLayoutStore? Store => _module.Layouts;

    private List<SavedArrangement> Saved => [.. (Store?.Settings.Arrangements ?? []).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];

    private bool Busy => _module.ArrangementsPreparing || _module.Arrangements?.IsRunning == true;

    private void Queue()
    {
        if (_renderQueued) return;
        _renderQueued = true;
        View.Dispatcher.BeginInvoke(() => { _renderQueued = false; Render(); });
    }

    // ---- the list ----

    public void Render()
    {
        if (_editor?.Parent is Panel old)
            old.Children.Remove(_editor);
        View.Children.Clear();
        var saved = Saved;
        if (_draft?.Id is { } editing && saved.All(a => a.Id != editing))
            CloseEditor(render: false);
        foreach (var arrangement in saved)
            View.Children.Add(_draft?.Id == arrangement.Id ? _editor! : RowView(arrangement));
        if (_draft is { Id: null })
            View.Children.Add(_editor!);
        if (_draft is not null)
            return;
        var add = Button("New arrangement", icon: LucideIcons.Plus);
        add.Margin = new Thickness(0);
        add.Click += (_, _) => New();
        var count = Styled(new TextBlock { Text = saved.Count switch { 0 => "No arrangements", 1 => "1 arrangement", int n => $"{n} arrangements" }, VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle");
        count.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        View.Children.Add(Line(count, add, SettingsLayout.RowPadding, 64));
    }

    /// <summary>"12 windows · 2 displays", or the display's name when there's one.</summary>
    internal static string Caption(SavedArrangement arrangement)
    {
        int windows = arrangement.Rows.Count;
        var displays = arrangement.Rows.Select(r => r.DisplayId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        string count = windows == 1 ? "1 window" : $"{windows} windows";
        return displays == 1 ? $"{count} · {arrangement.Rows[0].DisplayName}" : $"{count} · {displays} displays";
    }

    /// <summary>The row's caption while its run is going or after its launch was refused; null otherwise.</summary>
    private string? RunCaption(SavedArrangement arrangement, out bool caution)
    {
        caution = false;
        if (_module.ArrangementRun?.Id != arrangement.Id) return null;
        if (_module.ArrangementRefused is { } refused) { caution = true; return $"Not launched · {refused}"; }
        if (!Busy) return null;
        int ready = _module.ArrangementsPreparing ? 0 : _module.Arrangements?.Results.Count(r => r.State == ArrangementRowState.Ready) ?? 0;
        return $"Opening · {ready} of {arrangement.Rows.Count} ready";
    }

    private FrameworkElement RowView(SavedArrangement arrangement)
    {
        var icons = new StackPanel { Orientation = Orientation.Horizontal, Width = IconsWidth, VerticalAlignment = VerticalAlignment.Center };
        foreach (var row in arrangement.Rows.DistinctBy(r => r.AppIdentity.ToUpperInvariant()).Take(4))
        {
            var icon = Icon(row, 20);
            icon.Margin = new Thickness(0, 0, 4, 0);
            icons.Children.Add(icon);
        }
        string? run = RunCaption(arrangement, out bool caution);
        var name = Styled(new TextBlock { Text = arrangement.Name, TextTrimming = TextTrimming.CharacterEllipsis }, "BodyTextStyle");
        var caption = Styled(new TextBlock { Text = run ?? Caption(arrangement), TextTrimming = TextTrimming.CharacterEllipsis }, "CaptionTextStyle");
        caption.SetResourceReference(TextBlock.ForegroundProperty, caution ? "WarningTextBrush" : "TextSecondaryBrush");
        AutomationProperties.SetLiveSetting(caption, AutomationLiveSetting.Polite);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, caption }, Margin = new Thickness(0, 0, 12, 0) };

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (arrangement.Shortcut is { } keys)
        {
            var caps = Keycaps.Of(Keycaps.CapsOf(keys));
            caps.VerticalAlignment = VerticalAlignment.Center;
            caps.Margin = new Thickness(0, 0, 4, 0);
            AutomationProperties.SetName(caps, keys.ToString());
            right.Children.Add(caps);
        }
        bool running = Busy && _module.ArrangementRun?.Id == arrangement.Id;
        var launch = Button(running ? "Cancel" : "Launch");
        launch.IsEnabled = running || !Busy;
        AutomationProperties.SetName(launch, running ? $"Cancel {arrangement.Name}" : $"Launch {arrangement.Name}");
        launch.Click += (_, _) => { if (running) _module.CancelArrangement(); else Launch(arrangement.Id); };
        right.Children.Add(launch);
        var more = new IconButton { Icon = LucideIcons.Ellipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(more, $"{arrangement.Name} options");
        more.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Bottom };
            menu.Items.Add(Item("Edit", () => Edit(arrangement)));
            menu.Items.Add(Item("Delete", () => Confirm(more, () => Delete(arrangement.Id), $"Delete {arrangement.Name}?")));
            menu.IsOpen = true;
        };
        right.Children.Add(more);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(icons);
        grid.Children.Add(text);
        grid.Children.Add(right);
        var view = new Border { MinHeight = SettingsLayout.CaptionRowHeight, Padding = new Thickness(16, 8, 16, 8), Child = grid, Background = Brushes.Transparent, Tag = arrangement.Id };
        view.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && Inside<ButtonBase>(source)) return;
            Edit(arrangement);
        };
        return view;

        static MenuItem Item(string header, Action click)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => click();
            return item;
        }
    }

    private async void Launch(string id)
    {
        try { await _module.LaunchArrangementAsync(id, SurfaceDisplay()); }
        catch (ActionFailure ex)
        {
            Log.Warn("Snap: launching an arrangement was refused", ex);
            Queue();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warn("Snap: launching an arrangement failed", ex);
            Queue();
        }
    }

    /// <summary>The display Settings is on: a launch from it falls back there for a missing display.</summary>
    private string? SurfaceDisplay()
    {
        if (Window.GetWindow(View) is not { } window) return null;
        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return null;
        nint monitor = MonitorFromWindow(handle, 2);
        return _module.Displays.FirstOrDefault(d => d.Handle == monitor)?.Id;
    }

    private void Delete(string id)
    {
        if (Store is not { } store) return;
        try { store.DeleteArrangement(id); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log.Warn("Snap: deleting an arrangement failed", ex); }
        if (_draft?.Id == id) CloseEditor();
    }

    // ---- the review ----

    internal void New()
    {
        var draft = new Draft();
        Open(draft);
        Capture(draft);
    }

    internal void Edit(SavedArrangement arrangement) => Open(new Draft
    {
        Saved = arrangement,
        Name = arrangement.Name,
        Rows = [.. arrangement.Rows],
    });

    /// <summary>Opens a draft that's already filled, for tests and shots.</summary>
    internal void Open(Draft draft)
    {
        _draft = draft;
        _editor = BuildEditor(draft);
        Render();
    }

    private void CloseEditor(bool render = true)
    {
        _draft = null;
        _editor = null;
        _drawEditor = null;
        if (render) Render();
    }

    private async void Capture(Draft draft)
    {
        draft.Capturing = true;
        draft.CaptureError = null;
        _drawEditor?.Invoke();
        try
        {
            var capture = await _module.CaptureArrangementAsync();
            if (_draft != draft) return;
            Apply(draft, capture);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warn("Snap: capturing an arrangement failed", ex);
            draft.CaptureError = CapturingFailed;
        }
        finally
        {
            draft.Capturing = false;
            if (_draft == draft) _drawEditor?.Invoke();
        }
    }

    /// <summary>A capture into the draft: launch details the owner typed stay with the windows that still match.</summary>
    internal static void Apply(Draft draft, ArrangementCapture capture)
    {
        var before = draft.Rows.ToDictionary(r => r.Id);
        var next = new SavedArrangement { Rows = draft.Rows }.Recapture(capture.Rows).Rows;
        draft.Skipped = capture.SkippedWindows ?? capture.Skipped;
        draft.Kept.Clear();
        draft.Fresh.Clear();
        foreach (var row in next)
        {
            if (!before.TryGetValue(row.Id, out var old)) { if (before.Count > 0 && draft.Saved is not null) draft.Fresh.Add(row.Id); }
            else if (old.Arguments.Length > 0 || old.WorkingFolder.Length > 0) draft.Kept.Add(row.Id);
        }
        draft.Recaptured = draft.Saved is not null && before.Count > 0
            ? $"Recaptured · {draft.Fresh.Count} new · {before.Keys.Count(id => next.All(r => r.Id != id))} gone" : null;
        draft.Rows = next;
        draft.Open.RemoveWhere(id => next.All(r => r.Id != id));
        foreach (var row in next.Where(r => !Launchable(r))) draft.Open.Add(row.Id);
    }

    /// <summary>Why the draft can't be saved, or null. An empty name only turns Save off.</summary>
    internal string? Refusal(Draft draft)
    {
        if (draft.Capturing) return null;
        if (draft.CaptureError is { } error) return error;
        if (draft.Rows.Count == 0) return NoWindows;
        string name = draft.Name.Trim();
        if (name.Length > 0 && Store?.ArrangementNameTaken(name, draft.Id) == true) return NameTaken;
        foreach (var row in draft.Rows)
        {
            string app = row.App.Length > 0 ? row.App : "A window";
            if (!Launchable(row)) return $"{app} needs an app to launch";
            if (row.WorkingFolder.Length > 0 && !Path.IsPathFullyQualified(row.WorkingFolder)) return $"{app}'s working folder needs a full path";
            if (!row.IsValid) return $"{app} can't be saved";
        }
        return null;
    }

    /// <summary>A Start menu app, or a program's .exe; its working folder is checked on its own.</summary>
    private static bool Launchable(ArrangementRow row) => row.Target.Length > 0
        && (!Path.IsPathFullyQualified(row.Target) || IsExe(row.Target));

    private static bool IsExe(string path) => string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);

    private FrameworkElement BuildEditor(Draft draft)
    {
        var view = new StackPanel();
        view.SetResourceReference(Panel.BackgroundProperty, "HoverBrush");

        // Title row: what this is, Recapture and, for a saved one, Delete.
        var icon = Glyph(draft.Id is null ? LucideIcons.Plus : LucideIcons.Pencil, "TextPrimaryBrush");
        var title = Styled(new TextBlock { Text = draft.Id is null ? "New arrangement" : "Edit arrangement", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) }, "BodyTextStyle");
        var recapture = Button("Recapture");
        recapture.Margin = new Thickness(0);
        recapture.Click += (_, _) => Capture(draft);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Children = { recapture } };
        if (draft.Id is { } id)
        {
            var delete = Button("Delete");
            delete.Click += (_, _) => Confirm(delete, () => Delete(id), $"Delete {draft.Saved!.Name}?");
            actions.Children.Add(delete);
        }
        view.Children.Add(Line(new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, title } }, actions, SettingsLayout.RowPadding));

        var name = Field(draft.Name, "Name");
        name.Width = FieldWidth;
        name.MaxLength = 80;
        var nameFrame = new Border { CornerRadius = new CornerRadius(5), Child = name };
        var nameLabel = Styled(new TextBlock { Text = "Name", Margin = new Thickness(36, 0, 0, 0) }, "BodyTextStyle");
        view.Children.Add(Line(nameLabel, nameFrame, SettingsLayout.RowPadding));

        if (draft.Saved is { } saved)
        {
            var shortcut = new ShortcutSetting("Launch " + saved.Name, () => _module.EditArrangementShortcut(saved.Id));
            shortcut.Show(saved.Shortcut, null);
            var shortcutLabel = Styled(new TextBlock { Text = "Shortcut", Margin = new Thickness(36, 0, 0, 0) }, "BodyTextStyle");
            view.Children.Add(Line(shortcutLabel, shortcut, SettingsLayout.RowPadding));
        }

        var body = new StackPanel();
        view.Children.Add(body);

        var message = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var save = Button("Save", primary: true);
        save.Margin = new Thickness(0);
        var cancel = Button("Cancel");
        cancel.Click += (_, _) => CloseEditor();
        var footer = new DockPanel { Margin = SettingsLayout.RowPadding, MinHeight = 32 };
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

        void Validate()
        {
            string? refusal = Refusal(draft);
            bool taken = refusal == NameTaken;
            nameFrame.BorderThickness = new Thickness(taken ? 2 : 0);
            nameFrame.Margin = new Thickness(taken ? -2 : 0);
            nameFrame.SetResourceReference(Border.BorderBrushProperty, "WarningTextBrush");
            if (refusal is not null) Show(message, refusal);
            else if (draft.Recaptured is { } note) Note(message, note);
            else Show(message, null);
            save.IsEnabled = refusal is null && !draft.Capturing && draft.Name.Trim().Length > 0;
            recapture.IsEnabled = !draft.Capturing;
        }

        void Draw()
        {
            body.Children.Clear();
            if (draft.Capturing && draft.Rows.Count == 0)
                body.Children.Add(Centered("Capturing…"));
            else if (draft.CaptureError is { } error && draft.Rows.Count == 0)
                body.Children.Add(Centered(error, warning: true));
            else if (draft.Rows.Count == 0)
                body.Children.Add(Centered("No windows"));
            else
                DrawRows(draft, body, Draw, Validate);
            if (draft.Skipped.Count > 0)
            {
                var head = Styled(new TextBlock { Text = draft.Skipped.Count == 1 ? "1 window not captured" : $"{draft.Skipped.Count} windows not captured" }, "BodyTextStyle");
                head.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                var list = Styled(new TextBlock { Text = string.Join(" · ", draft.Skipped), TextWrapping = TextWrapping.Wrap }, "CaptionTextStyle");
                list.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                body.Children.Add(new Border { Padding = SettingsLayout.RowPadding, Child = new StackPanel { Children = { head, list } } });
            }
            Validate();
        }

        name.TextChanged += (_, _) => { draft.Name = name.Text; Validate(); };
        save.Click += (_, _) =>
        {
            Validate();
            if (!save.IsEnabled || Store is not { } store) return;
            var arrangement = new SavedArrangement
            {
                Id = draft.Id ?? Guid.NewGuid().ToString("N"),
                Name = draft.Name.Trim(),
                Shortcut = draft.Saved?.Shortcut,
                Rows = [.. draft.Rows],
            };
            try { store.SaveArrangement(arrangement); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log.Warn("Snap: saving an arrangement failed", ex);
                Show(message, SavingFailed);
                return;
            }
            CloseEditor();
        };
        _drawEditor = Draw;
        Draw();
        view.Loaded += (_, _) => { if (draft.Id is null) name.Focus(); };
        return view;
    }

    private static Border Centered(string text, bool warning = false)
    {
        var label = Styled(new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle");
        label.SetResourceReference(TextBlock.ForegroundProperty, warning ? "WarningTextBrush" : "TextSecondaryBrush");
        AutomationProperties.SetLiveSetting(label, AutomationLiveSetting.Polite);
        return new Border { MinHeight = 80, Padding = SettingsLayout.RowPadding, Child = label };
    }

    private static void Note(StackPanel message, string text)
    {
        message.Children.Clear();
        message.Visibility = Visibility.Visible;
        var glyph = Glyph(LucideIcons.Info, "TextSecondaryBrush");
        glyph.Margin = new Thickness(0, 0, 8, 0);
        var label = Styled(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle");
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        AutomationProperties.SetLiveSetting(label, AutomationLiveSetting.Polite);
        message.Children.Add(glyph);
        message.Children.Add(label);
    }

    // ---- the review's windows ----

    /// <summary>A display of the draft: the saved work area its windows were on, and whether it's here and the main one.</summary>
    private sealed record Group(string Id, string Name, Rect Area, bool Main, bool Connected, List<ArrangementRow> Rows);

    private List<Group> Groups(Draft draft)
    {
        var live = _module.Displays;
        return [.. draft.Rows.GroupBy(r => r.DisplayId, StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            var first = g.First();
            var p = first.Placement!;
            var display = live.FirstOrDefault(d => string.Equals(d.Id, g.Key, StringComparison.OrdinalIgnoreCase));
            return new Group(g.Key, display?.Name ?? first.DisplayName, new Rect(p.WorkLeft, p.WorkTop, p.WorkWidth, p.WorkHeight),
                display?.IsMain == true, display is not null, [.. g]);
        }).OrderByDescending(g => g.Main).ThenBy(g => g.Area.X).ThenBy(g => g.Area.Y)];
    }

    private void DrawRows(Draft draft, StackPanel body, Action redraw, Action validate)
    {
        var groups = Groups(draft);
        var preview = new ArrangementPreview();
        var lit = new Dictionary<string, Border>();
        foreach (var group in groups)
        {
            var screen = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
            screen.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            screen.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
            ArrangementPreview.SetArea(screen, group.Area);
            preview.Children.Add(screen);
            // Maximized windows underneath, so the others still show where they sit; then the topmost last, so it's on top.
            // Minimized windows are only in the list.
            foreach (var row in Enumerable.Reverse(group.Rows).OrderByDescending(r => r.Placement?.Maximized == true))
            {
                if (row.Placement is not { Minimized: false } p) continue;
                var rect = p.Maximized ? group.Area : Rect.Intersect(new Rect(p.Left, p.Top, p.Width, p.Height), group.Area);
                if (rect.IsEmpty) continue;
                var window = WindowPicture(row);
                // Opaque under the window's tint, so a window over another hides it, as on the desktop.
                var backing = new Border { CornerRadius = new CornerRadius(3), Child = window };
                backing.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
                ArrangementPreview.SetArea(backing, rect);
                ArrangementPreview.SetWithin(backing, group.Area);
                preview.Children.Add(backing);
                lit[row.Id] = window;
            }
            var label = new StackPanel();
            label.Children.Add(Styled(new TextBlock { Text = group.Name, TextTrimming = TextTrimming.CharacterEllipsis }, "CaptionTextStyle"));
            string? note = group.Main ? "Main display" : group.Connected ? null : "Not connected";
            if (note is not null)
            {
                var caption = Styled(new TextBlock { Text = note, TextTrimming = TextTrimming.CharacterEllipsis }, "CaptionTextStyle");
                caption.SetResourceReference(TextBlock.ForegroundProperty, group.Connected ? "TextSecondaryBrush" : "WarningTextBrush");
                label.Children.Add(caption);
            }
            ArrangementPreview.SetArea(label, group.Area);
            ArrangementPreview.SetIsLabel(label, true);
            preview.Children.Add(label);
        }
        AutomationProperties.SetName(preview, "Preview");
        body.Children.Add(new Border { Padding = new Thickness(50, 16, 50, 12), Child = preview });

        foreach (var group in groups)
        {
            string where = group.Main ? " · Main display" : group.Connected ? "" : " · Not connected";
            string count = group.Rows.Count == 1 ? "1 window" : $"{group.Rows.Count} windows";
            var header = Styled(new TextBlock { Text = $"{group.Name}{where} · {count}", FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, "CaptionTextStyle");
            header.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            AutomationProperties.SetHeadingLevel(header, AutomationHeadingLevel.Level3);
            body.Children.Add(new Border { Padding = new Thickness(16, 12, 16, 6), Child = header });
            foreach (var row in group.Rows)
            {
                body.Children.Add(WindowRow(draft, row, lit.GetValueOrDefault(row.Id), redraw, validate));
                if (draft.Open.Contains(row.Id))
                    body.Children.Add(Details(draft, row, redraw, validate));
            }
        }
    }

    private static Border WindowPicture(ArrangementRow row)
    {
        var icon = Icon(row, 12);
        icon.Margin = new Thickness(0, 0, 4, 0);
        var name = Styled(new TextBlock { Text = row.App, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }, "CaptionTextStyle");
        var line = new DockPanel { Margin = new Thickness(5, 4, 5, 0), VerticalAlignment = VerticalAlignment.Top, LastChildFill = true, ClipToBounds = true, Children = { icon, name } };
        var window = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Child = line, ClipToBounds = true };
        Light(window, false);
        return window;
    }

    private static void Light(Border window, bool lit)
    {
        window.BorderThickness = new Thickness(lit ? 2 : 1);
        window.SetResourceReference(Border.BorderBrushProperty, lit ? "AccentBrush" : "BorderStrongBrush");
        if (lit && window.TryFindResource("AccentBrush") is SolidColorBrush accent)
            window.Background = new SolidColorBrush(Color.FromArgb(0x40, accent.Color.R, accent.Color.G, accent.Color.B));
        else
            window.SetResourceReference(Border.BackgroundProperty, "ButtonBackgroundBrush");
    }

    /// <summary>"1536 × 2088 at 0, 0" on its display's work area, "Maximized", "Minimized · …".</summary>
    internal static string Where(ArrangementRow row)
    {
        if (row.Placement is not { } p) return "";
        string place = p.Maximized ? "Maximized" : $"{p.Width} × {p.Height} at {p.Left - p.WorkLeft}, {p.Top - p.WorkTop}";
        return p.Minimized ? $"Minimized · {place}" : place;
    }

    private FrameworkElement WindowRow(Draft draft, ArrangementRow row, Border? picture, Action redraw, Action validate)
    {
        var icon = Icon(row, 20);
        icon.Margin = new Thickness(0, 0, 14, 0);
        string title = row.TitleHint.Length > 0 ? row.TitleHint : row.App;
        var name = Styled(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle");
        var titleLine = new DockPanel { LastChildFill = true };
        if (draft.Fresh.Contains(row.Id))
        {
            var chip = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = Styled(new TextBlock { Text = "New" }, "CaptionTextStyle") };
            chip.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
            DockPanel.SetDock(chip, Dock.Right);
            titleLine.HorizontalAlignment = HorizontalAlignment.Left;
            titleLine.Children.Add(chip);
        }
        titleLine.Children.Add(name);
        bool launchable = Launchable(row);
        string where = Where(row) + (draft.Kept.Contains(row.Id) ? " · launch details kept" : "");
        var caption = Styled(new TextBlock { Text = launchable ? where : "No app to launch", TextTrimming = TextTrimming.CharacterEllipsis }, "CaptionTextStyle");
        caption.SetResourceReference(TextBlock.ForegroundProperty, launchable ? "TextSecondaryBrush" : "WarningTextBrush");
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { titleLine, caption }, Margin = new Thickness(0, 0, 12, 0) };

        var remove = Button("Remove");
        remove.Margin = new Thickness(0);
        AutomationProperties.SetName(remove, $"Remove {title}");
        remove.Click += (_, _) =>
        {
            draft.Rows.RemoveAll(r => r.Id == row.Id);
            draft.Open.Remove(row.Id);
            redraw();
        };
        bool open = draft.Open.Contains(row.Id);
        var chevron = new IconButton { Icon = LucideIcons.ChevronDown, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(open ? 180 : 0) };
        AutomationProperties.SetName(chevron, $"Launch details for {title}");
        AutomationProperties.SetItemStatus(chevron, open ? "Expanded" : "Collapsed");
        chevron.Click += (_, _) =>
        {
            if (!draft.Open.Remove(row.Id)) draft.Open.Add(row.Id);
            redraw();
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { remove, chevron } };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(right);
        var view = new Border { MinHeight = SettingsLayout.CaptionRowHeight, Padding = new Thickness(16, 8, 16, 8), Child = grid, Background = Brushes.Transparent };
        if (picture is not null)
        {
            void Update() => Light(picture, view.IsMouseOver || view.IsKeyboardFocusWithin);
            view.MouseEnter += (_, _) => Update();
            view.MouseLeave += (_, _) => Update();
            view.IsKeyboardFocusWithinChanged += (_, _) => Update();
        }
        return view;
    }

    /// <summary>A program: its path with Browse…, Arguments and Working folder. A Start menu app: its name, and nothing to set.</summary>
    private FrameworkElement Details(Draft draft, ArrangementRow row, Action redraw, Action validate)
    {
        var panel = new StackPanel();
        void Change(Func<ArrangementRow, ArrangementRow> change)
        {
            int index = draft.Rows.FindIndex(r => r.Id == row.Id);
            if (index < 0) return;
            draft.Rows[index] = row = change(draft.Rows[index]);
            validate();
        }
        Border Detail(string label, FrameworkElement right, string? caption = null)
        {
            var left = new StackPanel { Children = { Styled(new TextBlock { Text = label }, "BodyTextStyle") } };
            if (caption is not null)
            {
                var c = Styled(new TextBlock { Text = caption }, "CaptionTextStyle");
                c.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                left.Children.Add(c);
            }
            return Line(left, right, SettingsLayout.DependentPadding);
        }

        if (row.Target.Length > 0 && !Path.IsPathFullyQualified(row.Target))
        {
            var app = Styled(new TextBlock { Text = row.App }, "BodyTextStyle");
            app.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            panel.Children.Add(Detail("App", app, "Start menu"));
            return panel;
        }

        var browse = Button("Browse…");
        AutomationProperties.SetName(browse, $"Browse for {row.App}'s app");
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Apps (*.exe)|*.exe", CheckFileExists = true };
            if (row.Target.Length > 0 && Path.GetDirectoryName(row.Target) is { } folder && Directory.Exists(folder)) dialog.InitialDirectory = folder;
            if (dialog.ShowDialog(Window.GetWindow(View)) != true) return;
            Change(r => r with { Target = dialog.FileName });
            redraw();
        };
        FrameworkElement path;
        if (row.Target.Length == 0)
        {
            var missing = Styled(new TextBlock { Text = "Not found", VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle");
            missing.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            path = missing;
        }
        else
        {
            var text = Styled(new TextBlock { Text = row.Target, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300, FontFamily = Mono, ToolTip = row.Target }, "BodyTextStyle");
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            path = text;
        }
        panel.Children.Add(Detail("App", new StackPanel { Orientation = Orientation.Horizontal, Children = { path, browse } }));
        if (row.Target.Length == 0)
            return panel;

        var arguments = Field(row.Arguments, "Arguments");
        arguments.Width = FieldWidth;
        arguments.FontFamily = Mono;
        arguments.TextChanged += (_, _) => Change(r => r with { Arguments = arguments.Text });
        panel.Children.Add(Shrinks(Detail("Arguments", arguments), arguments, 0));

        var folder = Field(row.WorkingFolder, "Working folder");
        folder.Width = FieldWidth - 104;
        folder.FontFamily = Mono;
        folder.TextChanged += (_, _) => Change(r => r with { WorkingFolder = folder.Text.Trim() });
        var pick = Button("Browse…");
        AutomationProperties.SetName(pick, "Browse for a working folder");
        pick.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (Directory.Exists(row.WorkingFolder)) dialog.InitialDirectory = row.WorkingFolder;
            if (dialog.ShowDialog(Window.GetWindow(View)) == true) folder.Text = dialog.FolderName;
        };
        panel.Children.Add(Shrinks(Detail("Working folder", new StackPanel { Orientation = Orientation.Horizontal, Children = { folder, pick } }), folder, 104));
        return panel;
    }

    /// <summary>On a narrow page the field gives up width before its label is covered: 130 for the label, down to 160 wide.</summary>
    private static Border Shrinks(Border line, TextBox field, double beside)
    {
        double widest = field.Width;
        line.SizeChanged += (_, e) => field.Width = Math.Clamp(e.NewSize.Width - line.Padding.Left - line.Padding.Right - 130 - beside, 160, widest);
        return line;
    }

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    /// <summary>The app's real icon, its letters until Windows has it.</summary>
    internal static FrameworkElement Icon(ArrangementRow row, double size)
    {
        string name = row.App.Length > 0 ? row.App : "?";
        var letter = new TextBlock { Text = name[..Math.Min(2, name.Length)], FontSize = size <= 12 ? 7 : 9, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        letter.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var monogram = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 5), Child = letter };
        monogram.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
        return AppIcon.View(IconPath(row), size, monogram);
    }

    /// <summary>A program's path, or the Start menu entry a registered app opens from.</summary>
    internal static string? IconPath(ArrangementRow row) =>
        Path.IsPathFullyQualified(row.Target) ? row.Target
        : row.Target.Length > 0 ? @"shell:AppsFolder\" + row.Target
        : Path.IsPathFullyQualified(row.AppIdentity) ? row.AppIdentity : null;

    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
}

/// <summary>The review's preview: each display where it sits, scaled to fit, its windows on it and its name under it.</summary>
internal sealed class ArrangementPreview : Panel
{
    public static readonly DependencyProperty AreaProperty = DependencyProperty.RegisterAttached("Area", typeof(Rect), typeof(ArrangementPreview),
        new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
    public static readonly DependencyProperty IsLabelProperty = DependencyProperty.RegisterAttached("IsLabel", typeof(bool), typeof(ArrangementPreview),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
    public static void SetArea(UIElement e, Rect r) => e.SetValue(AreaProperty, r);
    public static Rect GetArea(UIElement e) => (Rect)e.GetValue(AreaProperty);
    public static void SetIsLabel(UIElement e, bool v) => e.SetValue(IsLabelProperty, v);
    public static bool GetIsLabel(UIElement e) => (bool)e.GetValue(IsLabelProperty);
    /// <summary>A window's display: the window is drawn inside it.</summary>
    public static readonly DependencyProperty WithinProperty = DependencyProperty.RegisterAttached("Within", typeof(Rect), typeof(ArrangementPreview),
        new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
    public static void SetWithin(UIElement e, Rect r) => e.SetValue(WithinProperty, r);
    public static Rect GetWithin(UIElement e) => (Rect)e.GetValue(WithinProperty);

    /// <summary>Half the space between two displays that touch, and a window's inset from the next.</summary>
    private const double MaxPictureHeight = 300, LabelHeight = 40, ScreenInset = 6, WindowInset = 1.5;

    private (Rect Bounds, double Scale) Fit(double width)
    {
        var bounds = Rect.Empty;
        foreach (UIElement child in InternalChildren)
            if (!GetIsLabel(child) && GetWithin(child).IsEmpty) bounds.Union(GetArea(child));
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return (bounds, 0);
        return (bounds, Math.Min(width / bounds.Width, MaxPictureHeight / bounds.Height));
    }

    private double _width = 600;

    /// <summary>Takes the width it's given and never asks for more; its height follows that width.</summary>
    protected override Size MeasureOverride(Size available)
    {
        if (!double.IsInfinity(available.Width)) _width = available.Width;
        var (bounds, scale) = Fit(_width);
        foreach (UIElement child in InternalChildren)
            child.Measure(Place(child, bounds, scale).Size);
        return new Size(0, scale == 0 ? 0 : bounds.Height * scale + LabelHeight);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (Math.Abs(final.Width - _width) > 0.5) { _width = final.Width; InvalidateMeasure(); }
        var (bounds, scale) = Fit(final.Width);
        foreach (UIElement child in InternalChildren)
            child.Arrange(Place(child, bounds, scale));
        return final;
    }

    /// <summary>A display inset so two that touch have space between them, a window inside its display, a name under its display.</summary>
    private static Rect Place(UIElement child, Rect bounds, double scale)
    {
        if (scale == 0) return new Rect();
        var within = GetWithin(child);
        var screen = Scaled(within.IsEmpty ? GetArea(child) : within, ScreenInset);
        if (GetIsLabel(child)) return new Rect(screen.X, screen.Bottom + 6, screen.Width, LabelHeight - 6);
        if (within.IsEmpty) return screen;
        var window = Rect.Intersect(Scaled(GetArea(child), 0), Scaled(within, ScreenInset + 1));
        if (window.IsEmpty) return new Rect();
        window.Inflate(-WindowInset, -WindowInset);
        return window.Width > 0 && window.Height > 0 ? window : new Rect(window.X, window.Y, 0, 0);

        Rect Scaled(Rect area, double inset)
        {
            var r = new Rect((area.X - bounds.X) * scale, (area.Y - bounds.Y) * scale, area.Width * scale, area.Height * scale);
            r.Inflate(-inset, -inset);
            return r.Width > 0 && r.Height > 0 ? r : new Rect(r.X, r.Y, 0, 0);
        }
    }
}
