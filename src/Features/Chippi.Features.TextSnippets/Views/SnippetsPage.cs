// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Core.Diagnostics;
using Chippi.UI.Controls;

namespace Chippi.Features.TextSnippets.Views;

/// <summary>
/// Settings › Text snippets (docs/phase-6/design/wave-3.html, boards snippets and sheet-snippets-states): Snippet expansion
/// and its sound, Excluded apps, then every snippet by folder with its trigger, when it expands and its switch. The editor
/// opens in the row's place, or under the search row for a new one; a trigger that can't be saved is refused by Save.
/// </summary>
internal sealed class SnippetsPage : UserControl
{
    private const double TriggerColumn = 170, ExpandColumn = 200, SwitchColumn = 60, OptionsColumn = 44;
    /// <summary>The editor's fields start where the triggers do and end level with the options column.</summary>
    private const double FieldsColumn = TriggerColumn + ExpandColumn + SwitchColumn + OptionsColumn;
    private const double Dim = 0.45;
    public const string RightAway = "Right away", AfterDelimiter = "After Space, Tab or Enter";
    private const string NoFolder = "No folder", NewFolder = "New folder…";

    private static readonly (string Label, string? Sound)[] Sounds =
    [
        ("None", null), ("Asterisk", "Asterisk"), ("Default beep", "Beep"), ("Exclamation", "Exclamation"),
        ("Critical stop", "Hand"), ("Question", "Question"),
    ];

    /// <summary>The snippet being edited: a copy, so Cancel leaves the saved one as it was.</summary>
    internal sealed class Draft
    {
        public string? Id;
        public string Name = "", Trigger = "", Text = "", Folder = "";
        public ExpandMode Expand = ExpandMode.AfterDelimiter;
        public bool IgnoreCase, Enabled = true;
        /// <summary>Set once the trigger field has been typed in, so a new snippet doesn't open refused.</summary>
        public bool TriggerTouched;
    }

    private readonly TextSnippetsModule _module;
    private readonly StackPanel _page = new();
    private readonly Border _top = new();
    private readonly ExcludedAppsCard _excluded;
    private readonly Card _card = new();
    private readonly Border _searchRow;
    private readonly SearchBox _search = new() { Placeholder = "Search snippets", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _count = Styled(new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }, "BodyTextStyle");
    private readonly Button _add;
    private Draft? _draft;
    private FrameworkElement? _editor;
    private Button _save = new();
    private TextBox? _text;
    private StackPanel _message = new();
    private bool _refreshing;
    private readonly Border _noResults;
    /// <summary>Each snippet's row, kept while the snippet is unchanged.</summary>
    private readonly Dictionary<string, (Snippet Snippet, FrameworkElement View)> _rows = new(StringComparer.Ordinal);
    /// <summary>What the card holds under the search row, in order: headings (no snippet) and rows, with their folder.</summary>
    private readonly List<(FrameworkElement View, Snippet? Snippet, string Folder)> _entries = [];

    public SnippetsPage(TextSnippetsModule module)
    {
        _module = module;
        _excluded = new ExcludedAppsCard(() => _module.Settings.IgnoredApps, apps => Save(s => s with { IgnoredApps = [.. apps] })) { Margin = new Thickness(0, 8, 0, 0) };
        _count.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _add = SettingsLayout.AddButton("snippet", () => StartEdit(new Draft { Folder = LastFolder() }));
        AutomationProperties.SetName(_search, "Search snippets");
        _search.TextChanged += (_, _) => Filter();
        _noResults = Centered("No results");
        _searchRow = SearchRow();
        _card.Children.Add(_searchRow);
        _page.Children.Add(_top);
        _page.Children.Add(_excluded);
        _page.Children.Add(SettingsLayout.Section("Snippets"));
        _page.Children.Add(_card);
        Content = _page;
        void Changed() => Dispatcher.BeginInvoke(Refresh);
        Loaded += (_, _) => { _module.Changed += Changed; Refresh(); };
        Unloaded += (_, _) => _module.Changed -= Changed;
        Refresh();
    }

    private IReadOnlyList<Snippet> All => _module.Settings.Snippets;

