// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Capture;
using Chippi.Core.Modules;
using Chippi.UI.Capture;
using Microsoft.Win32;

namespace Chippi.Features.ScreenCapture;

internal sealed class CaptureEditor : IDisposable
{
    private readonly ICaptureSession _session;
    private readonly ModuleContext _context;
    private readonly ScreenCaptureModel _model;
    private readonly RecentCaptureStore _store;
    private readonly Action<string, bool> _feedback;
    private readonly Func<bool> _hasText;
    private readonly Func<CapturedImage, bool, Task> _readText;
    private Func<Task>? _retry;
    private bool _filled;
    private readonly MarkupTools _tools = new();
    private readonly FinishBar _finish = new() { ShowCopyText = false, ShowScanQrCode = false };
    private readonly List<Canvas> _canvases = [];
    private readonly Dictionary<Canvas, Preview> _previews = [];
    private readonly HashSet<Window> _windows = [];
    private readonly CancellationTokenSource _cancel = new();
    private readonly Guid _id = Guid.NewGuid();
    private CaptureDraft? _draft;
    private Selection? _selection;
    private CaptureTool _tool = CaptureTool.Select;
    private readonly List<Point> _points = [];
    private bool _ownsPress, _drawing, _busy, _disposed, _resize, _pickingInk;
    private int _selected = -1;
    private MarkupObject? _original, _preview;
    private Point _down;
    private double _width = 3;
    private TextBox? _text;
    private TextMarkBar? _textBar;
    private Canvas? _textCanvas;
    private Point _textPoint;
    private int _textIndex = -1;
    private Int32Rect? _crop;
    private CaptureOutput? _output;

    internal CaptureDraft? Draft => _draft;
    internal byte[]? FeedbackPicture { get; private set; }
    internal string FeedbackSource { get; private set; } = "Screen capture";
    internal Func<Canvas, MouseEventArgs, Point>? PointerAt { get; set; }

