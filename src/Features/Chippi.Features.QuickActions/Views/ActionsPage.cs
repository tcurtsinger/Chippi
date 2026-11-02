// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.QuickActions.Views;

/// <summary>
/// The island's Actions page (docs/phase-5.5/quick-actions.md, Surfaces 2 to 4): the picker on first open, then the
/// picked buttons five across with each one's label and status under it, and the small lists (Power plan, Eject, Quit
/// apps, Power) stepping into the page. Nothing scrolls.
/// </summary>
internal sealed partial class ActionsPage : UserControl
{
    /// <summary>Four rows (16 or more picked) take 34-tall buttons so they fit; fewer take the home's 44.</summary>
    internal const double TallButton = 44, ShortButton = 34;

    internal const int Columns = 5;

    /// <summary>A list's rows: 8 to a column; past 8 it takes a second column, and more as it grows, up to 4.</summary>
    internal const int ListRows = 8;

    internal const int MaxListColumns = 4;

    /// <summary>How long a button says "Failed".</summary>
    internal static readonly TimeSpan FailedFor = TimeSpan.FromSeconds(4);

    private readonly QuickActionsModule _module;
    private readonly TextBlock _title = Ui.Text("Actions", 13);
    private readonly Grid _body = new() { Width = IslandPage.BodyWidth, Height = IslandPage.BodyHeight };
    private readonly System.Windows.Threading.DispatcherTimer _refresh = new() { Interval = QuickActionsModule.RefreshEvery };
    private readonly Dictionary<QuickActionId, DateTime> _failed = [];
    private readonly Dictionary<QuickActionId, bool> _setting = []; // a toggle's new state while Windows switches it
    private readonly HashSet<QuickActionId> _picking = [];
    private readonly List<Cell> _cells = [];
    private readonly List<ToggleButton> _picks = [];
    private TextBlock? _pickedText;
    private Button? _done;
    private string _built = ""; // what _body holds now: "picker", the grid's keys, or a list and its rows
    private QuickActionId? _list;
    private Popup? _confirm;
    private (string Text, DateTime Until)? _listFailure; // "Eject failed", at a list's foot
    private readonly TextBlock _listFailed = Ui.Text("", 12, "WarningTextBrush");

