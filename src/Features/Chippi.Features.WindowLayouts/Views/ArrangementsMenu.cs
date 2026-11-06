// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Chippi.Core.Actions;
using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.WindowLayouts.Views;

/// <summary>Pieces the island's Arrangements pages share: their text, glyphs and buttons in the island's styles.</summary>
internal static class IslandBits
{
    public static TextBlock Text(string text, double size = 12, string brush = "TextPrimaryBrush")
    {
        var block = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    public static LucideIcon Glyph(Geometry icon, double size = 16, string brush = "TextPrimaryBrush")
    {
        var glyph = new LucideIcon { Data = icon, Size = size, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, brush);
        return glyph;
    }

    public static Button Button(string text, bool accent = false)
    {
        var button = new Button { Content = text, Height = 28, MinWidth = 64, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(8, 0, 0, 0) };
        button.SetResourceReference(FrameworkElement.StyleProperty, accent ? "IslandAccentButtonStyle" : "IslandButtonStyle");
        AutomationProperties.SetName(button, text);
        return button;
    }

    public static Button Icon(Geometry icon, string name)
    {
        var button = new Button { Content = Glyph(icon, 14), Width = 24, Height = 24 };
        button.SetResourceReference(FrameworkElement.StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>The display a panel is on, for a launch from it: a missing display's windows go there.</summary>
    public static string? DisplayOf(Visual surface, WindowLayoutsModule module)
    {
        if (PresentationSource.FromVisual(surface) is not HwndSource { Handle: not 0 } source) return null;
        nint monitor = MonitorFromWindow(source.Handle, 2);
        return module.Displays.FirstOrDefault(d => d.Handle == monitor)?.Id;
    }

    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
}

/// <summary>
/// Actions › Snap › Arrangements (arrangements.html, sheet-arrangements-island): the saved arrangements two across, as
/// Actions lists are; pointing at one shows Launch, and pressing it launches. Manage opens Settings › Snap.
/// </summary>
internal sealed class ArrangementList : UserControl
{
    private const int Rows = 5, PerPage = Rows * 2;
    private readonly WindowLayoutsModule _module;
    private readonly Action _launched;
    private int _page;

    public ArrangementList(WindowLayoutsModule module, Action launched)
    {
        _module = module;
        _launched = launched;
        Follow(true);
        Loaded += (_, _) => { Follow(true); Render(); };
        Unloaded += (_, _) => Follow(false);
        Render();
    }

    private bool _queued;


    private bool _following;

    /// <summary>Follows the run from the start, before it's first shown, and stops once it's closed.</summary>
    private void Follow(bool on)
    {
        if (_following == on) return;
        _following = on;
        if (on) _module.ArrangementsChanged += Queue;
        else _module.ArrangementsChanged -= Queue;
    }

    private void Queue()
    {
        if (_queued) return;
        _queued = true;
        Dispatcher.BeginInvoke(() => { _queued = false; Render(); });
    }

    /// <summary>"12 windows", or with its one display's name: "3 windows · Built-in display".</summary>
    internal static string Caption(SavedArrangement a)
    {
        string count = a.Rows.Count == 1 ? "1 window" : $"{a.Rows.Count} windows";
        return a.Rows.Select(r => r.DisplayId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? $"{count} · {a.Rows[0].DisplayName}" : count;
    }

    private void Render()
    {
        var saved = (_module.Layouts?.Settings.Arrangements ?? []).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var grid = new Grid { Width = IslandPage.BodyWidth, Height = IslandPage.BodyHeight };
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (saved.Count == 0)
        {
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Children = { IslandBits.Glyph(LucideIcons.LayoutPanelLeft, 24, "IslandTextSecondaryBrush"), IslandBits.Text("No arrangements", 12, "IslandTextSecondaryBrush") } };
            ((FrameworkElement)empty.Children[1]).Margin = new Thickness(0, 10, 0, 0);
            ((FrameworkElement)empty.Children[0]).HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(empty);
        }
        else
        {
            int pages = (saved.Count + PerPage - 1) / PerPage;
            _page = Math.Clamp(_page, 0, pages - 1);
            var cells = new Grid();
            cells.ColumnDefinitions.Add(new ColumnDefinition());
            cells.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < Rows; r++) cells.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
            var shown = saved.Skip(_page * PerPage).Take(PerPage).ToList();
            for (int i = 0; i < shown.Count; i++)
            {
                var row = Row(shown[i]);
                Grid.SetColumn(row, i / Rows);
                Grid.SetRow(row, i % Rows);
                row.Margin = new Thickness(i / Rows == 0 ? 0 : 4, 0, i / Rows == 0 ? 4 : 0, 0);
                cells.Children.Add(row);
            }
            grid.Children.Add(cells);
        }
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var manage = IslandBits.Button("Manage");
        manage.Click += (_, _) => _module.OpenArrangementSettings();
        DockPanel.SetDock(manage, Dock.Right);
        footer.Children.Add(manage);
        if (_module.ArrangementRefused is { } refused && _module.ArrangementRun is { } run)
        {
            var line = IslandBits.Text($"Not launched · {run.Name} · {refused}", 12, "IslandCautionBrush");
            AutomationProperties.SetLiveSetting(line, AutomationLiveSetting.Polite);
            footer.Children.Add(line);
        }
        else footer.Children.Add(new Border());
        Grid.SetRow(footer, 1);
        grid.Children.Add(footer);
        Content = grid;
    }

    private Button Row(SavedArrangement arrangement)
    {
        bool running = _module.ArrangementRun?.Id == arrangement.Id && ArrangementSummary.Of(_module) is { Running: true };
        var summary = running ? ArrangementSummary.Of(_module) : null;
        var name = IslandBits.Text(arrangement.Name);
        var caption = IslandBits.Text(summary is not null ? $"Opening · {summary.Ready} of {summary.Total} ready" : Caption(arrangement), 11, "IslandTextTertiaryBrush");
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0), Children = { name, caption } };
        var verb = IslandBits.Text(running ? "Show" : "Launch", 12);
        verb.FontWeight = FontWeights.SemiBold;
        verb.Visibility = Visibility.Hidden;
        DockPanel.SetDock(verb, Dock.Right);
        var dock = new DockPanel { LastChildFill = true, Children = { verb, IslandBits.Glyph(LucideIcons.LayoutPanelLeft), labels } };
        DockPanel.SetDock(dock.Children[1], Dock.Left);
        var button = new Button { Content = dock, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 4, 10, 4), Height = 44 };
        button.SetResourceReference(StyleProperty, "IslandListRowStyle");
        AutomationProperties.SetName(button, running ? $"Show {arrangement.Name}" : $"Launch {arrangement.Name}");
        AutomationProperties.SetHelpText(button, caption.Text);
        void Lit() => verb.Visibility = button.IsMouseOver || button.IsKeyboardFocused ? Visibility.Visible : Visibility.Hidden;
        button.MouseEnter += (_, _) => Lit();
        button.MouseLeave += (_, _) => Lit();
        button.GotKeyboardFocus += (_, _) => Lit();
        button.LostKeyboardFocus += (_, _) => Lit();
        button.Click += (_, _) => Launch(arrangement.Id, running);
        return button;
    }

    private async void Launch(string id, bool running)
    {
        if (running) { _launched(); return; }
        try
        {
            var launch = _module.LaunchArrangementAsync(id, IslandBits.DisplayOf(this, _module));
            _launched();
            await launch;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warn("Snap: launching an arrangement from the island failed", ex);
            Render();
        }
    }
}

