// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.Features.AppSwitcher.Views;

/// <summary>
/// The App switcher's look (docs/phase-6/design/wave-3.html, boards switcher and sheet-switcher-states): search across the
/// top with All displays and This display beside it, then the windows as cards, a preview over each title. Up to three
/// rows show; more scroll by whole rows, so the live previews Windows draws on top are never cut by the edge. A minimized
/// window, an app in Apps without previews, or one Windows can't capture shows its icon instead.
/// </summary>
internal sealed class SwitcherBoard : Border
{
    public const double CardWidth = 176, PreviewHeight = 100, Gap = 8, Pad = 12, Widest = 938;
    public const int MaxRows = 3;
    private const double TitleHeight = 30;

    private readonly SearchBox _search = new() { Placeholder = "Search windows", Height = 36, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly SegmentedControl _displays = new() { Margin = new Thickness(Gap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly SegmentItem _all = new() { Label = "All displays", Value = false }, _this = new() { Label = "This display", Value = true };
    private readonly Grid _cards = new();
    private readonly Border _scroll = new() { Width = 3, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
    private readonly TextBlock _empty = new() { Text = "No results", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private readonly List<(SwitcherWindow Window, Border Slot, bool Live)> _slots = [];
    private readonly List<(Border Card, TextBlock Title, SwitcherWindow Window)> _drawn = [];
    private string _drawnKey = "";
    private SwitcherSession? _session;
    private Func<SwitcherWindow, bool> _wantsPreview = _ => false;
    private readonly HashSet<nint> _noThumbnail = [];
    private nint _failed;
    private int _firstRow;
    private bool _drawing;

    /// <param name="displayFilter">All displays (false) or This display (true) was chosen.</param>
    /// <param name="picked">A card was clicked: its index in the session's list.</param>
    public SwitcherBoard(Action<bool> displayFilter, Action<int> picked)
    {
        Picked = picked;
        CornerRadius = new CornerRadius(8); // Windows rounds the window to 8 too
        BorderThickness = new Thickness(1);
        Padding = new Thickness(Pad);
        SetResourceReference(BackgroundProperty, "IslandBrush");
        SetResourceReference(BorderBrushProperty, "IslandBorderBrush");
        SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        AutomationProperties.SetName(_search, "Search windows");
        _displays.ItemsSource = new[] { _all, _this };
        _displays.SelectedItem = _all;
        AutomationProperties.SetName(_displays, "Windows from");
        _displays.SelectionChanged += (_, _) =>
        {
            if (_drawing || _displays.SelectedItem is not SegmentItem { Value: bool on }) return;
            displayFilter(on);
            _search.Focus();
        };
        _search.TextChanged += (_, _) => { if (!_drawing) _session?.Type(_search.Text); };
        _scroll.SetResourceReference(Border.BackgroundProperty, "TextTertiaryBrush");
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, Gap) };
        DockPanel.SetDock(_displays, Dock.Right);
        top.Children.Add(_displays);
        top.Children.Add(_search);
        var body = new Grid { Children = { _cards, _empty, _scroll } };
        Child = new DockPanel { Children = { top, body } };
        DockPanel.SetDock(top, Dock.Top);
        PreviewMouseWheel += (_, e) =>
        {
            Scroll(e.Delta > 0 ? -1 : 1);
            e.Handled = true;
        };
    }

    private Action<int> Picked { get; }

    public SearchBox Search => _search;

    /// <summary>How many cards fit across at this width.</summary>
    public int Columns { get; private set; } = 5;

    /// <summary>The cards whose slot shows a live preview, in view now, for the window to draw them into.</summary>
    public IReadOnlyList<(SwitcherWindow Window, Border Slot, bool Live)> Slots => _slots;

    /// <summary>The width that holds <paramref name="columns"/> cards.</summary>
    public static double WidthFor(int columns) => columns * CardWidth + (columns - 1) * Gap + 2 * Pad + 2;

    /// <summary>The height of <paramref name="rows"/> rows of cards under the search.</summary>
    public static double HeightFor(int rows) => 2 * Pad + 2 + 36 + Gap + Math.Max(1, rows) * (CardHeight + Gap) - Gap;

    public static double CardHeight => 12 + PreviewHeight + TitleHeight;

    public void Show(SwitcherSession session, Func<SwitcherWindow, bool> wantsPreview, double width)
    {
        _session = session;
        _wantsPreview = wantsPreview;
        _noThumbnail.Clear();
        _failed = 0;
        _firstRow = 0;
        _drawnKey = "";
        Columns = Math.Max(1, (int)((width - 2 * Pad - 2 + Gap) / (CardWidth + Gap)));
        Width = WidthFor(Columns);
        _drawing = true;
        _search.Text = session.Query;
        _displays.SelectedItem = session.CurrentDisplayOnly ? _this : _all;
        _drawing = false;
        Draw();
    }

    /// <summary>Windows had no preview for this one after all: it shows its icon.</summary>
    public void NoThumbnail(nint handle)
    {
        if (_noThumbnail.Add(handle)) Draw();
    }

    /// <summary>The selected card says "Switch failed" until the selection or the list changes.</summary>
    public void Failed(SwitcherWindow window)
    {
        _failed = window.Handle;
        Draw();
    }

    /// <summary>The rows the list needs now, up to <see cref="MaxRows"/>.</summary>
    public int Rows => _session is null ? 1 : Math.Clamp((_session.Shown.Count + Columns - 1) / Columns, 1, MaxRows);

    public void Draw()
    {
        if (_session is not { } session) return;
        var shown = session.Shown;
        int rows = (shown.Count + Columns - 1) / Columns;
        int selectedRow = session.Selected < 0 ? 0 : session.Selected / Columns;
        if (session.Selected >= 0 && _failed != session.Selection?.Handle) _failed = 0;
        // The selection stays in view.
        if (selectedRow < _firstRow) _firstRow = selectedRow;
        if (selectedRow >= _firstRow + MaxRows) _firstRow = selectedRow - MaxRows + 1;
        _firstRow = Math.Clamp(_firstRow, 0, Math.Max(0, rows - MaxRows));

        // Only the selection moved: restyle the cards in place, so the previews stay where they are.
        int last = Math.Min(shown.Count, (_firstRow + MaxRows) * Columns);
        string key = $"{Columns}|{_firstRow}|{string.Join(",", Enumerable.Range(_firstRow * Columns, Math.Max(0, last - _firstRow * Columns)).Select(i => shown[i].Handle))}|{string.Join(",", _noThumbnail)}";
        if (key == _drawnKey)
        {
            for (int i = 0; i < _drawn.Count; i++)
                Restyle(_drawn[i].Card, _drawn[i].Title, _drawn[i].Window, _firstRow * Columns + i == session.Selected);
            return;
        }
        _drawnKey = key;
        _drawn.Clear();
        _cards.Children.Clear();
        _cards.ColumnDefinitions.Clear();
        _cards.RowDefinitions.Clear();
        _slots.Clear();
        for (int c = 0; c < Columns; c++) _cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardWidth + (c < Columns - 1 ? Gap : 0)) });
        int visibleRows = Math.Clamp(rows - _firstRow, 1, MaxRows);
        for (int r = 0; r < visibleRows; r++) _cards.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CardHeight + (r < visibleRows - 1 ? Gap : 0)) });
        for (int i = _firstRow * Columns; i < Math.Min(shown.Count, (_firstRow + MaxRows) * Columns); i++)
        {
            var card = Card(shown[i], i, i == session.Selected);
            Grid.SetColumn(card, i % Columns);
            Grid.SetRow(card, i / Columns - _firstRow);
            _cards.Children.Add(card);
        }
        _empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _cards.Height = visibleRows * (CardHeight + Gap) - Gap;

        // A thin bar at the right says where this is in a list of more than three rows.
        if (rows > MaxRows)
        {
            double track = _cards.Height;
            _scroll.Visibility = Visibility.Visible;
            _scroll.Height = Math.Max(24, track * MaxRows / rows);
            _scroll.Margin = new Thickness(0, (track - _scroll.Height) * _firstRow / Math.Max(1, rows - MaxRows), -Pad + 4, 0);
        }
        else
            _scroll.Visibility = Visibility.Collapsed;
        Drawn?.Invoke();
    }

    /// <summary>Raised after the cards were laid out again, for the window to move the live previews.</summary>
    public event Action? Drawn;

    private void Scroll(int by)
    {
        if (_session is null) return;
        int rows = (_session.Shown.Count + Columns - 1) / Columns;
        int next = Math.Clamp(_firstRow + by, 0, Math.Max(0, rows - MaxRows));
        if (next == _firstRow) return;
        _firstRow = next;
        // Keep the selection in view by moving it with the rows, as Alt+Tab's list does.
        if (_session.Selected >= 0)
        {
            int col = _session.Selected % Columns;
            int row = Math.Clamp(_session.Selected / Columns, _firstRow, _firstRow + MaxRows - 1);
            _session.Select(Math.Min(_session.Shown.Count - 1, row * Columns + col));
        }
        Draw();
    }

    private Border Card(SwitcherWindow window, int index, bool selected)
    {
        bool live = _wantsPreview(window) && !window.Minimized && !_noThumbnail.Contains(window.Handle);
        var slot = new Border { Height = PreviewHeight, CornerRadius = new CornerRadius(4), ClipToBounds = true };
        slot.SetResourceReference(Border.BackgroundProperty, live ? "WindowBackgroundBrush" : "ChipBrush");
        if (!live)
        {
            var big = Icon(window, 32);
            big.HorizontalAlignment = HorizontalAlignment.Center;
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { big } };
            if (window.Minimized)
            {
                var minimized = new TextBlock { Text = "Minimized", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
                minimized.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                stack.Children.Add(minimized);
            }
            slot.Child = stack;
        }
        _slots.Add((window, slot, live));

        var title = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0, 0, 0) };
        AutomationProperties.SetLiveSetting(title, AutomationLiveSetting.Polite);
        var small = Icon(window, 16);
        var line = new DockPanel { Height = TitleHeight, Children = { small, title } };
        var card = new Border
        {
            Width = CardWidth, Height = CardHeight, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = new DockPanel { Children = { slot, line } }, Cursor = Cursors.Hand,
        };
        DockPanel.SetDock(slot, Dock.Top);
        AutomationProperties.SetName(card, $"{window.Title}, {window.App}");
        card.MouseLeftButtonUp += (_, e) => { Picked(index); e.Handled = true; };
        Restyle(card, title, window, selected);
        _drawn.Add((card, title, window));
        return card;
    }

    /// <summary>The selected card has the accent border and tint; a failed switch says so in its title.</summary>
    private void Restyle(Border card, TextBlock title, SwitcherWindow window, bool selected)
    {
        bool failed = selected && _failed == window.Handle;
        title.Text = failed ? AppSwitcherModule.SwitchFailed : window.Title;
        title.SetResourceReference(TextBlock.ForegroundProperty, failed ? "IslandCautionBrush" : "TextPrimaryBrush");
        double pad = selected ? 5 : 6;
        card.Padding = new Thickness(pad, pad, pad, 0);
        card.BorderThickness = new Thickness(selected ? 2 : 1);
        card.SetResourceReference(Border.BorderBrushProperty, selected ? "AccentBrush" : "IslandBorderBrush");
        if (selected)
            card.Background = Tint("AccentBrush", 0x24);
        else
            card.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
    }

    private Brush Tint(string key, byte alpha)
    {
        var color = TryFindResource(key) is SolidColorBrush s ? s.Color : Colors.MediumPurple;
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    /// <summary>The app's own icon; its first letters only when Windows has none.</summary>
    private static FrameworkElement Icon(SwitcherWindow window, double size)
    {
        string name = window.App.Length > 0 ? window.App : "?";
        var letters = new TextBlock { Text = name[..Math.Min(2, name.Length)], FontSize = size * 0.45, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        letters.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var monogram = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 4), Child = letters };
        monogram.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
        var icon = window.Path.Length > 0 ? AppIcon.View(window.Path, size, monogram) : monogram;
        icon.VerticalAlignment = VerticalAlignment.Center;
        return icon;
    }
}
