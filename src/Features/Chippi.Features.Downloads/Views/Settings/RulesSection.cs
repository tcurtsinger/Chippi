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
    private readonly List<RuleSetting> _rules;
    private readonly StackPanel _list = new();
    private readonly TextBox _test = Ui.Field(name: "Test a file name");
    private readonly StackPanel _testResult = Ui.Row();
    private readonly Border _dropLine = new() { Height = 2, Margin = new Thickness(4, -4, 4, 2), IsHitTestVisible = false };
    private string? _open;
    private string? _match;
    private readonly Dictionary<string, ContentControl> _badges = [];
    private readonly System.Windows.Threading.DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int _dragFrom = -1;
    private int _dragTo = -1;

    public RulesSection(DownloadsModule module)
    {
        _module = module;
        _rules = [.. module.Settings.Rules.OrderBy(r => r.Order).Select(r => r.Clone())];
        _dropLine.SetResourceReference(Border.BackgroundProperty, "AccentGradientBrush");

        _test.Width = 300;
        _test.TextChanged += (_, _) => RunTest();
        var testGrid = Ui.Columns("Auto", "Auto", "*");
        testGrid.Children.Add(Ui.Row(Ui.Glyph(LucideIcons.Search, "TextTertiaryBrush", 14).Margin(0, 0, 8, 0), _test).At(0));
        testGrid.Children.Add(Ui.Glyph(LucideIcons.ChevronRight, "TextTertiaryBrush", 14).Margin(14, 0, 14, 0).At(1));
        testGrid.Children.Add(_testResult.At(2));
        var testBox = Ui.Panel(testGrid, new Thickness(14, 10, 14, 10));

        var add = Ui.Button("Add rule", icon: LucideIcons.Plus);
        add.HorizontalAlignment = HorizontalAlignment.Right;
        add.Click += (_, _) => AddRule();
        var header = Ui.Columns("Auto", "*", "Auto");
        header.Children.Add(Ui.Eyebrow("Rules").At(0));
        header.Children.Add(Ui.Text("First match wins", "CaptionTextStyle", "TextTertiaryBrush").Margin(10, 0, 0, 0).At(1));
        header.Children.Add(add.At(2));

        Content = Ui.Stack(testBox, header.Margin(0, 22, 0, 12), _list);
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
        _list.Children.Clear();
        _badges.Clear();
        if (_rules.Count == 0)
            _list.Children.Add(Ui.Surface(Ui.Text("No rules yet", "CaptionTextStyle", "TextTertiaryBrush"), new Thickness(14)));
        for (int i = 0; i < _rules.Count; i++)
        {
            var card = Card(_rules[i], i, out var handle);
            _list.Children.Add(card.Margin(0, 0, 0, 6));
            if (_rules[i].Id == focusHandleOf)
                Dispatcher.BeginInvoke(() => handle.Focus());
        }
    }

    private Border Card(RuleSetting rule, int index, out DragHandle handle)
    {
        bool open = rule.Id == _open;
        handle = new DragHandle { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Drag to reorder (Alt+Up/Down)" };
        System.Windows.Automation.AutomationProperties.SetName(handle, $"Reorder {rule.Name}");
        handle.MoveRequested += (_, delta) => Move(index, delta);
        handle.DragStarted += (_, _) => { _dragFrom = index; _dragTo = index; };
        handle.DragDelta += (s, e) => DragTo(index, ((DragHandle)s).ActualHeight, e);
        handle.DragCompleted += (_, _) => DropDragged();

        var summary = Ui.Text(RuleChecks.Summary(rule), "CaptionTextStyle", "TextTertiaryBrush");
        var title = Ui.Stack(Ui.Text(rule.Name.Length > 0 ? rule.Name : "New rule", "TitleTextStyle"), summary);
        var enabled = Ui.Switch($"{rule.Name} on", rule.Enabled);
        enabled.Click += (_, _) =>
        {
            rule.Enabled = enabled.IsChecked == true;
            Save();
            RunTest();
        };
        var chevron = Ui.Icon(open ? LucideIcons.Up : LucideIcons.ChevronDown, open ? $"Close {rule.Name}" : $"Edit {rule.Name}");
        chevron.Click += (_, _) => Toggle(rule);

        var head = Ui.Columns("Auto", "34", "*", "Auto", "Auto", "Auto");
        head.Background = Brushes.Transparent; // the whole header opens the rule
        head.Children.Add(handle.At(0));
        head.Children.Add(Ui.Mono($"{index + 1:00}", "TextTertiaryBrush").Margin(10, 0, 0, 0).At(1));
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
        var card = Ui.Surface(stack, new Thickness(12, 10, 10, 10));
        if (!rule.Enabled)
            title.Opacity = 0.6;
        if (open)
        {
            card.BorderBrush = null;
            card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        }
        card.PreviewKeyDown += (_, e) =>
        {
            // Alt+Up/Down moves the rule from anywhere inside it.
            if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.System && e.SystemKey is Key.Up or Key.Down)
            {
                e.Handled = true;
                Move(index, e.SystemKey == Key.Up ? -1 : 1);
            }
        };
        return card;
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
        // Rows are about one card apart; the gradient line shows where the rule will land.
        double rowHeight = _list.Children.Count > 0 ? _list.ActualHeight / Math.Max(1, _rules.Count) : 60;
        int target = Math.Clamp(index + (int)Math.Round(e.VerticalChange / rowHeight), 0, _rules.Count - 1);
        if (target == _dragTo)
            return;
        _dragTo = target;
        _list.Children.Remove(_dropLine);
        if (target != _dragFrom)
            _list.Children.Insert(target > _dragFrom ? target + 1 : target, _dropLine);
    }

    private void DropDragged()
    {
        _list.Children.Remove(_dropLine);
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
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = "New rule",
            Extensions = ["pdf"],
            Action = RuleActionKind.Move,
            Template = "Documents",
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
            Save();
            RunTest();
        }

        // Name
        var name = Ui.Field(rule.Name, name: "Rule name", width: 320);
        var nameError = Hidden(Ui.Warning(""));
        name.TextChanged += (_, _) =>
        {
            rule.Name = name.Text.Trim();
            Show(nameError, rule.Name.Length == 0 ? "A name is required" : null);
            Changed();
        };
        editor.Children.Add(Line("Name", Ui.Stack(name, nameError)));

        // Conditions: each turns on with a checkbox; an unchecked one's fields dim.
        editor.Children.Add(Ui.Eyebrow("Conditions").Margin(0, 14, 0, 8));

        var extensions = Ui.Field(rule.Extensions is { } e ? string.Join(", ", e) : "", name: "Extensions", width: 320);
        var extensionsError = Hidden(Ui.Warning(""));
        var extensionsOn = Ui.Check("Extension", rule.Extensions is not null);
        void ApplyExtensions()
        {
            var list = RuleChecks.SplitExtensions(extensions.Text);
            string? error = extensionsOn.IsChecked == true ? list.Count == 0 ? "Add an extension" : RuleChecks.Extensions(extensions.Text) : null;
            Show(extensionsError, error);
            rule.Extensions = extensionsOn.IsChecked == true ? list : null;
            Changed();
        }
        extensions.TextChanged += (_, _) => ApplyExtensions();
        editor.Children.Add(Condition(extensionsOn, "Extension", Ui.Stack(extensions, extensionsError), ApplyExtensions));

        var regex = Ui.Field(rule.NameRegex ?? "", mono: true, name: "Name pattern", width: 460);
        var regexError = Hidden(Ui.Warning(""));
        var regexOn = Ui.Check("Name matches", rule.NameRegex is { Length: > 0 });
        void ApplyRegex()
        {
            bool on = regexOn.IsChecked == true;
            Show(regexError, on ? regex.Text.Length == 0 ? "Add a pattern" : RuleChecks.Regex(regex.Text) : null);
            rule.NameRegex = on && regex.Text.Length > 0 ? regex.Text : null;
            Changed();
        }
        regex.TextChanged += (_, _) => ApplyRegex();
        editor.Children.Add(Condition(regexOn, "Name matches", Ui.Stack(regex, regexError), ApplyRegex));

        int unit = rule.MaxBytes is { } mx && mx % Units[2].Bytes == 0 && mx > 0 ? 2 : rule.MaxBytes is { } k && k % Units[1].Bytes != 0 ? 0 : 1;
        var min = Ui.Field(Amount(rule.MinBytes, Units[unit].Bytes), mono: true, name: "Minimum size", width: 90);
        var max = Ui.Field(Amount(rule.MaxBytes, Units[unit].Bytes), mono: true, name: "Maximum size", width: 90);
        var units = Ui.Choice(Units.Select(u => u.Label), unit, "Size unit", 76, mono: true);
        var sizeError = Hidden(Ui.Warning(""));
        var sizeOn = Ui.Check("Size", rule.MinBytes is not null || rule.MaxBytes is not null);
        void ApplySize()
        {
            bool on = sizeOn.IsChecked == true;
            long scale = Units[Math.Max(0, units.SelectedIndex)].Bytes;
            bool okMin = TryAmount(min.Text, scale, out long? low), okMax = TryAmount(max.Text, scale, out long? high);
            string? error = !on ? null : !okMin || !okMax ? "Use a number" : low is null && high is null ? "Add a minimum or maximum" : low > high ? "Min is above max" : null;
            Show(sizeError, error);
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

        var after = Ui.Field(rule.ModifiedAfter?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", mono: true, name: "Modified after", width: 120);
        var before = Ui.Field(rule.ModifiedBefore?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", mono: true, name: "Modified before", width: 120);
        var dateError = Hidden(Ui.Warning(""));
        var dateOn = Ui.Check("Modified", rule.ModifiedAfter is not null || rule.ModifiedBefore is not null);
        void ApplyDates()
        {
            bool on = dateOn.IsChecked == true;
            bool okAfter = TryDate(after.Text, out var from), okBefore = TryDate(before.Text, out var to);
            string? error = !on ? null : !okAfter || !okBefore ? "Use yyyy-mm-dd" : from is null && to is null ? "Add a date" : from > to ? "After is later than before" : null;
            Show(dateError, error);
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
        editor.Children.Add(Ui.Eyebrow("Action").Margin(0, 14, 0, 8));
        bool hold = rule.Action == RuleActionKind.Hold;
        var action = hold ? Ui.Segment("Action", 34, "Move to folder", "Rename", "Hold") : Ui.Segment("Action", 34, "Move to folder", "Rename");
        action.Width = hold ? 330 : 230;
        action.HorizontalAlignment = HorizontalAlignment.Left;
        action.SelectedIndex = (int)rule.Action;
        editor.Children.Add(Line("", action));

        var template = Ui.Field(rule.Template ?? "", mono: true, name: "Destination", width: 460);
        var templateNote = Ui.Row();
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
            tokens.Children.Clear();
            foreach (string token in rule.Action == RuleActionKind.Rename ? RuleChecks.RenameTokens : RuleChecks.MoveTokens)
            {
                var chip = Ui.Button(token, name: $"Insert {token}");
                chip.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
                chip.Padding = new Thickness(8, 0, 8, 0);
                chip.MinHeight = 26;
                chip.Height = 26;
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
        var targetBlock = Ui.Stack(template, templateNote.Margin(0, 6, 0, 0), tokens);
        var targetLine = Line("", targetBlock);
        var reasonLine = Line("Reason", reason);
        targetLine.Children.Add(targetLabel.At(0));
        editor.Children.Add(targetLine);
        editor.Children.Add(reasonLine);
        void ShowAction()
        {
            bool isHold = rule.Action == RuleActionKind.Hold;
            targetLine.Visibility = isHold ? Visibility.Collapsed : Visibility.Visible;
            reasonLine.Visibility = isHold ? Visibility.Visible : Visibility.Collapsed;
            targetLabel.Text = rule.Action == RuleActionKind.Rename ? "New name" : "Destination";
            System.Windows.Automation.AutomationProperties.SetName(template, targetLabel.Text);
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
            // A folder template rarely makes a good file name, and the other way round.
            template.Text = kind == RuleActionKind.Rename ? "{year}-{month}-{day} {filename}" : kind == RuleActionKind.Move ? "Documents" : template.Text;
            ShowAction();
            Changed();
        };
        ShowAction();

        // Duplicate, Delete
        var duplicate = Ui.Button("Duplicate", name: $"Duplicate {rule.Name}");
        duplicate.Click += (_, _) =>
        {
            var copy = rule.Clone();
            copy.Id = Guid.NewGuid().ToString("N")[..8];
            copy.Name = $"{rule.Name} copy";
            _rules.Insert(_rules.IndexOf(rule) + 1, copy);
            _open = copy.Id;
            Save();
            Render();
        };
        var delete = Ui.Button("Delete", icon: LucideIcons.Trash, name: $"Delete {rule.Name}");
        delete.Click += (_, _) =>
        {
            _rules.Remove(rule);
            _open = null;
            Save();
            RunTest();
            Render();
        };
        var divider = new Border { Height = 1, Margin = new Thickness(0, 14, 0, 12) };
        divider.SetResourceReference(Border.BackgroundProperty, "BorderSoftBrush");
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
            note.Children.Add(Ui.Text(problem, "CaptionTextStyle", "WarningBrush").Margin(5, 0, 0, 0));
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
        string where = rename ? $"Stays in place · {rendered}"
            : WindowsPaths.IsAbsolute(rendered) ? rendered
            : $"Under {Ui.FolderName(folder)} · {rendered}";
        note.Children.Add(Ui.Glyph(LucideIcons.Check, "SuccessBrush", 12));
        note.Children.Add(Ui.Text(where, "CaptionTextStyle", "TextTertiaryBrush").Margin(5, 0, 0, 0));
    }

    private static Grid Line(string label, UIElement field)
    {
        var grid = Ui.Columns("164", "*");
        if (label.Length > 0)
            grid.Children.Add(Ui.Text(label, "BodyTextStyle").At(0));
        grid.Children.Add(((FrameworkElement)field).At(1));
        grid.Margin = new Thickness(0, 0, 0, 10);
        return grid;
    }

    private static Grid Condition(CheckBox toggle, string label, FrameworkElement fields, Action apply)
    {
        var head = Ui.Row(toggle, Ui.Text(label, "BodyTextStyle").Margin(10, 0, 0, 0));
        void Dim()
        {
            fields.IsEnabled = toggle.IsChecked == true;
            fields.Opacity = fields.IsEnabled ? 1 : 0.45;
        }
        toggle.Click += (_, _) =>
        {
            Dim();
            apply();
        };
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
        _testResult.Children.Clear();
        string name = _test.Text.Trim();
        _match = null;
        if (name.Length == 0)
        {
            _testResult.Children.Add(Ui.Text("Type a file name to see where it goes", "CaptionTextStyle", "TextTertiaryBrush"));
        }
        else
        {
            string folder = SampleFolder();
            var rules = Valid(_rules).Select((r, i) => r.ToRule() with { Order = i }).ToList();
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
            _match = plan.RuleId;
            int index = _rules.FindIndex(r => r.Id == plan.RuleId);
            switch (plan.Kind)
            {
                case OperationKind.Move or OperationKind.Rename:
                    _testResult.Children.Add(Ui.Badge($"{index + 1:00} {_rules[index].Name}", BadgeKind.Source).Margin(0, 0, 10, 0));
                    _testResult.Children.Add(Ui.Text(Ui.Relative(folder, plan.Destination!), "BodyTextStyle"));
                    break;
                case OperationKind.Hold:
                    _testResult.Children.Add(Ui.Text($"Held · {plan.HoldReason ?? plan.Warnings.LastOrDefault()?.Detail ?? "held"}", "BodyTextStyle", "WarningBrush"));
                    break;
                default:
                    _match = null;
                    _testResult.Children.Add(Ui.Text($"No match · stays in {Ui.FolderName(folder)}", "BodyTextStyle", "TextTertiaryBrush"));
                    break;
            }
        }
        // The winning rule gets the Match badge, in place, so a field being edited keeps focus.
        foreach (var (id, host) in _badges)
            host.Content = id == _match ? Ui.Badge("Match", BadgeKind.Source).Margin(0, 0, 10, 0) : null;
    }

    private string SampleFolder() =>
        _module.Settings.Folders.FirstOrDefault()?.Path ?? DownloadsModule.DownloadsFolder() ?? Path.GetTempPath();

    // ---- saving

    private void Save()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Saves the rules that are valid, in list order. A rule mid-edit with a problem stays here until it is fixed.</summary>
    private void SaveNow()
    {
        _saveTimer.Stop();
        for (int i = 0; i < _rules.Count; i++)
            _rules[i].Order = i;
        var settings = _module.Settings.Clone();
        settings.Rules = [.. Valid(_rules).Select(r => r.Clone())];
        try
        {
            _module.SaveSettings(settings);
        }
        catch (InvalidOperationException)
        {
            // The organizer stopped; the page goes with it.
        }
    }

    private static IEnumerable<RuleSetting> Valid(IReadOnlyList<RuleSetting> rules)
    {
        var all = rules.Select(r => r.ToRule()).ToList();
        var bad = RuleValidator.Validate(all).Select(e => e.RuleId).ToHashSet(StringComparer.Ordinal);
        return rules.Where(r => !bad.Contains(r.Id) && (r.Action == RuleActionKind.Hold || RuleChecks.Template(r.Template ?? "", r.Action == RuleActionKind.Rename) is null));
    }

    // ---- helpers

    private static TextBlock Hidden(TextBlock block)
    {
        block.Visibility = Visibility.Collapsed;
        block.Margin = new Thickness(0, 4, 0, 0);
        return block;
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