/// <summary>
/// A run of an arrangement, named after it (arrangements.html, sheet-arrangements-island): the window rows in Actions' two
/// columns of five, each with its state and reason; problems get a Caution glyph as well as the word. Once the run ends,
/// problems and guesses sort first. Cancel while it runs; Dismiss, then Retry for failed rows or Launch after a stop.
/// </summary>
internal sealed class ArrangementRunPage : UserControl
{
    private const int Rows = 5, PerPage = Rows * 2;
    private readonly WindowLayoutsModule _module;
    private readonly Action _dismissed;
    private int _page;
    private bool _queued;

    public ArrangementRunPage(WindowLayoutsModule module, Action dismissed)
    {
        _module = module;
        _dismissed = dismissed;
        Follow(true);
        Loaded += (_, _) => { Follow(true); Render(); };
        Unloaded += (_, _) => Follow(false);
        Render();
    }


    private bool _following;

    /// <summary>Follows the run from the start, before it's first shown, and stops once it's closed.</summary>
    private void Follow(bool on)
    {
        if (_following == on) return;
        _following = on;
        if (on) _module.ArrangementsChanged += Queue;
        else _module.ArrangementsChanged -= Queue;
    }

    private void Queue()
    {
        if (_queued) return;
        _queued = true;
        Dispatcher.BeginInvoke(() => { _queued = false; Render(); });
    }