    public ActionsPage(QuickActionsModule module)
    {
        _module = module;
        _title.FontWeight = FontWeights.SemiBold;
        _listFailed.VerticalAlignment = VerticalAlignment.Bottom;
        _listFailed.HorizontalAlignment = HorizontalAlignment.Left;
        _listFailed.Margin = new Thickness(10, 0, 0, 10);
        AutomationProperties.SetLiveSetting(_listFailed, AutomationLiveSetting.Polite);
        Content = _body;
        _refresh.Tick += (_, _) => _ = _module.PollAsync();
        Loaded += (_, _) =>
        {
            _module.Changed += OnChanged;
            Render();
        };
        Unloaded += (_, _) => _module.Changed -= OnChanged;
        // Reading the PC runs only while the page shows; it opens on the buttons, not a list left open last time.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _refresh.Start();
                _ = _module.PollAsync();
                ShowPending();
            }
            else
            {
                _refresh.Stop();
                CloseConfirm();
                _list = null;
                Render();
            }
        };
        Render();
    }

    /// <summary>The page's header: "Actions", or the open list's name with Back returning to the buttons.</summary>
    public FrameworkElement Header => _title;

    /// <summary>The first button of what shows.</summary>
    public object? InitialFocus =>
        _built == "picker" ? _picks.FirstOrDefault() : _list is null ? _cells.FirstOrDefault(c => c.Button.IsEnabled)?.Button : FirstRowButton();

    /// <summary>What shows: "picker", "grid", or the open list ("quit", "eject", "plan", "power").</summary>
    internal string Showing => _built.Split(':')[0];

    /// <summary>The failure at a list's foot while it shows, else empty.</summary>
    internal string ListFailure => _listFailed.Visibility == Visibility.Visible ? _listFailed.Text : "";

    /// <summary>Presses an action's button, as a click does.</summary>
    internal void Press(QuickActionId id) => Pressed(_cells.First(c => c.Action.Id == id));

    /// <summary>Back: a list returns to the buttons; true when it did.</summary>
    public bool Back()
    {
        if (_list is null)
            return false;
        CloseConfirm();
        _list = null;
        Render();
        return true;
    }

    /// <summary>Esc: closes a confirm, then a list; true when it did, else the island closes.</summary>
    public bool Escape()
    {
        if (_confirm?.IsOpen == true)
        {
            CloseConfirm();
            return true;
        }
        return Back();
    }

    /// <summary>Shows the list or the confirm a shortcut asked for, once the page is on screen.</summary>
    public void ShowPending()
    {
        if (!IsVisible || _module.Pending is not { } id)
            return;
        _module.Pending = null;
        var action = QuickAction.Of(id);
        if (action.OpensList)
        {
            CloseConfirm();
            _list = id;
            Render();
            return;
        }
        _list = null;
        Render();
        if (action.Confirm is { } confirm && Describe(action, _module.State).Enabled)
        {
            UIElement anchor = (UIElement?)_cells.FirstOrDefault(c => c.Action.Id == id)?.Button ?? _title;
            AskFirst(anchor, confirm.Question, confirm.Verb, () => Run(id));
        }
    }

    /// <summary>A shortcut's action failed: its button says so.</summary>
    public void ShowFailed(QuickActionId id)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowFailed(id));
            return;
        }
        _failed[id] = DateTime.UtcNow + FailedFor;
        Render();
        _ = ClearFailedAsync();
    }

    private async Task ClearFailedAsync()
    {
        await Task.Delay(FailedFor + TimeSpan.FromMilliseconds(50));
        Render();
    }

    private void OnChanged()
    {
        if (Dispatcher.CheckAccess()) Render();
        else Dispatcher.BeginInvoke(Render);
    }

    internal void Render()
    {
        if (_list is { } list)
            RenderList(list);
        else if (_module.Settings.Shown is null || _module.Shown.Count == 0)
            RenderPicker();
        else
            RenderGrid();
        _title.Text = _list is { } open ? QuickAction.Of(open).Name : "Actions";
    }

    /// <summary>
    /// A button's status line, whether there is anything to act on, and whether it steps into a list (a chevron at its
    /// right): Plan's is the active plan, Eject's the drive or the count, Quit's the count; with nothing to act on, the
    /// button turns off and says so ("No drives", "None").
    /// </summary>
    internal static (string Status, bool Enabled, bool List) Describe(QuickAction action, QuickState state) => action.Id switch
    {
        QuickActionId.Plan => (state.Plans.FirstOrDefault(p => p.Active)?.Name ?? "", state.Plans.Count > 0, state.Plans.Count > 0),
        QuickActionId.Eject => state.Drives.Count switch
        {
            0 => ("No drives", false, false),
            1 => (DriveName(state.Drives[0].Name), true, false),
            var n => ($"{n} drives", true, true),
        },
        QuickActionId.Quit => state.Apps.Count == 0 ? ("None", false, false) : ($"{state.Apps.Count} open", true, true),
        QuickActionId.Frozen => state.Frozen == 0 ? ("None", false, false) : (state.Frozen == 1 ? "1 app" : $"{state.Frozen} apps", true, false),
        QuickActionId.Recycle => state.RecycleBinItems == 0 ? ("Empty", false, false) : ("", true, false),
        QuickActionId.Power => ("", true, true),
        _ => ("", true, false),
    };

    /// <summary>"Samsung T7 (E:)" without its letter, for the status line.</summary>
    internal static string DriveName(string name) => DriveLetter().Replace(name, "");

    [GeneratedRegex(@"\s*\([A-Z]:\)$")]
    private static partial Regex DriveLetter();

    /// <summary>The buttons' height for <paramref name="count"/> of them.</summary>
    internal static double ButtonHeight(int count) => (count + Columns - 1) / Columns >= 4 ? ShortButton : TallButton;

    // The buttons.

    private void RenderGrid()
    {
        var shown = _module.Shown;
        string built = "grid:" + string.Join(",", shown.Select(a => a.Key));
        if (_built != built)
        {
            _built = built;
            _cells.Clear();
            double height = ButtonHeight(shown.Count);
            // 10 between columns and 12 between rows (each cell's right and bottom margin, taken back at the edges).
            var grid = new UniformGrid { Columns = Columns, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, -10, -12) };
            foreach (var action in shown)
            {
                var cell = new Cell(action, height, this);
                _cells.Add(cell);
                grid.Children.Add(cell);
            }
            _body.Children.Clear();
            _body.Children.Add(grid);
        }
        var state = _module.State;
        var now = DateTime.UtcNow;
        foreach (var cell in _cells)
        {
            var id = cell.Action.Id;
            bool on = _setting.TryGetValue(id, out var setting) ? setting : state.On.GetValueOrDefault(id);
            cell.Show(Describe(cell.Action, state), on, _failed.TryGetValue(id, out var until) && until > now);
        }
    }

    private async void Pressed(Cell cell)
    {
        var action = cell.Action;
        var id = action.Id;
        var state = _module.State;
        if (action.Toggle && !action.OpensList)
        {
            // A press while the last one is still being applied does nothing, as on the home's toggles.
            if (_setting.ContainsKey(id))
                return;
            bool on = !state.On.GetValueOrDefault(id);
            _setting[id] = on;
            Render();
            try { await Act(id, () => _module.SetAsync(id, on)); }
            finally
            {
                _setting.Remove(id);
                Render();
            }
        }
        else if (id == QuickActionId.Eject && state.Drives is [var only])
            await Act(id, () => _module.EjectAsync(only));
        else if (action.OpensList)
        {
            _list = id;
            Render();
        }
        else if (action.Confirm is { } confirm)
            AskFirst(cell.Button, confirm.Question, confirm.Verb, () => Run(id));
        else
            Run(id);
    }

    private async void Run(QuickActionId id) => await Act(id, id == QuickActionId.Frozen ? _module.EndFrozenAppsAsync : () => _module.RunAsync(id));

    /// <summary>Runs an action; a failure says "Failed" under its button and, in a list, <paramref name="failure"/> at the list's foot.</summary>
    private async Task Act(QuickActionId id, Func<Task> work, string? failure = null)
    {
        try { await work(); }
        catch (Exception ex)
        {
            Log.Warn($"Quick actions: {QuickAction.Of(id).Key} failed", ex);
            if (failure is not null)
                _listFailure = (failure, DateTime.UtcNow + FailedFor);
            ShowFailed(id);
        }
    }

    private void AskFirst(UIElement anchor, string question, string verb, Action confirmed)
    {
        CloseConfirm();
        _confirm = Ui.Confirm(anchor, question, verb, confirmed);
        _confirm.IsOpen = true;
    }

    private void CloseConfirm()
    {
        if (_confirm is not null)
            _confirm.IsOpen = false;
        _confirm = null;
    }

    /// <summary>One button with its label and status under it, as on the home.</summary>
    private sealed class Cell : StackPanel
    {
        private readonly LucideIcon _glyph = new() { Size = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly LucideIcon _chevron = new() { Data = LucideIcons.ChevronRight, Size = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        private readonly TextBlock _name = Ui.Text("", 12);
        private readonly TextBlock _status = Ui.Muted("", 11);
        private bool _on;
        private bool _showing; // the page sets IsChecked itself, which isn't a press

        public Cell(QuickAction action, double height, ActionsPage page)
        {
            Action = action;
            Button = new ToggleButton { Height = height, Content = new Grid { Children = { _glyph, _chevron } }, Tag = action.Key };
            Button.SetResourceReference(StyleProperty, "IslandActionTileStyle");
            _glyph.SetBinding(LucideIcon.BrushProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = Button });
            _chevron.SetBinding(LucideIcon.BrushProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = Button });
            _chevron.Opacity = 0.8;
            // The page decides: the button follows the PC, not the press. Checked and Unchecked, not Click: a click raises
            // them too, and UI Automation's Toggle raises no Click.
            Button.Checked += (_, _) => Pressed(page);
            Button.Unchecked += (_, _) => Pressed(page);
            _name.Text = action.Label;
            _name.Height = 16;
            _name.Margin = new Thickness(0, 4, 0, 0);
            _name.HorizontalAlignment = HorizontalAlignment.Center;
            _status.Height = 13;
            _status.HorizontalAlignment = HorizontalAlignment.Center;
            Margin = new Thickness(0, 0, 10, 12);
            Children.Add(Button);
            Children.Add(_name);
            Children.Add(_status);
        }

        public QuickAction Action { get; }

        public ToggleButton Button { get; }

        private void Pressed(ActionsPage page)
        {
            if (_showing)
                return;
            Check(_on);
            page.Pressed(this);
        }

        private void Check(bool on)
        {
            _showing = true;
            try { Button.IsChecked = on; }
            finally { _showing = false; }
        }

        public void Show((string Status, bool Enabled, bool List) look, bool on, bool failed)
        {
            _on = Action.Toggle && !Action.OpensList && on;
            Check(_on);
            Button.IsEnabled = look.Enabled;
            _glyph.Data = Action.Id == QuickActionId.Camera && !on ? LucideIcons.CameraOff : Action.Icon;
            _chevron.Visibility = look.List ? Visibility.Visible : Visibility.Collapsed;
            string status = failed ? "Failed" : look.Status;
            _status.Text = status;
            _status.SetResourceReference(TextBlock.ForegroundProperty, failed ? "WarningTextBrush" : "IslandTextTertiaryBrush");
            _name.SetResourceReference(TextBlock.ForegroundProperty, look.Enabled ? "IslandTextBrush" : "IslandTextTertiaryBrush");
            string name = status.Length > 0 ? $"{Action.Label}, {status}" : Action.Label;
            AutomationProperties.SetName(Button, name);
            Button.ToolTip = status.Length > 0 ? $"{Action.Label} · {status}" : Action.Label;
        }
    }

    // First open: the picker.

    private void RenderPicker()
    {
        var offered = _module.Offered;
        string built = "picker:" + string.Join(",", offered.Select(a => a.Key));
        if (_built != built)
        {
            _built = built;
            _picks.Clear();
            var grid = new UniformGrid { Columns = Columns, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, -10, 0) };
            foreach (var action in offered)
                grid.Children.Add(PickCell(action));
            _pickedText = Ui.Muted();
            _done = Ui.Button("Done", "IslandAccentButtonStyle");
            _done.Click += (_, _) => _module.Pick(_picking);
            DockPanel.SetDock(_done, Dock.Right);
            var footer = new DockPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4), Children = { _done, _pickedText } };
            _body.Children.Clear();
            _body.Children.Add(grid);
            _body.Children.Add(footer);
        }
        foreach (var pick in _picks)
            pick.IsChecked = _picking.Contains((QuickActionId)pick.Tag);
        _pickedText!.Text = _picking.Count == 0 ? "None picked" : $"{_picking.Count} picked";
        _done!.IsEnabled = _picking.Count > 0;
    }

    private StackPanel PickCell(QuickAction action)
    {
        var check = new Grid { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0) };
        var ring = new Ellipse { StrokeThickness = 1.5 };
        ring.SetResourceReference(Shape.StrokeProperty, "IslandTextTertiaryBrush");
        var fill = new Ellipse();
        fill.SetResourceReference(Shape.FillProperty, "IslandAccentFillBrush");
        var tick = Ui.Glyph(LucideIcons.Check, 9, "IslandOnAccentBrush");
        tick.HorizontalAlignment = HorizontalAlignment.Center;
        check.Children.Add(ring);
        check.Children.Add(fill);
        check.Children.Add(tick);
        var glyph = Ui.Glyph(action.Icon);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        var button = new ToggleButton { Height = ShortButton, Tag = action.Id, Content = new Grid { Children = { glyph, check } } };
        button.SetResourceReference(StyleProperty, "IslandPickTileStyle");
        AutomationProperties.SetName(button, action.Label);
        button.ToolTip = action.Name;
        void Shown()
        {
            bool picked = button.IsChecked == true;
            ring.Visibility = picked ? Visibility.Collapsed : Visibility.Visible;
            fill.Visibility = tick.Visibility = picked ? Visibility.Visible : Visibility.Collapsed;
        }
        button.Checked += (_, _) => { Shown(); Picked(action.Id, true); };
        button.Unchecked += (_, _) => { Shown(); Picked(action.Id, false); };
        Shown();
        _picks.Add(button);
        var label = Ui.Text(action.Label, 12);
        label.Height = 16;
        label.Margin = new Thickness(0, 4, 0, 0);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        return new StackPanel { Margin = new Thickness(0, 0, 10, 12), Children = { button, label } };
    }

    private void Picked(QuickActionId id, bool picked)
    {
        if (picked ? !_picking.Add(id) : !_picking.Remove(id))
            return;
        Render();
    }

    // The lists, stepping into the page.

    private void RenderList(QuickActionId id)
    {
        var state = _module.State;
        string built = id switch
        {
            QuickActionId.Quit => "quit:" + string.Join(",", state.Apps.Select(a => $"{a.Path}|{a.Front}|{_module.Settings.Keeps(a.Path)}")),
            QuickActionId.Eject => "eject:" + string.Join(",", state.Drives.Select(d => d.Id)),
            QuickActionId.Plan => "plan:" + string.Join(",", state.Plans.Select(p => $"{p.Id}|{p.Active}")),
            _ => "power",
        };
        bool failing = _listFailure is { } failure && failure.Until > DateTime.UtcNow;
        _listFailed.Text = failing ? _listFailure!.Value.Text : "";
        _listFailed.Visibility = failing ? Visibility.Visible : Visibility.Collapsed;
        if (_built == built)
            return;
        _built = built;
        _body.Children.Clear();
        _body.Children.Add(_listFailed);
        switch (id)
        {
            case QuickActionId.Quit:
                QuitList(state);
                break;
            case QuickActionId.Eject:
                Rows(state.Drives.Select(d => DriveRow(d, Compact(state.Drives.Count))), "No drives");
                break;
            case QuickActionId.Plan:
                Rows(state.Plans.Select(PlanRow), "");
                break;
            default:
                Rows([
                    PowerRow(PowerChoice.Sleep, "Sleep", LucideIcons.Moon, "Sleep?"),
                    PowerRow(PowerChoice.Restart, "Restart", LucideIcons.RefreshCw, "Restart?"),
                    PowerRow(PowerChoice.ShutDown, "Shut down", LucideIcons.Power, "Shut down?"),
                    PowerRow(PowerChoice.SignOut, "Sign out", LucideIcons.LogOut, "Sign out?"),
                ], "");
                break;
        }
    }

    /// <summary>
    /// A list's columns: one per 8 rows, up to 4, so the page never scrolls and nothing is left out up to 32 rows. Past
    /// that the rest aren't listed, and "Quit all" still closes them.
    /// </summary>
    internal static int ListColumns(int count) => Math.Clamp((count + ListRows - 1) / ListRows, 1, MaxListColumns);

    /// <summary>Three or four columns are too narrow for a row's badge or detail; its tooltip carries them.</summary>
    private static bool Compact(int count) => ListColumns(count) >= 3;

    private void Rows(IEnumerable<FrameworkElement> rows, string empty)
    {
        var all = rows.ToList();
        int columns = ListColumns(all.Count);
        var list = all.Take(ListRows * columns).ToList();
        if (list.Count == 0)
        {
            var none = Ui.Muted(empty);
            none.HorizontalAlignment = HorizontalAlignment.Center;
            _body.Children.Add(none);
            return;
        }
        // 8 between columns.
        var grid = new UniformGrid { Columns = columns, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, columns > 1 ? -8 : 0, 0) };
        foreach (var row in list)
        {
            if (columns > 1)
                row.Margin = new Thickness(0, 0, 8, 0);
            grid.Children.Add(row);
        }
        _body.Children.Add(grid);
    }

    private void QuitList(QuickState state)
    {
        bool compact = Compact(state.Apps.Count);
        Rows(state.Apps.Select(app => AppRow(app, compact)), "No apps open");
        if (_module.Quitting(keepFront: false).Count == 0)
            return;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
        if (state.Apps.FirstOrDefault(a => a.Front) is { } front && _module.Quitting(keepFront: true).Count > 0)
        {
            var allBut = Ui.Button($"Quit all but {front.Name}");
            allBut.Margin = new Thickness(0, 0, 8, 0);
            allBut.Click += (_, _) => AskFirst(allBut, $"Quit all but {front.Name}?", "Quit", () => QuitAll(keepFront: true));
            buttons.Children.Add(allBut);
        }
        var all = Ui.Button("Quit all");
        all.Click += (_, _) => AskFirst(all, "Quit all apps?", "Quit", () => QuitAll(keepFront: false));
        buttons.Children.Add(all);
        _body.Children.Add(buttons);
    }

    private async void QuitAll(bool keepFront) => await Act(QuickActionId.Quit, () => _module.QuitAllAsync(keepFront), "Quit failed");

    private Border AppRow(OpenApp app, bool compact)
    {
        UIElement mark = AppIcon.Load(app.Path) is { } icon
            ? new Image { Source = icon, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center }
            : Ui.Monogram(app.Name);
        var quit = Ui.Icon(LucideIcons.Close, $"Quit {app.Name}");
        quit.Click += async (_, _) => await Act(QuickActionId.Quit, () => _module.QuitAsync(app), "Quit failed");
        var parts = new List<UIElement> { quit };
        string? badge = app.Front ? "Front" : _module.Settings.Keeps(app.Path) ? "Kept" : null;
        if (badge is not null && !compact)
            parts.Add(Ui.Badge(badge));
        return Row(mark, app.Name, parts, badge is null ? app.Name : $"{app.Name} · {badge}");
    }

    private Border DriveRow(RemovableDrive drive, bool compact)
    {
        var eject = Ui.Icon(LucideIcons.Eject, $"Eject {drive.Name}");
        eject.Click += async (_, _) => await Act(QuickActionId.Eject, () => _module.EjectAsync(drive), "Eject failed");
        if (compact)
            return Row(Ui.Glyph(LucideIcons.HardDrive), drive.Name, [eject], $"{drive.Name} · {Size(drive.Size)}");
        var size = Ui.Muted(Size(drive.Size), 11);
        size.Margin = new Thickness(8, 0, 4, 0);
        return Row(Ui.Glyph(LucideIcons.HardDrive), drive.Name, [eject, size]);
    }

    /// <summary>A list row that isn't a button itself (its buttons are at its end): 30 tall, a hover wash, 4 corners.</summary>
    private static Border Row(UIElement lead, string name, IReadOnlyList<UIElement> end, string? tip = null)
    {
        var dock = new DockPanel { LastChildFill = true };
        foreach (var part in end)
        {
            DockPanel.SetDock(part, Dock.Right);
            dock.Children.Add(part);
        }
        DockPanel.SetDock(lead, Dock.Left);
        dock.Children.Add(lead);
        var label = Ui.Text(name, 12);
        label.Margin = new Thickness(8, 0, 0, 0);
        label.ToolTip = tip ?? name;
        dock.Children.Add(label);
        var row = new Border { Height = 30, Padding = new Thickness(10, 0, 3, 0), CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Child = dock };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "IslandHoverBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    private Button PlanRow(PowerPlan plan)
    {
        var mark = new Ellipse { Tag = plan.Active ? "On" : null, VerticalAlignment = VerticalAlignment.Center };
        mark.SetResourceReference(StyleProperty, "IslandRadioMarkStyle");
        var name = Ui.Text(plan.Name, 12);
        name.Margin = new Thickness(8, 0, 0, 0);
        if (plan.Active)
            name.FontWeight = FontWeights.SemiBold;
        var row = ListButton(new DockPanel { Children = { mark, name } }, plan.Name);
        AutomationProperties.SetItemStatus(row, plan.Active ? "Active" : "");
        row.Click += async (_, _) => await Act(QuickActionId.Plan, () => _module.SetPowerPlanAsync(plan.Id), "Power plan failed");
        return row;
    }

    private Button PowerRow(PowerChoice choice, string name, Geometry icon, string question)
    {
        var label = Ui.Text(name, 12);
        label.Margin = new Thickness(8, 0, 0, 0);
        var row = ListButton(new DockPanel { Children = { Ui.Glyph(icon), label } }, name);
        row.Click += (_, _) => AskFirst(row, question, name, async () => await Act(QuickActionId.Power, () => _module.PowerAsync(choice), $"{name} failed"));
        return row;
    }

    private static Button ListButton(UIElement content, string name)
    {
        var row = new Button { Content = content };
        row.SetResourceReference(StyleProperty, "IslandListRowStyle");
        AutomationProperties.SetName(row, name);
        return row;
    }

    /// <summary>A list's first row (Plan, Power), or the first row's first button (Quit, Eject).</summary>
    private ButtonBase? FirstRowButton() =>
        _body.Children.OfType<UniformGrid>().FirstOrDefault()?.Children.OfType<FrameworkElement>().FirstOrDefault() switch
        {
            ButtonBase row => row,
            Border { Child: DockPanel row } => row.Children.OfType<ButtonBase>().FirstOrDefault(),
            _ => null,
        };

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
