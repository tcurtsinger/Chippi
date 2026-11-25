// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
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
/// below it. No fill and no blur: the wallpaper shows through, and the ink follows it, each group by what's under it. Hidden
/// while a fullscreen app covers its display. The bar itself never takes the keyboard; its menus hold it while they're open.
/// </summary>
internal sealed class MenuBarWindow : Window
{
    public const double Height28 = 28;

    /// <summary>The app's name is never cut shorter than this.</summary>
    private const double LeastName = 48;

    private readonly MenuBarController _bar;
    private readonly Canvas _root = new() { ClipToBounds = true };
    private readonly StackPanel _left = new() { Orientation = Orientation.Horizontal, Height = Height28 };
    private readonly StackPanel _right = new() { Orientation = Orientation.Horizontal, Height = Height28 };
    private readonly BarItem _mark, _appName, _overflow, _windowMenu;
    private readonly TextBlock _nameText = Text("", FontWeights.SemiBold);
    private readonly List<BarItem> _menus = [];
    private readonly StackPanel _badges = new() { Orientation = Orientation.Horizontal };
    private readonly BarItem _chevron, _language, _quick, _controls, _clock;
    private readonly UIElement[] _canGo; // the right group's items that give way on a narrow bar, in the order they go
    private readonly TextBlock _languageText = Text(""), _clockText = Text("");
    private readonly LucideIcon _wired = Glyph(LucideIcons.Network), _volume = Glyph(LucideIcons.Volume);
    private readonly WifiGlyph _wifi = new();
    private readonly BatteryGlyph _battery = new();
    private readonly ChippiPet _pet = new() { Size = 17, IsDecorative = true, Mood = PetMood.Idle };
    private AppBar? _appBar;
    private nint _hwnd;
    private InkStrip _strip;
    private BarInk? _leftInk, _rightInk;
    private (double From, double To) _leftSpan = (0, 0.3), _rightSpan = (0.8, 1);
    private ContextMenu? _open;
    private BarItem? _openItem;
    private bool _hidden, _languageWanted, _appHasWindow, _helpLast;

    public nint Monitor { get; }

    /// <summary>The bar in physical pixels, once placed.</summary>
    public Rect Bounds { get; private set; }

    public double Scale { get; private set; } = 1;