    private void Save(Func<TextSnippetsSettings, TextSnippetsSettings> change)
    {
        try { _module.Update(change); }
        catch (Exception ex) { Log.Warn("Text snippets: saving failed", ex); }
    }

    private void Refresh()
    {
        _refreshing = true;
        _top.Child = TopCard();
        _excluded.Render();
        _refreshing = false;
        RenderList();
    }

    // ---- Snippet expansion ----

    private Card TopCard()
    {
        var s = _module.Settings;
        var card = new Card();
        if (_module.Error is { } error)
        {
            var retry = Button("Retry");
            retry.Click += (_, _) => Save(x => x);
            var glyph = Glyph(LucideIcons.TriangleAlert, "WarningTextBrush");
            var label = Styled(new TextBlock { Text = SnippetExpansionFailed(error), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) }, "BodyTextStyle");
            AutomationProperties.SetLiveSetting(label, AutomationLiveSetting.Polite);
            var lead = new StackPanel { Orientation = Orientation.Horizontal, Children = { glyph, label } };
            card.Children.Add(Line(lead, retry, SettingsLayout.RowPadding));
        }
        var expand = Switch(s.ExpandWhileTyping, labelled: true);
        OnSwitched(expand, on => Save(x => x with { ExpandWhileTyping = on }));
        card.Children.Add(new SettingRow { Header = "Snippet expansion", Icon = LucideIcons.AlignLeft, Content = expand });

        var sound = SettingsLayout.Combo(Sounds.Select(x => (object)x.Label));
        sound.Width = 200;
        AutomationProperties.SetName(sound, "Expansion sound");
        sound.SelectedIndex = s.SoundOnExpand ? Math.Max(0, Array.FindIndex(Sounds, x => x.Sound == (s.Sound ?? "Asterisk"))) : 0;
        sound.SelectionChanged += (_, _) =>
        {
            if (_refreshing || sound.SelectedIndex < 0) return;
            var chosen = Sounds[sound.SelectedIndex].Sound;
            Save(x => x with { SoundOnExpand = chosen is not null, Sound = chosen });
        };
        var soundLabel = Styled(new TextBlock { Text = "Expansion sound", VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle");
        card.Children.Add(Line(soundLabel, sound, SettingsLayout.DependentPadding));
        return card;
    }

    /// <summary>The hook's error as the row says it: "Snippet expansion failed".</summary>
    private static string SnippetExpansionFailed(string error) => error.EndsWith("failed", StringComparison.Ordinal) ? "Snippet expansion failed" : error;

    // ---- the list ----

    /// <summary>
    /// Lays out the list: the new snippet's editor, then each folder's heading and rows. A row whose snippet hasn't changed
    /// is reused, and search only hides rows (<see cref="Filter"/>), so typing in it stays quick with hundreds of snippets.
    /// </summary>
    private void RenderList()
    {
        if (_editor?.Parent is Panel old)
            old.Children.Remove(_editor);
        var all = All;
        // The search row stays, so typing in it keeps the keyboard.
        while (_card.Children.Count > 1)
            _card.Children.RemoveAt(1);
        _entries.Clear();
        _searchRow.Visibility = all.Count > 0 || _draft is not null ? Visibility.Visible : Visibility.Collapsed;
        if (_draft is { Id: null })
            _card.Children.Add(_editor!);
        if (all.Count == 0 && _draft is null)
            _card.Children.Add(EmptyRow());
        _card.Children.Add(_noResults);
        var live = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in all.GroupBy(s => s.Folder.Trim(), StringComparer.CurrentCultureIgnoreCase))
        {
            if (folder.Key.Length > 0)
            {
                var heading = Heading(folder.First().Folder.Trim());
                _card.Children.Add(heading);
                _entries.Add((heading, null, folder.Key));
            }
            foreach (var snippet in folder)
            {
                live.Add(snippet.Id);
                FrameworkElement view;
                if (_draft?.Id == snippet.Id)
                    view = _editor!;
                else if (_rows.TryGetValue(snippet.Id, out var cached) && cached.Snippet == snippet)
                    view = cached.View;
                else
                    _rows[snippet.Id] = (snippet, view = RowView(snippet));
                _card.Children.Add(view);
                _entries.Add((view, snippet, folder.Key));
            }
        }
        foreach (string gone in _rows.Keys.Where(id => !live.Contains(id)).ToList())
            _rows.Remove(gone);
        Filter();
    }

