// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.UI.Capture;

/// <summary>
/// A Recent capture's page on the island, opened from the Capture panel's thumbnails (screen-capture.md, Recent): the
/// capture fitted whole, its file and size under it, and Open folder, Annotate, Save as and Copy, in the Text page's
/// layout (Capture results v3). Ctrl+C copies and Ctrl+Shift+S saves as. A deleted file reads File missing; the
/// retained copy keeps every verb working.
/// </summary>
public sealed class CaptureRecentView : UserControl
{
    internal const string FileMissing = "File missing";
    internal const string Removed = "Capture removed";
    internal const string CopyFailed = "Copy failed";
    internal const string SaveFailed = "Save failed";
    internal const string OpenFolderFailed = "Open folder failed";
    private const double FootHeight = 28, InfoHeight = 20;

    private readonly ICaptureRecentModel _model;
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly Viewbox _fit;
    private readonly Border _picture = new() { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
    private readonly ContentControl _gone = new() { Focusable = false };
    private readonly LucideIcon _fileGlyph = Glyph(LucideIcons.File, 14, "IslandTextTertiaryBrush");
    private readonly TextBlock _fileState = Line("", "IslandCautionBrush");
    private readonly TextBlock _fileName = Line("", "IslandTextSecondaryBrush");
    private readonly TextBlock _size = Line("", "IslandTextTertiaryBrush");
    private readonly DockPanel _info = new() { Height = InfoHeight, LastChildFill = true };
    private readonly TextBlock _status = Line("", "IslandTextTertiaryBrush");
    private readonly Button _openFolder = Verb("Open folder", LucideIcons.Folder);
    private readonly Button _annotate = Verb("Annotate", LucideIcons.Pencil);
    private readonly Button _saveAs = Verb("Save as");
    private readonly Button _copy = Verb("Copy", accent: true);
    private readonly StackPanel _verbs;
    private readonly DispatcherTimer _statusFor = new() { Interval = TimeSpan.FromSeconds(3) };
    private CaptureRecentResult? _shown;
    private bool _busy;

    public CaptureRecentView(ICaptureRecentModel model)
    {
        _model = model;
        Header = new DockPanel
        {
            LastChildFill = true,
            Children = { new TextBlock { Text = "Recent", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } },
        };

        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        // The capture at its own size, shrunk to fit and never enlarged: the thumbnail and the full image share the box,
        // so nothing moves when the full image arrives.
        _fit = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = _image };
        _picture.SetResourceReference(Border.BackgroundProperty, "IslandGalleryFaceBrush");
        _picture.SetResourceReference(Border.BorderBrushProperty, "IslandHairBrush");
        _picture.Child = new Grid { Children = { _fit, _gone } };

        _fileGlyph.Margin = new Thickness(0, 0, 6, 0);
        _fileState.Margin = new Thickness(0, 0, 6, 0);
        AutomationProperties.SetLiveSetting(_fileState, AutomationLiveSetting.Polite);
        _size.Margin = new Thickness(12, 0, 0, 0);
        _size.TextTrimming = TextTrimming.None;
        DockPanel.SetDock(_size, Dock.Right);
        _info.Children.Add(_size);
        // Docked, not stacked, so a long file name trims at the size instead of running under it.
        DockPanel.SetDock(_fileGlyph, Dock.Left);
        DockPanel.SetDock(_fileState, Dock.Left);
        _info.Children.Add(_fileGlyph);
        _info.Children.Add(_fileState);
        _info.Children.Add(_fileName);

        _openFolder.Click += (_, _) => OpenFolder();
        _annotate.Click += (_, _) => _model.Annotate();
        _saveAs.Click += async (_, _) => await SaveAs();
        _copy.Click += async (_, _) => await Copy();
        _statusFor.Tick += (_, _) => Say("");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _verbs = new StackPanel { Orientation = Orientation.Horizontal, Children = { _openFolder, _annotate, _saveAs, _copy } };
        DockPanel.SetDock(_verbs, Dock.Right);
        var foot = new DockPanel { Height = FootHeight, LastChildFill = true, Children = { _verbs, _status } };

        var body = new Grid
        {
            Width = IslandPage.BodyWidth,
            Height = IslandPage.BodyHeight,
            RowDefinitions =
            {
                new RowDefinition(),
                new RowDefinition { Height = new GridLength(8) },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(12) },
                new RowDefinition { Height = GridLength.Auto },
            },
            Children = { _picture, _info, foot },
        };
        Grid.SetRow(_info, 2);
        Grid.SetRow(foot, 4);
        Content = body;