    /// <summary>Its state and why: "Ready · On DELL U2723QE", "Skipped · Needs admin", "Launching failed · Timed out".</summary>
    internal static string StateLine(ArrangementRowResult r) => r.State switch
    {
        ArrangementRowState.Ready when r.SubstituteDisplay is { } other => $"Ready · On {other}",
        ArrangementRowState.Ready when r.Guessed => "Ready · Guessed",
        ArrangementRowState.Skipped => r.Reason is { Length: > 0 } why ? $"Skipped · {why}" : "Skipped",
        ArrangementRowState.Failed => (r.Reason ?? "Failed").Replace(": ", " · ", StringComparison.Ordinal),
        _ => r.State.ToString(),
    };

    private static int Order(ArrangementRowResult r) => r.State switch
    {
        ArrangementRowState.Failed or ArrangementRowState.Skipped => 0,
        ArrangementRowState.Cancelled => 1,
        ArrangementRowState.Ready when r.Guessed || r.SubstituteDisplay is not null => 2,
        _ => 3,
    };

    /// <summary>The rows as the page lists them: in the arrangement's order while it runs, problems first once it's done.</summary>
    internal IReadOnlyList<ArrangementRowResult> Listed()
    {
        var summary = ArrangementSummary.Of(_module);
        IReadOnlyList<ArrangementRowResult> results = _module.ArrangementsPreparing || _module.Arrangements is null
            ? [.. (_module.ArrangementRun?.Rows ?? []).Select(r => new ArrangementRowResult(r))]
            : _module.Arrangements.Results;
        return summary.Running ? results : [.. results.Select((r, i) => (r, i)).OrderBy(x => Order(x.r)).ThenBy(x => x.i).Select(x => x.r)];
    }

    private void Render()
    {
        var summary = ArrangementSummary.Of(_module);
        var listed = Listed();
        int pages = Math.Max(1, (listed.Count + PerPage - 1) / PerPage);
        _page = Math.Clamp(_page, 0, pages - 1);
        var grid = new Grid { Width = IslandPage.BodyWidth, Height = IslandPage.BodyHeight };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // The run's line, and the pager when it has more than ten windows.
        var top = new DockPanel { MinHeight = 24, Margin = new Thickness(0, 0, 0, 6) };
        if (pages > 1)
        {
            int start = _page * PerPage, shownCount = Math.Min(PerPage, listed.Count - start);
            var previous = IslandBits.Icon(LucideIcons.ChevronLeft, "Previous windows");
            previous.IsEnabled = _page > 0;
            previous.Click += (_, _) => { _page--; Render(); };
            var next = IslandBits.Icon(LucideIcons.ChevronRight, "Next windows");
            next.IsEnabled = _page < pages - 1;
            next.Click += (_, _) => { _page++; Render(); };
            var range = IslandBits.Text($"{start + 1}–{start + shownCount} of {listed.Count}", 11, "IslandTextTertiaryBrush");
            range.Margin = new Thickness(0, 0, 6, 0);
            var pager = new StackPanel { Orientation = Orientation.Horizontal, Children = { range, previous, next } };
            DockPanel.SetDock(pager, Dock.Right);
            top.Children.Add(pager);
        }
        var status = IslandBits.Text(summary.Status, 12, summary.Attention ? "IslandCautionBrush" : "IslandTextSecondaryBrush");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        top.Children.Add(status);
        grid.Children.Add(top);

        var cells = new Grid();
        cells.ColumnDefinitions.Add(new ColumnDefinition());
        cells.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < Rows; r++) cells.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        var shown = listed.Skip(_page * PerPage).Take(PerPage).ToList();
        for (int i = 0; i < shown.Count; i++)
        {
            var row = RowView(shown[i]);
            Grid.SetColumn(row, i / Rows);
            Grid.SetRow(row, i % Rows);
            row.Margin = new Thickness(i / Rows == 0 ? 10 : 18, 0, i / Rows == 0 ? 18 : 10, 0);
            cells.Children.Add(row);
        }
        Grid.SetRow(cells, 1);
        grid.Children.Add(cells);

