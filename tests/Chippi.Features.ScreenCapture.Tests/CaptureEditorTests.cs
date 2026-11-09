// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Capture;
using Chippi.Core.Modules;
using Chippi.Core.Clipboard;
using Chippi.Island.Tests;
using Chippi.UI.Capture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.ScreenCapture.Tests;

public sealed class CaptureEditorTests
{
    [Fact]
    public void Initial_selection_remains_movable_and_resizable_and_rebuilds_the_draft() => WpfTestThread.Run(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "chippi-adjust-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var store = new RecentCaptureStore(folder);
            var context = new ModuleContext("screen-capture", new SettingsStore(), NullLogger.Instance, null!, null!);
            using var model = new ScreenCaptureModel(context, store, _ => { }, _ => false);
            var session = new Session();
            using var editor = new CaptureEditor(session, context, model, store, (_, _) => { });
            session.Release(new(50, 40, 200, 150));
            Assert.False(session.Fixed);
            Assert.False(session.Tooling); // The overlay, rather than the markup canvas, receives an initial drag.

            session.Adjust(new(150, 100), new(170, 110));
            Assert.Equal(new PixelRect(70, 50, 200, 150), session.Selection!.Rect);
            Assert.Equal(new Int32Rect(0, 0, 200, 150), editor.Draft!.Crop);

            session.Adjust(new(270, 125), new(320, 125));
            Assert.Equal(new PixelRect(70, 50, 250, 150), session.Selection.Rect);
            Assert.Equal(250, editor.Draft!.Flatten().PixelWidth);
            Assert.Equal(150, editor.Draft.Flatten().PixelHeight);
            Assert.False(session.Fixed);
        }
        finally { Directory.Delete(folder, true); }
    });

    [Fact]
    public void Real_tool_events_move_resize_erase_crop_and_commit_text_in_source_pixels() => WpfTestThread.Run(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "chippi-editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var store = new RecentCaptureStore(folder);
            var context = new ModuleContext("screen-capture", new SettingsStore(), NullLogger.Instance, null!, null!);
            using var model = new ScreenCaptureModel(context, store, _ => { }, _ => false);
            var session = new Session();
            using var editor = new CaptureEditor(session, context, model, store, (_, _) => { });
            Point at = default;
            editor.PointerAt = (_, _) => at; // The owner's mouse never participates.
            session.Release();
            Assert.False(session.Fixed);
            void Tool(string name) => Click(Find(session.Tools!, name));
            bool upHandled = false;
            void Drag(Point from, Point to)
            {
                at = from;
                session.Canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                at = to;
                session.Canvas.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseMoveEvent });
                var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent };
                session.Canvas.RaiseEvent(up);
                upHandled = up.Handled;
            }
            Tool("Redact"); Drag(new(20, 20), new(80, 80));
            Assert.Single(editor.Draft!.Objects);
            Assert.Equal(-1, editor.SelectedMark); // Drawing never frames the new mark (live check, 2026-11-08).
            Tool("Select"); Drag(new(40, 40), new(50, 50));
            Assert.Equal(0, editor.SelectedMark);
            Assert.Equal(new Point(30, 30), editor.Draft.Objects[0].Points[0]);
            Drag(new(90, 90), new(120, 130));
            Assert.Equal(new Point(120, 130), editor.Draft.Objects[0].Points[1]);
            var pixels = editor.Draft.Flatten();
            byte[] sample = new byte[4];
            pixels.CopyPixels(new Int32Rect(25, 25, 1, 1), sample, 4, 0);
            Assert.Equal(255, sample[2]); // Moving redaction doesn't leave its old pixels behind in this unsaved draft.
            Tool("Eraser"); Drag(new(50, 50), new(50, 50));
            Assert.Empty(editor.Draft.Objects);
            Tool("Text"); Drag(new(15, 15), new(15, 15));
            Assert.True(upHandled, "A text press must consume its release before the selector sees it.");
            var text = Assert.Single(session.Canvas.Children.OfType<TextBox>());
            text.Text = "Source pixels";
            Tool("Pen");
            Assert.Equal("Source pixels", Assert.Single(editor.Draft.Objects).Text);
            Assert.Empty(session.Canvas.Children.OfType<TextBox>());
            Tool("Crop"); Drag(new(10, 10), new(110, 90));
            Click(Find(session.Output!, "Crop"));
            Assert.Equal(new Int32Rect(10, 10, 100, 80), editor.Draft.Crop);
            Assert.Equal(new PixelRect(10, 10, 100, 80), session.Preview!.Bounds);
            Click(Find(session.Tools!, "Undo"));
            Assert.Equal(new Int32Rect(0, 0, 400, 300), editor.Draft.Crop);
            Assert.Null(session.Preview);
            Click(Find(session.Tools!, "Redo"));
            Assert.Equal(100, editor.Draft.Flatten().PixelWidth);
        }
        finally { Directory.Delete(folder, true); }
    });

    [Fact]
    public void Failed_copy_exposes_retry_and_each_explicit_output_is_a_new_attempt() => WpfTestThread.Run(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "chippi-retry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var store = new RecentCaptureStore(folder);
            var clipboard = new BusyClipboard();
            var context = new ModuleContext("screen-capture", new SettingsStore(), NullLogger.Instance, null!, null!) { Clipboard = clipboard };
            using var model = new ScreenCaptureModel(context, store, _ => { }, _ => false);
            var session = new Session();
            using var editor = new CaptureEditor(session, context, model, store, (_, _) => { });
            session.Release();
            var finish = Assert.IsType<FinishBar>(session.Output);
            Click(finish.Copy);
            Assert.Equal("Copy failed", finish.Failure);
            Assert.Same(finish.Retry, finish.Main);
            Assert.Equal(1, clipboard.Writes);
            Click(finish.Retry);
            Assert.Equal(2, clipboard.Writes);
            Click(finish.Copy);
            Assert.Equal(3, clipboard.Writes);
            Assert.Empty(model.Recent);
        }
        finally { Directory.Delete(folder, true); }
    });

    [Theory]
    [InlineData("Copy text", false)]
    [InlineData("Scan QR code", true)]
    public void Each_reading_button_opens_its_page_before_the_frozen_screen_closes(string button, bool qr) => WpfTestThread.Run(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "chippi-read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var store = new RecentCaptureStore(folder);
            var context = new ModuleContext("screen-capture", new SettingsStore(), NullLogger.Instance, null!, null!);
            using var model = new ScreenCaptureModel(context, store, _ => { }, _ => false);
            var session = new Session();
            var reads = new List<(bool Qr, int ClosesBefore, int Width)>();
            using var editor = new CaptureEditor(session, context, model, store, (_, _) => { }, () => true,
                (image, scan) => { reads.Add((scan, session.Closes.Count, image.Width)); return Task.CompletedTask; });
            session.Release();
            Click(Find(session.Output!, button));
            Assert.Equal([(qr, 0, 400)], reads); // the page is asked for while the frozen screen still holds the foreground
            Assert.Equal([(CaptureEnd.Finished, false)], session.Closes); // then it closes, never handing the keyboard back
        }
        finally { Directory.Delete(folder, true); }
    });

    private sealed class BusyClipboard : IClipboardHub
    {
        public int Writes;
        public Task<ClipStatus> WriteAsync(ClipContent content, CancellationToken cancel = default)
        { Writes++; return Task.FromResult(ClipStatus.Busy); }
        public IDisposable Listen(Action<ClipCopy> copied, Func<string, bool>? skip = null) => throw new NotSupportedException();
        public Task<ClipRead> ReadAsync(CancellationToken cancel = default) => throw new NotSupportedException();
        public PasteTarget? CaptureTarget() => null;
        public Task<PasteOutcome> PasteAsync(ClipContent content, PasteTarget target, CancellationToken cancel = default) => throw new NotSupportedException();
        public Task<PasteOutcome> PastePlainTextAsync(CancellationToken cancel = default) => throw new NotSupportedException();
        public void Beep() { }
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Button Find(DependencyObject root, string name)
    {
        if (root is Button button && AutomationProperties.GetName(button) == name) return button;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            var found = FindOrNull(child, name);
            if (found != null) return found;
        }
        throw new InvalidOperationException("Missing button: " + name);
    }
    private static Button? FindOrNull(DependencyObject root, string name)
    {
        if (root is Button button && AutomationProperties.GetName(button) == name) return button;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            if (FindOrNull(child, name) is { } found) return found;
        return null;
    }
    private sealed class SettingsStore : IModuleSettings
    {
        public T Get<T>() where T : class, new() => new();
        public void Save<T>(T value) where T : class { }
    }
    private sealed class Session : ICaptureSession
    {
        public CaptureRequest Request { get; } = new(Chippi.Capture.CaptureMode.Region);
        public Canvas Canvas { get; } = new() { Width = 400, Height = 300 };
        public FrameworkElement? Tools, Output;
        public CapturePreview? Preview;
        public void SetPreview(CapturePreview? preview) => Preview = preview;
        public Freeze? Freeze { get; private set; }
        public Selection? Selection { get; private set; }
        public IReadOnlyList<CaptureSurface> Surfaces => [new("fixture", new(0, 0, 400, 300), 96, Canvas)];
        private Selector? _selector;
        public bool Tooling { get => _selector?.Tooling ?? false; set { if (_selector != null) _selector.Tooling = value; } }
        public bool Fixed => _selector?.Fixed ?? false;
        public void FixSelection() => _selector?.Fix();
        public event Action<Selection>? Selected;
        public event Action<System.Windows.Input.KeyEventArgs>? KeyDown { add { } remove { } }
        public event Action<PixelPoint, uint>? Picked { add { } remove { } }
        public event Action? Frozen { add { } remove { } }
        public event Action<CaptureEnd>? Closed { add { } remove { } }
        public void SetBars(FrameworkElement? tools, FrameworkElement? output) { Tools = tools; Output = output; }
        public List<(CaptureEnd End, bool HandBack)> Closes { get; } = [];
        public void Close(CaptureEnd end = CaptureEnd.Finished, bool handBack = true) => Closes.Add((end, handBack));
        public void Release(PixelRect? rect = null)
        {
            var bytes = new byte[400 * 300 * 4];
            for (int i = 0; i < bytes.Length; i += 4) { bytes[i + 2] = 255; bytes[i + 3] = 255; }
            var screen = new CapturedScreen("fixture", 0, new(0, 0, 400, 300), 96, bytes);
            Freeze = new Freeze([screen], [], DateTimeOffset.Now);
            var bounds = rect ?? screen.Bounds;
            _selector = new Selector(Freeze, Chippi.Capture.CaptureMode.Region, null, new(bounds.X, bounds.Y), new(4, 4), 6);
            _selector.Down(new(bounds.X, bounds.Y), false);
            _selector.Up(new(bounds.Right - 1, bounds.Bottom - 1));
            Selection = _selector.Selection;
            Selected?.Invoke(Selection!);
        }
        public void Adjust(PixelPoint from, PixelPoint to)
        {
            var before = _selector!.Selection;
            _selector.Down(from, false);
            _selector.Move(to);
            _selector.Up(to);
            Selection = _selector.Selection;
            if (Selection is { } selection && !ReferenceEquals(before, selection))
                Selected?.Invoke(selection);
        }
    }
}
