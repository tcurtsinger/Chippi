// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Chippi.Features.MacDesktop.Shell;
using Chippi.UI.Controls;
using Chippi.UI.Pet;

namespace Chippi.Features.MacDesktop.MenuBar;

/// <summary>
/// One display's menu bar (docs/mac-style/brief.md, Menu bar): 28 DIPs across the top, an AppBar so maximized windows start
/// below it. No fill and no blur: the wallpaper shows through, and the ink follows it. Hidden while a fullscreen app is in
/// front on its display. Never takes the keyboard: the app in front keeps it, so its menus run in it.
/// </summary>
internal sealed class MenuBarWindow : Window
{
    public const double Height28 = 28;

    private readonly MenuBarController _bar;
    private readonly Canvas _root = new() { ClipToBounds = true };
    private readonly StackPanel _left = new() { Orientation = Orientation.Horizontal, Height = Height28 };
    private readonly StackPanel _right = new() { Orientation = Orientation.Horizontal, Height = Height28 };
    private readonly BarItem _mark, _appName, _overflow, _windowMenu;
    private readonly List<BarItem> _menus = [];
    private readonly StackPanel _badges = new() { Orientation = Orientation.Horizontal };
    private readonly BarItem _chevron, _language, _quick, _controls, _clock;
    private readonly TextBlock _languageText = Text(""), _clockText = Text("");
    private readonly LucideIcon _network = Glyph(LucideIcons.Wifi), _volume = Glyph(LucideIcons.Volume);
    private readonly BatteryGlyph _battery = new();
    private readonly ChippiPet _pet = new() { Size = 17, IsDecorative = true, Mood = PetMood.Idle };
    private AppBar? _appBar;
    private nint _hwnd;
    private BarInk _ink = BarInk.Light;
    private ContextMenu? _open;
    private BarItem? _openItem;
    private bool _hidden;

    public nint Monitor { get; }

    /// <summary>The bar in physical pixels, once placed.</summary>
    public Rect Bounds { get; private set; }

    public double Scale { get; private set; } = 1;

    public MenuBarWindow(MenuBarController bar, nint monitor)
    {
        _bar = bar;
        Monitor = monitor;
        Title = "Chippi menu bar";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        // Alpha 1 of 255: invisible, and still the bar's (a fully clear pixel of a layered window clicks through).
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Content = _root;
        _root.Children.Add(_left);
        _root.Children.Add(_right);

        _pet.PetColor = bar.Shell.PetColor;
        _mark = new BarItem(_pet, "Chippi") { Padding = new Thickness(6, 0, 6, 0) };
        _mark.Pressed += () => Toggle(_mark, _bar.ChippiMenu);
        _appName = new BarItem(Text("", FontWeights.SemiBold), "App");
        _appName.Pressed += () => Toggle(_appName, _bar.AppMenu);
        _overflow = new BarItem(Text("»"), "More menus");
        _overflow.Pressed += () => Toggle(_overflow, () => _bar.OverflowMenu(VisibleMenus));
        _windowMenu = new BarItem(Text("Window"), "Window");
        _windowMenu.Pressed += () => Toggle(_windowMenu, _bar.WindowMenu);
        _left.Children.Add(_mark);
        _left.Children.Add(_appName);

        _chevron = new BarItem(Glyph(LucideIcons.Up), "Hidden icons");
        _chevron.Pressed += () => _ = _bar.ToggleHiddenIconsAsync(this, _chevron);
        _language = new BarItem(_languageText, "Input language");
        _language.Pressed += () => _ = _bar.Shell.PressAsync("Win+Space");
        var quick = new StackPanel { Orientation = Orientation.Horizontal, Children = { _network, _volume, _battery } };
        _volume.Margin = new Thickness(6, 0, 6, 0);
        _quick = new BarItem(quick, "Network, volume, battery");
        _quick.Pressed += () => _ = _bar.Shell.PressAsync("Win+A");
        _controls = new BarItem(Glyph(LucideIcons.Sliders), "Controls");
        _controls.Pressed += () => _bar.Shell.OpenNotchHome();
        _clockText.FontFeatures(); // tabular figures
        _clock = new BarItem(_clockText, "Notifications");
        _clock.Pressed += () => _ = _bar.Shell.PressAsync("Win+N");
        foreach (var item in new UIElement[] { _badges, _chevron, _language, _quick, _controls, _clock })
            _right.Children.Add(item);

        SourceInitialized += (_, _) =>
        {
            _hwnd = Native.MakeNoActivate(this, Hook);
            _appBar = AppBar.Register(_hwnd);
            Place();
        };
        Closed += (_, _) => _appBar?.Dispose();
    }

