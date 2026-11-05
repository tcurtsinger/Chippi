// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.Features.AppSwitcher.Views;
using Chippi.Island.Tests;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.AppSwitcher.Tests;

/// <summary>The overlay and Settings › App switcher against the approved boards (wave-3.html: switcher, sheet-switcher-states, switcher-settings).</summary>
public sealed class SwitcherScreensTests
{
    private static string Sys(string exe) => Path.Combine(Environment.SystemDirectory, exe);

    /// <summary>A dozen windows across two displays, with real program files so the icons are Windows' own.</summary>
    internal static List<SwitcherWindow> Twelve() =>
    [
        new(1, 1, "Notepad", "notes.txt - Notepad", Sys("notepad.exe"), false, 1),
        new(2, 2, "File Explorer", "Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), false, 1),
        new(3, 3, "Calculator", "Calculator", Sys("calc.exe"), false, 1),
        new(4, 4, "Command Prompt", "Command Prompt", Sys("cmd.exe"), false, 2),
        new(5, 5, "Task Manager", "Task Manager", Sys("taskmgr.exe"), true, 1),
        new(6, 6, "Notepad", "November notes.txt - Notepad", Sys("notepad.exe"), false, 2),
        new(7, 7, "Registry Editor", "Registry Editor", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "regedit.exe"), false, 1),
        new(8, 8, "Character Map", "Character Map", Sys("charmap.exe"), false, 2),
        new(9, 9, "System Information", "System Information", Sys("msinfo32.exe"), true, 1),
        new(10, 10, "Control Panel", "All Control Panel Items", Sys("control.exe"), false, 1),
        new(11, 11, "Remote Desktop", "Remote Desktop Connection", Sys("mstsc.exe"), false, 2),
        new(12, 12, "Event Viewer", "Event Viewer (Local)", Sys("eventvwr.exe"), false, 1),
    ];

    private static List<SwitcherWindow> Fifty() =>
        [.. Enumerable.Range(1, 50).Select(i => new SwitcherWindow(i, (uint)i, "Notepad", $"Document {i}.txt - Notepad", Sys("notepad.exe"), i % 9 == 0, 1))];

