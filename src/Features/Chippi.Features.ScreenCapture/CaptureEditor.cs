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
    private readonly MarkupTools _tools = new();
    private readonly FinishBar _finish = new();
    private readonly List<Canvas> _canvases = [];
    private readonly CancellationTokenSource _cancel = new();
    private EnterAction? _lastAction;
    private readonly Guid _id = Guid.NewGuid();
    private CaptureDraft? _draft;
    private Selection? _selection;
    private CaptureTool _tool = CaptureTool.Select;
    private readonly List<Point> _points = [];
    private bool _drawing, _busy, _disposed;
    private double _width = 3;
    private CaptureOutput? _output;

    public CaptureEditor(ICaptureSession session, ModuleContext context, ScreenCaptureModel model, RecentCaptureStore store, Action<string, bool> feedback)
    {
        _session = session; _context = context; _model = model; _store = store; _feedback = feedback;
        session.Selected += Selected;
        session.KeyDown += Key;
        _tools.ToolPicked += tool => { _tool = tool; _tools.Tool = tool; session.Tooling = tool != CaptureTool.Select; };
        _tools.UndoAsked += () => { _draft?.Undo(); Redraw(); };
        _tools.RedoAsked += () => { _draft?.Redo(); Redraw(); };
        _tools.InkAsked += button =>
        {
            var choice = new InkChoice();
            var popup = new Popup { Child = choice, PlacementTarget = button, Placement = PlacementMode.Right, StaysOpen = false, AllowsTransparency = true };
            choice.InkPicked += color => { _tools.Ink = color; popup.IsOpen = false; };
            choice.WidthPicked += width => { _width = width switch { InkWidth.Thin => 2, InkWidth.Thick => 8, _ => 4 }; };
            popup.IsOpen = true;
        };
        _finish.Picked += async action =>
        {
            if (action == FinishAction.Cancel) Cancel();
            else if (action == FinishAction.SaveAs) SaveAs();
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
        var old = _selection;
        var objects = _draft?.Objects.ToArray() ?? [];
        _selection = selection;
        _draft = new CaptureDraft(Bitmap(selection.Image()));
        if (old != null)
            foreach (var item in objects)
                _draft.Add(new MarkupObject(item.Tool, item.Points.Select(p => new Point(
                    (p.X * old.Scale + old.Rect.X - selection.Rect.X) / selection.Scale,
                    (p.Y * old.Scale + old.Rect.Y - selection.Rect.Y) / selection.Scale)), item.Color, item.Width, item.Fill, item.Text));
        _output = null;
        foreach (var surface in _session.Surfaces)
        {
            if (!_canvases.Contains(surface.Layer)) _canvases.Add(surface.Layer);
            surface.Layer.Background = Brushes.Transparent;
            surface.Layer.MouseLeftButtonDown -= Down; surface.Layer.MouseLeftButtonDown += Down;
            surface.Layer.MouseMove -= Move; surface.Layer.MouseMove += Move;
            surface.Layer.MouseLeftButtonUp -= Up; surface.Layer.MouseLeftButtonUp += Up;
        }
        _session.SetBars(_tools, _finish);
        Redraw();
        if (selection.FastPath && _model.Settings.CtrlDragCopies) _ = Finish(EnterAction.Copy);
    }
    private Point SourcePoint(Canvas canvas, MouseEventArgs e)
    {
        var surface = _session.Surfaces.First(s => ReferenceEquals(s.Layer, canvas));
        var point = e.GetPosition(canvas); var selection = _selection!;
        return new((point.X + surface.Bounds.X - selection.Rect.X) / selection.Scale,
            (point.Y + surface.Bounds.Y - selection.Rect.Y) / selection.Scale);
    }
    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (_busy || _draft == null || _tool == CaptureTool.Select) return;
        var canvas = (Canvas)sender;
        var point = SourcePoint(canvas, e);
        if (point.X < 0 || point.Y < 0 || point.X >= _draft.Crop.Width || point.Y >= _draft.Crop.Height) return;
        e.Handled = true;
        _points.Clear(); _points.Add(point); _drawing = true; canvas.CaptureMouse();
    }
    private void Move(object sender, MouseEventArgs e)
    {
        if (!_drawing) return;
        var point = SourcePoint((Canvas)sender, e);
        if (_tool is CaptureTool.Pen or CaptureTool.Highlighter) _points.Add(point);
        else { if (_points.Count > 1) _points.RemoveAt(1); _points.Add(point); }
        e.Handled = true;
    }
    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing || _draft == null) return;
        _drawing = false; ((Canvas)sender).ReleaseMouseCapture(); e.Handled = true;
        if (_points.Count == 1) _points.Add(SourcePoint((Canvas)sender, e));
        if (Enum.TryParse<MarkupTool>(_tool.ToString(), out var tool))
            _draft.Add(new(tool, _points, _tools.Ink, _width));
        _output = null; Redraw();
    }
    private void Redraw()
    {
        if (_draft == null || _selection == null || _disposed) return;
        var pixels = _draft.Flatten();
        foreach (var surface in _session.Surfaces)
        {
            surface.Layer.Children.Clear();
            var image = new Image { Source = pixels, Width = pixels.PixelWidth * _selection.Scale, Height = pixels.PixelHeight * _selection.Scale, IsHitTestVisible = false };
            Canvas.SetLeft(image, _selection.Rect.X - surface.Bounds.X + _draft.Crop.X * _selection.Scale);
            Canvas.SetTop(image, _selection.Rect.Y - surface.Bounds.Y + _draft.Crop.Y * _selection.Scale);
            surface.Layer.Children.Add(image);
        }
    }
    private async void Key(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Cancel(); }
        else if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; await Finish(_model.Settings.Enter); }
        else if (ctrl && e.Key == System.Windows.Input.Key.C) { e.Handled = true; await Finish(EnterAction.Copy); }
        else if (ctrl && e.Key == System.Windows.Input.Key.S)
        { e.Handled = true; if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) SaveAs(); else await Finish(EnterAction.Save); }
        else if (ctrl && e.Key == System.Windows.Input.Key.Z) { e.Handled = true; _draft?.Undo(); _output = null; Redraw(); }
        else if (ctrl && e.Key == System.Windows.Input.Key.Y) { e.Handled = true; _draft?.Redo(); _output = null; Redraw(); }
    }
    private async Task Finish(EnterAction action)
    {
        if (_busy || _draft == null || _disposed) return;
        _busy = true;
        try
        {
            var pixels = _draft.Flatten();
            bool retry = _output != null && _lastAction == action;
            if (!retry) _output = null;
            _lastAction = action;
            _output ??= new CaptureOutput(pixels,
                image => _context.Clipboard == null ? Task.FromResult(false) : CaptureClipboard.CopyAsync(_context.Clipboard, image, _cancel.Token),
                image => Task.FromResult(CaptureFiles.Save(image, _model.SaveFolder, _model.Settings.FileType, DateTimeOffset.Now)),
                (image, file) => { _cancel.Token.ThrowIfCancellationRequested(); if (_model.Settings.History) { _store.Put(_id, image, DateTimeOffset.Now, file); _model.RefreshRecent(); } });
            var result = retry ? await _output.RetryAsync() : await _output.FinishAsync(action);
            if (_disposed) return;
            _feedback(result.RecentFailed ? "Recent capture failed" : result.Title, result.CopyFailed || result.SaveFailed || result.RecentFailed);
            if (!result.CopyFailed && !result.SaveFailed) _session.Close();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _feedback("Capture failed", true); }
        finally { _busy = false; }
    }
    private void SaveAs()
    {
        if (_busy || _draft == null) return;
        var dialog = new SaveFileDialog { Filter = "PNG image|*.png|JPEG image|*.jpg", FileName = "Screenshot.png", OverwritePrompt = true, AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var pixels = _draft.Flatten();
            CaptureFiles.SaveAs(pixels, dialog.FileName, dialog.FilterIndex == 2 ? CaptureFileType.Jpeg : CaptureFileType.Png, true);
            if (_model.Settings.History) { _store.Put(_id, pixels, DateTimeOffset.Now, dialog.FileName); _model.RefreshRecent(); }
            _feedback("Saved", false); _session.Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _feedback("Save failed", true); }
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
        _disposed = true;
        _cancel.Cancel();
        _session.Selected -= Selected; _session.KeyDown -= Key;
        foreach (var surface in _session.Surfaces)
        {
            surface.Layer.MouseLeftButtonDown -= Down; surface.Layer.MouseMove -= Move; surface.Layer.MouseLeftButtonUp -= Up;
        }
        _draft = null; _output = null;
    }
}
