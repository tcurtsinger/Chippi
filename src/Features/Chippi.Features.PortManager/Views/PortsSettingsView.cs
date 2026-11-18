// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Net;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.Core.Clipboard;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using static Chippi.Features.PortManager.Views.PortUi;

namespace Chippi.Features.PortManager.Views;

/// <summary>
/// Settings › System › Ports (docs/phase-7/design/port-manager.html): the live table, listening or every connection, with its search,
/// Pause and Refresh, the row's verb with its question in the row itself, and the two shortcuts read-only. Nothing ends until the
/// verb in a row's question is pressed. The engine reads only while this page shows.
/// </summary>
public sealed class PortsSettingsView : ContentControl, IDisposable
{
    private static readonly GridLength[] ListeningColumns = [new(66), new(138), new(86), new(1, GridUnitType.Star), new(52), new(16), new(40), new(22)];
    private static readonly GridLength[] AllColumns = [new(66), new(122), new(136), new(78), new(1, GridUnitType.Star), new(48), new(16), new(40), new(20)];
    private static readonly TimeSpan StripFor = TimeSpan.FromSeconds(6);

    private readonly PortManagerEngine _engine;
    private readonly ModuleContext _context;
    private readonly PortFlow _flow;
    private readonly StackPanel _page = new() { MaxWidth = 1000 };
    private readonly StackPanel _table = new() { Margin = new Thickness(0, 2, 0, 4) };
    private readonly TextBlock _footer = new() { Margin = new Thickness(14, 6, 14, 10) };
    private readonly TextBlock _failure = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly SegmentedControl _mode;
    private readonly SearchBox _search = new() { Placeholder = "Port or name", Width = 220, Height = 32, VerticalAlignment = VerticalAlignment.Center };
    private readonly ToggleButton _pause = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly List<(string Name, ShortcutSetting View)> _shortcuts = [];
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _dismiss = new();
    private bool _all, _following, _disposed, _refreshing;
    private int? _landPort;
    private FrameworkElement? _landed;
    private Popup? _menu;

    public PortsSettingsView(PortManagerEngine engine, ModuleContext context, string? section = null, PortFlow? flow = null)
    {
        _engine = engine;
        _context = context;
        _flow = flow ?? new PortFlow(engine);
        if (section is not null && section.StartsWith("port:", StringComparison.Ordinal) && int.TryParse(section.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out int port))
            _landPort = port;
        // A port that Opens from Search may be held by a connection only; the list that shows it is All connections.
        _mode = SettingsUi_Segments();
        var toolbar = new Grid { Margin = new Thickness(0, 8, 0, 10) };
        for (int i = 0; i < 5; i++) toolbar.ColumnDefinitions.Add(new() { Width = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Add(toolbar, _mode);
        _search.Margin = new Thickness(10, 0, 0, 0);
        AutomationProperties.SetName(_search, "Search ports");
        Add(toolbar, _search, column: 1);
        var pauseLabel = new TextBlock { Text = "Pause", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        pauseLabel.SetResourceReference(StyleProperty, "BodyTextStyle");
        _pause.SetResourceReference(StyleProperty, "SwitchStyle");
        AutomationProperties.SetName(_pause, "Pause");
        var pause = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0), Children = { pauseLabel, _pause } };
        Add(toolbar, pause, column: 3);
        var refresh = new Button { MinWidth = 68, VerticalAlignment = VerticalAlignment.Center };
        refresh.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        refresh.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { Glyph(LucideIcons.RefreshCw, 14, "TextPrimaryBrush"), new TextBlock { Text = "Refresh", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center } },
        };
        AutomationProperties.SetName(refresh, "Refresh");
        refresh.Click += (_, _) => { if (!_refreshing) _ = RefreshAsync(); };
        Add(toolbar, refresh, column: 4);
        _page.Children.Add(toolbar);
        _failure.SetResourceReference(StyleProperty, "CaptionTextStyle");
        _failure.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_failure, AutomationLiveSetting.Polite);
        _page.Children.Add(_failure);
        _footer.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        _footer.FontSize = 12;
        var holder = new StackPanel();
        holder.Children.Add(_table);
        holder.Children.Add(_footer);
        var card = new Card();
        card.Children.Add(holder);
        _page.Children.Add(card);
        _page.Children.Add(SettingsLayout.Section("Shortcuts"));
        var shortcuts = new Card();
        foreach (var (name, icon) in new[] { (PortManagerModule.FindPort, LucideIcons.Search), (PortManagerModule.FreePort, LucideIcons.Network) })
        {
            var view = new ShortcutSetting(name, () => context.EditShortcut(name));
            shortcuts.Children.Add(new SettingRow { Header = name, Icon = icon, Content = view });
            _shortcuts.Add((name, view));
        }
        _page.Children.Add(shortcuts);
        _mode.SelectionChanged += (_, _) => { _all = ((_mode.SelectedItem as SegmentItem)?.Value as string) == "all"; _landPort = null; Rebuild(); };
        _search.TextChanged += (_, _) => { _landPort = null; Rebuild(); };
        _pause.Checked += PausedChanged;
        _pause.Unchecked += PausedChanged;
        _tick.Tick += (_, _) => { if (_flow.Step == FlowStep.Working) Rebuild(); else _tick.Stop(); };
        _dismiss.Tick += (_, _) => { _dismiss.Stop(); if (_flow.Step == FlowStep.Result) _flow.Dismiss(); };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Content = new ScrollViewer { Content = _page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Rebuild();
    }

