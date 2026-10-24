// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Core.Modules;
using Chippi.Features.Downloads.Model;
using Chippi.Rules;
using Chippi.UI.Controls;

namespace Chippi.Features.Downloads.Views.Settings;

/// <summary>
/// The rules editor (DESIGN.md §5): a "test a file name" box over an ordered accordion, one rule
/// open at a time. Edits save as soon as the rule is valid; there is no Save button. Reorder with
/// the grip (drag, or Up/Down while it has focus) or Alt+Up/Down anywhere in a rule.
/// </summary>
internal sealed class RulesSection : UserControl
{
    private static readonly (string Label, long Bytes)[] Units = [("KB", 1024), ("MB", 1024 * 1024), ("GB", 1024L * 1024 * 1024)];

    private readonly DownloadsModule _module;

    /// <summary>Drafts on screen and the committed rules that are saved and tested.</summary>
    private readonly RuleDrafts _drafts;

    private List<RuleSetting> _rules => _drafts.Drafts;
    private readonly ContentControl _list = new() { Focusable = false };
    private readonly List<Border> _rows = [];
    private readonly TextBox _test = Ui.BareField("Test a file name, like notes.txt", "Test a file name");
    private readonly TextBlock _testResult = Ui.Text("");
    private string? _open;
    private string? _match;
    private readonly Dictionary<string, ContentControl> _badges = [];
    private readonly System.Windows.Threading.DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int _dragFrom = -1;
    private int _dragTo = -1;

    public RulesSection(DownloadsModule module)
    {
        _module = module;
        _drafts = new RuleDrafts(module.Settings.Rules);

        // The test card: a search glyph, the field across the card, and where the file would go on the right.
        _test.TextChanged += (_, _) => RunTest();
        _testResult.Margin = new Thickness(14, 0, 0, 0);
        _testResult.VerticalAlignment = VerticalAlignment.Center;
        var testGrid = Ui.Columns("Auto", "*", "Auto");
        testGrid.Children.Add(Ui.Glyph(LucideIcons.Search, "TextSecondaryBrush", 16).Margin(0, 0, 12, 0).At(0));
        testGrid.Children.Add(_test.At(1));
        testGrid.Children.Add(_testResult.At(2));

        var add = Ui.Button("Add rule", icon: LucideIcons.Plus);
        add.Click += (_, _) => AddRule();
        var title = Ui.Row(Ui.Text("Rules", "HeadlineTextStyle"), Ui.Text("First match wins", "CaptionTextStyle").Margin(10, 0, 0, 0));
        var header = Ui.Columns("*", "Auto").Margin(0, 24, 0, 8);
        header.Children.Add(title.At(0));
        header.Children.Add(add.At(1));

        Content = Ui.Stack(Ui.Card(Ui.Pad(testGrid)).Margin(0, 16, 0, 0), header, _list);
        // Typing saves after a short pause, so the organizer is not re-applied on every key.
        _saveTimer.Tick += (_, _) => SaveNow();
        Unloaded += (_, _) =>
        {
            if (_saveTimer.IsEnabled)
                SaveNow();
        };
        Render();
        RunTest();
    }

    // ---- list

    private void Render(string? focusHandleOf = null)
    {
        _drafts.RevertProblems(); // editors are rebuilt from drafts
        _badges.Clear();
        _rows.Clear();
        if (_rules.Count == 0)
        {
            _list.Content = Ui.Card(Ui.Pad(Ui.Text("No rules yet", "BodyTextStyle", "TextTertiaryBrush")));
            return;
        }
        var card = new Card();
        for (int i = 0; i < _rules.Count; i++)
        {
            var row = RuleRow(_rules[i], i, out var handle);
            _rows.Add(row);
            card.Children.Add(row);
            if (_rules[i].Id == focusHandleOf)
                Dispatcher.BeginInvoke(() => handle.Focus());
        }
        _list.Content = card;
    }

