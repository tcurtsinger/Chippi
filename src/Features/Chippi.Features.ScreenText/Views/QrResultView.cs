// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.ScreenText.Views;

/// <summary>
/// The island's QR code page (Capture results v3): the code as it was in the capture on the left, and what it says on
/// the right, in full and selectable. Several codes show one at a time, switched in the header. A link opens only from
/// Open link, with its host shown first; every other payload has Copy only.
/// </summary>
public sealed class QrResultView : UserControl
{
    internal const string NotFound = "No QR code found";
    internal const string Unreadable = "Unreadable QR code";
    internal const string CheckAddress = "Check the address";
    internal const string Hidden = "••••••••";

    private readonly IQrPageModel _model;
    private readonly Button _previous = Bits.Icon(LucideIcons.ChevronLeft, "Previous code");
    private readonly Button _next = Bits.Icon(LucideIcons.ChevronRight, "Next code");
    private readonly TextBlock _place = Bits.Text("", 11, "IslandTextTertiaryBrush");
    private readonly StackPanel _pager;
    private readonly Border _code = new() { Width = Bits.CaptureWidth, Height = Bits.CaptureWidth, CornerRadius = new CornerRadius(4), Padding = new Thickness(8), Background = Brushes.White, VerticalAlignment = VerticalAlignment.Top };
    private readonly StackPanel _what = new();
    private readonly Border _card;
    private readonly Grid _split;
    private readonly ContentControl _message = new() { Focusable = false };
    private readonly TextBlock _status = Bits.Text("", 11, "IslandTextTertiaryBrush");
    private readonly Button _copy = Bits.Button("Copy");
    private readonly Button _open = Bits.Button("Open link", accent: true, glyph: LucideIcons.ExternalLink);
    private readonly Button _reselect = Bits.Button("Reselect");
    private readonly DispatcherTimer _copiedFor = new() { Interval = TimeSpan.FromSeconds(3) };
    private IReadOnlyList<QrCodeResult>? _codes;
    private int _index;
    private bool _revealed;
    private string? _copied;