    private static SegmentedControl SettingsUi_Segments()
    {
        var items = new List<SegmentItem> { new() { Label = "Listening", Value = "listening" }, new() { Label = "All connections", Value = "all" } };
        var control = new SegmentedControl { ItemsSource = items, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        control.SelectedItem = items[0];
        AutomationProperties.SetName(control, "Show");
        return control;
    }

    internal PortFlow Flow => _flow;
    internal bool ShowsAll => _all;

    /// <summary>For tests and shots: shows every connection, or only the listening ones.</summary>
    internal void ShowAll(bool all)
    {
        _all = all;
        _mode.SelectedIndex = all ? 1 : 0;
        Rebuild();
    }

    internal void SearchFor(string text) => _search.Text = text;

    internal void Redraw() => Rebuild();

    internal void Pause(bool on) => _pause.IsChecked = on;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Follow(true);
        _engine.SetVisible(true);
        Rebuild();
        if (_landed is not null) Dispatcher.BeginInvoke(() => { if (IsLoaded && !_disposed) _landed?.BringIntoView(); });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Follow(false);
        _engine.SetVisible(false);
        CloseMenu();
    }

    private void Follow(bool enabled)
    {
        if (_following == enabled || _disposed && enabled) return;
        if (enabled) { _engine.Changed += OnChanged; _flow.Changed += OnChanged; }
        else { _engine.Changed -= OnChanged; _flow.Changed -= OnChanged; _tick.Stop(); _dismiss.Stop(); }
        _following = enabled;
    }