    /// <summary>Shows the rows the search matches, their folders' headings and the count ("2 of 12"), or No results.</summary>
    private void Filter()
    {
        var all = All;
        string query = _search.Text.Trim();
        int shown = 0;
        var folders = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var (view, snippet, folder) in _entries)
        {
            if (snippet is null)
                continue;
            bool match = query.Length == 0 || Matches(snippet, query);
            if (match) shown++;
            bool show = match || _draft?.Id == snippet.Id;
            view.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) folders.Add(folder);
        }
        foreach (var (view, snippet, folder) in _entries)
            if (snippet is null)
                view.Visibility = folders.Contains(folder) ? Visibility.Visible : Visibility.Collapsed;
        _count.Text = query.Length == 0 ? Count(all.Count) : $"{shown} of {all.Count}";
        _noResults.Visibility = query.Length > 0 && shown == 0 && all.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(Snippet s, string query) =>
        s.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || s.Trigger.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || s.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase) || s.Folder.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private static string Count(int n) => n == 1 ? "1 snippet" : $"{n} snippets";

    /// <summary>Search at the left, up to 340 wide; the count and Add snippet at the right.</summary>
    private Border SearchRow()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 340 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 12 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _count, _add } };
        Grid.SetColumn(right, 2);
        grid.Children.Add(_search);
        grid.Children.Add(right);
        return new Border { MinHeight = 64, Padding = new Thickness(16, 12, 16, 12), Child = grid };
    }

    private FrameworkElement EmptyRow()
    {
        var text = Styled(new TextBlock { Text = "No snippets yet", VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle");
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        return new Border { MinHeight = 96, Padding = SettingsLayout.RowPadding, Child = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { text } } };
    }

    private static Border Centered(string text)
    {
        var label = Styled(new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle");
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        AutomationProperties.SetLiveSetting(label, AutomationLiveSetting.Polite);
        return new Border { MinHeight = 96, Padding = SettingsLayout.RowPadding, Child = label };
    }

    private static Border Heading(string folder)
    {
        var heading = Styled(new TextBlock { Text = folder, VerticalAlignment = VerticalAlignment.Center }, "CaptionTextStyle");
        heading.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return new Border { MinHeight = 36, Padding = new Thickness(16, 10, 16, 2), Child = heading };
    }

    private FrameworkElement RowView(Snippet snippet)
    {
        string name = Label(snippet);
        var grid = Columns();
        var title = Styled(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis }, "BodyTextStyle");
        var preview = Styled(new TextBlock { Text = Preview(snippet.Text), TextTrimming = TextTrimming.CharacterEllipsis }, "CaptionTextStyle");
        preview.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), Children = { title, preview } };
        var trigger = Keycap(snippet.Trigger);
        var expand = Styled(new TextBlock { Text = snippet.Expand == ExpandMode.RightAway ? RightAway : AfterDelimiter, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, "CaptionTextStyle");
        expand.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var on = Switch(snippet.Enabled, labelled: false);
        AutomationProperties.SetName(on, name);
        OnSwitched(on, value => Save(s => s with { Snippets = [.. s.Snippets.Select(x => x.Id == snippet.Id ? x with { Enabled = value } : x)] }));
        on.HorizontalAlignment = HorizontalAlignment.Left;
        var more = new IconButton { Icon = LucideIcons.Ellipsis, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetName(more, $"{name} options");
        more.Click += (_, _) =>
        {
            // Built when it opens, so a page of hundreds of snippets doesn't build hundreds of menus.
            var menu = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Bottom };
            var edit = new MenuItem { Header = "Edit" };
            edit.Click += (_, _) => StartEdit(From(snippet));
            var delete = new MenuItem { Header = "Delete" };
            delete.Click += (_, _) => Confirm(more, () => Delete(snippet.Id));
            menu.Items.Add(edit);
            menu.Items.Add(delete);
            menu.IsOpen = true;
        };
        foreach (var dimmed in new FrameworkElement[] { text, trigger, expand })
            dimmed.Opacity = snippet.Enabled ? 1 : Dim;
        Place(grid, text, 0);
        Place(grid, trigger, 1);
        Place(grid, expand, 2);
        Place(grid, on, 3);
        Place(grid, more, 4);
        var row = new Border { MinHeight = SettingsLayout.CaptionRowHeight, Padding = SettingsLayout.RowPadding, Child = grid, Background = Brushes.Transparent };
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && (Inside<ButtonBase>(source)))
                return;
            StartEdit(From(snippet));
        };
        return row;
    }

    private static bool Inside<T>(DependencyObject source) where T : DependencyObject
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node))
            if (node is T) return true;
        return false;
    }

    /// <summary>A snippet's name, else its trigger, as Search shows it.</summary>
    internal static string Label(Snippet s) => string.IsNullOrWhiteSpace(s.Name) ? s.Trigger : s.Name;

    /// <summary>The text on one line, its lines joined with a dot as the board shows, so a long one trims instead of growing the row.</summary>
    internal static string Preview(string text) => string.Join(" · ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));

    private static Grid Columns()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 120 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TriggerColumn) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ExpandColumn) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SwitchColumn) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(OptionsColumn) });
        return grid;
    }

    private static void Place(Grid grid, FrameworkElement element, int column, int span = 1)
    {
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, span);
        element.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(element);
    }

    /// <summary>The trigger in the code font on a keycap, from the column's left edge.</summary>
    private static Border Keycap(string trigger)
    {
        var label = Styled(new TextBlock { Text = trigger, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, "CaptionTextStyle");
        label.SetResourceReference(TextBlock.FontFamilyProperty, "CodeFont");
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var cap = new Border { Child = label, Padding = new Thickness(8, 3, 8, 3), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = TriggerColumn - 16 };
        cap.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
        cap.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
        return cap;
    }

    // ---- the editor ----

    internal static Draft From(Snippet s) => new()
    {
        Id = s.Id, Name = s.Name, Trigger = s.Trigger, Text = s.Text, Folder = s.Folder.Trim(), Expand = s.Expand, IgnoreCase = s.IgnoreCase,
        Enabled = s.Enabled, TriggerTouched = true,
    };

    /// <summary>A new snippet goes in the folder last used, so adding several in a row keeps them together.</summary>
    private string LastFolder() => All.Count > 0 ? All[^1].Folder.Trim() : "";

    internal void StartEdit(Draft draft)
    {
        _draft = draft;
        _editor = BuildEditor(draft);
        RenderList();
        Dispatcher.BeginInvoke(() => (draft.Id is null ? _editor?.FindName("Name") as UIElement : null)?.Focus());
    }

    private void CloseEditor()
    {
        _draft = null;
        _editor = null;
        RenderList();
    }

    private void Delete(string id)
    {
        if (_draft?.Id == id)
        {
            _draft = null;
            _editor = null;
        }
        Save(s => s with { Snippets = [.. s.Snippets.Where(x => x.Id != id)] });
        RenderList();
    }

    private FrameworkElement BuildEditor(Draft draft)
    {
        var view = new StackPanel();
        NameScope.SetNameScope(view, new NameScope());
        view.SetResourceReference(Panel.BackgroundProperty, "HoverBrush");

        var titleIcon = Glyph(draft.Id is null ? LucideIcons.Plus : LucideIcons.Pencil, "TextPrimaryBrush");
        var title = Styled(new TextBlock { Text = draft.Id is null ? "New snippet" : "Edit snippet", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) }, "BodyTextStyle");
        var head = new StackPanel { Orientation = Orientation.Horizontal, Children = { titleIcon, title } };
        Button? delete = null;
        if (draft.Id is { } id)
        {
            delete = Button("Delete");
            delete.Margin = new Thickness(0);
            delete.Click += (_, _) => Confirm(delete, () => Delete(id));
        }
        view.Children.Add(Line(head, delete, SettingsLayout.RowPadding, MinHeight: SettingsLayout.RowHeight));

        var trigger = Field(draft.Trigger, "Trigger");
        trigger.SetResourceReference(TextBox.FontFamilyProperty, "CodeFont");
        trigger.MaxLength = SnippetRules.MaxTrigger;
        var triggerFrame = new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(0), Child = trigger };
        trigger.TextChanged += (_, _) => { draft.Trigger = trigger.Text; draft.TriggerTouched = true; Validate(); };
        var name = Field(draft.Name, "Name");
        view.RegisterName("Name", name);
        name.TextChanged += (_, _) => { draft.Name = name.Text; Validate(); };
        view.Children.Add(EditorRow("Name", name));

        view.Children.Add(EditorRow("Trigger", triggerFrame));

        _text = Styled(new TopAlignedField { Text = draft.Text, Tag = "Text" }, "TextFieldStyle");
        AutomationProperties.SetName(_text, "Text");
        _text.AcceptsReturn = true;
        _text.AcceptsTab = false;
        _text.TextWrapping = TextWrapping.Wrap;
        _text.Height = double.NaN;
        _text.MinHeight = 88;
        _text.MaxHeight = 220;
        _text.VerticalContentAlignment = VerticalAlignment.Top;
        _text.Padding = new Thickness(0, 6, 0, 6);
        _text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _text.TextChanged += (_, _) => { draft.Text = _text.Text; Validate(); };
        var text = _text;
        var insertDate = Button("Insert date or time…");
        insertDate.Margin = new Thickness(0);
        insertDate.Click += (_, _) => DateTimePicker.Open(insertDate, token => InsertToken(text, token));
        var insertClipboard = Button("Insert clipboard");
        insertClipboard.Click += (_, _) => InsertToken(text, "{{clipboard}}");
        var inserts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), Children = { insertDate, insertClipboard } };
        var textBox = new StackPanel { Width = FieldsColumn, Children = { _text, inserts } };
        view.Children.Add(EditorRow("Text", textBox, top: true));

        var expand = SettingsLayout.Combo(new object[] { AfterDelimiter, RightAway });
        expand.Width = FieldsColumn;
        AutomationProperties.SetName(expand, "Expand");
        expand.SelectedIndex = draft.Expand == ExpandMode.RightAway ? 1 : 0;
        expand.SelectionChanged += (_, _) => draft.Expand = expand.SelectedIndex == 1 ? ExpandMode.RightAway : ExpandMode.AfterDelimiter;
        view.Children.Add(EditorRow("Expand", expand));

        var ignore = Switch(draft.IgnoreCase, labelled: true);
        OnSwitched(ignore, on => { draft.IgnoreCase = on; Validate(); });
        AutomationProperties.SetName(ignore, "Ignore capitalization");
        ignore.HorizontalAlignment = HorizontalAlignment.Right;
        view.Children.Add(EditorRow("Ignore capitalization", new Border { Child = ignore }));

        // The combo style has no editable template, so a new folder is typed in a field that takes the combo's place.
        var folders = All.Select(s => s.Folder.Trim()).Where(f => f.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        if (draft.Folder.Length > 0 && !folders.Contains(draft.Folder, StringComparer.CurrentCultureIgnoreCase))
            folders.Add(draft.Folder);
        var choices = new List<object> { NoFolder };
        choices.AddRange(folders);
        choices.Add(NewFolder);
        var folder = SettingsLayout.Combo(choices);
        AutomationProperties.SetName(folder, "Folder");
        folder.SelectedItem = draft.Folder.Length == 0 ? NoFolder : folders.First(f => string.Equals(f, draft.Folder, StringComparison.CurrentCultureIgnoreCase));
        var newFolder = Field("", "Folder name");
        newFolder.Visibility = Visibility.Collapsed;
        object? before = folder.SelectedItem;
        folder.SelectionChanged += (_, _) =>
        {
            if (folder.SelectedItem is not string chosen) return;
            if (chosen == NewFolder)
            {
                folder.Visibility = Visibility.Collapsed;
                newFolder.Visibility = Visibility.Visible;
                newFolder.Text = "";
                newFolder.Focus();
                return;
            }
            before = chosen;
            draft.Folder = chosen == NoFolder ? "" : chosen;
        };
        newFolder.TextChanged += (_, _) => draft.Folder = newFolder.Text.Trim();
        void BackToList()
        {
            newFolder.Visibility = Visibility.Collapsed;
            folder.Visibility = Visibility.Visible;
            folder.SelectedItem = before;
            folder.Focus();
        }
        view.Children.Add(EditorRow("Folder", new Grid { Children = { folder, newFolder } }));
        foreach (var c in new FrameworkElement[] { folder, newFolder }) c.Width = FieldsColumn;

        _message = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _save = Button("Save", primary: true);
        _save.Margin = new Thickness(0);
        _save.Click += (_, _) => Commit();
        var cancel = Button("Cancel");
        cancel.Click += (_, _) => CloseEditor();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { _save, cancel } };
        var footer = new DockPanel { Margin = SettingsLayout.RowPadding };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_message);
        view.Children.Add(footer);
        view.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && newFolder.IsKeyboardFocused)
            {
                BackToList();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && e.OriginalSource is not ComboBox { IsDropDownOpen: true })
            {
                CloseEditor();
                e.Handled = true;
            }
        };

        void Validate()
        {
            string? error = draft.TriggerTouched ? SnippetRules.Check(draft.Trigger, All, draft.Id, draft.IgnoreCase) : null;
            string? warning = error is null ? SnippetRules.Warn(draft.Trigger, All, draft.Id) : null;
            bool refused = error is not null;
            trigger.BorderThickness = new Thickness(0);
            triggerFrame.BorderThickness = new Thickness(refused ? 2 : 0);
            triggerFrame.Margin = new Thickness(refused ? -2 : 0);
            triggerFrame.SetResourceReference(Border.BorderBrushProperty, "WarningTextBrush");
            ShowMessage(error ?? warning, refused);
            _save.IsEnabled = SnippetRules.Check(draft.Trigger, All, draft.Id, draft.IgnoreCase) is null && draft.Text.Length > 0;
        }
        Validate();
        return view;
    }

    private static readonly Thickness EditorPadding = new(16, 8, 16, 8);

    /// <summary>A row in the editor: the label inset like the list's text, the control in the fields' column at the right.</summary>
    private static Border EditorRow(string label, FrameworkElement control, bool top = false)
    {
        var text = Styled(new TextBlock { Text = label, VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Center, Margin = new Thickness(36, top ? 6 : 0, 0, 0) }, "BodyTextStyle");
        var grid = new Grid { MinHeight = 48 - 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 120 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(control, 1);
        control.Width = FieldsColumn;
        control.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Center;
        grid.Children.Add(control);
        return new Border { Padding = EditorPadding, Child = grid };
    }

    private static Border Line(FrameworkElement left, FrameworkElement? right, Thickness padding, double MinHeight = SettingsLayout.RowHeight)
    {
        var dock = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        if (right is not null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);
        }
        left.VerticalAlignment = VerticalAlignment.Center;
        dock.Children.Add(left);
        return new Border { MinHeight = MinHeight, Padding = padding, Child = dock };
    }

    /// <summary>An error by Save in Caution with its glyph; a warning (a trigger that starts another) in secondary with an info glyph.</summary>
    private void ShowMessage(string? text, bool refused)
    {
        _message.Children.Clear();
        _message.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        if (text is null) return;
        string brush = refused ? "WarningTextBrush" : "TextSecondaryBrush";
        var glyph = Glyph(refused ? LucideIcons.TriangleAlert : LucideIcons.Info, brush);
        glyph.Margin = new Thickness(0, 0, 8, 0);
        var label = Styled(new TextBlock { Text = text.TrimEnd('.'), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }, "BodyTextStyle");
        label.SetResourceReference(TextBlock.ForegroundProperty, brush);
        AutomationProperties.SetLiveSetting(label, AutomationLiveSetting.Polite);
        _message.Children.Add(glyph);
        _message.Children.Add(label);
    }

    internal void Commit()
    {
        var d = _draft;
        if (d is null || SnippetRules.Check(d.Trigger, All, d.Id, d.IgnoreCase) is not null || d.Text.Length == 0)
            return;
        var saved = new Snippet
        {
            Id = d.Id ?? Guid.NewGuid().ToString("N"), Name = d.Name.Trim(), Trigger = d.Trigger, Text = d.Text, Expand = d.Expand,
            IgnoreCase = d.IgnoreCase, Folder = d.Folder.Trim(), Enabled = d.Enabled,
        };
        _draft = null;
        _editor = null;
        Save(s => s with { Snippets = d.Id is null ? [.. s.Snippets, saved] : [.. s.Snippets.Select(x => x.Id == d.Id ? saved : x)] });
        RenderList();
    }

    private static void InsertToken(TextBox box, string token)
    {
        int at = box.CaretIndex;
        box.SelectedText = token;
        box.CaretIndex = at + token.Length;
        box.SelectionLength = 0;
        box.Focus();
    }

    // ---- shared pieces ----

    /// <summary>"Delete this snippet?" under the button, with Cancel and Delete (Critical).</summary>
    private static void Confirm(FrameworkElement anchor, Action confirmed)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None };
        var panel = ConfirmPanel(() => { popup.IsOpen = false; anchor.Focus(); }, () => { popup.IsOpen = false; confirmed(); }, out var first);
        popup.Child = panel;
        popup.Opened += (_, _) => first.Focus();
        popup.IsOpen = true;
    }

    internal static Border ConfirmPanel(Action cancelled, Action confirmed, out Button first)
    {
        var cancel = Button("Cancel");
        cancel.Margin = new Thickness(0);
        cancel.Click += (_, _) => cancelled();
        var act = Styled(new Button { Content = "Delete", Margin = new Thickness(8, 0, 0, 0) }, "CriticalButtonStyle");
        act.Click += (_, _) => confirmed();
        var question = Styled(new TextBlock { Text = "Delete this snippet?", FontWeight = FontWeights.SemiBold }, "BodyTextStyle");
        var body = new StackPanel { Margin = new Thickness(16), Children = { question, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { cancel, act } } } };
        KeyboardNavigation.SetTabNavigation(body, KeyboardNavigationMode.Cycle);
        body.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            cancelled();
            e.Handled = true;
        };
        first = cancel;
        return Flyout(body, 260);
    }

    internal static Border Flyout(UIElement body, double minWidth = 0)
    {
        var panel = new Border { MinWidth = minWidth, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = body };
        panel.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        panel.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        return panel;
    }

    internal static TextBox Field(string text, string placeholder)
    {
        var box = Styled(new TextBox { Text = text, Tag = placeholder, VerticalContentAlignment = VerticalAlignment.Center }, "TextFieldStyle");
        AutomationProperties.SetName(box, placeholder);
        return box;
    }

    private static ToggleButton Switch(bool on, bool labelled)
    {
        var toggle = Styled(new ToggleButton { IsChecked = on, VerticalAlignment = VerticalAlignment.Center }, labelled ? "SwitchWithLabelStyle" : "SwitchStyle");
        if (labelled) toggle.Content = on ? "On" : "Off";
        return toggle;
    }

    /// <summary>A UI Automation toggle raises Checked and Unchecked, not Click.</summary>
    private static void OnSwitched(ToggleButton toggle, Action<bool> changed)
    {
        void Handler(object sender, RoutedEventArgs e)
        {
            if (toggle.Content is string) toggle.Content = toggle.IsChecked == true ? "On" : "Off";
            changed(toggle.IsChecked == true);
        }
        toggle.Checked += Handler;
        toggle.Unchecked += Handler;
    }

    internal static Button Button(string text, bool primary = false) =>
        Styled(new Button { Content = text, Margin = new Thickness(8, 0, 0, 0) }, primary ? "PrimaryButtonStyle" : "SecondaryButtonStyle");

    private static LucideIcon Glyph(Geometry data, string brush)
    {
        var icon = new LucideIcon { Data = data, Size = 16, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(LucideIcon.BrushProperty, brush);
        return icon;
    }

    internal static T Styled<T>(T element, string style) where T : FrameworkElement
    {
        element.SetResourceReference(StyleProperty, style);
        return element;
    }
}

/// <summary>A multi-line field: the text starts at the top, where the shared field style centres one line.</summary>
internal sealed class TopAlignedField : TextBox
{
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_ContentHost") is FrameworkElement host)
            host.VerticalAlignment = VerticalAlignment.Top;
    }
}