    private static void Theme()
    {
        if (Application.Current.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString.EndsWith("SettingsStyles.xaml", StringComparison.Ordinal) == true))
            return;
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/Chippi;component/Settings/SettingsStyles.xaml") });
        bool light = WpfTestThread.ShotsTheme == "Light";
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#6B4FBB" : "#B79CFF"));
        Application.Current.Resources["OnAccentBrush"] = light ? Brushes.White : Brushes.Black;
        foreach (var w in Twelve())
            AppIcon.LoadAsync(w.Path, 128).GetAwaiter().GetResult();
    }

    private static (SwitcherBoard Board, List<bool> Filter) Board(SwitcherSession session, double width = SwitcherBoard.Widest, Func<SwitcherWindow, bool>? previews = null)
    {
        var filter = new List<bool>();
        var board = new SwitcherBoard(on => { filter.Add(on); session.ShowCurrentDisplayOnly(on); }, i => session.Select(i));
        board.Show(session, previews ?? (_ => true), width);
        session.Changed += board.Draw;
        return (board, filter);
    }

    [Fact]
    public void Five_cards_across_three_rows_and_the_selection_after_the_window_you_came_from()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var session = new SwitcherSession(Twelve(), original: 1, monitor: 1, currentDisplayOnly: false);
            var (board, _) = Board(session);
            Assert.Equal(5, board.Columns);
            Assert.Equal(3, board.Rows);
            Assert.Equal(12, board.Slots.Count);
            Assert.Equal(SwitcherBoard.Widest, SwitcherBoard.WidthFor(5));
            Assert.Equal(SwitcherBoard.Widest, board.Width);
            Assert.Equal(1, session.Selected);
            // Minimized windows show their icon and say so; the rest are live slots.
            Assert.Equal([5, 9], board.Slots.Where(s => !s.Live).Select(s => (int)s.Window.Handle));
            Assert.Equal(2, Texts(board).Count(t => t == "Minimized"));
            Shot(board, "switcher");
        });
    }

    [Fact]
    public void Typing_searches_titles_and_apps_and_nothing_matching_says_so()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var session = new SwitcherSession(Twelve(), 1, 1, false);
            var (board, _) = Board(session);
            board.Search.Text = "no";
            Assert.Equal("no", session.Query);
            Assert.Equal(["notes.txt - Notepad", "November notes.txt - Notepad"], session.Shown.Select(w => w.Title));
            Assert.Equal(1, board.Rows);
            Assert.Equal(SwitcherBoard.HeightFor(1), SwitcherBoard.HeightFor(board.Rows));
            Shot(board, "switcher-search");
            board.Search.Text = "zoom";
            Assert.Empty(board.Slots);
            Assert.Contains("No results", Texts(board));
            Shot(board, "switcher-empty");
        });
    }

    [Fact]
    public void This_display_lists_only_its_windows_and_keeps_the_choice()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var session = new SwitcherSession(Twelve(), 1, 2, false);
            var (board, filter) = Board(session);
            var segments = Find<SegmentedControl>(board).Single();
            segments.SelectedItem = ((IEnumerable<object>)segments.ItemsSource).Last();
            Assert.Equal([true], filter);
            Assert.All(session.Shown, w => Assert.Equal(2, w.Monitor));
            Assert.Equal(4, board.Slots.Count);
            Shot(board, "switcher-this-display");
        });
    }

    [Fact]
    public void Previews_off_or_an_app_without_previews_shows_the_icon_and_Windows_refusing_one_does_too()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var settings = new AppSwitcherSettings { NoPreview = [Sys("notepad.exe")] };
            var session = new SwitcherSession(Twelve(), 1, 1, false);
            var (board, _) = Board(session, previews: w => SwitcherOverlay.WantsPreview(settings, w));
            Assert.Equal([1, 5, 6, 9], board.Slots.Where(s => !s.Live).Select(s => (int)s.Window.Handle));
            board.NoThumbnail(2);
            Assert.False(board.Slots.Single(s => s.Window.Handle == 2).Live);
            Shot(board, "switcher-no-preview");
            Assert.False(SwitcherOverlay.WantsPreview(new AppSwitcherSettings { Previews = false }, Twelve()[2]));
            Assert.True(SwitcherOverlay.WantsPreview(new AppSwitcherSettings(), Twelve()[2]));
        });
    }

    [Fact]
    public void A_switch_that_fails_says_so_on_its_card_until_the_selection_moves()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var session = new SwitcherSession(Twelve(), 1, 1, false);
            var (board, _) = Board(session);
            board.Failed(session.Selection!);
            Assert.Contains(AppSwitcherModule.SwitchFailed, Texts(board));
            Assert.DoesNotContain("Downloads", Texts(board));
            Shot(board, "switcher-failed");
            session.Move(1);
            Assert.DoesNotContain(AppSwitcherModule.SwitchFailed, Texts(board));
            Assert.Contains("Downloads", Texts(board));
        });
    }

    [Fact]
    public void Fifty_windows_show_three_rows_and_scroll_by_rows_to_keep_the_selection_in_view()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var session = new SwitcherSession(Fifty(), 1, 1, false);
            var (board, _) = Board(session);
            Assert.Equal(15, board.Slots.Count);
            session.Select(42);
            Assert.Contains(board.Slots, s => s.Window.Handle == 43);
            Assert.Equal(15, board.Slots.Count);
            Assert.Equal(31, (int)board.Slots[0].Window.Handle); // rows 7 to 9 of 10
            Shot(board, "switcher-50");
            // Back to the first: the list scrolls up to it.
            session.Select(0);
            Assert.Equal(1, (int)board.Slots[0].Window.Handle);
        });
    }

    [Fact]
    public void A_narrow_display_fits_fewer_cards()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var session = new SwitcherSession(Twelve(), 1, 1, false);
            var (board, _) = Board(session, width: 600);
            Assert.Equal(3, board.Columns);
            Assert.Equal(9, board.Slots.Count);
            Assert.Equal(SwitcherBoard.WidthFor(3), board.Width); // no empty strip at the right
            Shot(board, "switcher-narrow");
        });
    }

    private sealed class PageUi : IModuleUi
    {
        public Func<string?, object>? Page;
        public IIslandTile AddIslandTile(IslandTileSpec spec) => throw new InvalidOperationException("No home tile");
        public void SetSettingsPage(Func<string?, object> page) => Page = page;
        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) { }
    }

    private static (AppSwitcherModule Module, AppSwitcherPage Page, SettingsStore Store, Hotkey Hotkey) StartPage(AppSwitcherSettings settings, Action<string>? editShortcut = null)
    {
        var pc = new FakePc { Open = Twelve(), Front = 1 };
        var store = new SettingsStore { Value = settings };
        var hotkey = new Hotkey();
        var ui = new PageUi();
        var module = new AppSwitcherModule(_ => new FakeView(pc), () => pc, () => new FakeEvents(), () => hotkey);
        module.StartAsync(new ModuleContext(AppSwitcherModule.Id, store, NullLogger.Instance, new IslandActivityHub().CreatePublisher(AppSwitcherModule.Id), ui) { EditShortcut = editShortcut ?? (_ => { }) }, default).GetAwaiter().GetResult();
        return (module, (AppSwitcherPage)ui.Page!(null), store, hotkey);
    }

    [Fact]
    public void The_page_has_the_shortcut_the_display_filter_previews_and_apps_without_previews()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var (module, page, store, _) = StartPage(new AppSwitcherSettings { NoPreview = [Sys("notepad.exe")] });
            Layout(page, 768);
            var texts = Texts(page);
            foreach (var t in new[] { "Opening", "Shortcut", "Not set", "Windows", "Windows from", "Previews", "Apps without previews", "1 app", "Notepad", "notepad.exe" })
                Assert.Contains(t, texts);
            Assert.Contains(Find<Button>(page), b => AutomationProperties.GetName(b) == "Edit App switcher shortcut");
            Assert.DoesNotContain(Find<Button>(page), b => AutomationProperties.GetName(b) is "Record shortcut" or "Change shortcut" or "Remove shortcut");
            Shot(page, "switcher-settings", 768);
            Shot(page, "switcher-settings-narrow", 640);

            var from = Find<ComboBox>(page).Single(c => AutomationProperties.GetName(c) == "Windows from");
            from.SelectedIndex = 1;
            Assert.True(store.Value.CurrentDisplayOnly);
            var previews = Find<ToggleButton>(page).Single(t => AutomationProperties.GetName(t) == "Previews");
            previews.IsChecked = false;
            Assert.False(store.Value.Previews);
            Find<Button>(page).Single(b => AutomationProperties.GetName(b) == "Remove Notepad").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Empty(store.Value.NoPreview);
            module.StopAsync(default).GetAwaiter().GetResult();
        });
    }

    [Fact]
    public void The_shortcut_is_read_only_and_Edit_opens_it_in_Settings_Shortcuts()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var edits = new List<string>();
            var (module, page, store, hotkey) = StartPage(new AppSwitcherSettings(), edits.Add);
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); // follows the module, as in Settings
            Layout(page, 768);
            Assert.Equal("Not set", page.ShortcutText);
            page.ShortcutView.EditButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(["App switcher"], edits);
            // Settings › Shortcuts saves through the module; the page follows.
            var shortcut = new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x4A);
            Assert.True(module.ChangeShortcut(shortcut));
            Assert.Equal(shortcut, store.Value.Shortcut);
            Assert.Equal(shortcut, hotkey.Registered);
            Assert.Equal(shortcut.ToString(), page.ShortcutText);
            Assert.DoesNotContain(Find<Button>(page), b => AutomationProperties.GetName(b) is "Record shortcut" or "Change shortcut" or "Remove shortcut");
            Shot(page, "switcher-settings-set", 768, around: "Opening");
            hotkey.Pressed!();
            Assert.True(module.Switcher!.IsOpen);
            module.Switcher.Dismiss();
            module.RemoveShortcut();
            Assert.Null(store.Value.Shortcut);
            Assert.Equal("Not set", page.ShortcutText);
            module.StopAsync(default).GetAwaiter().GetResult();
        });
    }

    /// <summary>
    /// The real overlay over the test's own windows, with Windows drawing their live previews into it: shown without
    /// activating, on the second display when there is one, captured from the screen and timed. Runs when
    /// <c>CHIPPI_SWITCHER_LIVE</c> names a folder; never takes the foreground (asserted).
    /// </summary>
    [Fact]
    public void Live_overlay_with_previews()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_SWITCHER_LIVE") is not { Length: > 0 } folder)
            return;
        WpfTestThread.Run(() =>
        {
            Theme();
            Directory.CreateDirectory(folder);
            nint before = GetForegroundWindow();
            var colors = new[] { "#2B6CB0", "#2F855A", "#B7791F", "#9B2C2C", "#553C9A", "#285E61", "#97266D", "#4A5568" };
            var sources = new List<Window>();
            var listed = new List<SwitcherWindow>();
            var fixture = Fifty();
            try
            {
                for (int i = 0; i < 50; i++)
                {
                    var face = new Grid { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i % colors.Length])) };
                    face.Children.Add(new TextBlock { Text = $"Document {i + 1}", FontSize = 48, Foreground = Brushes.White, Margin = new Thickness(24) });
                    var w = new Window { Title = fixture[i].Title, Width = 800, Height = 500, Left = -4000, Top = -4000, ShowActivated = false, ShowInTaskbar = false, Content = face };
                    w.Show();
                    if (fixture[i].Minimized) w.WindowState = WindowState.Minimized;
                    sources.Add(w);
                    listed.Add(fixture[i] with { Handle = new WindowInteropHelper(w).Handle, ProcessId = (uint)Environment.ProcessId });
                }
                nint monitor = Monitors().OrderBy(m => m.Primary).First().Handle; // a second display when there is one
                var report = new StringBuilder();
                foreach (int count in new[] { 12, 50 })
                {
                    var pc = new FakePc { Open = [.. listed.Take(count)] };
                    var switcher = new Switcher(pc, new RecentWindows(), s => new SwitcherOverlay(s));
                    var overlay = new SwitcherOverlay(switcher);
                    var session = new SwitcherSession(pc.Open, 0, monitor, false);
                    var clock = Stopwatch.StartNew();
                    ((ISwitcherView)overlay).Show(session, monitor);
                    int live = overlay.Board.Slots.Count(s => s.Live);
                    while (overlay.PreviewCount < live && clock.ElapsedMilliseconds < 3000) Pump();
                    double shown = clock.Elapsed.TotalMilliseconds;
                    report.AppendLine(CultureInfoInvariant($"{count} windows: shown with {overlay.PreviewCount} of {live} previews in {shown:0.0} ms"));
                    Wait(400); // DWM draws the previews on its next frames
                    Capture(overlay, Path.Combine(folder, $"switcher-live-{count}-{WpfTestThread.ShotsTheme.ToLowerInvariant()}.png"));

                    var keys = new List<double>();
                    foreach (string q in new[] { "d", "do", "doc", "docu", "document 4" })
                    {
                        clock.Restart();
                        overlay.Board.Search.Text = q;
                        Pump();
                        keys.Add(clock.Elapsed.TotalMilliseconds);
                    }
                    report.AppendLine(CultureInfoInvariant($"  search keystrokes (draw and previews moved): {string.Join(", ", keys.Select(k => k.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))} ms"));
                    if (count == 50) Capture(overlay, Path.Combine(folder, $"switcher-live-search-{WpfTestThread.ShotsTheme.ToLowerInvariant()}.png"));
                    overlay.Board.Search.Text = "";
                    Pump();
                    var moves = new List<double>();
                    for (int i = 0; i < 10; i++)
                    {
                        clock.Restart();
                        if (i % 2 == 0) session.MoveRow(overlay.Board.Columns, down: true);
                        else session.Move(1);
                        Pump();
                        moves.Add(clock.Elapsed.TotalMilliseconds);
                    }
                    report.AppendLine(CultureInfoInvariant($"  arrow keys: {string.Join(", ", moves.Select(k => k.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))} ms"));
                    ((ISwitcherView)overlay).Hide();
                    Assert.Equal(0, overlay.PreviewCount);
                    overlay.Close();
                }
                File.WriteAllText(Path.Combine(folder, "switcher-live-timings.txt"), report.ToString());
            }
            finally
            {
                foreach (var w in sources) w.Close();
            }
            Assert.Equal(before, GetForegroundWindow());
        });
    }

    private static string CultureInfoInvariant(FormattableString s) => s.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static void Wait(int ms)
    {
        var end = Stopwatch.StartNew();
        while (end.ElapsedMilliseconds < ms) { Pump(); Thread.Sleep(15); }
    }

    private static void Capture(Window window, string file)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        GetWindowRect(hwnd, out var r);
        const int margin = 24;
        int x = r.Left - margin, y = r.Top - margin, w = r.Right - r.Left + 2 * margin, h = r.Bottom - r.Top + 2 * margin;
        nint screen = GetDC(0), dc = CreateCompatibleDC(screen), bitmap = CreateCompatibleBitmap(screen, w, h);
        try
        {
            nint old = SelectObject(dc, bitmap);
            BitBlt(dc, 0, 0, w, h, screen, x, y, 0x00CC0020 | 0x40000000);
            SelectObject(dc, old);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(source));
            using var stream = File.Create(file);
            png.Save(stream);
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(dc);
            ReleaseDC(0, screen);
        }
    }

    private static List<(nint Handle, bool Primary)> Monitors()
    {
        var found = new List<(nint, bool)>();
        EnumDisplayMonitors(0, 0, (m, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            GetMonitorInfo(m, ref info);
            found.Add((m, (info.Flags & 1) != 0));
            return true;
        }, 0);
        return found;
    }

    private static void Layout(FrameworkElement view, double width)
    {
        view.Measure(new Size(width, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, width, view.DesiredSize.Height));
        view.UpdateLayout();
    }

    // Rows draw their header and caption in their template, so read those off the row.
    private static List<string> Texts(DependencyObject root) =>
        [.. Find<TextBlock>(root).Select(t => t.Text), .. Find<SettingRow>(root).SelectMany(r => new[] { r.Header, r.Description })];

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is UIElement { Visibility: not Visibility.Visible }) yield break;
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }

    /// <summary>The view at 2x when CHIPPI_ISLAND_SHOTS names a folder: the board over a plain desktop, a page on Settings' background.</summary>
    private static void Shot(FrameworkElement view, string name, double width = double.NaN, string? around = null)
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        bool board = view is SwitcherBoard;
        double layoutWidth = double.IsNaN(width) ? view.Width : width;
        view.Measure(new Size(layoutWidth, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, layoutWidth, view.DesiredSize.Height));
        view.UpdateLayout();
        double top = 0, h = view.ActualHeight, w = view.ActualWidth;
        if (around is not null && Find<TextBlock>(view).FirstOrDefault(t => t.Text == around) is { } heading)
        {
            top = Math.Max(0, heading.TranslatePoint(new Point(), view).Y - 24);
            h = Math.Min(h - top, 200);
        }
        double pad = board ? 40 : 16;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            Brush back = board ? new SolidColorBrush(WpfTestThread.ShotsTheme == "Light" ? Color.FromRgb(0xC9, 0xD3, 0xDE) : Color.FromRgb(0x1B, 0x22, 0x2C))
                : (Brush)Application.Current.Resources["WindowBackgroundBrush"];
            dc.DrawRectangle(back, null, new Rect(0, 0, w + 2 * pad, h + 2 * pad));
            dc.DrawRectangle(new VisualBrush(view) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, top, w, h) }, null, new Rect(pad, pad, w, h));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling((w + 2 * pad) * 2), (int)Math.Ceiling((h + 2 * pad) * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, $"{name}-{WpfTestThread.ShotsTheme.ToLowerInvariant()}.png"));
        png.Save(file);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

    private delegate bool MonitorProc(nint monitor, nint dc, nint rect, nint data);

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorProc proc, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint dest, int x, int y, int w, int h, nint src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
}