    private void OnChanged()
    {
        if (_disposed) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(OnChanged); return; }
        if (_flow.Step == FlowStep.Working) _tick.Start();
        if (_flow.Step == FlowStep.Result)
        {
            _dismiss.Interval = StripFor;
            _dismiss.Stop();
            _dismiss.Start();
        }
        Rebuild();
    }

    private async Task RefreshAsync()
    {
        _refreshing = true;
        try { await _engine.RefreshAsync(); }
        catch (OperationCanceledException) { }
        finally { _refreshing = false; }
    }

    private void PausedChanged(object sender, RoutedEventArgs e)
    {
        _engine.Paused = _pause.IsChecked == true;
        Rebuild();
    }

    // ---- the table ----

    private GridLength[] Columns => _all ? AllColumns : ListeningColumns;

    private Grid RowGrid(double height)
    {
        var grid = new Grid { Height = height, Margin = new Thickness(14, 0, 8, 0) };
        foreach (var width in Columns) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        return grid;
    }

    private static TextBlock T(string text, double size = 12, string brush = "TextPrimaryBrush")
    {
        var block = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private void Rebuild()
    {
        if (_disposed) return;
        CloseMenu();
        _table.Children.Clear();
        _landed = null;
        var header = RowGrid(30);
        string[] names = _all ? ["Protocol", "Local", "Remote", "State", "Process", "PID", "", "", ""] : ["Protocol", "Local", "State", "Process", "PID", "", "", ""];
        for (int i = 0; i < names.Length; i++)
        {
            var label = T(names[i], 11, "TextTertiaryBrush");
            Add(header, label, column: i);
        }
        var headerBox = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerBox.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        _table.Children.Add(headerBox);

        string query = _search.Text.Trim();
        var shown = _engine.Rows.Where(r => (_all || r.Row.Endpoint.IsHeld) && PortFilter.Matches(r.Row, query)).ToList();
        string? flowKey = _flow.Step == FlowStep.Idle ? null : _flow.Row?.Key;
        bool stripPlaced = false;
        foreach (var item in shown)
        {
            if (flowKey is not null && !stripPlaced && item.Row.Key == flowKey) { _table.Children.Add(Strip()); stripPlaced = true; continue; }
            if (flowKey is not null && !stripPlaced && _flow.Row is { } anchor && PortManagerEngine.Compare(anchor.Endpoint, item.Row.Endpoint) < 0)
            {
                _table.Children.Add(Strip());
                stripPlaced = true;
            }
            _table.Children.Add(Row(item));
        }
        if (flowKey is not null && !stripPlaced) _table.Children.Add(Strip());
        if (shown.Count == 0 && flowKey is null)
        {
            var none = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 28, 0, 28) };
            var mark = Glyph(LucideIcons.Network, 24, "TextTertiaryBrush");
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            string text = query.Length > 0 && query.All(char.IsAsciiDigit) ? $"Port {query} not in use" : "No results";
            var line = new TextBlock { Text = text, FontSize = 13, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            line.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            none.Children.Add(mark);
            none.Children.Add(line);
            _table.Children.Add(none);
        }
        int count = shown.Count(r => r.Mark != PortMark.Ended);
        _footer.Text = _engine.Paused ? "Paused" : _all ? $"{count} connections" : $"{count} listening";
        _failure.Text = _engine.Error ?? "";
        _failure.Visibility = _failure.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (name, view) in _shortcuts)
        {
            var slot = _context.Shortcuts.List.FirstOrDefault(s => s.Owner == PortManagerModule.Id && s.Name == name);
            view.Show(slot?.Shortcut, slot?.Error);
        }
    }

    private static string AddressText(IPAddress address, int port) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{address.ToString().Split('%')[0]}]:{port}" : $"{address}:{port}";

    private static string TagOf(PortRow row) => row.Holder.Kind switch
    {
        HolderKind.Service => "Service",
        HolderKind.Windows when row.Holder.Pid != 0 => "Windows",
        HolderKind.Shared => "Shared service",
        _ => "",
    };

    private UIElement Row(PortListRow item)
    {
        var row = item.Row;
        var e = row.Endpoint;
        bool ended = item.Mark == PortMark.Ended, isNew = item.Mark == PortMark.New;
        bool landed = _landPort is { } land && e.LocalPort == land;
        var grid = RowGrid(34);
        var cells = new List<UIElement>();
        void Cell(UIElement element) { cells.Add(element); Add(grid, element, column: cells.Count - 1); }
        Cell(T(e.Protocol == PortProtocol.Tcp ? "TCP" : "UDP"));
        Cell(T(AddressText(e.LocalAddress, e.LocalPort)));
        if (_all)
        {
            bool dim = e.RemoteAddress is null || e.RemoteAddress.Equals(IPAddress.Any) || e.RemoteAddress.Equals(IPAddress.IPv6Any);
            Cell(T(e.RemoteAddress is null ? "*:*" : AddressText(e.RemoteAddress, e.RemotePort), 12, dim ? "TextTertiaryBrush" : "TextPrimaryBrush"));
        }
        Cell(T(e.Protocol == PortProtocol.Udp ? "—" : PortAnswer.StateText(e.State), 12, e.Protocol == PortProtocol.Udp ? "TextTertiaryBrush" : "TextPrimaryBrush"));
        // The process: its mark, its name, and a tag for what it is.
        var process = new Grid { Margin = new Thickness(0, 0, 8, 0) };
        process.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        process.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        process.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var mark = Mark(row.Holder, 16);
        mark.Margin = new Thickness(0, 0, 8, 0);
        Add(process, mark);
        string name = row.Holder.Pid == 0 ? "Closing connection" : row.Holder.Kind == HolderKind.Chippi ? "Chippi" : row.Holder.Name;
        var nameText = T(name);
        nameText.Margin = new Thickness(0);
        Add(process, nameText, column: 1);
        string tag = TagOf(row);
        if (tag.Length > 0)
        {
            var tagText = T("· " + tag, 12, "TextTertiaryBrush");
            tagText.Margin = new Thickness(8, 0, 0, 0);
            Add(process, tagText, column: 2);
        }
        if (row.Holder.Path is { Length: > 0 } path) process.ToolTip = path;
        Cell(process);
        Cell(T(row.Holder.Pid.ToString(CultureInfo.InvariantCulture), 12, "TextTertiaryBrush"));
        PortVerb verb = ended ? PortVerb.None : row.Verb;
        // The shield has its own narrow column, left of the verb.
        var shieldCell = new Grid();
        if (verb != PortVerb.None && row.NeedsAdmin) shieldCell.Children.Add(new UacShield(13) { HorizontalAlignment = HorizontalAlignment.Center });
        Cell(shieldCell);
        TextBlock? verbText = null;
        if (verb != PortVerb.None)
        {
            string label = verb == PortVerb.Stop ? "Stop" : "Free";
            verbText = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            verbText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var button = new Button { Content = verbText, Padding = new Thickness(0), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Left };
            button.SetResourceReference(StyleProperty, "SubtleButtonStyle");
            button.Padding = new Thickness(0);
            AutomationProperties.SetName(button, $"{label} {(verb == PortVerb.Stop ? PortFlow.ServiceName(row) : row.Holder.Name)}");
            button.Click += (_, _) => _flow.Ask(row);
            Cell(button);
        }
        else Cell(new Grid());
        // The menu: three copies, shown with the pointer on the row or the keyboard in it.
        var more = new Button { Content = Glyph(LucideIcons.Ellipsis, 16, "TextTertiaryBrush"), Opacity = 0, Padding = new Thickness(0), Width = 22, Height = 24 };
        more.SetResourceReference(StyleProperty, "SubtleButtonStyle");
        AutomationProperties.SetName(more, $"More for port {e.LocalPort}");
        more.Click += (_, _) => ShowMenu(more, row);
        more.GotKeyboardFocus += (_, _) => more.Opacity = 1;
        more.LostKeyboardFocus += (_, _) => { if (!more.IsMouseOver) more.Opacity = 0; };
        Cell(more);

        var root = new Grid { Margin = new Thickness(0) };
        var back = new Border { CornerRadius = new CornerRadius(4), IsHitTestVisible = false };
        if (landed || isNew)
        {
            back.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            back.Opacity = 0.12;
        }
        root.Children.Add(back);
        if (landed)
        {
            var edge = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), IsHitTestVisible = false };
            edge.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            root.Children.Add(edge);
            _landed ??= root;
        }
        if (isNew)
        {
            var bar = new Border { Width = 3, Margin = new Thickness(3, 8, 0, 8), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2), IsHitTestVisible = false };
            bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            root.Children.Add(bar);
        }
        root.Children.Add(grid);
        if (ended)
        {
            root.Opacity = 0.42;
            foreach (var cell in cells.OfType<TextBlock>()) cell.TextDecorations = TextDecorations.Strikethrough;
            nameText.TextDecorations = TextDecorations.Strikethrough;
        }
        root.MouseEnter += (_, _) =>
        {
            if (!landed && !isNew) { back.SetResourceReference(Border.BackgroundProperty, "SurfaceSelectedBrush"); back.Opacity = 1; }
            more.Opacity = 1;
            if (verbText is not null) { verbText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush"); verbText.FontWeight = FontWeights.SemiBold; }
        };
        root.MouseLeave += (_, _) =>
        {
            if (!landed && !isNew) { back.ClearValue(Border.BackgroundProperty); }
            if (!more.IsKeyboardFocused) more.Opacity = 0;
            if (verbText is not null) { verbText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); verbText.FontWeight = FontWeights.Normal; }
        };
        return root;
    }

    // ---- the row's menu ----

    private void ShowMenu(Button anchor, PortRow row)
    {
        CloseMenu();
        var e = row.Endpoint;
        var items = new StackPanel();
        void Item(string label, string value)
        {
            var line = new Grid();
            line.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            Add(line, new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) });
            var hint = new TextBlock { Text = value, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            Add(line, hint, column: 1);
            var button = new Button { Content = line, Height = 32, MinWidth = 220, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 0, 10, 0) };
            button.SetResourceReference(StyleProperty, "SubtleButtonStyle");
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) =>
            {
                CloseMenu();
                if (_context.Clipboard is { } clipboard) _ = clipboard.WriteAsync(ClipContent.FromText(value));
            };
            items.Children.Add(button);
        }
        Item("Copy port", e.LocalPort.ToString(CultureInfo.InvariantCulture));
        Item("Copy address", AddressText(e.LocalAddress, e.LocalPort));
        Item("Copy PID", row.Holder.Pid.ToString(CultureInfo.InvariantCulture));
        var box = new Border { Child = items, Padding = new Thickness(4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        box.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
        _menu = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Child = box, IsOpen = true };
        _menu.Closed += (_, _) => _menu = null;
    }

    private void CloseMenu()
    {
        if (_menu is { } menu) { _menu = null; menu.IsOpen = false; }
    }

    // ---- the question and its results, in the row ----

    private Button StripButton(string text, Action action, bool critical = false, bool shield = false)
    {
        object content = text;
        if (shield)
        {
            content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { new UacShield(14) { Margin = new Thickness(0, 0, 8, 0) }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
            };
        }
        var button = new Button { Content = content, Height = 26, MinWidth = 64, Padding = new Thickness(10, 0, 10, 0), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        button.SetResourceReference(StyleProperty, critical ? "CriticalButtonStyle" : "SecondaryButtonStyle");
        button.Height = 26;
        button.FontSize = 12;
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>A 46-high strip in the row's place: tinted by its brush, drawn as the mockup's strips.</summary>
    private static FrameworkElement StripFrame(string brush, double tint, UIElement content, bool neutral = false)
    {
        var root = new Grid { Height = 46, Margin = new Thickness(4, 2, 4, 2) };
        var back = new Border { CornerRadius = new CornerRadius(4), IsHitTestVisible = false };
        var edge = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), IsHitTestVisible = false };
        if (neutral)
        {
            back.SetResourceReference(Border.BackgroundProperty, "SurfaceSelectedBrush");
            edge.SetResourceReference(Border.BorderBrushProperty, "ButtonBorderBrush");
        }
        else
        {
            back.SetResourceReference(Border.BackgroundProperty, brush);
            back.Opacity = tint;
            edge.SetResourceReference(Border.BorderBrushProperty, brush);
        }
        root.Children.Add(back);
        root.Children.Add(edge);
        root.Children.Add(content);
        return root;
    }

    private static Grid StripLine()
    {
        var line = new Grid { Margin = new Thickness(14, 0, 10, 0) };
        line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        return line;
    }

    private static StackPanel Buttons(params UIElement[] buttons)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var button in buttons) strip.Children.Add(button);
        return strip;
    }

    private static Grid Message(string text, string? small = null, string brush = "TextPrimaryBrush")
    {
        var host = new Grid { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        host.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        host.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var main = new TextBlock { Text = text, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        main.SetResourceReference(TextBlock.ForegroundProperty, brush);
        Add(host, main);
        if (!string.IsNullOrEmpty(small))
        {
            var detail = new TextBlock { Text = small, FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            Add(host, detail, column: 1);
        }
        return host;
    }

    private FrameworkElement Strip()
    {
        var row = _flow.Row!;
        string who = row.Verb == PortVerb.Stop ? PortFlow.ServiceName(row) : row.Holder.Name;
        var line = StripLine();
        switch (_flow.Step)
        {
            case FlowStep.Asking or FlowStep.ForceAsking:
            {
                Add(line, Mark(row.Holder, 16));
                string small = _flow.Force || row.Verb == PortVerb.Stop ? "" : row.Holder.PortCount > 1 ? $"{row.Holder.PortCount} ports · PID {row.Holder.Pid}" : $"PID {row.Holder.Pid}";
                var message = Message(_flow.Title, small);
                message.HorizontalAlignment = HorizontalAlignment.Left;
                Add(line, message, column: 1);
                string verb = row.Verb == PortVerb.Stop ? "Stop" : _flow.Force ? "Force end" : "End";
                var cancel = StripButton("Cancel", () => _flow.Cancel());
                var confirm = StripButton(verb, () => _ = _flow.ConfirmAsync(), critical: true, shield: row.NeedsAdmin);
                var buttons = Buttons(cancel, confirm);
                buttons.HorizontalAlignment = HorizontalAlignment.Right;
                Add(line, buttons, column: 1);
                var frame = StripFrame("AccentBrush", 0.12, line);
                Dispatcher.BeginInvoke(() => { if (IsLoaded && cancel.IsVisible && !cancel.IsKeyboardFocusWithin) cancel.Focus(); }, DispatcherPriority.Input);
                return frame;
            }
            case FlowStep.Working:
            {
                Add(line, Mark(row.Holder, 16));
                if (_flow.ShowsClosing)
                {
                    Add(line, Message("Closing " + who), column: 1);
                    var fill = new Border { Height = 4, Width = 120 * _flow.Progress, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) };
                    fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    var track = new Border { Width = 120, Height = 4, CornerRadius = new CornerRadius(2), Child = fill, VerticalAlignment = VerticalAlignment.Center };
                    track.SetResourceReference(Border.BackgroundProperty, "BorderSoftBrush");
                    var tail = Buttons(track, StripButton("Cancel", () => _flow.Cancel()));
                    Add(line, tail, column: 1);
                    tail.HorizontalAlignment = HorizontalAlignment.Right;
                }
                else Add(line, Message(_flow.WaitsForAdmin ? "Waiting for admin" : _flow.Title.TrimEnd('?')), column: 1);
                return StripFrame("AccentBrush", 0.12, line);
            }
            default:
                return ResultStrip(row, who);
        }
    }

    private FrameworkElement ResultStrip(PortRow row, string who)
    {
        var outcome = _flow.Outcome;
        var line = StripLine();
        if (outcome is null) return StripFrame("AccentBrush", 0.12, line);
        switch (outcome.Kind)
        {
            case OutcomeKind.Freed or OutcomeKind.Holder:
                Add(line, Glyph(LucideIcons.CircleCheck, 16, "SuccessBrush"));
                Add(line, Message(outcome.Kind == OutcomeKind.Freed ? outcome.Title : $"Port {row.Endpoint.LocalPort} freed"), column: 1);
                return StripFrame("SuccessBrush", 0.16, line);
            case OutcomeKind.Failed:
            {
                Add(line, Glyph(LucideIcons.TriangleAlert, 16, "WarningTextBrush"));
                Add(line, Message(outcome.Title, null), column: 1);
                var retry = Buttons(StripButton("Retry", () => { _ = _flow.RetryAsync(); }));
                retry.HorizontalAlignment = HorizontalAlignment.Right;
                Add(line, retry, column: 1);
                return StripFrame("WarningBrush", 0.10, line);
            }
            case OutcomeKind.Kept:
                Add(line, Mark(row.Holder, 16));
                Add(line, Message("Kept", null, "WarningTextBrush"), column: 1);
                return StripFrame("WarningBrush", 0.10, line);
            default:
                Add(line, Glyph(LucideIcons.Info, 16, "TextTertiaryBrush"));
                Add(line, Message(outcome.Detail ?? outcome.Title, null, "TextSecondaryBrush"), column: 1);
                return StripFrame("", 0, line, neutral: true);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Follow(false);
        _disposed = true;
        _tick.Stop();
        _dismiss.Stop();
        CloseMenu();
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        _pause.Checked -= PausedChanged;
        _pause.Unchecked -= PausedChanged;
    }
}
