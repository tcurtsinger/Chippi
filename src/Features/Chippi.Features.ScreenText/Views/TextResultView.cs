// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.ScreenText.Views;

/// <summary>
/// The island's Text page (Capture results v3): the capture on the left and the text it read, editable, on the right.
/// The lines the caret or the selection touches light up on the capture, while the text is as it was read. Copy all
/// copies the text as edited; Ctrl+C copies a selection, as in any text box. Nothing is copied until Travis asks.
/// </summary>
public sealed class TextResultView : UserControl
{
    internal const string NoText = "No text found";
    internal const string NoLanguage = "No OCR language installed";
    internal const string NoLanguageFix = "Add one in Windows Settings.";
    internal const string Failed = "Text recognition failed";
    internal const string CopyFailed = "Copy failed";
    internal const string Reading = "Reading text";

    private readonly ITextPageModel _model;
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly Canvas _marks = new() { IsHitTestVisible = false };
    private readonly Grid _picture = new();
    private readonly Viewbox _fit;
    private readonly TextBox _box = new()
    {
        AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(6), FontSize = 12,
        IsInactiveSelectionHighlightEnabled = true, // the selected line lights in the text as on the capture, focused or not
    };
    private readonly Border _textCard;
    private readonly Grid _textArea = new();
    private readonly ContentControl _message = new() { Focusable = false };
    private readonly TextBlock _status = Bits.Text("", 11, "IslandTextTertiaryBrush");
    private readonly Button _reselect = Bits.Button("Reselect");
    private readonly Button _retry = Bits.Button("Retry");
    private readonly Button _copyAll = Bits.Button("Copy all", accent: true);
    private readonly DispatcherTimer _copiedFor = new() { Interval = TimeSpan.FromSeconds(3) };
    private TextResult? _shown;
    private string? _copied;

    public TextResultView(ITextPageModel model)
    {
        _model = model;
        Header = Bits.Header("Text");

        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _picture.Children.Add(_image);
        _picture.Children.Add(_marks);
        _fit = new Viewbox { Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top, Child = _picture };
        var capture = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = _fit, ClipToBounds = true };
        capture.SetResourceReference(Border.BorderBrushProperty, "IslandHairBrush");
        capture.SetResourceReference(Border.BackgroundProperty, "IslandPanelBrush");
        AutomationProperties.SetName(capture, "What was read");

        _box.SetResourceReference(Control.ForegroundProperty, "IslandTextBrush");
        _box.SetResourceReference(TextBoxBase.CaretBrushProperty, "IslandTextBrush");
        _box.SetResourceReference(TextBoxBase.SelectionBrushProperty, "IslandAccentBrush");
        _box.SetResourceReference(Control.FontFamilyProperty, "UiFont");
        TextBlock.SetLineHeight(_box, 18);
        AutomationProperties.SetName(_box, "Extracted text");
        _box.TextChanged += (_, _) => { ShowStatus(); Mark(); };
        _box.SelectionChanged += (_, _) => Mark();
        _box.LostKeyboardFocus += (_, _) => Mark();
        _textArea.Children.Add(_box);
        _textArea.Children.Add(_message);
        _textCard = Bits.Card(_textArea);
        // The card shows where the keyboard is: its edge takes the accent while the text has it.
        _box.GotKeyboardFocus += (_, _) => _textCard.SetResourceReference(Border.BorderBrushProperty, "IslandAccentBrush");
        _box.LostKeyboardFocus += (_, _) => _textCard.SetResourceReference(Border.BorderBrushProperty, "IslandCardBorderBrush");