        // A missing display's name, then the verbs.
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        if (summary.Running)
        {
            var cancel = IslandBits.Button("Cancel");
            cancel.Click += (_, _) => _module.CancelArrangement();
            buttons.Children.Add(cancel);
        }
        else
        {
            var dismiss = IslandBits.Button("Dismiss");
            dismiss.Click += (_, _) => { _module.DismissArrangement(); _dismissed(); };
            buttons.Children.Add(dismiss);
            var results = _module.Arrangements?.Results ?? [];
            if (summary.Cancelled > 0 && _module.ArrangementRun is { } run)
            {
                var launch = IslandBits.Button("Launch", accent: true);
                launch.Click += async (_, _) =>
                {
                    try { await _module.LaunchArrangementAsync(run.Id, IslandBits.DisplayOf(this, _module)); }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { Log.Warn("Snap: launching an arrangement again failed", ex); Render(); }
                };
                buttons.Children.Add(launch);
            }
            else if (results.Any(r => r.State == ArrangementRowState.Failed))
            {
                var retry = IslandBits.Button("Retry", accent: true);
                retry.Click += (_, _) => _ = _module.RetryArrangementAsync();
                buttons.Children.Add(retry);
            }
        }
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        var missing = listed.Where(r => r.SubstituteDisplay is not null).Select(r => r.Row.DisplayName).Distinct().ToList();
        footer.Children.Add(missing.Count == 0 ? new Border() : IslandBits.Text($"{string.Join(", ", missing)} not connected", 11, "IslandTextSecondaryBrush"));
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);
        Content = grid;
    }

    private static FrameworkElement RowView(ArrangementRowResult r)
    {
        var icon = ArrangementsSection.Icon(r.Row, 16);
        icon.Margin = new Thickness(0, 0, 10, 0);
        bool problem = r.State is ArrangementRowState.Failed or ArrangementRowState.Skipped;
        string title = r.Row.TitleHint.Length > 0 ? r.Row.TitleHint : r.Row.App;
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            Children = { IslandBits.Text(title), IslandBits.Text(StateLine(r), 11, problem ? "IslandCautionBrush" : "IslandTextTertiaryBrush") } };
        FrameworkElement? mark = r.State switch
        {
            ArrangementRowState.Ready => IslandBits.Glyph(LucideIcons.Check, 16, "SuccessBrush"),
            ArrangementRowState.Failed or ArrangementRowState.Skipped => IslandBits.Glyph(LucideIcons.TriangleAlert, 16, "IslandCautionBrush"),
            ArrangementRowState.Opening or ArrangementRowState.Placing => Spinner(),
            _ => null,
        };
        var dock = new DockPanel { LastChildFill = true };
        if (mark is not null) { DockPanel.SetDock(mark, Dock.Right); dock.Children.Add(mark); }
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        dock.Children.Add(labels);
        AutomationProperties.SetName(dock, $"{title}, {StateLine(r)}");
        return dock;
    }

    /// <summary>A three-quarter ring that turns while a window opens or is placed; still when Windows' animations are off.</summary>
    private static FrameworkElement Spinner()
    {
        var arc = new Path { Width = 14, Height = 14, StrokeThickness = 1.75, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Parse("M 7,1.25 A 5.75,5.75 0 1 1 1.25,7"), VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5) };
        arc.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        var turn = new RotateTransform();
        arc.RenderTransform = turn;
        if (SystemParameters.ClientAreaAnimation)
            turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
        return new Border { Width = 16, Height = 16, Child = arc };
    }
}
