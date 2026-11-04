// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Chippi.UI.Controls;

namespace Chippi.Features.QuickActions.Views;

internal sealed partial class ActionsPage
{
    private readonly TextBlock _listFailed = Ui.Text("", 12, "WarningTextBrush");
    private readonly Dictionary<(QuickActionId Action, string Key), string> _rowFailures = [];
    private int _listPage;
    private QuickActionId? _pagedList;
    private ButtonBase? _firstRow;
    private string? _deferredListFocus;

    // Focus is injected by background fixtures; production uses the actual keyboard and never activates a window.
    internal Func<UIElement?> CurrentListFocus { get; set; } = () => Keyboard.FocusedElement as UIElement;
    internal Action<UIElement> SetListFocus { get; set; } = element => element.Focus();

    internal int ListPage => _listPage;
    internal int ListPageCount { get; private set; } = 1;
    internal string? ConfirmationQuestion { get; private set; }
    internal IReadOnlyList<OpenApp>? ConfirmedApps { get; private set; }

    private void ResetListPage()
    {
        _listPage = 0;
        _pagedList = null;
        _deferredListFocus = null;
    }

    private void RenderList(QuickActionId id)
    {
        // Keep the reviewed list stable while its confirmation is open. The command owns its exact snapshot.
        if (_confirm is not null)
            return;
        if (_pagedList != id)
        {
            _pagedList = id;
            _listPage = 0;
        }
        var state = _module.State;
        int count = id switch
        {
            QuickActionId.Quit => state.Apps.Count,
            QuickActionId.Eject => state.Drives.Count,
            QuickActionId.Plan => state.Plans.Count,
            _ => 4,
        };
        ListPageCount = Math.Max(1, (count + ListRows * MaxListColumns - 1) / (ListRows * MaxListColumns));
        _listPage = Math.Clamp(_listPage, 0, ListPageCount - 1);
        string content = id switch
        {
            QuickActionId.Quit => "quit:" + string.Join(",", state.Apps.Select(a => $"{a.Path}|{a.Name}|{a.Front}|{_module.Settings.Keeps(a.Path)}")),
            QuickActionId.Eject => "eject:" + string.Join(",", state.Drives.Select(d => $"{d.Id}|{d.Name}|{d.Size}")),
            QuickActionId.Plan => "plan:" + string.Join(",", state.Plans.Select(p => $"{p.Id}|{p.Name}|{p.Active}")),
            _ => "power:",
        };
        string built = content + ":" + _listPage + ":" + string.Join(",", _rowFailures.Where(f => f.Key.Action == id))
            + ":" + string.Join(",", _running.Where(r => r.Action == id));
        // Compatibility diagnostic for automation; visible errors themselves live inside the affected row.
        _listFailed.Text = _rowFailures.FirstOrDefault(f => f.Key.Action == id).Value ?? "";
        _listFailed.Visibility = _listFailed.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_built == built)
            return;
        var focused = CurrentListFocus();
        var oldFocus = Buttons(_body).FirstOrDefault(b => ReferenceEquals(b, focused) || b.IsKeyboardFocusWithin);
        string? focusKey = oldFocus?.Tag as string ?? (ReferenceEquals(focused, _body) ? _deferredListFocus : null);
        _deferredListFocus = null;
        _built = built;
        _body.Children.Clear();
        _firstRow = null;
        switch (id)
        {
            case QuickActionId.Quit:
                QuitList(state);
                break;
            case QuickActionId.Eject:
                Rows(state.Drives, DriveRow, "No drives", "drives");
                break;
            case QuickActionId.Plan:
                Rows(state.Plans, PlanRow, "No power plans", "plans");
                break;
            default:
                Rows(new[]
                {
                    (PowerChoice.Sleep, "Sleep", LucideIcons.Moon, "Sleep?"),
                    (PowerChoice.Restart, "Restart", LucideIcons.RefreshCw, "Restart?"),
                    (PowerChoice.ShutDown, "Shut down", LucideIcons.Power, "Shut down?"),
                    (PowerChoice.SignOut, "Sign out", LucideIcons.LogOut, "Sign out?"),
                }, choice => PowerRow(choice.Item1, choice.Item2, choice.Item3, choice.Item4), "", "actions");
                break;
        }
        if (focusKey is not null)
        {
            var target = Buttons(_body).FirstOrDefault(b => Equals(b.Tag, focusKey));
            if (target is null || !target.IsEnabled)
            {
                // A pending action must not move Enter onto a different app's Quit button. Park focus safely until
                // its target is available again, unless the user moves it elsewhere while the work is running.
                // A removed target leaves focus here too, so completing bulk Quit cannot focus a kept app's Quit.
                _deferredListFocus = target is null ? null : focusKey;
                _body.Focusable = true;
                KeyboardNavigation.SetIsTabStop(_body, false);
                SetListFocus(_body);
            }
            else
                SetListFocus(target);
        }
    }

    /// <summary>Readable columns, five rows each. More entries get another page, never a narrower column.</summary>
    internal static int ListColumns(int count) => Math.Clamp((count + ListRows - 1) / ListRows, 1, MaxListColumns);

    internal void TurnListPage(int delta)
    {
        int next = Math.Clamp(_listPage + delta, 0, ListPageCount - 1);
        if (next == _listPage)
            return;
        CloseConfirm();
        _listPage = next;
        Render();
        if (_firstRow is { } first) SetListFocus(first);
    }

    private void Rows<T>(IReadOnlyList<T> all, Func<T, FrameworkElement> make, string empty, string noun)
    {
        int count = all.Count;
        if (count == 0)
        {
            var none = Ui.Muted(empty);
            none.HorizontalAlignment = HorizontalAlignment.Center;
            _body.Children.Add(none);
            return;
        }
        int start = _listPage * ListRows * MaxListColumns;
        var visible = all.Skip(start).Take(ListRows * MaxListColumns).ToList();
        int columns = ListColumns(visible.Count);
        var top = new DockPanel { Height = 28, VerticalAlignment = VerticalAlignment.Top, LastChildFill = true };
        if (ListPageCount > 1)
        {
            var previous = Ui.Icon(LucideIcons.ChevronLeft, $"Previous {noun}");
            previous.Tag = "previous-page";
            previous.IsEnabled = _listPage > 0;
            previous.Click += (_, _) => TurnListPage(-1);
            var next = Ui.Icon(LucideIcons.ChevronRight, $"Next {noun}");
            next.Tag = "next-page";
            next.IsEnabled = _listPage < ListPageCount - 1;
            next.Click += (_, _) => TurnListPage(1);
            var range = Ui.Muted($"{start + 1}\u2013{start + visible.Count} of {count}", 11);
            range.Margin = new Thickness(4, 0, 4, 0);
            var pager = new StackPanel { Orientation = Orientation.Horizontal, Children = { previous, range, next } };
            DockPanel.SetDock(pager, Dock.Right);
            top.Children.Add(pager);
        }
        if (_list == QuickActionId.Quit)
            top.Children.Add(Ui.Muted($"{count} {(count == 1 ? "app" : "apps")} open", 11));
        _body.Children.Add(top);
        var grid = new UniformGrid
        {
            Columns = columns, Rows = (visible.Count + columns - 1) / columns,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 34, columns > 1 ? -8 : 0, 0),
        };
        foreach (var item in visible)
        {
            var row = make(item);
            if (columns > 1)
                row.Margin = new Thickness(0, 0, 8, 0);
            _firstRow ??= Buttons(row).FirstOrDefault(b => b.IsEnabled);
            grid.Children.Add(row);
        }
        _body.Children.Add(grid);
    }

    private void QuitList(QuickState state)
    {
        Rows(state.Apps, AppRow, "No apps open", "apps");
        if (_module.Quitting(keepFront: false).Count == 0)
            return;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4),
        };
        if (state.Apps.FirstOrDefault(a => a.Front) is { } front && !_module.Settings.Keeps(front.Path) && _module.Quitting(keepFront: true).Count > 0)
        {
            var allBut = Ui.Button($"Quit all but {front.Name}");
            allBut.Tag = "quit-others";
            allBut.Content = Ui.Text($"Quit all but {front.Name}");
            allBut.MaxWidth = 300;
            allBut.ToolTip = $"Quit all but {front.Name}";
            allBut.Margin = new Thickness(0, 0, 8, 0);
            allBut.Click += (_, _) => ConfirmQuit(allBut, keepFront: true);
            buttons.Children.Add(allBut);
        }
        var all = Ui.Button("Quit all");
        all.Tag = "quit-all";
        all.Click += (_, _) => ConfirmQuit(all, keepFront: false);
        buttons.Children.Add(all);
        foreach (Button button in buttons.Children)
            button.IsEnabled = !_running.Any(r => r.Action == QuickActionId.Quit);
        _body.Children.Add(buttons);
    }

    private void ConfirmQuit(UIElement anchor, bool keepFront)
    {
        var targets = _module.Quitting(keepFront).ToArray();
        if (targets.Length == 0)
            return;
        var paths = targets.Select(a => a.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var spared = _module.State.Apps.Where(a => !paths.Contains(a.Path)).Select(a => a.Name).ToArray();
        string? detail = spared.Length > 0 ? "Keep open: " + string.Join(", ", spared) : null;
        string question = $"Quit {targets.Length} {(targets.Length == 1 ? "app" : "apps")}?";
        AskFirst(anchor, question, "Quit", () => QuitSnapshot(targets), detail);
        ConfirmedApps = targets;
    }

    private async void QuitSnapshot(IReadOnlyList<OpenApp> targets)
    {
        await Act(QuickActionId.Quit, async () =>
        {
            IReadOnlyList<OpenApp>? failed = null;
            try { await _module.QuitAllAsync(targets); failed = []; }
            catch (QuitAppsException ex) { failed = ex.Apps; throw; }
            finally
            {
                if (failed is not null)
                {
                    var paths = failed.Select(a => a.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var target in targets.Where(a => !paths.Contains(a.Path)))
                        _rowFailures.Remove((QuickActionId.Quit, target.Path));
                }
            }
        }, "Quit failed", "bulk");
    }

    private Border AppRow(OpenApp app)
    {
        UIElement mark = AppIcon.View(app.Path, 16, Ui.Monogram(app.Name));
        bool failing = _rowFailures.ContainsKey((QuickActionId.Quit, app.Path));
        var quit = Ui.Icon(failing ? LucideIcons.RefreshCw : LucideIcons.Close, failing ? $"Retry quitting {app.Name}" : $"Quit {app.Name}");
        quit.Tag = "quit:" + app.Path.ToUpperInvariant();
        quit.IsEnabled = !Running(QuickActionId.Quit, app.Path) && !Running(QuickActionId.Quit, "bulk");
        quit.Click += async (_, _) => await Act(QuickActionId.Quit, () => _module.QuitAsync(app), "Quit failed", app.Path);
        var parts = new List<UIElement> { quit };
        string badge = string.Join(" \u00b7 ", new[] { app.Front ? "Front" : null, _module.Settings.Keeps(app.Path) ? "Kept" : null }.Where(x => x is not null));
        if (badge.Length > 0)
        {
            var tag = Ui.Badge(badge);
            tag.Margin = new Thickness(4, 0, 0, 0);
            parts.Add(tag);
        }
        return Row(mark, app.Name, parts, failing ? "Quit failed" : "", failing, badge.Length > 0 ? $"{app.Name} \u00b7 {badge}" : app.Name);
    }

    private Border DriveRow(RemovableDrive drive)
    {
        bool failing = _rowFailures.ContainsKey((QuickActionId.Eject, drive.Id));
        var eject = Ui.Icon(failing ? LucideIcons.RefreshCw : LucideIcons.Eject, failing ? $"Retry ejecting {drive.Name}" : $"Eject {drive.Name}");
        eject.Tag = "eject:" + drive.Id;
        eject.IsEnabled = !Running(QuickActionId.Eject, drive.Id);
        eject.Click += async (_, _) => await Act(QuickActionId.Eject, () => _module.EjectAsync(drive), "Eject failed", drive.Id);
        return Row(Ui.Glyph(LucideIcons.HardDrive), drive.Name, [eject], failing ? "Eject failed" : Size(drive.Size), failing);
    }

    private static Border Row(UIElement lead, string name, IReadOnlyList<UIElement> end, string detail, bool failed, string? tip = null)
    {
        var dock = new DockPanel { LastChildFill = true };
        foreach (var part in end)
        {
            DockPanel.SetDock(part, Dock.Right);
            dock.Children.Add(part);
        }
        DockPanel.SetDock(lead, Dock.Left);
        dock.Children.Add(lead);
        var labels = Labels(name, detail, failed);
        labels.Margin = new Thickness(8, 0, 0, 0);
        labels.ToolTip = tip ?? name;
        dock.Children.Add(labels);
        var row = new Border
        {
            Height = 42, Padding = new Thickness(5, 0, 3, 0), CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent, Child = dock,
        };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "IslandHoverBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    private static StackPanel Labels(string name, string detail, bool failed)
    {
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(Ui.Text(name, 12));
        if (detail.Length > 0)
        {
            var second = Ui.Text(detail, 11, failed ? "WarningTextBrush" : "IslandTextTertiaryBrush");
            if (failed)
                AutomationProperties.SetLiveSetting(second, AutomationLiveSetting.Polite);
            labels.Children.Add(second);
        }
        return labels;
    }

    private Button PlanRow(PowerPlan plan)
    {
        var mark = new Ellipse { Tag = plan.Active ? "On" : null, VerticalAlignment = VerticalAlignment.Center };
        mark.SetResourceReference(StyleProperty, "IslandRadioMarkStyle");
        bool failed = _rowFailures.ContainsKey((QuickActionId.Plan, plan.Id.ToString()));
        var name = Labels(plan.Name, failed ? "Power plan failed" : "", failed);
        name.Margin = new Thickness(8, 0, 0, 0);
        if (plan.Active)
            ((TextBlock)name.Children[0]).FontWeight = FontWeights.SemiBold;
        var row = ListButton(new DockPanel { Children = { mark, name } }, plan.Name);
        row.Tag = "plan:" + plan.Id;
        row.IsEnabled = !_running.Any(r => r.Action == QuickActionId.Plan);
        AutomationProperties.SetItemStatus(row, failed ? "Power plan failed" : plan.Active ? "Active" : "");
        row.Click += async (_, _) => await Act(QuickActionId.Plan, () => _module.SetPowerPlanAsync(plan.Id), "Power plan failed", plan.Id.ToString());
        return row;
    }

    private Button PowerRow(PowerChoice choice, string name, Geometry icon, string question)
    {
        bool failed = _rowFailures.ContainsKey((QuickActionId.Power, choice.ToString()));
        var labels = Labels(name, failed ? $"{name} failed" : "", failed);
        labels.Margin = new Thickness(8, 0, 0, 0);
        var row = ListButton(new DockPanel { Children = { Ui.Glyph(icon), labels } }, name);
        row.Tag = "power:" + choice;
        row.IsEnabled = !_running.Any(r => r.Action == QuickActionId.Power);
        row.Click += (_, _) => AskFirst(row, question, name, async () => await Act(QuickActionId.Power, () => _module.PowerAsync(choice), $"{name} failed", choice.ToString()));
        return row;
    }

    private static Button ListButton(UIElement content, string name)
    {
        var row = new Button { Content = content, Height = 42 };
        row.SetResourceReference(StyleProperty, "IslandListRowStyle");
        AutomationProperties.SetName(row, name);
        row.ToolTip = name;
        return row;
    }

    private ButtonBase? FirstRowButton() => _firstRow;

    private static IEnumerable<ButtonBase> Buttons(DependencyObject root)
    {
        if (root is ButtonBase button)
            yield return button;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Buttons(child))
                yield return descendant;
    }

    /// <summary>A drive's size as Explorer shows it, three figures cut, not rounded: "931 GB", "57.7 GB", "3.63 TB".</summary>
    internal static string Size(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        int decimals = unit == 0 || size >= 100 ? 0 : size >= 10 ? 1 : 2;
        double scale = Math.Pow(10, decimals);
        double cut = Math.Floor(size * scale) / scale;
        return $"{cut.ToString(decimals == 0 ? "0" : "0." + new string('#', decimals), CultureInfo.CurrentCulture)} {units[unit]}";
    }
}
