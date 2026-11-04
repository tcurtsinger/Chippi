// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.QuickActions.Views;

/// <summary>
/// The island's Actions page (docs/phase-5.5/quick-actions.md, Surfaces 2 to 4): the picker on first open, then the
/// picked full tiles five across, with an in-widget layout editor and small lists (Power plan, Eject, Quit
/// apps, Power) stepping into the page. Nothing scrolls.
/// </summary>
internal sealed partial class ActionsPage : UserControl
{
    /// <summary>The whole tile includes its icon, label and status. Four rows use the compact height.</summary>
    internal const double TallButton = 76, ShortButton = 61;

    internal const int Columns = 5;

    /// <summary>Readable lists show five rows in each of at most two columns, then another page.</summary>
    internal const int ListRows = 5;

    internal const int MaxListColumns = 2;

    /// <summary>How long a button says "Failed".</summary>
    internal static readonly TimeSpan FailedFor = TimeSpan.FromSeconds(4);

    private readonly QuickActionsModule _module;
    private readonly TextBlock _title = Ui.Text("Actions", 14);
    private readonly DockPanel _header = new();
    private readonly Button _edit = Ui.Icon(LucideIcons.Pencil, "Edit actions");
    private readonly Button _done = Ui.Button("Done", "IslandAccentButtonStyle");
    private readonly Grid _body = new() { Width = IslandPage.BodyWidth, Height = IslandPage.BodyHeight };
    private readonly System.Windows.Threading.DispatcherTimer _refresh = new() { Interval = QuickActionsModule.RefreshEvery };
    private readonly Dictionary<QuickActionId, bool> _setting = []; // a toggle's new state while Windows switches it
    private readonly List<Cell> _cells = [];
    private string _built = ""; // what _body holds now: "picker", the grid's keys, or a list and its rows
    private QuickActionId? _list;