    private Border RuleRow(RuleSetting rule, int index, out DragHandle handle)
    {
        bool open = rule.Id == _open;
        handle = new DragHandle { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Drag to reorder (Alt+Up/Down)" };
        System.Windows.Automation.AutomationProperties.SetName(handle, $"Reorder {rule.Name}");
        handle.MoveRequested += (_, delta) => Move(index, delta);
        handle.DragStarted += (_, _) => { _dragFrom = index; _dragTo = index; };
        handle.DragDelta += (s, e) => DragTo(index, ((DragHandle)s).ActualHeight, e);
        handle.DragCompleted += (_, _) => DropDragged();

        var summary = Ui.Text(RuleChecks.Summary(rule), "CaptionTextStyle", "TextTertiaryBrush");
        summary.ToolTip = DestinationTip(rule);
        var title = Ui.Stack(Ui.Text(rule.Name.Length > 0 ? rule.Name : "New rule", "TitleTextStyle"), summary);
        var enabled = Ui.Switch($"{rule.Name} on", rule.Enabled);
        enabled.SetResourceReference(StyleProperty, "SwitchWithLabelStyle"); // "On" or "Off" left of the switch (DESIGN.md §5 Rows)
        void Enable()
        {
            if (enabled.IsChecked == rule.Enabled)
                return;
            rule.Enabled = enabled.IsChecked == true;
            title.Opacity = rule.Enabled ? 1 : 0.6;
            Save();
            RunTest();
        }
        // Checked/Unchecked, so a UI Automation toggle reaches the rule too.
        enabled.Checked += (_, _) => Enable();
        enabled.Unchecked += (_, _) => Enable();
        var chevron = Ui.Icon(open ? LucideIcons.Up : LucideIcons.ChevronDown, open ? $"Close {rule.Name}" : $"Edit {rule.Name}");
        chevron.Click += (_, _) => Toggle(rule);

        var head = Ui.Columns("Auto", "34", "*", "Auto", "Auto", "Auto");
        head.Background = Brushes.Transparent; // the whole header opens the rule
        head.Children.Add(handle.At(0));
        head.Children.Add(Ui.Number($"{index + 1}", "TextTertiaryBrush").Margin(10, 0, 0, 0).At(1));
        head.Children.Add(title.At(2));
        var badge = new ContentControl { Focusable = false, IsTabStop = false, VerticalAlignment = VerticalAlignment.Center };
        _badges[rule.Id] = badge;
        head.Children.Add(badge.At(3));
        head.Children.Add(enabled.At(4));
        head.Children.Add(chevron.Margin(8, 0, 0, 0).At(5));
        head.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && !IsInside<ButtonBase>(source) && !IsInside<Thumb>(source))
                Toggle(rule);
        };