    private int VisibleMenus => _menus.Count(m => m.Visibility == Visibility.Visible);

    private static TextBlock Text(string text, FontWeight? weight = null) =>
        new() { Text = text, FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };

    private static LucideIcon Glyph(Geometry data) => new() { Data = data, Size = 15, VerticalAlignment = VerticalAlignment.Center };

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)msg == AppBar.CallbackMessage)
        {
            if ((uint)wParam == Native.AbnFullscreenApp)
                Dispatcher.BeginInvoke(() => _bar.FullscreenChanged());
            else if ((uint)wParam == Native.AbnPosChanged)
                Dispatcher.BeginInvoke(() => { _appBar?.Invalidate(); Place(); });
        }
        return 0;
    }

    /// <summary>Puts the bar across the top of its display and reserves its strip.</summary>
    public void Place()
    {
        if (_hwnd == 0 || Native.Area(Monitor) is not { } area)
            return;
        Scale = area.Scale;
        double height = Math.Round(Height28 * area.Scale);
        Bounds = new Rect(area.Monitor.Left, area.Monitor.Top, area.Monitor.Width, height);
        if (!_hidden)
            _appBar?.Reserve(ScreenEdge.Top, area.Monitor, height);
        Native.Place(_hwnd, Bounds);
        _root.Width = Bounds.Width / Scale;
        _root.Height = Height28;
        Arrange();
    }

    /// <summary>Hides the bar while a fullscreen app is in front on its display, and gives its strip back meanwhile.</summary>
    public void SetHidden(bool hidden)
    {
        if (hidden == _hidden)
            return;
        _hidden = hidden;
        if (hidden)
        {
            _open?.SetCurrentValue(ContextMenu.IsOpenProperty, false);
            Hide();
        }
        else
        {
            Show();
            Place();
        }
    }

    // ----- What it shows -----

    public void SetInk(BarInk ink)
    {
        _ink = ink;
        var text = new SolidColorBrush(ink == BarInk.Light ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A));
        text.Freeze();
        Foreground = text;
        var shadow = ink == BarInk.Light
            ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.45 }
            : new DropShadowEffect { Color = Colors.White, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.45 };
        shadow.Freeze();
        var hover = new SolidColorBrush(ink == BarInk.Light ? Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0, 0, 0));
        hover.Freeze();
        foreach (var item in Items())
            item.SetInk(text, hover, shadow);
        _battery.Ink = text;
    }

    private IEnumerable<BarItem> Items() =>
        _left.Children.OfType<BarItem>().Concat(_right.Children.OfType<BarItem>()).Concat(_badges.Children.OfType<BarItem>());

    /// <summary>The app in front and its menu titles.</summary>
    public void SetApp(string name, IReadOnlyList<string> menus)
    {
        ((TextBlock)_appName.Content).Text = name;
        AutomationProperties.SetName(_appName, name);
        if (!_menus.Select(m => ((TextBlock)m.Content).Text).SequenceEqual(menus))
        {
            foreach (var old in _menus)
                _left.Children.Remove(old);
            _menus.Clear();
            for (int i = 0; i < menus.Count; i++)
            {
                int index = i;
                var item = new BarItem(Text(menus[i]), menus[i]);
                item.Pressed += () => Toggle(item, () => _bar.AppMenuAt(index));
                _menus.Add(item);
                _left.Children.Insert(2 + i, item);
            }
        }
        _left.Children.Remove(_overflow);
        _left.Children.Remove(_windowMenu);
        _left.Children.Add(_overflow);
        _left.Children.Add(_windowMenu);
        SetInk(_ink);
        Arrange();
    }

    public void SetStatus(StatusReadings status)
    {
        _language.Visibility = status.Language is null ? Visibility.Collapsed : Visibility.Visible;
        _languageText.Text = status.Language ?? "";
        _network.Data = status.Connection == StatusReadings.Network.Wired ? LucideIcons.Network : LucideIcons.Wifi;
        _network.Opacity = status.Connection == StatusReadings.Network.None ? 0.45 : 1;
        _battery.Visibility = status.Battery is null ? Visibility.Collapsed : Visibility.Visible;
        _battery.Level = (status.Battery ?? 100) / 100.0;
        _battery.Charging = status.Charging;
        _quick.ToolTip = status.Battery is { } percent ? $"{percent}%" : null;
        Arrange();
    }

    public void SetClock(string text)
    {
        if (_clockText.Text == text)
            return;
        _clockText.Text = text;
        Arrange();
    }

    /// <summary>Chippi's status badges: each a coloured disc with its glyph, opening its page.</summary>
    public void SetBadges(IReadOnlyList<(string Name, Geometry Glyph, Brush Fill, Brush Ink, Action Open)> badges)
    {
        _badges.Children.Clear();
        foreach (var badge in badges)
        {
            var disc = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = badge.Fill,
                Child = new LucideIcon { Data = badge.Glyph, Size = 11, Brush = badge.Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var item = new BarItem(disc, badge.Name, keepsColor: true) { Padding = new Thickness(5, 0, 5, 0) };
            item.Pressed += badge.Open;
            _badges.Children.Add(item);
        }
        SetInk(_ink);
        Arrange();
    }

    /// <summary>
    /// Lays the two groups out, clear of the notch when it's on this bar: the app's menus that would touch it go into ».
    /// Called again while the notch is dragged, so the items make room before the drop.
    /// </summary>
    public void Arrange()
    {
        if (_root.Width is not > 0)
            return;
        double Width(UIElement element)
        {
            element.Measure(new Size(double.PositiveInfinity, Height28));
            return element.DesiredSize.Width;
        }
        foreach (var menu in _menus)
            menu.Visibility = Visibility.Visible;
        _overflow.Visibility = Visibility.Visible;
        double fixedLeft = Width(_mark) + Width(_appName) + Width(_windowMenu);
        var widths = _menus.Select(m => Width(m)).ToArray();
        double overflow = Width(_overflow);
        double right = Width(_right);
        (double, double)? notch = null;
        if (_bar.Shell.Notch() is { } n && n.Monitor == Monitor && Scale > 0)
            notch = ((n.Left - Bounds.Left) / Scale, (n.Right - Bounds.Left) / Scale);
        var arrangement = MenuBarLayout.Arrange(_root.Width, fixedLeft, widths, overflow, right, notch);
        for (int i = 0; i < _menus.Count; i++)
            _menus[i].Visibility = i < arrangement.MenusShown ? Visibility.Visible : Visibility.Collapsed;
        _overflow.Visibility = arrangement.Overflows(_menus.Count) ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(_left, arrangement.LeftStart);
        Canvas.SetLeft(_right, arrangement.RightEnd - right);
    }

    // ----- Menus -----

    private void Toggle(BarItem item, Func<IReadOnlyList<BarMenuItem>> items)
    {
        if (_open is { IsOpen: true } && _openItem == item)
        {
            _open.IsOpen = false;
            return;
        }
        OpenMenu(item, items);
    }

    private void OpenMenu(BarItem item, Func<IReadOnlyList<BarMenuItem>> items)
    {
        if (_open is { IsOpen: true } open)
        {
            _openItem?.SetOpen(false);
            open.IsOpen = false;
        }
        var list = items();
        if (list.Count == 0)
            return;
        _openItem = item;
        item.SetOpen(true);
        var menu = BarMenu.Open(item, list, closed: () =>
        {
            item.SetOpen(false);
            if (_openItem == item) { _openItem = null; _open = null; }
            _bar.MenuClosed();
        });
        _open = menu;
        // Moving across the titles with a menu open switches menus, as on a Mac. The menu holds the mouse meanwhile.
        menu.PreviewMouseMove += (_, _) =>
        {
            if (_openItem is null || TitleUnder(Native.Cursor()) is not { } other || other == _openItem || !_left.Children.Contains(other))
                return;
            Dispatcher.BeginInvoke(other.Activate);
        };
    }

    private BarItem? TitleUnder(Point physical)
    {
        if (PresentationSource.FromVisual(_root) is null)
            return null;
        var local = _root.PointFromScreen(physical);
        if (local.Y < 0 || local.Y > Height28)
            return null;
        return _left.Children.OfType<BarItem>().FirstOrDefault(item =>
            item.Visibility == Visibility.Visible && item.Pressable
            && new Rect(item.TranslatePoint(new Point(), _root), item.RenderSize).Contains(local));
    }

    /// <summary>A pixel of the bar, for the hidden icons' flyout to hang under.</summary>
    public Point Below(FrameworkElement item)
    {
        var center = item.PointToScreen(new Point(item.ActualWidth / 2, 0));
        return new Point(center.X, Bounds.Bottom);
    }

    /// <summary>One title or status item: 22 tall, 5 corners, the ink's hover.</summary>
    internal sealed class BarItem : Border
    {
        private readonly bool _keepsColor;
        private Brush _hover = Brushes.Transparent;
        private bool _open;

        public event Action? Pressed;

        public bool Pressable => Pressed is not null;

        public UIElement Content => Child;

        public BarItem(UIElement content, string name, bool keepsColor = false)
        {
            _keepsColor = keepsColor;
            Child = content;
            Height = 22;
            Padding = new Thickness(8, 0, 8, 0);
            CornerRadius = new CornerRadius(5);
            Background = Brushes.Transparent;
            VerticalAlignment = VerticalAlignment.Center;
            Margin = new Thickness(1, 0, 1, 0);
            AutomationProperties.SetName(this, name);
            ToolTipService.SetInitialShowDelay(this, 700);
            MouseEnter += (_, _) => Background = _hover;
            MouseLeave += (_, _) => { if (!_open) Background = Brushes.Transparent; };
            MouseLeftButtonDown += (_, e) => { e.Handled = true; Pressed?.Invoke(); };
        }

        public void Activate() => Pressed?.Invoke();

        public void SetOpen(bool open)
        {
            _open = open;
            Background = open || IsMouseOver ? _hover : Brushes.Transparent;
        }

        public void SetInk(Brush text, Brush hover, Effect shadow)
        {
            _hover = hover;
            if (_open) Background = hover;
            Paint(Child, text, shadow);
        }

        private void Paint(UIElement element, Brush text, Effect shadow)
        {
            switch (element)
            {
                case TextBlock block:
                    block.Foreground = text;
                    block.Effect = shadow;
                    break;
                case LucideIcon icon:
                    icon.Brush = text;
                    icon.Effect = shadow;
                    break;
                case BatteryGlyph battery:
                    battery.Ink = text;
                    battery.Effect = shadow;
                    break;
                case Panel panel:
                    foreach (UIElement child in panel.Children)
                        Paint(child, text, shadow);
                    break;
                case Border border when !_keepsColor && border.Child is { } inner:
                    Paint(inner, text, shadow);
                    break;
                case ChippiPet pet:
                    pet.Effect = shadow;
                    break;
            }
        }
    }

    /// <summary>The battery: Lucide's outline with its charge filled in.</summary>
    internal sealed class BatteryGlyph : FrameworkElement
    {
        private Brush _ink = Brushes.White;
        private double _level = 1;
        private bool _charging;

        public BatteryGlyph()
        {
            Width = 22;
            Height = 15;
            VerticalAlignment = VerticalAlignment.Center;
        }

        public Brush Ink { get => _ink; set { _ink = value; InvalidateVisual(); } }
        public double Level { get => _level; set { _level = Math.Clamp(value, 0, 1); InvalidateVisual(); } }
        public bool Charging { get => _charging; set { _charging = value; InvalidateVisual(); } }

        protected override void OnRender(DrawingContext dc)
        {
            var pen = new Pen(_ink, 1.25);
            var body = new Rect(1, 3, 17, 10);
            dc.DrawRoundedRectangle(null, pen, body, 2.5, 2.5);
            dc.DrawLine(new Pen(_ink, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new Point(20.5, 6.5), new Point(20.5, 9.5));
            double fill = Math.Max(1.5, (body.Width - 4) * _level);
            var level = _level <= 0.1 && !_charging ? new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0x57)) : _ink;
            dc.DrawRoundedRectangle(level, null, new Rect(body.Left + 2, body.Top + 2, fill, body.Height - 4), 1, 1);
        }
    }
}

internal static class TextFeatures
{
    /// <summary>Tabular figures, so the clock doesn't shift as its digits change.</summary>
    public static void FontFeatures(this TextBlock text) => System.Windows.Documents.Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
}