    public ActionsPage(QuickActionsModule module)
    {
        _module = module;
        _title.FontWeight = FontWeights.SemiBold;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi.Features.QuickActions;component/Views/ActionsStyles.xaml") });
        _edit.Width = 28;
        _edit.Height = 24;
        _edit.Click += (_, _) => BeginEdit();
        _done.Height = _done.MinHeight = 24;
        _done.Click += (_, _) => FinishEdit();
        DockPanel.SetDock(_edit, Dock.Right);
        DockPanel.SetDock(_done, Dock.Right);
        _header.Children.Add(_edit);
        _header.Children.Add(_done);
        _header.Children.Add(_title);
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
        Unloaded += (_, _) => { _module.Changed -= OnChanged; CancelEditorDrag(); };
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
                DiscardEdit();
                Render();
            }
        };
        Render();
    }

    /// <summary>The page's header: "Actions", or the open list's name with Back returning to the buttons.</summary>
    public FrameworkElement Header => _header;

    internal string Title => _title.Text;

    /// <summary>The first button of what shows.</summary>
    public object? InitialFocus =>
        _editing ? _picks.FirstOrDefault() : _list is null ? (object?)_cells.FirstOrDefault(c => c.Button.IsEnabled)?.Button ?? _edit : FirstRowButton();

    /// <summary>What shows: "picker", "grid", or the open list ("quit", "eject", "plan", "power").</summary>
    internal string Showing => _built.Split(':')[0];

    /// <summary>The failure at a list's foot while it shows, else empty.</summary>
    internal string ListFailure => _listFailed.Visibility == Visibility.Visible ? _listFailed.Text : "";

    /// <summary>Presses an action's button, as a click does.</summary>
    internal void Press(QuickActionId id)
    {
        if (!_editing) Pressed(_cells.First(c => c.Action.Id == id));
    }

    /// <summary>Back: a list returns to the buttons; true when it did.</summary>
    public bool Back()
    {
        if (_editing)
        {
            bool first = _choosing;
            DiscardEdit();
            if (!first)
            {
                Render();
                _edit.Focus();
            }
            return !first;
        }
        if (_list is not { } prior)
            return false;
        CloseConfirm();
        _list = null;
        ResetListPage();
        Render();
        _cells.FirstOrDefault(c => c.Action.Id == prior)?.Button.Focus();
        return true;
    }

    /// <summary>Esc: closes a confirm, then a list; true when it did, else the island closes.</summary>
    public bool Escape()
    {
        if (_gesture is not null)
        {
            CancelEditorDrag();
            return true;
        }
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
        DiscardEdit();
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

    private void OnChanged()
    {
        if (Dispatcher.CheckAccess()) Render();
        else Dispatcher.BeginInvoke(Render);
    }

    internal void Render()
    {
        PruneFailures();
        bool first = _module.Settings.Shown is null || _module.Settings.Shown.Count == 0;
        if (_editing && _choosing && !first)
            DiscardEdit(); // another view completed the first pick
        if (_list is { } list)
            RenderList(list);
        else if (_editing || first)
        {
            if (!_editing) StartEdit(first: true);
            RenderPicker();
        }
        else
            RenderGrid();
        _title.Text = _list is { } open ? QuickAction.Of(open).Name : _editing ? _choosing ? "Choose actions" : "Edit actions" : "Actions";
        _edit.Visibility = !_editing && _list is null ? Visibility.Visible : Visibility.Collapsed;
        _done.Visibility = _editing ? Visibility.Visible : Visibility.Collapsed;
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
        var offered = _module.Offered.ToDictionary(a => a.Key);
        var slots = _module.Layout.Select(key => key is not null && offered.TryGetValue(key, out var action) && _module.Settings.Shows(action.Id) ? action : null).ToArray();
        string built = "grid:" + string.Join(",", slots.Select(a => a?.Key));
        if (_built != built)
        {
            _built = built;
            _cells.Clear();
            int last = Array.FindLastIndex(slots, a => a is not null);
            int rows = Math.Max(1, last / Columns + 1);
            double height = ButtonHeight(rows * Columns);
            var grid = SlotGrid(rows, 7);
            grid.Height = rows * (height + 7);
            grid.VerticalAlignment = VerticalAlignment.Center;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] is not { } action) continue;
                var cell = new Cell(action, height, this);
                _cells.Add(cell);
                Put(grid, cell.Button, i, 7);
            }
            _body.Children.Clear();
            _body.Children.Add(grid);
            if (_cells.Count == 0)
            {
                var empty = Ui.Muted("No actions available");
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                _body.Children.Add(empty);
            }
        }
        var state = _module.State;
        foreach (var cell in _cells)
        {
            var id = cell.Action.Id;
            bool on = _setting.TryGetValue(id, out var setting) ? setting : state.On.GetValueOrDefault(id);
            cell.Show(Describe(cell.Action, state), on, _failed.TryGetValue(id, out var until) && until > DateTime.UtcNow);
        }
    }

    private static Grid SlotGrid(int rows, double gap)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, -gap, -gap) };
        for (int i = 0; i < Columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < rows; i++) grid.RowDefinitions.Add(new RowDefinition());
        return grid;
    }

    private static void Put(Grid grid, FrameworkElement child, int slot, double gap)
    {
        Grid.SetColumn(child, slot % Columns);
        Grid.SetRow(child, slot / Columns);
        child.Margin = new Thickness(0, 0, gap, gap);
        grid.Children.Add(child);
    }

    private async void Pressed(Cell cell)
    {
        if (_editing || !cell.Button.IsEnabled) return;
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
            await Act(id, () => _module.EjectAsync(only), "Eject failed", only.Id);
        else if (action.OpensList)
        {
            _list = id;
            Render();
            FirstRowButton()?.Focus();
        }
        else if (action.Confirm is { } confirm)
            AskFirst(cell.Button, confirm.Question, confirm.Verb, () => Run(id));
        else
            Run(id);
    }

    private async void Run(QuickActionId id) => await Act(id, id == QuickActionId.Frozen ? _module.EndFrozenAppsAsync : () => _module.RunAsync(id));

    /// <summary>The complete tile is one semantic button or toggle, including its label and state.</summary>
    private sealed class Cell
    {
        private readonly LucideIcon _glyph = new() { Size = 18, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly LucideIcon _chevron = new() { Data = LucideIcons.ChevronRight, Size = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 6, 0) };
        private readonly TextBlock _status = Ui.Muted("", 11);
        private bool _on, _showing;

        public Cell(QuickAction action, double height, ActionsPage page)
        {
            Action = action;
            Button = action.Toggle && !action.OpensList ? new ToggleButton() : new Button();
            Button.Height = height;
            Button.Tag = action.Key;
            Button.SetResourceReference(FrameworkElement.StyleProperty, Button is ToggleButton ? "ActionGalleryToggleStyle" : "ActionGalleryButtonStyle");
            BindInk(_glyph, LucideIcon.BrushProperty);
            BindInk(_chevron, LucideIcon.BrushProperty);
            var name = Ui.Text(action.Label, 12);
            name.FontWeight = FontWeights.SemiBold;
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.Margin = new Thickness(2, 3, 2, 0);
            BindInk(name, TextBlock.ForegroundProperty);
            _status.HorizontalAlignment = HorizontalAlignment.Center;
            _status.Margin = new Thickness(3, 1, 3, 0);
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _glyph, name, _status } };
            Button.Content = new Grid { Children = { stack, _chevron } };
            if (Button is ToggleButton toggle)
            {
                // UI Automation Toggle raises Checked/Unchecked, never Click.
                toggle.Checked += (_, _) => Pressed(page);
                toggle.Unchecked += (_, _) => Pressed(page);
            }
            else Button.Click += (_, _) => page.Pressed(this);
        }

        public QuickAction Action { get; }
        public ButtonBase Button { get; }

        private void BindInk(DependencyObject element, DependencyProperty property) =>
            System.Windows.Data.BindingOperations.SetBinding(element, property, new System.Windows.Data.Binding(nameof(Control.Foreground)) { Source = Button });

        private void Pressed(ActionsPage page)
        {
            if (_showing) return;
            Check(_on);
            page.Pressed(this);
        }

        private void Check(bool on)
        {
            if (Button is not ToggleButton toggle) return;
            _showing = true;
            try { toggle.IsChecked = on; }
            finally { _showing = false; }
        }

        public void Show((string Status, bool Enabled, bool List) look, bool on, bool failed)
        {
            _on = Action.Toggle && !Action.OpensList && on;
            Check(_on);
            Button.IsEnabled = look.Enabled;
            // Keep the real toggle state, but give failure ink a neutral surface even when the toggle remains on.
            if (failed)
            {
                Button.SetResourceReference(Control.BackgroundProperty, "IslandGalleryFaceBrush");
                Button.SetResourceReference(Control.ForegroundProperty, "IslandTextBrush");
            }
            else
            {
                Button.ClearValue(Control.BackgroundProperty);
                Button.ClearValue(Control.ForegroundProperty);
            }
            _glyph.Data = Action.Id == QuickActionId.Camera && !on ? LucideIcons.CameraOff : Action.Icon;
            _chevron.Visibility = look.List ? Visibility.Visible : Visibility.Collapsed;
            string status = failed ? $"{Action.Label} failed" : look.Status.Length > 0 ? look.Status : Button is ToggleButton ? on ? "On" : "Off" : "";
            _status.Text = status;
            _status.Visibility = status.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _status.SetResourceReference(TextBlock.ForegroundProperty, failed ? "WarningTextBrush" : _on ? "IslandOnAccentBrush" : "IslandTextSecondaryBrush");
            AutomationProperties.SetName(Button, Action.Name);
            AutomationProperties.SetItemStatus(Button, status);
            AutomationProperties.SetHelpText(Button, failed ? $"{Action.Name} failed. Activate to retry." : Action.Name);
            Button.ToolTip = status.Length > 0 ? $"{Action.Name} \u00b7 {status}" : Action.Name;
        }
    }
}