        PreviewKeyDown += Keys;
        Unloaded += (_, _) => { _statusFor.Stop(); Say(""); };
        // Subscribed from construction, not Loaded, so a page built before it shows stays current.
        _model.Changed += () => { if (Dispatcher.CheckAccess()) Refresh(); else Dispatcher.BeginInvoke(Refresh); };
        Refresh();
    }

    /// <summary>The page's header: "Recent". The island puts Back before it.</summary>
    public DockPanel Header { get; }

    /// <summary>Copy, the page's main verb, for the island's keyboard focus when the page takes it.</summary>
    public Button InitialFocus => _copy;

    internal Button CopyButton => _copy;

    internal Button SaveAsButton => _saveAs;

    internal Button AnnotateButton => _annotate;

    internal Button OpenFolderButton => _openFolder;

    internal string Status => _status.Text;

    /// <summary>The line under the capture: "File missing", the file's name, and the size and age.</summary>
    internal string Info => string.Join(" | ", new[] { _fileState.Text, _fileName.Text, _size.Text }.Where(t => t.Length > 0));

    internal FrameworkElement Picture => _picture;

    internal ImageSource? Shown => _image.Source;

    /// <summary>Where the capture is drawn inside its card, after layout.</summary>
    internal Rect ShownBounds => _image.TransformToAncestor(_picture).TransformBounds(new Rect(_image.RenderSize));

    private void Refresh()
    {
        var result = _model.Result;
        if (result?.Id != _shown?.Id)
        {
            _statusFor.Stop();
            Say("");
        }
        _shown = result;
        bool here = result is not null;
        _fit.Visibility = _info.Visibility = _verbs.Visibility = here ? Visibility.Visible : Visibility.Hidden;
        _gone.Content = here ? null : Message(LucideIcons.Image, Removed);
        if (result is null)
        {
            _image.Source = null;
            AutomationProperties.SetName(_picture, Removed);
            return;
        }

        _image.Source = result.Image ?? result.Thumbnail;
        // One capture pixel to one screen pixel at most, so a small capture stays sharp at 150% and 200%.
        var dpi = VisualTreeHelper.GetDpi(this);
        _image.Width = Math.Max(1, result.Width / dpi.DpiScaleX);
        _image.Height = Math.Max(1, result.Height / dpi.DpiScaleY);
        string size = string.Create(CultureInfo.CurrentCulture, $"{result.Width} × {result.Height}");
        AutomationProperties.SetName(_picture, $"Capture, {size}");
        _size.Text = $"{size} · {result.When}";

        string? name = result.SavedFile is { Length: > 0 } file ? Path.GetFileName(file) : null;
        _fileState.Text = result.FileMissing ? FileMissing : "";
        _fileState.Visibility = result.FileMissing ? Visibility.Visible : Visibility.Collapsed;
        _fileName.Text = name ?? "";
        _fileName.SetResourceReference(TextBlock.ForegroundProperty, result.FileMissing ? "IslandTextTertiaryBrush" : "IslandTextSecondaryBrush");
        _fileGlyph.Data = result.FileMissing ? LucideIcons.FileX : LucideIcons.File;
        _fileGlyph.SetResourceReference(LucideIcon.BrushProperty, result.FileMissing ? "IslandCautionBrush" : "IslandTextTertiaryBrush");
        _fileGlyph.Visibility = name is null && !result.FileMissing ? Visibility.Collapsed : Visibility.Visible;
        ShowBusy();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Refresh();
    }

    private async Task Copy()
    {
        if (_busy || _shown is null) return;
        _busy = true;
        ShowBusy();
        bool copied;
        try { copied = await _model.CopyAsync(); }
        finally { _busy = false; ShowBusy(); }
        Say(copied ? "Copied" : CopyFailed, failed: !copied);
    }

    private async Task SaveAs()
    {
        if (_busy || _shown is null) return;
        _busy = true;
        ShowBusy();
        CaptureSave saved;
        try { saved = await _model.SaveAsAsync(); }
        finally { _busy = false; ShowBusy(); }
        switch (saved.Outcome)
        {
            case CaptureSaveOutcome.Saved:
                Say(saved.File is { Length: > 0 } file ? $"Saved · {Path.GetFileName(file)}" : "Saved");
                break;
            case CaptureSaveOutcome.Failed:
                Say(SaveFailed, failed: true);
                break;
        }
        _saveAs.Focus();
    }

    private void OpenFolder()
    {
        if (_shown is null) return;
        if (_model.OpenFolder()) Say("");
        else Say(OpenFolderFailed, failed: true);
    }

    private void Keys(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (e.Key == Key.C && modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            _ = Copy();
        }
        else if (e.Key == Key.S && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            _ = SaveAs();
        }
    }

    private void ShowBusy() => _copy.IsEnabled = _saveAs.IsEnabled = _annotate.IsEnabled = !_busy;

    /// <summary>What just happened, in the foot. A success fades after 3 seconds; a failure stays until the next try.</summary>
    private void Say(string text, bool failed = false)
    {
        _statusFor.Stop();
        _status.Text = text;
        _status.SetResourceReference(TextBlock.ForegroundProperty, failed ? "IslandCautionBrush" : "IslandTextTertiaryBrush");
        if (text.Length > 0 && !failed) _statusFor.Start();
    }

    private static Button Verb(string text, Geometry? glyph = null, bool accent = false)
    {
        object content = text;
        if (glyph is not null)
        {
            var icon = Glyph(glyph, 14, accent ? "IslandOnAccentBrush" : "IslandTextSecondaryBrush");
            icon.Margin = new Thickness(0, 0, 6, 0);
            content = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } } };
        }
        var button = new Button { Content = content, Height = 28, MinWidth = 64, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(8, 0, 0, 0) };
        button.SetResourceReference(StyleProperty, accent ? "IslandAccentButtonStyle" : "IslandButtonStyle");
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static TextBlock Line(string text, string brush)
    {
        var block = new TextBlock { Text = text, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private static StackPanel Message(Geometry icon, string line)
    {
        var glyph = Glyph(icon, 18, "IslandTextTertiaryBrush");
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        var text = Line(line, "IslandTextSecondaryBrush");
        text.FontSize = 12;
        text.Margin = new Thickness(0, 6, 0, 0);
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        return new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { glyph, text } };
    }

    private static LucideIcon Glyph(Geometry data, double size, string brush)
    {
        var glyph = new LucideIcon { Data = data, Size = size, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, brush);
        return glyph;
    }
}