        var stack = Ui.Stack(head);
        if (open)
            stack.Children.Add(Editor(rule, summary));
        // The border is the drop line while a drag targets this row.
        // Padding plus the 2px border makes the 10 / 16 row padding.
        var row = new Border { Child = stack, Padding = new Thickness(16, 8, 16, 8), MinHeight = 60, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0, 2, 0, 2) };
        if (!rule.Enabled)
            title.Opacity = 0.6;
        row.PreviewKeyDown += (_, e) =>
        {
            // Alt+Up/Down moves the rule from anywhere inside it.
            if (e.Key == Key.System && e.SystemKey is Key.Up or Key.Down) // Alt+arrow arrives as a system key
            {
                e.Handled = true;
                Move(index, e.SystemKey == Key.Up ? -1 : 1);
            }
        };
        return row;
    }

    private void Toggle(RuleSetting rule)
    {
        _open = _open == rule.Id ? null : rule.Id;
        Render();
    }

    private void Move(int index, int delta)
    {
        int target = index + delta;
        if (target < 0 || target >= _rules.Count)
            return;
        var rule = _rules[index];
        _rules.RemoveAt(index);
        _rules.Insert(target, rule);
        Save();
        RunTest();
        Render(focusHandleOf: rule.Id);
    }

    private void DragTo(int index, double handleHeight, DragDeltaEventArgs e)
    {
        if (_dragFrom < 0)
            return;
        // Rows are about one row apart; an accent edge shows where the rule will land.
        double rowHeight = _list.ActualHeight > 0 ? _list.ActualHeight / Math.Max(1, _rules.Count) : 60;
        int target = Math.Clamp(index + (int)Math.Round(e.VerticalChange / rowHeight), 0, _rules.Count - 1);
        if (target == _dragTo)
            return;
        _dragTo = target;
        ShowDropLine(target == _dragFrom ? -1 : target, below: target > _dragFrom);
    }

    /// <summary>The 2px accent line on the target row's top edge (moving up) or bottom edge (moving down).</summary>
    private void ShowDropLine(int row, bool below)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (i == row)
            {
                _rows[i].SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                _rows[i].BorderThickness = below ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0);
                _rows[i].Padding = below ? new Thickness(16, 10, 16, 8) : new Thickness(16, 8, 16, 10);
            }
            else
            {
                _rows[i].BorderBrush = Brushes.Transparent;
                _rows[i].BorderThickness = new Thickness(0, 2, 0, 2);
                _rows[i].Padding = new Thickness(16, 8, 16, 8);
            }
        }
    }

    private void DropDragged()
    {
        ShowDropLine(-1, false);
        int from = _dragFrom, to = _dragTo;
        _dragFrom = _dragTo = -1;
        if (from < 0 || to < 0 || from == to)
            return;
        var rule = _rules[from];
        _rules.RemoveAt(from);
        _rules.Insert(to, rule);
        Save();
        RunTest();
        Dispatcher.BeginInvoke(() => Render(focusHandleOf: rule.Id)); // after the thumb finishes its own drag
    }

    private void AddRule()
    {
        var rule = new RuleSetting
        {
            Id = RuleDrafts.NewId(),
            Name = "New rule",
            Extensions = ["pdf"],
            Action = RuleActionKind.Move,
            Template = DocumentsFolder(),
        };
        _rules.Add(rule);
        _open = rule.Id;
        Save();
        Render();
    }

    // ---- editor

    private StackPanel Editor(RuleSetting rule, TextBlock summary)
    {
        var editor = new StackPanel { Margin = new Thickness(48, 14, 6, 4) };
        void Changed()
        {
            summary.Text = RuleChecks.Summary(rule);
            summary.ToolTip = DestinationTip(rule);
            Save();
            RunTest();
        }

        // Name
        var name = Ui.Field(rule.Name, name: "Rule name", width: 320);
        var nameError = Hidden(Ui.Warning(""));
        name.TextChanged += (_, _) =>
        {
            rule.Name = name.Text.Trim();
            Flag(rule, "name", nameError, rule.Name.Length == 0 ? "A name is required" : null);
            Changed();
        };
        editor.Children.Add(Line("Name", Ui.Stack(name, nameError)));

        // Conditions: each turns on with a checkbox; an unchecked one's fields dim.
        editor.Children.Add(Ui.Text("Conditions", "HeadlineTextStyle").Margin(0, 14, 0, 8));

        var extensions = Ui.Field(rule.Extensions is { } e ? string.Join(", ", e) : "", name: "Extensions", width: 320);
        var extensionsError = Hidden(Ui.Warning(""));
        var extensionsOn = Ui.Check("Extension", rule.Extensions is not null);
        void ApplyExtensions()
        {
            var list = RuleChecks.SplitExtensions(extensions.Text);
            string? error = extensionsOn.IsChecked == true ? list.Count == 0 ? "Add an extension" : RuleChecks.Extensions(extensions.Text) : null;
            Flag(rule, "extensions", extensionsError, error);
            rule.Extensions = extensionsOn.IsChecked == true ? list : null;
            Changed();
        }
        extensions.TextChanged += (_, _) => ApplyExtensions();
        editor.Children.Add(Condition(extensionsOn, "Extension", Ui.Stack(extensions, extensionsError), ApplyExtensions));

        var regex = Ui.Field(rule.NameRegex ?? "", code: true, name: "Name pattern");
        var regexError = Hidden(Ui.Warning(""));
        var regexOn = Ui.Check("Name matches", rule.NameRegex is { Length: > 0 });
        void ApplyRegex()
        {
            bool on = regexOn.IsChecked == true;
            Flag(rule, "regex", regexError, on ? regex.Text.Length == 0 ? "Add a pattern" : RuleChecks.Regex(regex.Text) : null);
            rule.NameRegex = on && regex.Text.Length > 0 ? regex.Text : null;
            Changed();
        }
        regex.TextChanged += (_, _) => ApplyRegex();
        editor.Children.Add(Condition(regexOn, "Name matches", Ui.Stack(Wide(regex), regexError), ApplyRegex));

        int unit = rule.MaxBytes is { } mx && mx % Units[2].Bytes == 0 && mx > 0 ? 2 : rule.MaxBytes is { } k && k % Units[1].Bytes != 0 ? 0 : 1;
        var min = Ui.Field(Amount(rule.MinBytes, Units[unit].Bytes), name: "Minimum size", width: 90);
        var max = Ui.Field(Amount(rule.MaxBytes, Units[unit].Bytes), name: "Maximum size", width: 90);
        var units = Ui.Choice(Units.Select(u => u.Label), unit, "Size unit", 76);
        var sizeError = Hidden(Ui.Warning(""));
        var sizeOn = Ui.Check("Size", rule.MinBytes is not null || rule.MaxBytes is not null);
        void ApplySize()
        {
            bool on = sizeOn.IsChecked == true;
            long scale = Units[Math.Max(0, units.SelectedIndex)].Bytes;
            bool okMin = TryAmount(min.Text, scale, out long? low), okMax = TryAmount(max.Text, scale, out long? high);
            string? error = !on ? null : !okMin || !okMax ? "Use a number" : low is null && high is null ? "Add a minimum or maximum" : low > high ? "Min is above max" : null;
            Flag(rule, "size", sizeError, error);
            rule.MinBytes = on && error is null ? low : null;
            rule.MaxBytes = on && error is null ? high : null;
            Changed();
        }
        min.TextChanged += (_, _) => ApplySize();
        max.TextChanged += (_, _) => ApplySize();
        units.SelectionChanged += (_, _) => ApplySize();
        var sizeFields = Ui.Row(Ui.Text("Min", "CaptionTextStyle", "TextTertiaryBrush").Margin(0, 0, 8, 0), min,
            Ui.Text("Max", "CaptionTextStyle", "TextTertiaryBrush").Margin(14, 0, 8, 0), max, units.Margin(8, 0, 0, 0));
        editor.Children.Add(Condition(sizeOn, "Size", Ui.Stack(sizeFields, sizeError), ApplySize));

        var after = Ui.Field(rule.ModifiedAfter?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", name: "Modified after", width: 120);
        var before = Ui.Field(rule.ModifiedBefore?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", name: "Modified before", width: 120);
        var dateError = Hidden(Ui.Warning(""));
        var dateOn = Ui.Check("Modified", rule.ModifiedAfter is not null || rule.ModifiedBefore is not null);
        void ApplyDates()
        {
            bool on = dateOn.IsChecked == true;
            bool okAfter = TryDate(after.Text, out var from), okBefore = TryDate(before.Text, out var to);
            string? error = !on ? null : !okAfter || !okBefore ? "Use yyyy-mm-dd" : from is null && to is null ? "Add a date" : from > to ? "After is later than before" : null;
            Flag(rule, "dates", dateError, error);
            rule.ModifiedAfter = on && error is null ? from : null;
            rule.ModifiedBefore = on && error is null ? to : null;
            Changed();
        }
        after.TextChanged += (_, _) => ApplyDates();
        before.TextChanged += (_, _) => ApplyDates();
        var dateFields = Ui.Row(Ui.Text("After", "CaptionTextStyle", "TextTertiaryBrush").Margin(0, 0, 8, 0), after,
            Ui.Text("Before", "CaptionTextStyle", "TextTertiaryBrush").Margin(14, 0, 8, 0), before);
        editor.Children.Add(Condition(dateOn, "Modified", Ui.Stack(dateFields, dateError), ApplyDates));

        // Action
        editor.Children.Add(Ui.Text("Action", "HeadlineTextStyle").Margin(0, 14, 0, 8));
        bool hold = rule.Action == RuleActionKind.Hold;
        var action = hold ? Ui.Segment("Action", "Move to folder", "Rename", "Hold") : Ui.Segment("Action", "Move to folder", "Rename");
        action.SelectedIndex = (int)rule.Action;
        editor.Children.Add(Line("", action));

        // Rename types its template. Move shows the folder, chosen with the Windows folder picker,
        // which has its own New folder button.
        var templateNote = Ui.Row();
        var template = Ui.Field(rule.Template ?? "", code: true, name: "New name");
        var path = Ui.Field(name: "Destination", width: 320);
        path.IsReadOnly = true;
        var browse = Ui.Button("Browse…", name: "Choose destination folder");
        browse.Margin = new Thickness(8, 0, 0, 0);
        var pathRow = Ui.Row(path, browse);
        void ShowPath()
        {
            path.Text = DestinationShown(rule.Template ?? "");
            path.ToolTip = DestinationTip(rule);
        }
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose destination", Multiselect = false };
            if (ExistingFolder(rule.Template ?? "") is { } start)
                dialog.InitialDirectory = start;
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
                return;
            rule.Template = dialog.FolderName;
            ShowPath();
            ShowTemplateNote(rule, templateNote);
            Changed();
        };
        var tokens = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var targetLabel = Ui.Text(rule.Action == RuleActionKind.Rename ? "New name" : "Destination", "BodyTextStyle");
        var reason = Ui.Field(rule.HoldReason ?? "", name: "Hold reason", width: 320);
        void ApplyTemplate()
        {
            rule.Template = template.Text;
            ShowTemplateNote(rule, templateNote);
            Changed();
        }
        void FillTokens()
        {
            // Only a new name takes placeholders; a destination is just the folder picked.
            tokens.Children.Clear();
            if (rule.Action != RuleActionKind.Rename)
                return;
            foreach (string token in RuleChecks.RenameTokens)
            {
                var chip = Ui.Button(token, name: $"Insert {token}");
                chip.SetResourceReference(Control.FontFamilyProperty, "CodeFont");
                chip.Padding = new Thickness(8, 0, 8, 0);
                chip.MinHeight = 22;
                chip.Height = 22;
                chip.Margin = new Thickness(0, 0, 6, 6);
                chip.Click += (_, _) =>
                {
                    int at = template.CaretIndex;
                    template.Text = template.Text.Insert(at, token);
                    template.CaretIndex = at + token.Length;
                    template.Focus();
                };
                tokens.Children.Add(chip);
            }
        }
        template.TextChanged += (_, _) => ApplyTemplate();
        reason.TextChanged += (_, _) =>
        {
            rule.HoldReason = reason.Text.Trim();
            Changed();
        };
        var targetBlock = Ui.Stack(Wide(template), pathRow, templateNote.Margin(0, 6, 0, 0), tokens);
        var targetLine = Line("", targetBlock);
        var reasonLine = Line("Reason", reason);
        targetLine.Children.Add(TopLabel(targetLabel).At(0));
        editor.Children.Add(targetLine);
        editor.Children.Add(reasonLine);
        void ShowAction()
        {
            bool isHold = rule.Action == RuleActionKind.Hold;
            targetLine.Visibility = isHold ? Visibility.Collapsed : Visibility.Visible;
            reasonLine.Visibility = isHold ? Visibility.Visible : Visibility.Collapsed;
            bool rename = rule.Action == RuleActionKind.Rename;
            targetLabel.Text = rename ? "New name" : "Destination";
            template.Visibility = rename ? Visibility.Visible : Visibility.Collapsed;
            pathRow.Visibility = rename ? Visibility.Collapsed : Visibility.Visible;
            ShowPath();
            FillTokens();
            ShowTemplateNote(rule, templateNote);
        }
        action.SelectionChanged += (_, _) =>
        {
            if (action.SelectedIndex < 0)
                return;
            var kind = (RuleActionKind)action.SelectedIndex;
            if (kind == rule.Action)
                return;
            rule.Action = kind;
            // A folder rarely makes a good file name, and the other way round.
            if (kind == RuleActionKind.Rename)
                template.Text = "{year}-{month}-{day} {filename}";
            else if (kind == RuleActionKind.Move)
                rule.Template = DocumentsFolder();
            ShowAction();
            Changed();
        };
        ShowAction();

        // Duplicate, Delete
        var duplicate = Ui.Button("Duplicate", name: $"Duplicate {rule.Name}");
        duplicate.IsEnabled = _drafts.IsCommitted(rule.Id); // a rule that has never been valid has nothing to copy
        duplicate.Click += (_, _) =>
        {
            if (_drafts.Duplicate(rule) is not { } copy)
                return;
            _open = copy.Id;
            Save();
            Render();
        };
        var delete = Ui.Button("Delete", icon: LucideIcons.Trash, name: $"Delete {rule.Name}");
        delete.Click += (_, _) =>
        {
            _drafts.Remove(rule);
            _open = null;
            Save();
            RunTest();
            Render();
        };
        var divider = new Border { Height = 1, Margin = new Thickness(0, 14, 0, 12) };
        divider.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        editor.Children.Add(divider);
        editor.Children.Add(Ui.Row(duplicate, delete.Margin(8, 0, 0, 0)));
        return editor;
    }

    /// <summary>Under the template: the amber problem, or a check and where a sample file would go.</summary>
    private void ShowTemplateNote(RuleSetting rule, StackPanel note)
    {
        note.Children.Clear();
        if (rule.Action == RuleActionKind.Hold)
            return;
        bool rename = rule.Action == RuleActionKind.Rename;
        if (RuleChecks.Template(rule.Template ?? "", rename) is { } problem)
        {
            note.Children.Add(Ui.Glyph(LucideIcons.Info, "WarningBrush", 12));
            note.Children.Add(Ui.Text(problem, "CaptionTextStyle", "WarningTextBrush").Margin(5, 0, 0, 0));
            return;
        }
        string folder = SampleFolder();
        string sample = _test.Text.Trim() is { Length: > 0 } t ? t : "file.pdf";
        string rendered;
        try
        {
            rendered = Templates.Render(rule.Template!, sample, DateOnly.FromDateTime(DateTime.Now));
        }
        catch (ArgumentException)
        {
            return;
        }
        // The destination field already shows a plain folder; the note adds what it can't.
        string? where = rename ? $"Stays in place · {rendered}"
            : !WindowsPaths.IsAbsolute(rendered) ? "Inside each watched folder"
            : rendered != rule.Template ? rendered
            : null;
        if (where is null)
            return;
        note.Children.Add(Ui.Glyph(LucideIcons.Check, "SuccessBrush", 12));
        note.Children.Add(Ui.Text(where, "CaptionTextStyle", "TextTertiaryBrush").Margin(5, 0, 0, 0));
    }

    private static Grid Line(string label, UIElement field)
    {
        var grid = Ui.Columns("164", "*");
        if (label.Length > 0)
            grid.Children.Add(TopLabel(Ui.Text(label, "BodyTextStyle")).At(0));
        grid.Children.Add(((FrameworkElement)field).At(1));
        grid.Margin = new Thickness(0, 0, 0, 10);
        return grid;
    }

    /// <summary>Labels sit level with the first line of their field, not the middle of a tall block.</summary>
    private static TextBlock TopLabel(TextBlock label)
    {
        label.VerticalAlignment = VerticalAlignment.Top;
        label.Margin = new Thickness(0, 8, 0, 0);
        return label;
    }

    private static Grid Condition(CheckBox toggle, string label, FrameworkElement fields, Action apply)
    {
        var head = Ui.Row(toggle, Ui.Text(label, "BodyTextStyle").Margin(10, 0, 0, 0));
        void Dim()
        {
            fields.IsEnabled = toggle.IsChecked == true;
            fields.Opacity = fields.IsEnabled ? 1 : 0.45;
        }
        // Checked/Unchecked, so a UI Automation toggle applies the condition too.
        void Flipped()
        {
            Dim();
            apply();
        }
        toggle.Checked += (_, _) => Flipped();
        toggle.Unchecked += (_, _) => Flipped();
        Dim();
        var grid = Ui.Columns("164", "*");
        grid.Children.Add(head.At(0));
        grid.Children.Add(fields.At(1));
        grid.Margin = new Thickness(0, 0, 0, 10);
        return grid;
    }

    // ---- test a file name

    private void RunTest()
    {
        string name = _test.Text.Trim();
        _match = null;
        _testResult.Text = "";
        if (name.Length > 0)
        {
            string folder = SampleFolder();
            // What would actually run: the committed rules, not drafts mid-edit.
            var rules = RuleDrafts.Valid(_drafts.Effective()).Select((r, i) => r.ToRule() with { Order = i }).ToList();
            PlannedOperation plan;
            try
            {
                var now = DateTimeOffset.Now;
                plan = Planner.Plan(new FileFacts(name, Path.Combine(folder, name), 1024, now, now), rules, new PlanContext(folder, now, TimeZoneInfo.Local, []));
            }
            catch (ArgumentException)
            {
                plan = new PlannedOperation(OperationKind.Hold, name, null, null, [new PlanWarning(WarningKind.InvalidPath, "Not a valid file name")]);
            }
            _match = plan.Kind is OperationKind.Move or OperationKind.Rename or OperationKind.Hold ? plan.RuleId : null;
            int index = _rules.FindIndex(r => r.Id == plan.RuleId);
            // DESIGN.md §5: "Txt Files → Documents" in Secondary, a hold in caution, no match in Caption.
            (string text, string style, string brush) = plan.Kind switch
            {
                OperationKind.Move or OperationKind.Rename =>
                    ($"{(index >= 0 ? _rules[index].Name : "Rule")} → {Ui.Relative(folder, plan.Destination!)}", "BodyTextStyle", "TextSecondaryBrush"),
                OperationKind.Hold => ($"Held · {plan.HoldReason ?? plan.Warnings.LastOrDefault()?.Detail ?? "held"}", "BodyTextStyle", "WarningTextBrush"),
                _ => ($"No rule matches · stays in {Ui.FolderName(folder)}", "CaptionTextStyle", "TextTertiaryBrush"),
            };
            _testResult.Text = text;
            _testResult.SetResourceReference(StyleProperty, style);
            _testResult.SetResourceReference(TextBlock.ForegroundProperty, brush);
        }
        // The winning rule gets a neutral Match badge, in place, so a field being edited keeps focus.
        foreach (var (id, host) in _badges)
            host.Content = id == _match ? Ui.Badge("Match", BadgeKind.Needs).Margin(0, 0, 10, 0) : null;
    }

    private string SampleFolder() =>
        _module.Settings.Folders.FirstOrDefault()?.Path ?? DownloadsModule.DownloadsFolder() ?? Path.GetTempPath();

    // ---- destinations
    // A relative destination (rules from before the folder picker) stays relative: it means "inside
    // the watched folder the file is in", which no single full path can say once two folders are watched.

    private static string DocumentsFolder() => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <summary>The destination field: a chosen folder in full, a relative one under the watched folder's name ("Downloads\Documents").</summary>
    private string DestinationShown(string template) =>
        WindowsPaths.IsAbsolute(template) ? template : Path.Combine(Ui.FolderName(SampleFolder()), template);

    /// <summary>The full path on hover, for a Move rule.</summary>
    private string? DestinationTip(RuleSetting rule) =>
        rule.Action != RuleActionKind.Move || string.IsNullOrEmpty(rule.Template) ? null
        : WindowsPaths.IsAbsolute(rule.Template) ? rule.Template
        : Path.Combine(SampleFolder(), rule.Template);

    /// <summary>Where the picker opens: the deepest existing folder of the destination, else the watched folder.</summary>
    private string? ExistingFolder(string template)
    {
        string? folder = template.IndexOf('{') is int at and >= 0 ? template[..at] : template;
        folder = WindowsPaths.IsAbsolute(folder) ? folder : Path.Combine(SampleFolder(), folder);
        while (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            folder = Path.GetDirectoryName(folder);
        return string.IsNullOrEmpty(folder) ? null : folder;
    }

    // ---- saving

    /// <summary>Commits every fully valid draft, then saves after a short pause.</summary>
    private void Save()
    {
        _drafts.Commit();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Saves the committed rules in list order. A draft with a problem leaves its last valid version in place.</summary>
    private void SaveNow()
    {
        _saveTimer.Stop();
        _drafts.Commit();
        var settings = _module.Settings.Clone();
        settings.Rules = _drafts.Effective();
        try
        {
            _module.SaveSettings(settings);
        }
        catch (InvalidOperationException)
        {
            // The organizer stopped; the page goes with it.
        }
    }

    // ---- helpers

    /// <summary>
    /// A long field (a pattern, a name template) fills its column up to 460, never past the page edge.
    /// The cap is on a grid column: a capped stretched field would center itself.
    /// </summary>
    private static Grid Wide(TextBox field)
    {
        field.HorizontalAlignment = HorizontalAlignment.Stretch;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 460 });
        grid.Children.Add(field);
        return grid;
    }

    private static TextBlock Hidden(TextBlock block)
    {
        block.Visibility = Visibility.Collapsed;
        block.Margin = new Thickness(0, 4, 0, 0);
        return block;
    }

    /// <summary>Shows or clears a field's problem and remembers it, so the rule is not committed while it shows.</summary>
    private void Flag(RuleSetting rule, string field, TextBlock block, string? message)
    {
        _drafts.SetError(rule.Id, field, message is not null);
        Show(block, message);
    }

    private static void Show(TextBlock block, string? message)
    {
        block.Text = message ?? "";
        block.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string Amount(long? bytes, long scale) =>
        bytes is { } b ? (b / (double)scale).ToString("0.##", CultureInfo.InvariantCulture) : "";

    private static bool TryAmount(string text, long scale, out long? bytes)
    {
        bytes = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value < 0)
            return false;
        bytes = (long)Math.Round(value * scale);
        return true;
    }

    private static bool TryDate(string text, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        date = parsed;
        return true;
    }

    private static bool IsInside<T>(DependencyObject element) where T : DependencyObject
    {
        for (var current = element; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is T)
                return true;
        }
        return false;
    }
}