    public QrResultView(IQrPageModel model)
    {
        _model = model;
        _place.Margin = new Thickness(2, 0, 2, 0);
        _pager = new StackPanel { Orientation = Orientation.Horizontal, Children = { _previous, _place, _next } };
        Header = Bits.Header("QR code", _pager);
        _previous.Click += (_, _) => Go(-1);
        _next.Click += (_, _) => Go(1);

        _card = Bits.Card(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Content = _what }, new Thickness(10, 12, 10, 12));
        _card.VerticalAlignment = VerticalAlignment.Top;
        _split = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(Bits.CaptureWidth) },
                new ColumnDefinition { Width = new GridLength(12) },
                new ColumnDefinition(),
            },
            Children = { _code, _card },
        };
        Grid.SetColumn(_card, 2);

        _copy.Click += (_, _) => Copy();
        _open.Click += (_, _) => { if (Current?.Link is { } link) _model.Open(link); };
        _reselect.Click += (_, _) => _model.Reselect();
        _copiedFor.Tick += (_, _) => { _copiedFor.Stop(); _copied = null; ShowStatus(); };
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { _reselect, _copy, _open } };
        DockPanel.SetDock(buttons, Dock.Right);
        var foot = new DockPanel { Height = Bits.FootHeight, LastChildFill = true, Children = { buttons, _status } };

        var body = new Grid
        {
            Width = IslandPage.BodyWidth,
            Height = IslandPage.BodyHeight,
            RowDefinitions = { new RowDefinition(), new RowDefinition { Height = new GridLength(12) }, new RowDefinition { Height = GridLength.Auto } },
            Children = { _split, _message, foot },
        };
        Grid.SetRow(foot, 2);
        Content = body;

        _model.Changed += () => { if (Dispatcher.CheckAccess()) Refresh(); else Dispatcher.BeginInvoke(Refresh); };
        Refresh();
    }

    /// <summary>The page's header: "QR code", and "2 of 3" with its arrows when there are several.</summary>
    public DockPanel Header { get; }

    internal QrCodeResult? Current => _codes is { Count: > 0 } codes ? codes[Math.Clamp(_index, 0, codes.Count - 1)] : null;

    internal Button PreviousButton => _previous;

    internal Button NextButton => _next;

    internal FrameworkElement Pager => _pager;

    internal Button CopyButton => _copy;

    internal Button OpenButton => _open;

    internal Button ReselectButton => _reselect;

    internal FrameworkElement Message => _message;

    internal FrameworkElement Result => _split;

    internal string Status => _status.Text;

    private void Refresh()
    {
        if (!ReferenceEquals(_codes, _model.Codes))
        {
            _codes = _model.Codes;
            _index = 0;
            _revealed = false;
            _copied = null;
        }
        Show();
    }

    private void Go(int step)
    {
        _index = Math.Clamp(_index + step, 0, (_codes?.Count ?? 1) - 1);
        _revealed = false;
        _copied = null;
        Show();
    }

    private void Show()
    {
        var state = _model.State;
        var code = state == QrScanState.Read ? Current : null;
        int count = _codes?.Count ?? 0;
        _pager.Visibility = code is not null && count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _place.Text = $"{(_index + 1).ToString(CultureInfo.CurrentCulture)} of {count.ToString(CultureInfo.CurrentCulture)}";
        AutomationProperties.SetName(_pager, $"Code {_place.Text}");
        _previous.IsEnabled = _index > 0;
        _next.IsEnabled = _index < count - 1;

        _split.Visibility = code is null ? Visibility.Collapsed : Visibility.Visible;
        _message.Visibility = code is null ? Visibility.Visible : Visibility.Collapsed;
        _message.Content = code is not null ? null
            : state == QrScanState.Unreadable ? Bits.Message(LucideIcons.TriangleAlert, Unreadable, caution: true)
            : Bits.Message(LucideIcons.QrCode, NotFound);
        _reselect.Visibility = code is null ? Visibility.Visible : Visibility.Collapsed;
        _copy.Visibility = code is null ? Visibility.Collapsed : Visibility.Visible;
        _open.Visibility = code?.Link is null ? Visibility.Collapsed : Visibility.Visible;
        // The main verb takes the accent: Open link for a link, otherwise Copy.
        _copy.SetResourceReference(StyleProperty, code?.Link is null ? "IslandAccentButtonStyle" : "IslandButtonStyle");
        ShowStatus();
        if (code is null) return;

        _code.Child = code.Image is { } image
            ? new Image { Source = image, Stretch = Stretch.Uniform }
            : Bits.Glyph(LucideIcons.QrCode, 48, "IslandTextTertiaryBrush");
        if (code.Image is null) _code.SetResourceReference(Border.BackgroundProperty, "IslandPanelBrush");
        else _code.Background = Brushes.White; // a code reads on white, as it was printed
        if (_code.Child is Image shown) RenderOptions.SetBitmapScalingMode(shown, BitmapScalingMode.NearestNeighbor);
        AutomationProperties.SetName(_code, "The code as it was captured");

        _what.Children.Clear();
        string kind = KindName(code.Kind);
        _what.Children.Add(Bits.Text(kind, 11, "IslandTextTertiaryBrush"));
        if (code.Fields.Count == 0)
            _what.Children.Add(Selectable(code.Payload, kind, 13, new Thickness(0, 4, 0, 0)));
        else
            foreach (var field in code.Fields)
                _what.Children.Add(Field(field));
        if (code.Link is { } link)
            _what.Children.Add(Host(link, code.CheckAddress));
    }

    /// <summary>Wi-Fi's Name, Security and Password, a contact's lines: the name over its value.</summary>
    private FrameworkElement Field(QrField field)
    {
        var name = Bits.Text(field.Name, 11, "IslandTextSecondaryBrush");
        name.Margin = new Thickness(0, 8, 0, 0);
        var panel = new StackPanel { Children = { name } };
        if (!field.Secret)
        {
            panel.Children.Add(Selectable(field.Value, field.Name, 13, new Thickness(0, 2, 0, 0)));
            return panel;
        }
        var value = _revealed ? (FrameworkElement)Selectable(field.Value, field.Name, 13, new Thickness(0)) : Bits.Text(Hidden, 13);
        if (!_revealed) AutomationProperties.SetName(value, $"{field.Name} hidden");
        var show = new Button { Content = _revealed ? "Hide" : "Show", Height = 24, MinWidth = 0, Padding = new Thickness(8, 0, 8, 0), FontSize = 11, Margin = new Thickness(8, 0, 0, 0) };
        show.SetResourceReference(StyleProperty, "IslandButtonStyle");
        AutomationProperties.SetName(show, $"{(_revealed ? "Hide" : "Show")} {field.Name.ToLowerInvariant()}");
        show.Click += (_, _) => { _revealed = !_revealed; Show(); };
        DockPanel.SetDock(show, Dock.Right);
        panel.Children.Add(new DockPanel { Margin = new Thickness(0, 2, 0, 0), LastChildFill = true, Children = { show, value } });
        return panel;
    }

    /// <summary>The link's host, emphasized, under the address; "Check the address" beside a host that may not be what it looks like.</summary>
    private static FrameworkElement Host(Uri link, bool check)
    {
        var glyph = Bits.Glyph(LucideIcons.Link, 12);
        glyph.Margin = new Thickness(0, 0, 6, 0);
        var host = Bits.Text(link.Host, 12, "IslandTextBrush");
        host.FontWeight = FontWeights.SemiBold;
        var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0), Children = { glyph, host } };
        if (check)
        {
            var caution = Bits.Text(CheckAddress, 11, "IslandCautionBrush");
            caution.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(caution);
        }
        AutomationProperties.SetName(row, check ? $"Opens {link.Host}. {CheckAddress}" : $"Opens {link.Host}");
        return row;
    }

    /// <summary>Text that reads as text but can be selected and copied in part: a borderless, read-only box.</summary>
    private static TextBox Selectable(string text, string name, double size, Thickness margin)
    {
        var box = new TextBox
        {
            // The box's text sits 2 in from its edge; pulled back so it lines up with the label above it.
            Text = text, IsReadOnly = true, IsReadOnlyCaretVisible = false, TextWrapping = TextWrapping.Wrap, FontSize = size,
            Margin = new Thickness(margin.Left - 2, margin.Top, margin.Right, margin.Bottom), IsInactiveSelectionHighlightEnabled = true,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0),
        };
        box.SetResourceReference(Control.ForegroundProperty, "IslandTextBrush");
        box.SetResourceReference(TextBoxBase.SelectionBrushProperty, "IslandAccentBrush");
        box.SetResourceReference(Control.FontFamilyProperty, "UiFont");
        TextBlock.SetLineHeight(box, 18);
        AutomationProperties.SetName(box, name);
        return box;
    }

    private void Copy()
    {
        if (Current is not { } code) return;
        _copied = _model.Copy(code.Payload) ? "Copied" : "Copy failed";
        _copiedFor.Stop();
        if (_copied == "Copied") _copiedFor.Start();
        ShowStatus();
    }

    private void ShowStatus()
    {
        _status.Text = _copied ?? "";
        _status.SetResourceReference(TextBlock.ForegroundProperty, _copied == "Copy failed" ? "IslandCautionBrush" : "IslandTextTertiaryBrush");
    }

    internal static string KindName(QrKind kind) => kind switch
    {
        QrKind.WiFi => "Wi-Fi",
        _ => kind.ToString(),
    };
}