        var split = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(Bits.CaptureWidth) },
                new ColumnDefinition { Width = new GridLength(12) },
                new ColumnDefinition(),
            },
            Children = { capture, _textCard },
        };
        Grid.SetColumn(_textCard, 2);

        _reselect.Click += (_, _) => _model.Reselect();
        _retry.Click += (_, _) => _model.Retry();
        _copyAll.Click += (_, _) => Copy();
        _copiedFor.Tick += (_, _) => { _copiedFor.Stop(); _copied = null; ShowStatus(); };
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { _reselect, _retry, _copyAll } };
        DockPanel.SetDock(buttons, Dock.Right);
        var foot = new DockPanel { Height = Bits.FootHeight, LastChildFill = true, Children = { buttons, _status } };

        var body = new Grid
        {
            Width = IslandPage.BodyWidth,
            Height = IslandPage.BodyHeight,
            RowDefinitions = { new RowDefinition(), new RowDefinition { Height = new GridLength(12) }, new RowDefinition { Height = GridLength.Auto } },
            Children = { split, foot },
        };
        Grid.SetRow(foot, 2);
        Content = body;

        // Subscribed from construction, not Loaded, so the page is current while it's built but not yet shown.
        _model.Changed += () => { if (Dispatcher.CheckAccess()) Refresh(); else Dispatcher.BeginInvoke(Refresh); };
        Refresh();
    }

    /// <summary>The page's header: "Text".</summary>
    public DockPanel Header { get; }

    internal TextBox TextBox => _box;

    internal Button ReselectButton => _reselect;

    internal Button RetryButton => _retry;

    internal Button CopyAllButton => _copyAll;

    internal string Status => _status.Text;

    internal FrameworkElement Message => _message;

    /// <summary>The lines lit on the capture, by their place in the result's lines.</summary>
    internal IReadOnlyList<int> Marked { get; private set; } = [];

    private void Refresh()
    {
        var result = _model.Result;
        if (!ReferenceEquals(result, _shown))
        {
            _shown = result;
            _copied = null;
            _image.Source = result?.Capture;
            _picture.Width = result?.Capture.PixelWidth ?? 0;
            _picture.Height = result?.Capture.PixelHeight ?? 0;
            _box.Text = result is { State: TextReadState.Read } ? result.Text : "";
            _box.CaretIndex = 0;
        }
        var state = result?.State;
        bool read = state == TextReadState.Read;
        _box.Visibility = read ? Visibility.Visible : Visibility.Collapsed;
        _message.Visibility = read ? Visibility.Collapsed : Visibility.Visible;
        _message.Content = state switch
        {
            TextReadState.NoText => Bits.Message(LucideIcons.ScanText, NoText),
            TextReadState.NoLanguage => Bits.Message(LucideIcons.TriangleAlert, NoLanguage, NoLanguageFix, caution: true),
            TextReadState.Failed => Bits.Message(LucideIcons.TriangleAlert, Failed, caution: true),
            _ => _model.Reading ? Bits.Message(LucideIcons.ScanText, Reading) : null,
        };
        _copyAll.Visibility = read ? Visibility.Visible : Visibility.Collapsed;
        _reselect.IsEnabled = _retry.IsEnabled = !_model.Reading;
        _retry.Visibility = result is null ? Visibility.Collapsed : Visibility.Visible;
        ShowStatus();
        Mark();
    }

    private void Copy()
    {
        _copied = _model.Copy(_box.Text) ? "Copied" : CopyFailed;
        _copiedFor.Stop();
        if (_copied != CopyFailed) _copiedFor.Start(); // "Copy failed" stays until the next try
        ShowStatus();
    }

    /// <summary>"English · 31 words", or what just happened.</summary>
    private void ShowStatus()
    {
        var result = _model.Result;
        string text = _model.Reading ? Reading
            : _copied is { } copied ? copied
            : result is { State: TextReadState.Read } ? $"{result.Language} · {Bits.Words(_box.Text)}"
            : "";
        _status.Text = text;
        _status.SetResourceReference(TextBlock.ForegroundProperty, text == CopyFailed ? "IslandCautionBrush" : "IslandTextTertiaryBrush");
    }

    /// <summary>Lights the lines the caret or the selection touches, while the text is still as it was read.</summary>
    private void Mark()
    {
        _marks.Children.Clear();
        var result = _shown;
        if (result is not { State: TextReadState.Read } || _box.Text != result.Text || (!_box.IsKeyboardFocusWithin && _box.SelectionLength == 0))
        {
            Marked = [];
            return;
        }
        int start = _box.SelectionStart, end = start + _box.SelectionLength;
        var marked = new List<int>();
        for (int i = 0; i < result.Lines.Count; i++)
        {
            var line = result.Lines[i];
            if (line.Start > end || line.Start + line.Length < start) continue;
            marked.Add(i);
            var mark = new Rectangle { Width = line.Box.Width, Height = line.Box.Height, Opacity = 0.35, RadiusX = 2, RadiusY = 2 };
            mark.SetResourceReference(Shape.FillProperty, "IslandAccentBrush");
            Canvas.SetLeft(mark, line.Box.X);
            Canvas.SetTop(mark, line.Box.Y);
            _marks.Children.Add(mark);
        }
        Marked = marked;
    }
}