    public CaptureEditor(ICaptureSession session, ModuleContext context, ScreenCaptureModel model, RecentCaptureStore store, Action<string, bool> feedback, Func<bool>? hasText = null, Func<CapturedImage, bool, Task>? readText = null)
    {
        _hasText = hasText ?? (() => false);
        _readText = readText ?? ((_, _) => Task.CompletedTask);
        _session = session; _context = context; _model = model; _store = store; _feedback = feedback;
        _tools.Tool = CaptureTool.Select;
        session.Selected += Selected;
        session.KeyDown += Key;
        _tools.ToolPicked += PickTool;
        _tools.UndoAsked += () => History(redo: false);
        _tools.RedoAsked += () => History(redo: true);
        _tools.InkAsked += button =>
        {
            var choice = new InkChoice { ShowFill = _tool is CaptureTool.Rectangle or CaptureTool.Ellipse, Filled = _filled };
            var popup = new Popup { Child = choice, PlacementTarget = button, Placement = PlacementMode.Right, StaysOpen = false, AllowsTransparency = true };
            if (Window.GetWindow(button) is { } window) popup.Resources = window.Resources;
            choice.FillPicked += filled => { _filled = filled; popup.IsOpen = false; };
            choice.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { popup.IsOpen = false; e.Handled = true; } };
            choice.InkPicked += color => { _tools.Ink = color; popup.IsOpen = false; };
            choice.WidthPicked += width => { _width = width switch { InkWidth.Thin => 2, InkWidth.Thick => 8, _ => 4 }; popup.IsOpen = false; };
            choice.PickAsked += () => { popup.IsOpen = false; _pickingInk = true; _session.Tooling = true; };
            popup.IsOpen = true;
        };
        _finish.Picked += async action =>
        {
            if (action == FinishAction.Retry && _retry != null) await _retry();
            else if (action is FinishAction.CopyText or FinishAction.ScanQrCode) await ReadText(action == FinishAction.ScanQrCode);
            else if (action == FinishAction.Cancel) Cancel();
            else if (action == FinishAction.SaveAs) await SaveAs();
            else if (action == FinishAction.Copy) await Finish(EnterAction.Copy);
            else if (action == FinishAction.Save) await Finish(EnterAction.Save);
        };
    }

    private static BitmapSource Bitmap(CapturedImage image)
    {
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Stride);
        bitmap.Freeze(); return bitmap;
    }
    private void Selected(Selection selection)
    {
        if (_busy) return;
        CommitText();
        var old = _selection;
        var objects = _draft?.Objects.ToArray() ?? [];
        _selection = selection;
        _draft = new CaptureDraft(Bitmap(selection.Image()));
        if (old != null)
            foreach (var item in objects)
                _draft.Add(new MarkupObject(item.Tool, item.Points.Select(p => new Point(
                    (p.X * old.Scale + old.Rect.X - selection.Rect.X) / selection.Scale,
                    (p.Y * old.Scale + old.Rect.Y - selection.Rect.Y) / selection.Scale)), item.Color, item.Width, item.Fill, item.Text, item.Bold));
        InvalidateOutput(); _selected = -1;
        _finish.ShowCopyText = _finish.ShowScanQrCode = _hasText();
        foreach (var surface in _session.Surfaces)
        {
            if (!_canvases.Contains(surface.Layer))
            {
                _canvases.Add(surface.Layer);
                surface.Layer.Background = Brushes.Transparent;
                surface.Layer.MouseLeftButtonDown += Down;
                surface.Layer.MouseMove += Move;
                surface.Layer.MouseLeftButtonUp += Up;
                var preview = new Preview(this, surface) { IsHitTestVisible = false, Width = surface.Bounds.Width, Height = surface.Bounds.Height };
                _previews.Add(surface.Layer, preview);
                surface.Layer.Children.Add(preview);
                if (Window.GetWindow(surface.Layer) is { } window && _windows.Add(window)) window.PreviewKeyDown += PreviewKey;
            }
        }
        _session.SetBars(selection.FastPath && _model.Settings.CtrlDragCopies ? null : _tools, selection.FastPath && _model.Settings.CtrlDragCopies ? null : _finish);
        Redraw();
        if (selection.FastPath && _model.Settings.CtrlDragCopies) _ = Finish(EnterAction.Copy);
    }
    private void PickTool(CaptureTool tool)
    {
        CommitText(); _crop = null; _selected = -1;
        _tool = tool; _tools.Tool = tool;
        Redraw();
    }
    private Point SourcePoint(Canvas canvas, MouseEventArgs e)
    {
        if (PointerAt != null) return PointerAt(canvas, e);
        var surface = _session.Surfaces.First(s => ReferenceEquals(s.Layer, canvas));
        var point = e.GetPosition(canvas); var selection = _selection!;
        return new((point.X + surface.Bounds.X - selection.Rect.X) / selection.Scale,
            (point.Y + surface.Bounds.Y - selection.Rect.Y) / selection.Scale);
    }
    private int Hit(Point point)
    {
        if (_draft == null) return -1;
        for (int i = _draft.Objects.Count - 1; i >= 0; i--)
            if (_draft.Objects[i].Hit(point)) return i;
        return -1;
    }
    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (_busy || _draft == null || _selection == null) return;
        var canvas = (Canvas)sender;
        var point = SourcePoint(canvas, e);
        if (_pickingInk)
        {
            var virtualPoint = new PixelPoint((int)Math.Round(_selection.Rect.X + point.X * _selection.Scale),
                (int)Math.Round(_selection.Rect.Y + point.Y * _selection.Scale));
            if (_session.Freeze?.PixelAt(virtualPoint) is { } color)
                _tools.Ink = Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);
            _pickingInk = false; _ownsPress = true; e.Handled = true; Redraw(); return;
        }
        if (!new Rect(_draft.Crop.X, _draft.Crop.Y, _draft.Crop.Width, _draft.Crop.Height).Contains(point)) return;
        CommitText(); _ownsPress = true; e.Handled = true;
        if (_tool == CaptureTool.Text) { BeginText(canvas, point, -1); return; }
        if (_tool == CaptureTool.Eraser)
        {
            int hit = Hit(point);
            if (hit >= 0) { _draft.Remove(hit); InvalidateOutput(); _selected = -1; Redraw(); }
            return;
        }
        _resize = _selected >= 0 && _selected < _draft.Objects.Count &&
            (point - _draft.Objects[_selected].Bounds.BottomRight).Length <= 8 / _selection.Scale;
        if (_tool == CaptureTool.Select)
        {
            if (!_resize) _selected = Hit(point);
            if (_selected < 0) { Redraw(); return; }
            if (e.ClickCount == 2 && _draft.Objects[_selected].Tool == MarkupTool.Text)
            { BeginText(canvas, _draft.Objects[_selected].Points[0], _selected); return; }
            _original = _draft.Objects[_selected];
        }
        _points.Clear(); _points.Add(point); _down = point; _drawing = true;
        canvas.CaptureMouse(); Redraw();
    }
    private void Move(object sender, MouseEventArgs e)
    {
        if (!_drawing || _draft == null) return;
        var point = SourcePoint((Canvas)sender, e);
        e.Handled = true;
        if (_tool == CaptureTool.Select && _original != null)
        {
            var from = _original.Bounds;
            var to = new Rect(from.TopLeft, new Point(Math.Max(from.Left + 1, point.X), Math.Max(from.Top + 1, point.Y)));
            _preview = _resize ? _original.Resize(from, to) : _original.Move(point - _down);
        }
        else
        {
            if (_tool is CaptureTool.Pen or CaptureTool.Highlighter) _points.Add(point);
            else { if (_points.Count > 1) _points.RemoveAt(1); _points.Add(point); }
            if (Enum.TryParse<MarkupTool>(_tool.ToString(), out var tool)) _preview = new(tool, _points, _tools.Ink, _width, _filled);
            if (_tool == CaptureTool.Crop) _crop = ClampCrop(new Rect(_down, point));
        }
        Redraw();
    }
    private Int32Rect? ClampCrop(Rect bounds)
    {
        var crop = _draft!.Crop;
        bounds.Intersect(new Rect(crop.X, crop.Y, crop.Width, crop.Height));
        if (bounds.IsEmpty) return null;
        int left = (int)Math.Ceiling(bounds.Left), top = (int)Math.Ceiling(bounds.Top);
        int right = (int)Math.Floor(bounds.Right), bottom = (int)Math.Floor(bounds.Bottom);
        return right > left && bottom > top ? new Int32Rect(left, top, right - left, bottom - top) : null;
    }
    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (_ownsPress) { _ownsPress = false; e.Handled = true; }
        if (!_drawing || _draft == null) return;
        Move(sender, e);
        _drawing = false; ((Canvas)sender).ReleaseMouseCapture(); e.Handled = true;
        if (_preview != null)
        {
            if (_tool == CaptureTool.Select && _selected >= 0) _draft.Replace(_selected, _preview);
            else { _draft.Add(_preview); _selected = _draft.Objects.Count - 1; }
        }
        _preview = null; _original = null; InvalidateOutput();
        if (_tool == CaptureTool.Crop && _crop != null)
        {
            var bar = new CropBar();
            bar.Cancelled += () => { _crop = null; _session.SetBars(_tools, _finish); Redraw(); };
            bar.Cropped += () => { if (_crop is { } crop) _draft.SetCrop(crop); _crop = null; _session.SetBars(_tools, _finish); Redraw(); };
            _session.SetBars(_tools, bar);
        }
        Redraw();
    }
    private void BeginText(Canvas canvas, Point point, int index)
    {
        var old = index >= 0 ? _draft!.Objects[index] : null;
        _textCanvas = canvas; _textPoint = point; _textIndex = index;
        _text = new TextBox { Text = old?.Text ?? "", AcceptsReturn = false, MinWidth = 100, FontFamily = new FontFamily("Segoe UI"),
            FontSize = old?.Width ?? 18, FontWeight = old?.Bold == true ? FontWeights.Bold : FontWeights.Normal,
            Foreground = new SolidColorBrush(old?.Color ?? _tools.Ink), Background = Brushes.Transparent, BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1), Padding = new Thickness(0), LayoutTransform = new ScaleTransform(_selection!.Scale, _selection.Scale) };
        _textBar = new TextMarkBar { TextSize = (int)_text.FontSize, Bold = old?.Bold ?? false };
        _textBar.SizePicked += size => { if (_text != null) _text.FontSize = size; };
        _textBar.BoldChanged += bold => { if (_text != null) _text.FontWeight = bold ? FontWeights.Bold : FontWeights.Normal; };
        var surface = _session.Surfaces.First(s => ReferenceEquals(s.Layer, canvas));
        double x = _selection.Rect.X - surface.Bounds.X + point.X * _selection.Scale;
        double y = _selection.Rect.Y - surface.Bounds.Y + point.Y * _selection.Scale;
        Canvas.SetLeft(_text, x); Canvas.SetTop(_text, y);
        _textBar.LayoutTransform = new ScaleTransform(surface.Dpi / 96d, surface.Dpi / 96d);
        Canvas.SetLeft(_textBar, x); Canvas.SetTop(_textBar, y + _text.FontSize * _selection.Scale + 8);
        canvas.Children.Add(_text); canvas.Children.Add(_textBar);
        _text.Focus(); _text.SelectAll();
    }
    private void CommitText()
    {
        if (_text == null || _draft == null) return;
        var text = _text; _text = null;
        if (text.Text.Length > 0)
        {
            var item = new MarkupObject(MarkupTool.Text, [_textPoint, _textPoint + new Vector(1, 1)],
                ((SolidColorBrush)text.Foreground).Color, text.FontSize, text: text.Text, bold: text.FontWeight == FontWeights.Bold);
            if (_textIndex >= 0) _draft.Replace(_textIndex, item); else _draft.Add(item);
        }
        else if (_textIndex >= 0) _draft.Remove(_textIndex);
        _textCanvas?.Children.Remove(text);
        if (_textBar != null) _textCanvas?.Children.Remove(_textBar);
        _textBar = null; _textCanvas = null; _selected = -1; InvalidateOutput();
        Redraw();
    }
    private void PreviewKey(object sender, KeyEventArgs e)
    {
        if (_text != null)
        {
            if (e.Key == System.Windows.Input.Key.B && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                bool bold = _text.FontWeight != FontWeights.Bold;
                _text.FontWeight = bold ? FontWeights.Bold : FontWeights.Normal;
                if (_textBar != null) _textBar.Bold = bold;
                e.Handled = true; return;
            }
            if (e.Key is System.Windows.Input.Key.Escape or System.Windows.Input.Key.Enter) { CommitText(); e.Handled = true; }
            return; // TextBox owns its typing, arrows, Ctrl+A/C and undo.
        }
        if (_selected < 0 || _draft == null || _selected >= _draft.Objects.Count) return;
        Vector delta = e.Key switch
        {
            System.Windows.Input.Key.Left => new(-1, 0), System.Windows.Input.Key.Right => new(1, 0),
            System.Windows.Input.Key.Up => new(0, -1), System.Windows.Input.Key.Down => new(0, 1), _ => default,
        };
        if (delta.LengthSquared > 0)
        { _draft.Replace(_selected, _draft.Objects[_selected].Move(delta)); InvalidateOutput(); Redraw(); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Delete)
        { _draft.Remove(_selected); _selected = -1; InvalidateOutput(); Redraw(); e.Handled = true; }
    }
    private void Redraw()
    {
        if (_draft == null || _selection == null || _disposed) return;
        _tools.CanUndo = _draft.CanUndo; _tools.CanRedo = _draft.CanRedo;
        _session.Tooling = _tool != CaptureTool.Select || _draft.Objects.Count > 0;
        var crop = _draft.Crop;
        _session.SetPreview(_draft.IsCropped ? new CapturePreview(new PixelRect(
            _selection.Rect.X + (int)Math.Round(crop.X * _selection.Scale),
            _selection.Rect.Y + (int)Math.Round(crop.Y * _selection.Scale),
            Math.Max(1, (int)Math.Round(crop.Width * _selection.Scale)),
            Math.Max(1, (int)Math.Round(crop.Height * _selection.Scale))), new PixelSize(crop.Width, crop.Height)) : null);
        foreach (var preview in _previews.Values) preview.InvalidateVisual();
    }
    private void History(bool redo)
    {
        CommitText();
        if (redo) _draft?.Redo(); else _draft?.Undo();
        _selected = -1; InvalidateOutput(); Redraw();
    }
    private sealed class Preview(CaptureEditor owner, CaptureSurface surface) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            if (owner._selection is not { } selection || owner._draft is not { } draft) return;
            dc.PushTransform(new TranslateTransform(selection.Rect.X - surface.Bounds.X, selection.Rect.Y - surface.Bounds.Y));
            dc.PushTransform(new ScaleTransform(selection.Scale, selection.Scale));
            draft.Paint(dc, owner._text != null ? owner._textIndex : owner._original != null ? owner._selected : -1);
            dc.PushClip(new RectangleGeometry(new Rect(draft.Crop.X, draft.Crop.Y, draft.Crop.Width, draft.Crop.Height)));
            owner._preview?.Draw(dc);
            dc.Pop();
            Rect? frame = owner._crop is { } crop ? new Rect(crop.X, crop.Y, crop.Width, crop.Height)
                : owner._selected >= 0 && owner._selected < draft.Objects.Count ? (owner._preview ?? draft.Objects[owner._selected]).Bounds : null;
            if (frame is { IsEmpty: false } bounds)
            {
                var pen = new Pen(Brushes.White, 1 / selection.Scale) { DashStyle = DashStyles.Dash };
                dc.DrawRectangle(null, pen, bounds);
                double size = 6 / selection.Scale;
                dc.DrawRectangle(Brushes.White, null, new Rect(bounds.Right - size / 2, bounds.Bottom - size / 2, size, size));
            }
            dc.Pop(); dc.Pop();
        }
    }
    private async void Key(KeyEventArgs e)
    {
        if (_text != null) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; if (_pickingInk) { _pickingInk = false; Redraw(); } else Cancel(); }
        else if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; if (_retry != null) await _retry(); else await Finish(_model.Settings.Enter); }
        else if (ctrl && e.Key == System.Windows.Input.Key.C) { e.Handled = true; await Finish(EnterAction.Copy); }
        else if (ctrl && e.Key == System.Windows.Input.Key.S) { e.Handled = true; if (shift) await SaveAs(); else await Finish(EnterAction.Save); }
        else if (ctrl && e.Key == System.Windows.Input.Key.Z) { e.Handled = true; History(redo: shift); }
        else if (ctrl && e.Key == System.Windows.Input.Key.Y) { e.Handled = true; History(redo: true); }
        else if (!ctrl && Keyboard.Modifiers == ModifierKeys.None)
        {
            var key = e.Key.ToString();
            var tool = Enum.GetValues<CaptureTool>().FirstOrDefault(t => CaptureBars.Letter(t)?.ToString() == key);
            if (e.Key == System.Windows.Input.Key.V || tool != CaptureTool.Select) { e.Handled = true; PickTool(tool); }
        }
    }
    private void InvalidateOutput()
    {
        _output = null; _retry = null; _finish.Failure = null;
    }
    internal void RefreshProviders() => _finish.ShowCopyText = _finish.ShowScanQrCode = _hasText();
    private async Task ReadText(bool qr)
    {
        if (_busy || _draft == null || !_hasText()) return;
        CommitText();
        var bitmap = new FormatConvertedBitmap(_draft.Flatten(), PixelFormats.Bgra32, null, 0);
        var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var image = new CapturedImage(bitmap.PixelWidth, bitmap.PixelHeight, pixels);
        _session.Close(CaptureEnd.Finished, handBack: !qr);
        await _readText(image, qr);
    }
    private async Task Finish(EnterAction action, bool retry = false)
    {
        if (_busy || _draft == null || _disposed) return;
        retry = retry && _output != null && _text == null;
        CommitText();
        _busy = true;
        try
        {
            var pixels = _draft.Flatten();
            if (!retry) InvalidateOutput();
            _output ??= new CaptureOutput(pixels,
                image => _context.Clipboard == null ? Task.FromResult(false) : CaptureClipboard.CopyAsync(_context.Clipboard, image, _cancel.Token),
                image => CaptureFiles.OnWorkerAsync(() => CaptureFiles.Save(image, _model.SaveFolder, _model.Settings.FileType, DateTimeOffset.Now)),
                async (image, file) =>
                {
                    _cancel.Token.ThrowIfCancellationRequested();
                    if (_model.Settings.History)
                    {
                        await CaptureFiles.OnWorkerAsync(() => { _cancel.Token.ThrowIfCancellationRequested(); _store.Put(_id, image, DateTimeOffset.Now, file); return true; });
                        _cancel.Token.ThrowIfCancellationRequested();
                        _model.RefreshRecent();
                    }
                });
            var result = retry ? await _output.RetryAsync() : await _output.FinishAsync(action);
            if (_disposed) return;
            FeedbackSource = result.SavedFile != null ? Path.GetFileName(result.SavedFile)
                : $"{CaptureBarLine.ModeName(_session.Request.Mode)} · {pixels.PixelWidth} × {pixels.PixelHeight}";
            if (result.Copied || result.Saved)
                FeedbackPicture = await CaptureFiles.OnWorkerAsync(() => CaptureFiles.Thumbnail(pixels));
            if (_disposed) return;
            _feedback(result.RecentFailed ? "Recent capture failed" : result.Title, result.CopyFailed || result.SaveFailed || result.RecentFailed);
            if (!result.CopyFailed && !result.SaveFailed)
            {
                _finish.Failure = null; _retry = null; _session.Close();
            }
            else
            {
                _session.SetBars(_tools, _finish);
                _finish.Failure = result.Title;
                _retry = () => Finish(action, retry: true);
                _finish.Main.Focus();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _feedback("Capture failed", true); }
        finally { _busy = false; }
    }
    private async Task SaveAs()
    {
        if (_busy || _draft == null) return;
        CommitText();
        var dialog = new SaveFileDialog { Filter = "PNG image|*.png|JPEG image|*.jpg", FileName = "Screenshot.png", OverwritePrompt = true, AddExtension = true };
        var owner = Window.GetWindow(_finish);
        if ((owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) != true) return;
        await SaveAsTo(dialog.FileName, dialog.FilterIndex == 2 ? CaptureFileType.Jpeg : CaptureFileType.Png);
    }
    private async Task SaveAsTo(string path, CaptureFileType type)
    {
        if (_busy || _draft == null || _disposed) return;
        CommitText(); InvalidateOutput(); _busy = true;
        try
        {
            var pixels = _draft.Flatten();
            await CaptureFiles.OnWorkerAsync(() => { CaptureFiles.SaveAs(pixels, path, type, true); return true; });
            if (_disposed) return;
            FeedbackSource = Path.GetFileName(path);
            FeedbackPicture = await CaptureFiles.OnWorkerAsync(() => CaptureFiles.Thumbnail(pixels));
            if (_disposed) return;
            try
            {
                if (_model.Settings.History)
                {
                    await CaptureFiles.OnWorkerAsync(() => { _cancel.Token.ThrowIfCancellationRequested(); _store.Put(_id, pixels, DateTimeOffset.Now, path); return true; });
                    if (!_disposed) _model.RefreshRecent();
                }
                if (!_disposed) _feedback("Saved", false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            { if (!_disposed) _feedback("Recent capture failed", true); }
            if (!_disposed) _session.Close();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_disposed)
            {
                _feedback("Save failed", true);
                _finish.Failure = "Save failed";
                _retry = () => SaveAsTo(path, type);
                _finish.Main.Focus();
            }
        }
        finally { _busy = false; }
    }
    public void AskDiscard(Action next)
    {
        var popup = new ContextMenu();
        var cancel = new MenuItem { Header = "Cancel" };
        var discard = new MenuItem { Header = "Discard" };
        discard.Click += (_, _) => { Cancel(); next(); };
        popup.Items.Add(new MenuItem { Header = "Discard capture?", IsEnabled = false });
        popup.Items.Add(cancel); popup.Items.Add(discard);
        popup.PlacementTarget = _finish; popup.IsOpen = true;
    }
    public void Cancel() => _session.Close(CaptureEnd.Cancelled);
    public void Dispose()
    {
        _disposed = true; _cancel.Cancel();
        foreach (var window in _windows) window.PreviewKeyDown -= PreviewKey;
        _session.Selected -= Selected; _session.KeyDown -= Key;
        foreach (var canvas in _canvases)
        {
            canvas.MouseLeftButtonDown -= Down; canvas.MouseMove -= Move; canvas.MouseLeftButtonUp -= Up;
        }
        _canvases.Clear(); _windows.Clear(); _previews.Clear();
        _text = null; _textBar = null; _textCanvas = null;
        _draft = null; _output = null; _retry = null;
    }
}