    public MenuBarWindow(MenuBarController bar, nint monitor, InkStrip? ink = null)
    {
        _bar = bar;
        _strip = ink ?? InkStrip.Flat(0);
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
        _mark = new BarItem(_pet, "Chippi", inset: 6);
        _mark.Pressed += () => Toggle(_mark, () => _bar.ChippiMenu(_mark));
        _nameText.TextTrimming = TextTrimming.CharacterEllipsis;
        _appName = new BarItem(_nameText, "App");
        _appName.Pressed += () => Toggle(_appName, () => _bar.AppMenu(_appName));
        _overflow = new BarItem(Text("»"), "More menus");
        _overflow.Pressed += () => Toggle(_overflow, () => _bar.OverflowMenu(VisibleMenus));
        _windowMenu = new BarItem(Text("Window"), "Window");
        _windowMenu.Pressed += () => Toggle(_windowMenu, _bar.WindowMenu);

        _chevron = new BarItem(Glyph(LucideIcons.Up), "Hidden icons", tip: true);
        _chevron.Pressed += () => _ = _bar.ToggleHiddenIconsAsync(this, _chevron);
        _language = new BarItem(_languageText, "Input language");
        _language.Pressed += () => _ = _bar.Shell.PressAsync("Win+Space");
        var quick = new StackPanel { Orientation = Orientation.Horizontal, Children = { _wired, _wifi, _volume, _battery } };
        _volume.Margin = new Thickness(6, 0, 6, 0);
        _quick = new BarItem(quick, "Network, volume, battery");
        _quick.Pressed += () => _ = _bar.ToggleFlyoutAsync(this, _quick, "Win+A");
        _controls = new BarItem(Glyph(LucideIcons.Sliders), "Controls", tip: true);
        _controls.Pressed += () => _bar.Shell.OpenNotchHome();
        _clockText.FontFeatures(); // tabular figures
        _clock = new BarItem(_clockText, "Notifications");
        _clock.Pressed += () => _ = _bar.ToggleFlyoutAsync(this, _clock, "Win+N");
        foreach (var item in new UIElement[] { _badges, _chevron, _language, _quick, _controls, _clock })
            _right.Children.Add(item);
        // Controls is the notch's own home, a click away; the language and the hidden icons have keys of their own (Win+Space,
        // Win+B); Chippi's badges and the network, volume and battery go last. The clock never goes.
        _canGo = [_controls, _language, _chevron, _badges, _quick];
        Order(windowBeforeLast: false);

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
        else if ((uint)msg == Native.WmDpiChanged)
            Dispatcher.BeginInvoke(Place); // the display's scale changed: WPF resized the window by its own rule, the bar's is 28 DIPs
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

    /// <summary>Hides the bar while a fullscreen app covers its display.</summary>
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

    /// <summary>How light the wallpaper is under this bar; each group takes its ink from the part it sits over.</summary>
    public void SetInk(InkStrip strip)
    {
        _strip = strip;
        ApplyInk(force: false);
    }

    /// <summary>The ink each group has now, for the shots and tests.</summary>
    internal (BarInk Left, BarInk Right) Inks => (_leftInk ?? BarInk.Light, _rightInk ?? BarInk.Light);

    private void ApplyInk(bool force)
    {
        var left = _strip.Ink(_leftSpan.From, _leftSpan.To, _leftInk);
        var right = _strip.Ink(_rightSpan.From, _rightSpan.To, _rightInk);
        if (!force && left == _leftInk && right == _rightInk)
            return;
        (_leftInk, _rightInk) = (left, right);
        Paint(_left.Children.OfType<BarItem>(), left);
        Paint(_right.Children.OfType<BarItem>().Concat(_badges.Children.OfType<BarItem>()), right);
    }

    private static void Paint(IEnumerable<BarItem> items, BarInk ink)
    {
        var text = new SolidColorBrush(ink == BarInk.Light ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A));
        text.Freeze();
        var shadow = ink == BarInk.Light
            ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.45 }
            : new DropShadowEffect { Color = Colors.White, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.45 };
        shadow.Freeze();
        var hover = new SolidColorBrush(ink == BarInk.Light ? Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0, 0, 0));
        hover.Freeze();
        foreach (var item in items)
            item.SetInk(text, hover, shadow);
    }

    /// <summary>The app in front and its menu titles.</summary>
    public void SetApp(string name, IReadOnlyList<string> menus)
    {
        _nameText.Text = name;
        AutomationProperties.SetName(_appName, name);
        if (!_menus.Select(m => ((TextBlock)m.Content).Text).SequenceEqual(menus))
        {
            _menus.Clear();
            for (int i = 0; i < menus.Count; i++)
            {
                int index = i;
                var item = new BarItem(Text(menus[i]), menus[i]);
                item.Pressed += () => Toggle(item, () => _bar.AppMenuAt(index));
                _menus.Add(item);
            }
            // An app with a Window menu of its own gets the bar's Window items in it, not a second "Window" beside it.
            _appHasWindow = menus.Any(AppMenus.IsWindowTitle);
            _helpLast = menus.Count > 0 && AppMenus.IsHelpTitle(menus[^1]);
            Order(windowBeforeLast: false);
        }
        ApplyInk(force: true);
        Arrange();
    }

    /// <summary>
    /// The left group's order: the mark, the app's name, its menus, » and Window. Window goes before the app's Help when
    /// every menu shows; with menus in », it's last, where it can always be reached.
    /// </summary>
    private void Order(bool windowBeforeLast)
    {
        var wanted = new List<UIElement> { _mark, _appName };
        wanted.AddRange(_menus);
        wanted.Add(_overflow);
        if (windowBeforeLast && _menus.Count > 0)
            wanted.Insert(wanted.Count - 2, _windowMenu);
        else
            wanted.Add(_windowMenu);
        if (_left.Children.Cast<UIElement>().SequenceEqual(wanted))
            return;
        _left.Children.Clear();
        foreach (var item in wanted)
            _left.Children.Add(item);
    }

    public void SetStatus(StatusReadings status)
    {
        _languageWanted = status.Language is not null;
        _languageText.Text = status.Language ?? "";
        bool wifi = status.Connection != StatusReadings.Network.Wired;
        _wifi.Visibility = wifi ? Visibility.Visible : Visibility.Collapsed;
        _wired.Visibility = wifi ? Visibility.Collapsed : Visibility.Visible;
        _wifi.Bars = status.Connection == StatusReadings.Network.None ? 0 : status.Bars;
        _wifi.Opacity = status.Connection == StatusReadings.Network.None ? 0.45 : 1;
        bool silent = status.Muted || status.Volume == 0;
        _volume.Data = silent ? LucideIcons.VolumeMuted : LucideIcons.Volume;
        _battery.Visibility = status.Battery is null ? Visibility.Collapsed : Visibility.Visible;
        _battery.Level = (status.Battery ?? 100) / 100.0;
        _battery.Charging = status.Charging;
        _quick.ToolTip = StatusText(status);
        Arrange();
    }

    /// <summary>"Wi-Fi · Volume 45% · Battery 64%": what the three glyphs stand for.</summary>
    internal static string StatusText(StatusReadings status)
    {
        var parts = new List<string>
        {
            status.Connection switch { StatusReadings.Network.Wired => "Ethernet", StatusReadings.Network.WiFi => "Wi-Fi", _ => "Offline" },
        };
        if (status.Muted)
            parts.Add("Muted");
        else if (status.Volume is { } volume)
            parts.Add($"Volume {volume}%");
        if (status.Battery is { } battery)
            parts.Add($"Battery {battery}%");
        return string.Join(" · ", parts);
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
            var item = new BarItem(disc, badge.Name, keepsColor: true, inset: 5, tip: true);
            item.Pressed += badge.Open;
            _badges.Children.Add(item);
        }
        ApplyInk(force: true);
        Arrange();
    }

    /// <summary>
    /// Lays the two groups out, clear of the notch when it's on this bar and of each other: the app's menus that don't fit
    /// go into », then the right group's lesser items go, then the app's name is cut short (MenuBarLayout). Called again
    /// while the notch is dragged, so the items make room before the drop.
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
        // Everything as it would be with all the room in the world, to measure.
        foreach (var menu in _menus)
            menu.Visibility = Visibility.Visible;
        _overflow.Visibility = Visibility.Visible;
        _windowMenu.Visibility = _appHasWindow ? Visibility.Collapsed : Visibility.Visible;
        _language.Visibility = _languageWanted ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in _canGo)
            if (item != _language)
                item.Visibility = Visibility.Visible;
        _nameText.MaxWidth = double.PositiveInfinity;
        _mark.Reach(0, 0);
        _clock.Reach(0, 0);

        double name = Width(_nameText);
        double fixedLeft = Width(_mark) + Width(_appName) + Width(_windowMenu);
        var widths = _menus.Select(m => Width(m)).ToArray();
        double overflow = Width(_overflow);
        var canGo = _canGo.Select(Width).ToArray();
        double right = Width(_right);
        (double, double)? notch = null;
        if (_bar.Shell.Notch() is { } n && n.Monitor == Monitor && Scale > 0)
            notch = ((n.Left - Bounds.Left) / Scale, (n.Right - Bounds.Left) / Scale);
        var arrangement = MenuBarLayout.Arrange(_root.Width, fixedLeft, widths, overflow, right, notch, canGo, Math.Max(0, name - LeastName));

        for (int i = 0; i < _menus.Count; i++)
            _menus[i].Visibility = i < arrangement.MenusShown ? Visibility.Visible : Visibility.Collapsed;
        bool overflows = arrangement.Overflows(_menus.Count);
        _overflow.Visibility = overflows ? Visibility.Visible : Visibility.Collapsed;
        Order(windowBeforeLast: _helpLast && !overflows && !_appHasWindow);
        for (int i = 0; i < arrangement.RightDropped; i++)
            _canGo[i].Visibility = Visibility.Collapsed;
        if (arrangement.NameTrim > 0)
            _nameText.MaxWidth = Math.Max(LeastName, name - arrangement.NameTrim);
        // The ends of the bar are the mark's and the clock's: a press in the corner, or on the top row of pixels, lands.
        _mark.Reach(arrangement.LeftStart == MenuBarLayout.Pad ? MenuBarLayout.Pad : 0, 0);
        _clock.Reach(0, arrangement.RightEnd == _root.Width - MenuBarLayout.Pad ? MenuBarLayout.Pad : 0);

        double leftWidth = Width(_left), rightWidth = Width(_right);
        Canvas.SetLeft(_left, arrangement.LeftStart);
        Canvas.SetLeft(_right, arrangement.RightEnd - rightWidth);
        _leftSpan = (arrangement.LeftStart / _root.Width, (arrangement.LeftStart + leftWidth) / _root.Width);
        _rightSpan = ((arrangement.RightEnd - rightWidth) / _root.Width, arrangement.RightEnd / _root.Width);
        ApplyInk(force: false);
    }

    /// <summary>Lays the bar out <paramref name="width"/> DIPs wide without a window, for the offscreen shots and tests.</summary>
    internal Canvas LayOut(double width)
    {
        _root.Width = width;
        _root.Height = Height28;
        Arrange();
        _root.Measure(new Size(width, Height28));
        _root.Arrange(new Rect(0, 0, width, Height28));
        _root.UpdateLayout();
        return _root;
    }

    /// <summary>The two groups' places on the bar, DIPs from its left, as last laid out.</summary>
    internal (Rect Left, Rect Right) Groups => (
        new Rect(Canvas.GetLeft(_left), 0, _left.ActualWidth, Height28),
        new Rect(Canvas.GetLeft(_right), 0, _right.ActualWidth, Height28));

    /// <summary>The titles and status items showing now, left to right, by name.</summary>
    internal IReadOnlyList<string> Showing => [.. _left.Children.OfType<BarItem>().Concat(_right.Children.OfType<UIElement>().SelectMany(
            child => child is Panel panel ? panel.Children.OfType<BarItem>().Where(_ => panel.Visibility == Visibility.Visible) : child is BarItem item ? [item] : []))
        .Where(item => item.Visibility == Visibility.Visible).Select(AutomationProperties.GetName)];

    // ----- Menus -----

    private void Toggle(BarItem item, Func<IReadOnlyList<BarMenuItem>> items)
    {
        // A press on the title whose menu is open closes it. The press itself has already closed the menu by the time it
        // gets here (a click outside a menu does), and the menu is still fading out: so it's this title's menu, open or
        // just closed, and the press must not open it again.
        if (_openItem == item && _open is { } own)
        {
            if (own.IsOpen)
                own.IsOpen = false;
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

    /// <summary>
    /// One title or status item. What's drawn is 22 tall with 5 corners and the ink's hover; what takes the press is the
    /// bar's whole height, so the top row of pixels works, and the first and last reach to the ends of the bar.
    /// </summary>
    internal sealed class BarItem : Border
    {
        private readonly Border _pill;
        private readonly bool _keepsColor;
        private Brush _hover = Brushes.Transparent;
        private bool _open;

        public event Action? Pressed;

        public bool Pressable => Pressed is not null;

        public UIElement Content => _pill.Child;

        /// <param name="inset">The room on each side of the content inside what's drawn.</param>
        /// <param name="tip">A glyph alone: its name shows as a tooltip.</param>
        public BarItem(UIElement content, string name, bool keepsColor = false, double inset = 8, bool tip = false)
        {
            _keepsColor = keepsColor;
            _pill = new Border
            {
                Child = content,
                Height = 22,
                Padding = new Thickness(inset, 0, inset, 0),
                CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Child = _pill;
            Height = Height28;
            Background = Brushes.Transparent; // takes the press where nothing is drawn
            Padding = new Thickness(1, 0, 1, 0);
            AutomationProperties.SetName(this, name);
            if (tip)
                ToolTip = name;
            ToolTipService.SetInitialShowDelay(this, 700);
            ToolTipService.SetPlacement(this, PlacementMode.Bottom);
            MouseEnter += (_, _) => _pill.Background = _hover;
            MouseLeave += (_, _) => { if (!_open) _pill.Background = Brushes.Transparent; };
            MouseLeftButtonDown += (_, e) => { e.Handled = true; Pressed?.Invoke(); };
        }

        public void Activate() => Pressed?.Invoke();

        /// <summary>
        /// Extends what takes the press by <paramref name="left"/> and <paramref name="right"/> DIPs past the item's own
        /// place, into the bar's padding, without moving anything.
        /// </summary>
        public void Reach(double left, double right)
        {
            Margin = new Thickness(-left, 0, -right, 0);
            Padding = new Thickness(1 + left, 0, 1 + right, 0);
        }

        public void SetOpen(bool open)
        {
            _open = open;
            _pill.Background = open || IsMouseOver ? _hover : Brushes.Transparent;
        }

        public void SetInk(Brush text, Brush hover, Effect shadow)
        {
            _hover = hover;
            if (_open || IsMouseOver) _pill.Background = hover;
            Paint(_pill.Child, text, shadow);
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
                case InkGlyph glyph:
                    glyph.Ink = text;
                    glyph.Effect = shadow;
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

        protected override AutomationPeer OnCreateAutomationPeer() => new ItemPeer(this);

        /// <summary>Screen readers and UI Automation see each item as a button with its name that a press invokes.</summary>
        private sealed class ItemPeer(BarItem item) : FrameworkElementAutomationPeer(item), IInvokeProvider
        {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

            protected override string GetClassNameCore() => "MenuBarItem";

            protected override bool IsContentElementCore() => true;

            protected override bool IsControlElementCore() => true;

            public override object? GetPattern(PatternInterface pattern) =>
                pattern == PatternInterface.Invoke && item.Pressable ? this : base.GetPattern(pattern);

            public void Invoke() => item.Activate();
        }
    }

    /// <summary>A glyph the bar draws itself, in the ink of its group.</summary>
    internal abstract class InkGlyph : FrameworkElement
    {
        private Brush _ink = Brushes.White;

        public Brush Ink { get => _ink; set { _ink = value; InvalidateVisual(); } }
    }

    /// <summary>The battery: Lucide's outline with its charge filled in.</summary>
    internal sealed class BatteryGlyph : InkGlyph
    {
        private double _level = 1;
        private bool _charging;

        public BatteryGlyph()
        {
            Width = 22;
            Height = 15;
            VerticalAlignment = VerticalAlignment.Center;
        }

        public double Level { get => _level; set { _level = Math.Clamp(value, 0, 1); InvalidateVisual(); } }
        public bool Charging { get => _charging; set { _charging = value; InvalidateVisual(); } }

        protected override void OnRender(DrawingContext dc)
        {
            var pen = new Pen(Ink, 1.25);
            var body = new Rect(1, 3, 17, 10);
            dc.DrawRoundedRectangle(null, pen, body, 2.5, 2.5);
            dc.DrawLine(new Pen(Ink, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new Point(20.5, 6.5), new Point(20.5, 9.5));
            double fill = Math.Max(1.5, (body.Width - 4) * _level);
            var level = _level <= 0.1 && !_charging ? new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0x57)) : Ink;
            dc.DrawRoundedRectangle(level, null, new Rect(body.Left + 2, body.Top + 2, fill, body.Height - 4), 1, 1);
        }
    }

    /// <summary>Wi-Fi: Lucide's dot and three arcs, the arcs above the signal drawn faint.</summary>
    internal sealed class WifiGlyph : InkGlyph
    {
        private const double Size = 15;
        private static readonly Geometry Dot = Frozen("M12,20 h0.01");
        private static readonly Geometry[] Arcs = [Frozen("M8.5,16.43 a5,5 0 0 1 7,0"), Frozen("M5,12.86 a10,10 0 0 1 14,0"), Frozen("M2,8.82 a15,15 0 0 1 20,0")];
        private int? _bars;

        public WifiGlyph()
        {
            Width = Height = Size;
            VerticalAlignment = VerticalAlignment.Center;
        }

        /// <summary>The signal, 0 to 4 as Windows counts it; null (unknown) draws it full.</summary>
        public int? Bars { get => _bars; set { _bars = value; InvalidateVisual(); } }

        /// <summary>How many of the three arcs are lit for a signal of <paramref name="bars"/>: the dot is the first bar.</summary>
        public static int Lit(int? bars) => bars is { } known ? Math.Clamp(known - 1, 0, 3) : 3;

        protected override void OnRender(DrawingContext dc)
        {
            // Authored in Lucide's 24 box; the stroke is 30 / size there, so it draws 1.25 like the other glyphs.
            var pen = new Pen(Ink, 30 / Size) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.PushTransform(new ScaleTransform(Size / 24, Size / 24));
            dc.DrawGeometry(null, pen, Dot);
            int lit = Lit(_bars);
            for (int i = 0; i < Arcs.Length; i++)
            {
                if (i >= lit) dc.PushOpacity(0.3);
                dc.DrawGeometry(null, pen, Arcs[i]);
                if (i >= lit) dc.Pop();
            }
            dc.Pop();
        }

        private static Geometry Frozen(string data)
        {
            var geometry = Geometry.Parse(data);
            geometry.Freeze();
            return geometry;
        }
    }
}

internal static class TextFeatures
{
    /// <summary>Tabular figures, so the clock doesn't shift as its digits change.</summary>
    public static void FontFeatures(this TextBlock text) => System.Windows.Documents.Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
}
